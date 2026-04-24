using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DiskSpaceAnalyzer.Models;

namespace DiskSpaceAnalyzer.Services.Mft;

// IFileSystemService implementation backed by a single raw read of the NTFS
// Master File Table. Orders of magnitude faster than FindFirstFile-based
// traversal on large volumes, at the cost of requiring:
//   * admin privileges (raw device access)
//   * NTFS-formatted local volume
//   * whole-volume read even when scanning a subfolder (still faster in practice)
//
// Semantic note vs. FileSystemService / ParallelFileSystemService:
// this engine enumerates MFT records, not tree edges. Reparse points (junctions,
// directory symlinks, mount points) therefore appear as empty directories — we
// count each real file exactly once in its canonical home rather than following
// links. This is usually the more accurate answer for "how much space does this
// volume use" (no double-counting via junctions to the same volume), but it is a
// behaviour delta from the API-based engines.
public sealed class MftFileSystemService : IFileSystemService
{
    private const long RootDirFrn = 5; // fixed by the NTFS spec

    public bool TrackIndividualFiles { get; set; } = true;

    public Task<ScanResult> ScanDirectoryAsync(
        string path,
        ScanMode mode,
        IProgress<ScanProgress> progress,
        CancellationToken cancellationToken)
    {
        // All of this is CPU/IO-bound work; wrap once in Task.Run so the VM can
        // stay on the UI thread without blocking.
        return Task.Run(() => ScanCore(path, mode, progress, cancellationToken), cancellationToken);
    }

    // ---- IFileSystemService surface -----------------------------------------

    public IEnumerable<string> GetDrives()
    {
        foreach (var d in DriveInfo.GetDrives())
            if (d.IsReady)
                yield return d.RootDirectory.FullName;
    }

    public bool DirectoryExists(string path)
    {
        return Directory.Exists(path);
    }

    public DirectoryItem GetDirectoryInfo(string path)
    {
        var info = new DirectoryInfo(path);
        return new DirectoryItem
        {
            Name = info.Name,
            FullPath = info.FullName,
            LastModified = info.LastWriteTime,
            IsDirectory = true
        };
    }

    private ScanResult ScanCore(string path, ScanMode mode, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        PreflightChecks(path);

        var started = DateTime.UtcNow;

        var volumeRoot = Path.GetPathRoot(path)!;
        var scanner = new MftVolumeScanner(volumeRoot, progress, ct);
        var index = scanner.Scan();

        ct.ThrowIfCancellationRequested();

        progress?.Report(new ScanProgress { CurrentPath = $"Read {index.Capacity:N0} MFT records; building tree…" });

        var childrenByParent = BuildChildrenMap(index);

        // Resolve the requested path to an FRN by walking down from root.
        var rootFrn = ResolvePathToFrn(path, volumeRoot, index, childrenByParent)
                      ?? throw new DirectoryNotFoundException(
                          $"MFT scan did not find '{path}' on {volumeRoot}.");

        long totalFiles = 0, totalDirs = 0;
        var errors = new List<string>();

        var root = BuildRoot(
            rootFrn, path, mode,
            index, childrenByParent,
            TrackIndividualFiles,
            ref totalFiles, ref totalDirs,
            ct);

        AssignPercentages(root);

        if (index.AttributeListUnresolved > 0)
            errors.Add($"{index.AttributeListUnresolved:N0} files with $ATTRIBUTE_LIST fragmentation " +
                       "could not be resolved from extension records; their sizes are reported as 0. " +
                       "This is extremely rare (usually pagefile, hibernation, or very large " +
                       "heavily-fragmented files). Use the Parallel engine for exact sizes on these.");

        if (index.CorruptRecords > 0)
            errors.Add($"{index.CorruptRecords:N0} MFT records failed fixup validation and were skipped.");

        return new ScanResult
        {
            RootDirectory = root,
            TotalSize = root.Size,
            TotalFiles = totalFiles,
            TotalDirectories = totalDirs,
            ScanDuration = DateTime.UtcNow - started,
            ErrorCount = errors.Count,
            Errors = errors
        };
    }

    // ---- preflight ----------------------------------------------------------

    private static void PreflightChecks(string path)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Directory does not exist: {path}");

        if (!MftElevationHelper.IsNtfsVolume(path))
            throw new MftUnavailableException(MftUnavailableReason.NotNtfs,
                $"'{path}' is not on an NTFS volume. MFT scanning requires local NTFS.");

