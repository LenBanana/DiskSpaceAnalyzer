using System;
using System.Buffers;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using DiskSpaceAnalyzer.Models;
using Microsoft.Win32.SafeHandles;

namespace DiskSpaceAnalyzer.Services.Mft;

// Opens a raw NTFS volume, locates the $MFT file, then streams every file
// record into an MftVolumeIndex. All disk I/O happens here; tree building
// happens in MftFileSystemService.
internal sealed class MftVolumeScanner
{
    // 8 MiB streaming buffer: fewer syscalls than 4 MiB with no measurable
    // downside; still fits in L3 on modern CPUs. SEQUENTIAL_SCAN hints to the
    // Windows cache manager which does its own read-ahead regardless.
    private const int ReadBufferBytes = 8 * 1024 * 1024;

    private const long MftFrn = 0; // MFT record #0 describes the MFT itself
    private const long RootDirFrn = 5; // root directory is always FRN 5 on NTFS
    private readonly CancellationToken _ct;

    // Harvested during the main streaming pass; applied to the index afterwards.
    // Key = base FRN of a fragmented file; value = best (maximum) $DATA real size
    // observed across that file's extension records. Only entries whose base
    // record carried $ATTRIBUTE_LIST but no in-base $DATA benefit.
    private readonly Dictionary<long, long> _extensionDataSizes = new();
    private readonly IProgress<ScanProgress>? _progress;

    private readonly string _volumeRoot;

    public MftVolumeScanner(string volumeRoot, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        _volumeRoot = volumeRoot;
        _progress = progress;
        _ct = ct;
    }

