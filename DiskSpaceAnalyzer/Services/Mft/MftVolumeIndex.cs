using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace DiskSpaceAnalyzer.Services.Mft;

// Flat, struct-of-arrays index of every in-use MFT record on a volume.
// Each parallel array is keyed by FRN (file reference number). Names are
// pooled into fixed-size segments (see below) so we never reallocate a
// multi-hundred-megabyte buffer mid-scan.
//
// Scale reference: a 2 TB volume with ~4M files uses ~120 MB of index
// (8+8+4+4+4+1 = 29 B/entry × 4M + ~120 MB names) — comfortable in memory
// and a fraction of what the full DirectoryItem tree would consume.
internal sealed class MftVolumeIndex
{
    // Name-pool segment size. 4 Mi chars = 8 MiB bytes — already on the LOH
    // (everything above ~85 KB is), but uniformly-sized so the LOH stays tidy
    // and the old "double-and-copy" peak (old buffer + new buffer resident at
    // the same time) goes away. A 10 M-file volume at avg 20 chars allocates
    // ~50 segments of 8 MiB each = 400 MiB total, no copies.
    private const int ChunkShift = 22;
    private const int ChunkSize = 1 << ChunkShift; // 4 194 304 chars
    private const int ChunkMask = ChunkSize - 1;

    public const byte FlagInUse = 0x01;
    public const byte FlagDirectory = 0x02;
    public const byte FlagHasAttributeList = 0x04;
    public const byte FlagDataResolvedFromBase = 0x08;

    private readonly List<char[]> _nameChunks = new();
    private int _cursor; // next free position in the virtual address space
    public byte[] Flags; // bits: see FlagXxx below
    public long[] LastModifiedTicks;
    public int[] NameLength;
    public int[] NameOffset; // absolute offset into the virtual name pool

    public long[] ParentFrn; // -1 for slots that are vacant/deleted
    public long[] Size; // $DATA real size (0 for directories)

    public MftVolumeIndex(int capacity, long volumeSerial, string volumeRoot)
    {
        Capacity = capacity;
        VolumeSerial = volumeSerial;
        VolumeRoot = volumeRoot;

        ParentFrn = new long[capacity];
        Size = new long[capacity];
        LastModifiedTicks = new long[capacity];
        NameOffset = new int[capacity];
        NameLength = new int[capacity];
        Flags = new byte[capacity];

        ParentFrn.AsSpan().Fill(-1);
    }

    public int Capacity { get; }
    public long VolumeSerial { get; }
    public string VolumeRoot { get; }

    // Counts populated by MftVolumeScanner; surfaced in ScanResult.Errors.
    public int AttributeListUnresolved { get; set; }
    public int CorruptRecords { get; set; }

    // Append a name to the pool and return its absolute offset.
    // If the name would straddle a chunk boundary we skip to the start of a
    // fresh chunk; the wasted tail is at most ~255 chars (NTFS filename limit),
    // a rounding error against a 4 Mi-char chunk.
    public int AppendName(ReadOnlySpan<char> chars)
    {
        Debug.Assert(chars.Length <= ChunkSize, "filename exceeds chunk size");

        var inChunk = _cursor & ChunkMask;
        var remaining = ChunkSize - inChunk;

        if (chars.Length > remaining)
        {
            _cursor += remaining; // skip tail; no entry references it
            inChunk = 0;
        }

        var offset = _cursor;
        var chunkIdx = offset >> ChunkShift;

        // Materialise chunks up to and including the one we're writing to. Handles
        // the first-ever append, landing exactly on a chunk boundary, and the skip
        // case above — all with one check.
        while (_nameChunks.Count <= chunkIdx)
            _nameChunks.Add(new char[ChunkSize]);

        chars.CopyTo(_nameChunks[chunkIdx].AsSpan(inChunk));
        _cursor += chars.Length;
        return offset;
    }

    public ReadOnlySpan<char> GetName(long frn)
    {
        var i = (int)frn;
        var offset = NameOffset[i];
        var length = NameLength[i];
        var chunkIdx = offset >> ChunkShift;
        var inChunk = offset & ChunkMask;
        return _nameChunks[chunkIdx].AsSpan(inChunk, length);
    }

    public string GetNameString(long frn)
    {
        var i = (int)frn;
        var offset = NameOffset[i];
        var length = NameLength[i];
        var chunkIdx = offset >> ChunkShift;
        var inChunk = offset & ChunkMask;
        return new string(_nameChunks[chunkIdx], inChunk, length);
    }

    public bool IsInUse(long frn)
    {
        return frn >= 0 && frn < Capacity && (Flags[(int)frn] & FlagInUse) != 0;
    }

    public bool IsDirectory(long frn)
    {
        return (Flags[(int)frn] & FlagDirectory) != 0;
    }
}