        if (!MftElevationHelper.IsElevated())
            throw new MftUnavailableException(MftUnavailableReason.NotElevated,
                "MFT scanning requires administrator privileges.");

        var root = Path.GetPathRoot(path) ?? string.Empty;
        if (root.Length < 2 || root[1] != ':')
            throw new MftUnavailableException(MftUnavailableReason.UnsupportedPath,
                $"MFT scanning requires a drive-letter path (got '{path}').");
    }

    // ---- tree construction --------------------------------------------------

    // Build reverse-lookup: parent-FRN → list of child FRNs.
    // Uses flat lists so allocation is proportional to child count.
    private static List<int>?[] BuildChildrenMap(MftVolumeIndex index)
    {
        var map = new List<int>?[index.Capacity];
        for (var i = 0; i < index.Capacity; i++)
        {
            var flags = index.Flags[i];
            if ((flags & MftVolumeIndex.FlagInUse) == 0) continue;

            var parent = index.ParentFrn[i];
            if (parent < 0 || parent >= index.Capacity) continue;
            if (parent == i) continue; // root self-reference

            var list = map[parent] ??= new List<int>(4);
            list.Add(i);
        }

        return map;
    }

    private static long? ResolvePathToFrn(string path, string volumeRoot,
        MftVolumeIndex index, List<int>?[] childrenByParent)
    {
        var normalizedRoot = volumeRoot.TrimEnd('\\', '/');
        var normalizedPath = path.TrimEnd('\\', '/');

        // Exact-volume scan → root directory itself.
        if (normalizedPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            return RootDirFrn;

        var relative = normalizedPath
            .Substring(normalizedRoot.Length)
            .TrimStart('\\', '/');

        var current = RootDirFrn;
        foreach (var segment in relative.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            var kids = childrenByParent[(int)current];
            if (kids == null) return null;

            long? next = null;
            foreach (var kid in kids)
            {
                if (!index.IsDirectory(kid)) continue;
                if (index.GetName(kid).Equals(segment.AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    next = kid;
                    break;
                }
            }

            if (next == null) return null;
            current = next.Value;
        }

        return current;
    }

    // Drives tree construction. In Recursive mode we split work across the root's
    // top-level subdirectories and build each one in parallel: their subtrees are
    // disjoint, so each worker can allocate DirectoryItem/FileItem objects into
    // its own sub-tree with no synchronisation beyond the final root merge.
    // TopLevel mode uses a single iterative aggregation per top-level child.
    private static DirectoryItem BuildRoot(
        long rootFrn,
        string rootPath,
        ScanMode mode,
        MftVolumeIndex index,
        List<int>?[] childrenByParent,
        bool trackFiles,
        ref long totalFiles,
        ref long totalDirs,
        CancellationToken ct)
    {
        // Match ParallelFileSystemService's convention: root's display name is the
        // leaf directory name, not the full path. GetFileName returns "" for a
        // drive-letter root (e.g. "C:\"), so fall back to the path in that case.
        var rootName = Path.GetFileName(rootPath.TrimEnd('\\', '/'));
        if (string.IsNullOrEmpty(rootName)) rootName = rootPath;

        var root = NewDirectoryItem(rootFrn, rootName, rootPath, null, index);
        totalDirs++;

        var kids = childrenByParent[(int)rootFrn];
        if (kids == null)
        {
            root.FileCount = 0;
            root.DirectoryCount = 0;
            return root;
        }

        // Split root's direct children by kind so we can pre-size the root's lists
        // correctly and hand only the directory slice to the parallel loop.
        var dirChildren = new List<int>(kids.Count);
        var fileChildren = new List<int>(kids.Count);
        foreach (var k in kids)
            (index.IsDirectory(k) ? dirChildren : fileChildren).Add(k);

        root.Children = new List<DirectoryItem>(dirChildren.Count);
        root.Files = new List<FileItem>(trackFiles ? fileChildren.Count : 0);

        // --- root's direct files ---
        long rootFilesSize = 0;
        foreach (var f in fileChildren)
        {
            var fs = index.Size[f];
            rootFilesSize += fs;
            if (trackFiles)
            {
                var fname = index.GetNameString(f);
                root.Files.Add(new FileItem
                {
                    Name = fname,
                    FullPath = CombinePath(rootPath, fname),
                    Size = fs,
                    LastModified = TicksToDate(index.LastModifiedTicks[f]),
                    Extension = Path.GetExtension(fname)
                });
            }
        }

        totalFiles += fileChildren.Count;

        // --- root's direct subdirectories — parallel ---
        // One iteration processes an ENTIRE subtree (tens to millions of nodes),
        // so the aggregation hit of touching shared counters at the end of each
        // iteration is negligible compared with the work inside BuildSubtreeIterative.
        var subtrees = new DirectoryItem[dirChildren.Count];
        long subSizeAgg = 0;

        var parallelism = Math.Max(1, Environment.ProcessorCount);
        var po = new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = parallelism };

        var filesRef = totalFiles;
        var dirsRef = totalDirs;

        Parallel.For(0, dirChildren.Count, po, i =>
        {
            var childFrn = dirChildren[i];
            var childName = index.GetNameString(childFrn);
            var childPath = CombinePath(rootPath, childName);
            long localFiles = 0, localDirs = 0, localSize;

            if (mode == ScanMode.Recursive)
            {
                var sub = BuildSubtreeIterative(childFrn, childName, childPath, root, index, childrenByParent,
                    trackFiles, ref localFiles, ref localDirs, ct);
                subtrees[i] = sub;
                localSize = sub.Size;
            }
            else
            {
                long s = 0, f = 0, d = 0;
                AccumulateSubtree(childFrn, index, childrenByParent, ref s, ref f, ref d, ct);

                subtrees[i] = new DirectoryItem
                {
                    Name = childName,
                    FullPath = childPath,
                    IsDirectory = true,
                    Size = s,
                    FileCount = f,
                    DirectoryCount = d,
                    LastModified = TicksToDate(index.LastModifiedTicks[childFrn]),
                    Parent = root
                };
                localSize = s;
                localFiles = f;
                localDirs = d + 1;
            }

            Interlocked.Add(ref filesRef, localFiles);
            Interlocked.Add(ref dirsRef, localDirs);
            Interlocked.Add(ref subSizeAgg, localSize);
        });

        foreach (var st in subtrees) root.Children.Add(st);

        totalFiles = filesRef;
        totalDirs = dirsRef;

        root.FileCount = fileChildren.Count;
        root.DirectoryCount = dirChildren.Count;
        root.Size = rootFilesSize + subSizeAgg;
        return root;
    }

    // Iterative two-phase tree build for one subtree rooted at `rootFrn`.
    //
    //   Phase 1 (pre-order via explicit stack): create every DirectoryItem and
    //   attach files directly. We record items in discovery order so phase 2 can
    //   walk them in reverse — that gives us a post-order traversal without
    //   needing a separate marking scheme.
    //
    //   Phase 2: iterate discovered items from deepest to shallowest, summing
    //   each child's Size into its parent. Because items discovered later are
    //   always at least as deep as earlier ones in DFS pre-order, the reverse
    //   pass is a valid bottom-up accumulation.
    //
    // Lists inside each DirectoryItem are pre-sized from the known child counts,
    // which avoids the repeated reallocations the default List<> growth causes
    // on fan-out directories (e.g. node_modules, C:\Windows\WinSxS).
    private static DirectoryItem BuildSubtreeIterative(
        long rootFrn,
        string rootName,
        string rootFullPath,
        DirectoryItem? parent,
        MftVolumeIndex index,
        List<int>?[] childrenByParent,
        bool trackFiles,
        ref long totalFiles,
        ref long totalDirs,
        CancellationToken ct)
    {
        var rootItem = NewDirectoryItem(rootFrn, rootName, rootFullPath, parent, index);

        // Capacity hint: subtrees routinely contain thousands of directories;
        // start with a modest pre-allocation and let the list grow if needed.
        var allDirs = new List<DirectoryItem>(64);
        var workStack = new Stack<(long Frn, DirectoryItem Item)>(64);
        workStack.Push((rootFrn, rootItem));

        long localFiles = 0;
        long localDirs = 1; // counts rootItem

        // Reuse these lists across iterations to avoid allocating a pair per dir.
        var dirKids = new List<int>(16);
        var fileKids = new List<int>(16);

        while (workStack.Count > 0)
        {
            // Periodic cancel check — this loop can run for seconds on huge volumes.
            if ((allDirs.Count & 0x3FF) == 0) ct.ThrowIfCancellationRequested();

            var (frn, item) = workStack.Pop();
            allDirs.Add(item);

            var kids = childrenByParent[(int)frn];
            if (kids == null)
            {
                item.FileCount = 0;
                item.DirectoryCount = 0;
                continue;
            }

            dirKids.Clear();
            fileKids.Clear();
            foreach (var k in kids)
                (index.IsDirectory(k) ? dirKids : fileKids).Add(k);

            item.Children = new List<DirectoryItem>(dirKids.Count);
            item.Files = new List<FileItem>(trackFiles ? fileKids.Count : 0);
            item.FileCount = fileKids.Count;
            item.DirectoryCount = dirKids.Count;

            long localFileSize = 0;
            foreach (var f in fileKids)
            {
                var sz = index.Size[f];
                localFileSize += sz;
                if (trackFiles)
                {
                    var fname = index.GetNameString(f);
                    item.Files.Add(new FileItem
                    {
                        Name = fname,
                        FullPath = CombinePath(item.FullPath, fname),
                        Size = sz,
                        LastModified = TicksToDate(index.LastModifiedTicks[f]),
                        Extension = Path.GetExtension(fname)
                    });
                }
            }

            localFiles += fileKids.Count;
            item.Size = localFileSize; // child-dir sizes added in phase 2

            foreach (var d in dirKids)
            {
                var dname = index.GetNameString(d);
                var dirItem = NewDirectoryItem(d, dname, CombinePath(item.FullPath, dname), item, index);
                item.Children.Add(dirItem);
                workStack.Push((d, dirItem));
                localDirs++;
            }
        }

        // Phase 2 — bottom-up size aggregation. Items at the end of the list are
        // deepest; summing each item's children into its own Size in reverse
        // order guarantees every child's Size is already finalised.
        for (var i = allDirs.Count - 1; i >= 0; i--)
        {
            var d = allDirs[i];
            var sum = d.Size;
            var kidList = d.Children;
            for (var k = 0; k < kidList.Count; k++) sum += kidList[k].Size;
            d.Size = sum;
        }

        Interlocked.Add(ref totalFiles, localFiles);
        Interlocked.Add(ref totalDirs, localDirs);
        return rootItem;
    }

    private static DirectoryItem NewDirectoryItem(
        long frn, string name, string fullPath, DirectoryItem? parent, MftVolumeIndex index)
    {
        return new DirectoryItem
        {
            Name = name,
            FullPath = fullPath,
            IsDirectory = true,
            LastModified = TicksToDate(index.LastModifiedTicks[(int)frn]),
            Parent = parent
        };
    }

    private static void AccumulateSubtree(
        long frn, MftVolumeIndex index, List<int>?[] childrenByParent,
        ref long sizeOut, ref long fileOut, ref long dirOut,
        CancellationToken ct)
    {
        // Iterative DFS so huge trees can't overflow the stack.
        var stack = new Stack<long>(64);
        stack.Push(frn);

        var checkCounter = 0;
        while (stack.Count > 0)
        {
            if ((++checkCounter & 0xFFF) == 0) ct.ThrowIfCancellationRequested();
            var cur = stack.Pop();
            var kids = childrenByParent[(int)cur];
            if (kids == null) continue;

            foreach (var k in kids)
                if (index.IsDirectory(k))
                {
                    dirOut++;
                    stack.Push(k);
                }
                else
                {
                    fileOut++;
                    sizeOut += index.Size[k];
                }
        }
    }

    private static void AssignPercentages(DirectoryItem root)
    {
        if (root.Size <= 0) return;

        var stack = new Stack<DirectoryItem>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var d = stack.Pop();
            if (d.Size <= 0) continue;
            double total = d.Size;
            foreach (var c in d.Children)
            {
                c.PercentageOfParent = c.Size / total * 100.0;
                stack.Push(c);
            }

            foreach (var f in d.Files)
                f.PercentageOfParent = f.Size / total * 100.0;
        }
    }

    // ---- helpers ------------------------------------------------------------

    private static string CombinePath(string parent, string name)
    {
        if (parent.EndsWith('\\') || parent.EndsWith('/')) return parent + name;
        return parent + Path.DirectorySeparatorChar + name;
    }

    private static DateTime TicksToDate(long filetime)
    {
        if (filetime <= 0) return DateTime.MinValue;
        try
        {
            return DateTime.FromFileTimeUtc(filetime).ToLocalTime();
        }
        catch
        {
            return DateTime.MinValue;
        }
    }
}