    public MftVolumeIndex Scan()
    {
        using var volume = OpenVolume(_volumeRoot);
        var volumeData = GetVolumeData(volume);

        var recordSize = (int)volumeData.BytesPerFileRecordSegment;
        var sectorSize = (int)volumeData.BytesPerSector;
        long clusterSize = volumeData.BytesPerCluster;
        var mftStartOffset = volumeData.MftStartLcn * clusterSize;
        var mftValidBytes = volumeData.MftValidDataLength;
        var totalRecords = (int)(mftValidBytes / recordSize);

        if (recordSize <= 0 || sectorSize <= 0 || clusterSize <= 0 || totalRecords <= 0)
            throw new MftUnavailableException(MftUnavailableReason.InvalidMft,
                "Volume reported implausible MFT geometry.");

        // Step 1 — read MFT record #0 in isolation to recover $MFT's own data runs.
        var mftRuns = ReadMftDataRuns(volume, mftStartOffset, recordSize, sectorSize);
        if (mftRuns.Count == 0)
            throw new MftUnavailableException(MftUnavailableReason.InvalidMft,
                "Failed to decode $MFT data runs from record 0.");

        var index = new MftVolumeIndex(totalRecords, volumeData.VolumeSerialNumber, _volumeRoot);

        // Step 2 — stream every run of the $MFT file sequentially, parsing records
        // inline with no per-record allocation.
        var rented = ArrayPool<byte>.Shared.Rent(ReadBufferBytes);
        try
        {
            var bufferUsable = ReadBufferBytes / recordSize * recordSize; // cluster-aligned
            long recordsProcessed = 0;
            var mftRemaining = mftValidBytes;
            long bytesRead = 0;
            long nextReportAt = 0;

            foreach (var (lcn, clusterCount) in mftRuns)
            {
                if (mftRemaining <= 0) break;

                var runOffset = lcn * clusterSize;
                var runBytes = Math.Min(clusterCount * clusterSize, mftRemaining);

                Seek(volume, runOffset);

                while (runBytes > 0)
                {
                    _ct.ThrowIfCancellationRequested();

                    var toRead = (int)Math.Min(bufferUsable, runBytes);
                    var read = ReadBlock(volume, rented, toRead);
                    if (read <= 0) break;

                    var recordsInBuffer = read / recordSize;
                    for (var r = 0; r < recordsInBuffer; r++)
                    {
                        var frn = recordsProcessed + r;
                        if (frn >= totalRecords) break;

                        var recordSpan = rented.AsSpan(r * recordSize, recordSize);
                        ParseRecord(frn, recordSpan, sectorSize, index);
                    }

                    recordsProcessed += recordsInBuffer;
                    runBytes -= read;
                    mftRemaining -= read;
                    bytesRead += read;

                    // Smooth progress tick every ~32 MiB of MFT consumed.
                    if (bytesRead >= nextReportAt)
                    {
                        var pct = (double)bytesRead / mftValidBytes * 100.0;
                        _progress?.Report(new ScanProgress
                        {
                            CurrentPath = $"[MFT] {recordsProcessed:N0} / {totalRecords:N0} records  ({pct:F1}%)",
                            ProcessedItems = recordsProcessed
                        });
                        nextReportAt = bytesRead + 32L * 1024 * 1024;
                    }
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }

        ApplyExtensionDataSizes(index);
        return index;
    }

    // Extension records can appear before their base record in MFT order. Once
    // the streaming pass is complete we resolve every fragmented file in one
    // pass: for each base record that carried $ATTRIBUTE_LIST but had no $DATA
    // locally, transplant the best size harvested from its extension records.
    private void ApplyExtensionDataSizes(MftVolumeIndex index)
    {
        var unresolved = 0;

        for (var i = 0; i < index.Capacity; i++)
        {
            var flags = index.Flags[i];
            if ((flags & MftVolumeIndex.FlagInUse) == 0) continue;
            if ((flags & MftVolumeIndex.FlagHasAttributeList) == 0) continue;
            if ((flags & MftVolumeIndex.FlagDirectory) != 0) continue;
            if ((flags & MftVolumeIndex.FlagDataResolvedFromBase) != 0) continue;

            if (_extensionDataSizes.TryGetValue(i, out var extSize) && extSize > 0)
                index.Size[i] = extSize;
            else
                unresolved++;
        }

        index.AttributeListUnresolved = unresolved;
    }

    // ---- volume open / ioctl -------------------------------------------------

    private static SafeFileHandle OpenVolume(string volumeRoot)
    {
        // "C:\" → "\\.\C:"   (strip trailing slash, prepend device namespace)
        var rootLetter = volumeRoot.TrimEnd('\\', '/');
        if (rootLetter.Length < 2 || rootLetter[1] != ':')
            throw new MftUnavailableException(MftUnavailableReason.UnsupportedPath,
                $"MFT scanner requires a drive-letter path, got '{volumeRoot}'.");

        var devicePath = $@"\\.\{rootLetter}";

        var handle = NativeMethods.CreateFile(
            devicePath,
            NativeMethods.GENERIC_READ,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE | NativeMethods.FILE_SHARE_DELETE,
            IntPtr.Zero,
            NativeMethods.OPEN_EXISTING,
            NativeMethods.FILE_FLAG_SEQUENTIAL_SCAN,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            var err = Marshal.GetLastWin32Error();
            handle.Dispose();
            var reason = err == 5 /* ERROR_ACCESS_DENIED */
                ? MftUnavailableReason.NotElevated
                : MftUnavailableReason.VolumeOpenFailed;
            throw new MftUnavailableException(reason,
                $"CreateFile('{devicePath}') failed: {new Win32Exception(err).Message}");
        }

        return handle;
    }

    private static unsafe NativeMethods.NtfsVolumeDataBuffer GetVolumeData(SafeFileHandle h)
    {
        NativeMethods.NtfsVolumeDataBuffer buf;
        var ok = NativeMethods.DeviceIoControl(
            h,
            NativeMethods.FSCTL_GET_NTFS_VOLUME_DATA,
            IntPtr.Zero, 0,
            &buf, (uint)sizeof(NativeMethods.NtfsVolumeDataBuffer),
            out _,
            IntPtr.Zero);

        if (!ok)
        {
            var err = Marshal.GetLastWin32Error();
            throw new MftUnavailableException(MftUnavailableReason.FsControlFailed,
                $"FSCTL_GET_NTFS_VOLUME_DATA failed: {new Win32Exception(err).Message}");
        }

        return buf;
    }

    private static void Seek(SafeFileHandle h, long offset)
    {
        if (!NativeMethods.SetFilePointerEx(h, offset, out _, NativeMethods.FILE_BEGIN))
            throw new IOException($"SetFilePointerEx to {offset:X} failed.", Marshal.GetLastWin32Error());
    }

    private static unsafe int ReadBlock(SafeFileHandle h, byte[] buffer, int length)
    {
        uint read;
        bool ok;
        fixed (byte* p = buffer)
        {
            ok = NativeMethods.ReadFile(h, p, (uint)length, out read, IntPtr.Zero);
        }

        if (!ok)
            throw new IOException("ReadFile failed while streaming the MFT.", Marshal.GetLastWin32Error());
        return (int)read;
    }

    // ---- $MFT record #0 bootstrap -------------------------------------------

    private static List<(long Lcn, long ClusterCount)> ReadMftDataRuns(
        SafeFileHandle volume, long mftStartOffset, int recordSize, int sectorSize)
    {
        var record = new byte[recordSize];
        Seek(volume, mftStartOffset);
        var read = ReadBlock(volume, record, recordSize);
        if (read != recordSize)
            throw new MftUnavailableException(MftUnavailableReason.InvalidMft,
                "Short read on MFT record 0.");

        if (!NtfsLayout.ApplyFixups(record, sectorSize))
            throw new MftUnavailableException(MftUnavailableReason.InvalidMft,
                "MFT record 0 failed fixup validation.");

        ReadOnlySpan<byte> span = record;
        int attrPos = NtfsLayout.ReadU16(span, NtfsLayout.HdrAttrsOffset);
        var usedSize = (int)NtfsLayout.ReadU32(span, NtfsLayout.HdrUsedSize);

        var runs = new List<(long, long)>(64);
        var sawAttributeList = false;

        while (attrPos < usedSize)
        {
            if (!NtfsLayout.TryParseAttributeHeader(span, attrPos, out var attr)) break;

            if (attr.Type == NtfsLayout.AttrAttributeList)
                sawAttributeList = true;

            if (attr.Type == NtfsLayout.AttrData && attr.IsNonResident && attr.NameLength == 0)
            {
                // Data runs start at the offset stored in the non-resident header.
                int runsOffset = NtfsLayout.ReadU16(span, attrPos + 32);
                var runsLen = attr.TotalLength - runsOffset;
                if (runsLen > 0 && attrPos + runsOffset + runsLen <= span.Length)
                    NtfsLayout.DecodeDataRuns(span.Slice(attrPos + runsOffset, runsLen), runs);
                break;
            }

            attrPos += attr.TotalLength;
        }

        // On extreme volumes (tens of TB), $MFT's own data runs can overflow
        // record 0 and be pushed into extension records via $ATTRIBUTE_LIST. We
        // don't bootstrap that recursively — fall back rather than silently
        // producing a truncated MFT read.
        if (runs.Count == 0 && sawAttributeList)
            throw new MftUnavailableException(MftUnavailableReason.InvalidMft,
                "$MFT itself uses $ATTRIBUTE_LIST on this volume (unusually large/fragmented). " +
                "The MFT engine cannot bootstrap from record 0 alone in this case. " +
                "Use the Parallel engine for this volume.");

        return runs;
    }

    // ---- per-record parsing -------------------------------------------------

    private void ParseRecord(long frn, Span<byte> record, int sectorSize, MftVolumeIndex index)
    {
        if (!NtfsLayout.ApplyFixups(record, sectorSize))
        {
            index.CorruptRecords++;
            return;
        }

        ReadOnlySpan<byte> r = record;
        var flags = NtfsLayout.ReadU16(r, NtfsLayout.HdrFlags);
        if ((flags & NtfsLayout.FlagInUse) == 0) return;

        var baseRef = NtfsLayout.ReadU64(r, NtfsLayout.HdrBaseRef);

        // Extension records are just holders for overflowed attributes of another
        // base record. They have no identity of their own, but they CAN carry the
        // unnamed $DATA whose size we need. Harvest that and return.
        if (baseRef != 0)
        {
            ParseExtensionRecord(r, baseRef);
            return;
        }

        var isDirectory = (flags & NtfsLayout.FlagDirectory) != 0;
        int attrPos = NtfsLayout.ReadU16(r, NtfsLayout.HdrAttrsOffset);
        var usedSize = (int)NtfsLayout.ReadU32(r, NtfsLayout.HdrUsedSize);

        long lastModified = 0;
        byte bestNamespace = 255;
        var bestNameOffset = -1;
        var bestNameLength = 0;
        long bestNameParent = -1;
        long dataSize = 0;
        var seenUnnamedData = false;
        var hasAttributeList = false;

        while (attrPos < usedSize && attrPos + 16 <= r.Length)
        {
            if (!NtfsLayout.TryParseAttributeHeader(r, attrPos, out var attr)) break;

            switch (attr.Type)
            {
                case NtfsLayout.AttrStandardInformation:
                    if (!attr.IsNonResident && attr.ResidentValueLength >= 24)
                        // Offset 8 of $STANDARD_INFORMATION: last-modified FILETIME.
                        lastModified = NtfsLayout.ReadI64(r, attr.ResidentValueOffset + 8);
                    break;

                case NtfsLayout.AttrAttributeList:
                    hasAttributeList = true;
                    break;

                case NtfsLayout.AttrFileName:
                    if (!attr.IsNonResident && attr.ResidentValueLength >= 66)
                    {
                        var vOff = attr.ResidentValueOffset;
                        var parentRef = NtfsLayout.ReadU64(r, vOff);
                        var nameLen = r[vOff + 64];
                        var ns = r[vOff + 65];

                        var rank = NamespaceRank(ns);
                        var bestRank = bestNamespace == 255 ? int.MaxValue : NamespaceRank(bestNamespace);

                        if (rank < bestRank && vOff + 66 + nameLen * 2 <= r.Length)
                        {
                            bestNamespace = ns;
                            bestNameOffset = vOff + 66;
                            bestNameLength = nameLen;
                            bestNameParent = NtfsLayout.ExtractFrn(parentRef);
                        }
                    }

                    break;

                case NtfsLayout.AttrData:
                    // Unnamed $DATA is the primary file stream; named = ADS (ignored).
                    // If multiple unnamed $DATA extents appear (only possible with
                    // $ATTRIBUTE_LIST, typically in the same record), keep the one
                    // at StartingVCN=0 because that's where the authoritative size
                    // lives — otherwise we may capture a zero from a mid-stream
                    // extent header.
                    if (attr.NameLength == 0)
                    {
                        var thisSize = attr.IsNonResident ? attr.NonResidentRealSize : attr.ResidentValueLength;
                        var isPrimary = !attr.IsNonResident || attr.NonResidentStartingVcn == 0;
                        if (isPrimary || !seenUnnamedData)
                        {
                            dataSize = thisSize;
                            seenUnnamedData = true;
                        }
                    }

                    break;
            }

            attrPos += attr.TotalLength;
        }

        if (bestNameOffset < 0) return; // no usable name — orphan record, skip

        var nameOffset = index.AppendName(MemoryMarshal.Cast<byte, char>(r.Slice(bestNameOffset, bestNameLength * 2)));

        var i = (int)frn;
        index.ParentFrn[i] = bestNameParent;
        index.Size[i] = isDirectory ? 0 : dataSize;
        index.LastModifiedTicks[i] = lastModified;
        index.NameOffset[i] = nameOffset;
        index.NameLength[i] = bestNameLength;

        var f = MftVolumeIndex.FlagInUse;
        if (isDirectory) f |= MftVolumeIndex.FlagDirectory;
        if (hasAttributeList) f |= MftVolumeIndex.FlagHasAttributeList;
        if (seenUnnamedData) f |= MftVolumeIndex.FlagDataResolvedFromBase;
        index.Flags[i] = f;
    }

    // An extension record contributes nothing we track EXCEPT potentially the
    // unnamed $DATA size for its owning base record. We record the largest size
    // observed — for $DATA, every extent's non-resident header carries the same
    // authoritative size, but taking max is defensive against malformed mid-
    // stream extents that store 0.
    private void ParseExtensionRecord(ReadOnlySpan<byte> r, ulong baseRef)
    {
        var baseFrn = NtfsLayout.ExtractFrn(baseRef);
        if (baseFrn < 0) return;

        int attrPos = NtfsLayout.ReadU16(r, NtfsLayout.HdrAttrsOffset);
        var usedSize = (int)NtfsLayout.ReadU32(r, NtfsLayout.HdrUsedSize);

        while (attrPos < usedSize && attrPos + 16 <= r.Length)
        {
            if (!NtfsLayout.TryParseAttributeHeader(r, attrPos, out var attr)) break;

            if (attr.Type == NtfsLayout.AttrData && attr.NameLength == 0)
            {
                var size = attr.IsNonResident ? attr.NonResidentRealSize : attr.ResidentValueLength;
                if (size > 0)
                {
                    if (_extensionDataSizes.TryGetValue(baseFrn, out var existing))
                    {
                        if (size > existing) _extensionDataSizes[baseFrn] = size;
                    }
                    else
                    {
                        _extensionDataSizes[baseFrn] = size;
                    }
                }
            }

            attrPos += attr.TotalLength;
        }
    }

    private static int NamespaceRank(byte ns)
    {
        return ns switch
        {
            NtfsLayout.NsWin32AndDos => 0, // canonical long name
            NtfsLayout.NsWin32 => 1, // long name
            NtfsLayout.NsPosix => 2, // case-sensitive name (rare)
            NtfsLayout.NsDos => 3, // 8.3 short name — use only if nothing else
            _ => 4
        };
    }
}