using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace DiskSpaceAnalyzer.Services.Mft;

// On-disk NTFS constants and stateless binary decoders for MFT records,
// attributes, and data runs. Kept as static helpers that operate on
// ReadOnlySpan<byte> so they allocate nothing during a scan.
internal static class NtfsLayout
{
    // FILE record signature ("FILE" in little-endian).
    public const uint FileSignature = 0x454C4946;

    // Record header layout (ntfs.h FILE_RECORD_SEGMENT_HEADER).
    public const int HdrFixupOffset = 4; // u16 offset to Update Sequence Array
    public const int HdrFixupCount = 6; // u16 count of USA entries (1 USN + N originals)
    public const int HdrFlags = 22; // u16 flags
    public const int HdrUsedSize = 24; // u32 bytes in use within this segment
    public const int HdrAttrsOffset = 20; // u16 offset of first attribute
    public const int HdrBaseRef = 32; // u64 base file-record reference (0 if this IS the base)
    public const int HdrRecordNumber = 44; // u32 (NT5.1+) this record's own number

    public const ushort FlagInUse = 0x0001;
    public const ushort FlagDirectory = 0x0002;

    // Attribute type markers.
    public const uint AttrStandardInformation = 0x10;
    public const uint AttrAttributeList = 0x20;
    public const uint AttrFileName = 0x30;
    public const uint AttrData = 0x80;
    public const uint AttrEnd = 0xFFFFFFFF;

    // Filename namespaces (prefer WIN32 / WIN32&DOS, avoid DOS-only short names).
    public const byte NsPosix = 0;
    public const byte NsWin32 = 1;
    public const byte NsDos = 2;
    public const byte NsWin32AndDos = 3;

    // Per-sector fixup: last 2 bytes of each sector in an MFT record are overwritten
    // with the USN for torn-write detection. Restore them from the Update Sequence
    // Array before parsing attributes.
    public static bool ApplyFixups(Span<byte> record, int bytesPerSector)
    {
        if (record.Length < 8) return false;
        if (BinaryPrimitives.ReadUInt32LittleEndian(record) != FileSignature) return false;

        int usaOffset = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(HdrFixupOffset));
        int usaCount = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(HdrFixupCount));
        if (usaCount < 2) return false;

        var sectorCount = usaCount - 1;
        if (sectorCount * bytesPerSector > record.Length) return false;
        if (usaOffset + usaCount * 2 > record.Length) return false;

        // The USN occupies the first 2 bytes of the USA; the remaining entries are
        // the original sector tail values we must copy back into place.
        var expectedUsn = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(usaOffset));
        for (var i = 0; i < sectorCount; i++)
        {
            var tailPos = (i + 1) * bytesPerSector - 2;
            var usaPos = usaOffset + 2 + i * 2;

            var actual = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(tailPos));
            if (actual != expectedUsn) return false; // torn write — record is corrupt

            var original = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(usaPos));
            BinaryPrimitives.WriteUInt16LittleEndian(record.Slice(tailPos), original);
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long ReadI64(ReadOnlySpan<byte> s, int o)
    {
        return BinaryPrimitives.ReadInt64LittleEndian(s.Slice(o));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ReadU64(ReadOnlySpan<byte> s, int o)
    {
        return BinaryPrimitives.ReadUInt64LittleEndian(s.Slice(o));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ReadU32(ReadOnlySpan<byte> s, int o)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(s.Slice(o));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort ReadU16(ReadOnlySpan<byte> s, int o)
    {
        return BinaryPrimitives.ReadUInt16LittleEndian(s.Slice(o));
    }

    // Low 48 bits of a file reference are the MFT record number; the top 16 bits
    // hold the sequence number (we discard it — we only use FRN to key records).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long ExtractFrn(ulong reference)
    {
        return (long)(reference & 0x0000FFFFFFFFFFFFUL);
    }

    // Decodes NTFS data runs: a stream of compact (length, offset) pairs whose
    // LCNs are cumulative. Terminates on a zero header byte or end of span.
    // Caller supplies the list to append into so we reuse the caller's capacity.
    public static void DecodeDataRuns(ReadOnlySpan<byte> runs, List<(long Lcn, long ClusterCount)> dst)
    {
        long currentLcn = 0;
        var pos = 0;

        while (pos < runs.Length)
        {
            var header = runs[pos++];
            if (header == 0) break;

            var lengthBytes = header & 0x0F;
            var offsetBytes = (header >> 4) & 0x0F;
            if (lengthBytes == 0 || pos + lengthBytes + offsetBytes > runs.Length) break;

            var length = ReadSignedVarLen(runs, pos, lengthBytes);
            pos += lengthBytes;

            long offset = 0;
            var sparse = offsetBytes == 0;
            if (!sparse)
            {
                offset = ReadSignedVarLen(runs, pos, offsetBytes);
                pos += offsetBytes;
            }

            if (sparse)
                // Sparse run — advance VCN only, no actual cluster to read. We skip it
                // because sparse regions don't exist on disk (important for $MFT's own
                // runs, though in practice $MFT is never sparse).
                continue;

            currentLcn += offset;
            if (length > 0 && currentLcn >= 0)
                dst.Add((currentLcn, length));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long ReadSignedVarLen(ReadOnlySpan<byte> buf, int pos, int n)
    {
        // Little-endian, signed: the top bit of the most-significant byte is the sign.
        long v = 0;
        for (var i = 0; i < n; i++)
            v |= (long)buf[pos + i] << (i * 8);

        // Sign-extend.
        var shift = (8 - n) * 8;
        if (shift > 0) v = (v << shift) >> shift;
        return v;
    }

    public static bool TryParseAttributeHeader(ReadOnlySpan<byte> record, int pos, out AttributeView view)
    {
        view = default;
        if (pos + 16 > record.Length) return false;

        var type = ReadU32(record, pos);
        if (type == AttrEnd) return false;

        var length = ReadU32(record, pos + 4);
        if (length == 0 || pos + length > record.Length) return false;

        var nonResident = record[pos + 8];
        var nameLength = record[pos + 9];

        view.Type = type;
        view.TotalLength = (int)length;
        view.IsNonResident = nonResident != 0;
        view.NameLength = nameLength;

        if (!view.IsNonResident)
        {
            if (pos + 24 > record.Length) return false;
            var valLen = ReadU32(record, pos + 16);
            var valOff = ReadU16(record, pos + 20);
            if (pos + valOff + valLen > record.Length) return false;
            view.ResidentValueOffset = pos + valOff;
            view.ResidentValueLength = (int)valLen;
            view.NonResidentRealSize = 0;
        }
        else
        {
            if (pos + 56 > record.Length) return false;
            view.ResidentValueOffset = 0;
            view.ResidentValueLength = 0;
            view.NonResidentStartingVcn = ReadI64(record, pos + 16);
            view.NonResidentRealSize = ReadI64(record, pos + 48);
        }

        return true;
    }

    public struct AttributeView
    {
        public uint Type;
        public int TotalLength;
        public bool IsNonResident;
        public byte NameLength;
        public int ResidentValueOffset;
        public int ResidentValueLength;
        public long NonResidentStartingVcn;
        public long NonResidentRealSize;
    }
}