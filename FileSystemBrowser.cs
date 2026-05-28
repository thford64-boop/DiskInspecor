// ============================================================
// FileSystemBrowser.cs – Read-only filesystem enumeration.
// Supports hidden, system, reparse points, junctions, symlinks.
// Includes background recursive indexer for Everything-style search.
// ALL OPERATIONS ARE READ-ONLY.
// ============================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DiskInspector.Models;

namespace DiskInspector.Core
{
    public static class FileSystemBrowser
    {
        // ---- Directory listing ----

        public static List<FileSystemEntry> GetEntries(
            string path,
            bool showHidden = true,
            bool showSystem = true)
        {
            var entries = new List<FileSystemEntry>();

            var enumOptions = new EnumerationOptions
            {
                AttributesToSkip      = 0,
                IgnoreInaccessible    = true,
                ReturnSpecialDirectories = false,
                RecurseSubdirectories = false,
            };

            // Directories
            try
            {
                foreach (string dirPath in Directory.EnumerateDirectories(path, "*", enumOptions))
                {
                    var e = BuildEntry(dirPath, true);
                    if (e != null) entries.Add(e);
                }
            }
            catch (UnauthorizedAccessException)
            {
                entries.Add(MakeAccessDeniedEntry(path, true));
            }
            catch (Exception ex)
            {
                entries.Add(MakeErrorEntry(path, ex.Message, true));
            }

            // Files
            try
            {
                foreach (string filePath in Directory.EnumerateFiles(path, "*", enumOptions))
                {
                    var e = BuildEntry(filePath, false);
                    if (e != null) entries.Add(e);
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (Exception ex)
            {
                entries.Add(MakeErrorEntry(path, ex.Message, false));
            }

            if (!showHidden || !showSystem)
                entries.RemoveAll(e =>
                    (!showHidden && e.IsHidden) || (!showSystem && e.IsSystem));

            return entries;
        }

        private static FileSystemEntry? BuildEntry(string fullPath, bool isDirectory)
        {
            try
            {
                FileSystemInfo fsi = isDirectory
                    ? new DirectoryInfo(fullPath)
                    : new FileInfo(fullPath);

                if (!fsi.Exists) return null;

                var entry = new FileSystemEntry
                {
                    Name           = fsi.Name,
                    FullPath       = fsi.FullName,
                    IsDirectory    = isDirectory,
                    Attributes     = fsi.Attributes,
                    CreationTime   = SafeTime(fsi, t => t.CreationTime),
                    LastWriteTime  = SafeTime(fsi, t => t.LastWriteTime),
                    LastAccessTime = SafeTime(fsi, t => t.LastAccessTime),
                    AttributeString = BuildAttributeString(fsi.Attributes),
                };

                if (!isDirectory && fsi is FileInfo fi)
                    try { entry.Size = fi.Length; } catch { entry.Size = -1; }

                if ((fsi.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    entry.IsSymLink = false;
                    entry.IsJunction = false;
                    DetectReparseType(fullPath, entry);
                }

                return entry;
            }
            catch (UnauthorizedAccessException)
            {
                return new FileSystemEntry
                {
                    Name = Path.GetFileName(fullPath), FullPath = fullPath,
                    IsDirectory = isDirectory, AttributeString = "[ACCESS DENIED]",
                    Attributes = isDirectory ? FileAttributes.Directory : 0,
                };
            }
            catch { return null; }
        }

        private static void DetectReparseType(string path, FileSystemEntry entry)
        {
            try
            {
                IntPtr h = NativeMethods.CreateFile(path,
                    NativeMethods.GENERIC_READ,
                    NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                    IntPtr.Zero, NativeMethods.OPEN_EXISTING,
                    NativeMethods.FILE_FLAG_BACKUP_SEMANTICS | NativeMethods.FILE_FLAG_OPEN_REPARSE_POINT,
                    IntPtr.Zero);

                if (h == NativeMethods.INVALID_HANDLE_VALUE) return;
                try
                {
                    byte[] buf = new byte[16 * 1024];
                    bool ok = NativeMethods.DeviceIoControl(h, NativeMethods.FSCTL_GET_REPARSE_POINT,
                        null, 0, buf, (uint)buf.Length, out uint returned, IntPtr.Zero);

                    if (ok && returned > 8)
                    {
                        uint tag = BitConverter.ToUInt32(buf, 0);
                        if (tag == 0xA000000C) { entry.IsSymLink  = true; entry.LinkTarget = ReadPrintName(buf, 20, 12, 14); }
                        else if (tag == 0xA0000003) { entry.IsJunction = true; entry.LinkTarget = ReadPrintName(buf, 16, 12, 14); }
                    }
                }
                finally { NativeMethods.CloseHandle(h); }
            }
            catch { }
        }

        private static string? ReadPrintName(byte[] buf, int baseOff, int offOff, int lenOff)
        {
            try
            {
                ushort off = BitConverter.ToUInt16(buf, offOff);
                ushort len = BitConverter.ToUInt16(buf, lenOff);
                if (baseOff + off + len > buf.Length) return null;
                return System.Text.Encoding.Unicode.GetString(buf, baseOff + off, len);
            }
            catch { return null; }
        }

        public static string BuildAttributeString(FileAttributes attr)
        {
            var sb = new System.Text.StringBuilder();
            if ((attr & FileAttributes.Directory)        != 0) sb.Append('D');
            if ((attr & FileAttributes.ReadOnly)         != 0) sb.Append('R');
            if ((attr & FileAttributes.Hidden)           != 0) sb.Append('H');
            if ((attr & FileAttributes.System)           != 0) sb.Append('S');
            if ((attr & FileAttributes.Archive)          != 0) sb.Append('A');
            if ((attr & FileAttributes.Compressed)       != 0) sb.Append('C');
            if ((attr & FileAttributes.Encrypted)        != 0) sb.Append('E');
            if ((attr & FileAttributes.ReparsePoint)     != 0) sb.Append('L');
            if ((attr & FileAttributes.SparseFile)       != 0) sb.Append('P');
            if ((attr & FileAttributes.Temporary)        != 0) sb.Append('T');
            if ((attr & FileAttributes.Offline)          != 0) sb.Append('O');
            if ((attr & FileAttributes.NotContentIndexed)!= 0) sb.Append('I');
            return sb.Length == 0 ? "N" : sb.ToString();
        }

        private static FileSystemEntry MakeAccessDeniedEntry(string path, bool isDir) =>
            new() { Name = Path.GetFileName(path), FullPath = path,
                    IsDirectory = isDir, AttributeString = "[ACCESS DENIED]" };

        private static FileSystemEntry MakeErrorEntry(string path, string msg, bool isDir) =>
            new() { Name = Path.GetFileName(path), FullPath = path,
                    IsDirectory = isDir, AttributeString = $"[ERROR: {msg}]" };

        private static DateTime SafeTime(FileSystemInfo fsi, Func<FileSystemInfo, DateTime> fn)
        { try { return fn(fsi); } catch { return DateTime.MinValue; } }

        public static bool IsRootPath(string path) =>
            path.Length <= 3 && path.Length >= 2 && path[1] == ':';

        public static int GetIconIndex(FileSystemEntry entry)
        {
            if (entry.IsDirectory)
            {
                if (entry.IsJunction) return 4;
                if (entry.IsSymLink)  return 5;
                return 1;
            }
            if (entry.IsSymLink)      return 5;
            if (entry.IsReparsePoint) return 4;
            return 2;
        }

        // ================================================================
        //  FAST INDEXER — FindFirstFileEx + FIND_FIRST_EX_LARGE_FETCH
        //  This is the same Win32 technique used by Everything.exe.
        //  LARGE_FETCH tells the kernel to buffer thousands of directory
        //  entries per round-trip instead of one at a time, giving a
        //  massive speedup on both SSDs and HDDs.
        // ================================================================

        // P/Invoke for FindFirstFileEx / FindNextFile
        [System.Runtime.InteropServices.StructLayout(
            System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private struct WIN32_FIND_DATAW
        {
            public uint    dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
            public uint    nFileSizeHigh;
            public uint    nFileSizeLow;
            public uint    dwReserved0;
            public uint    dwReserved1;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 260)]
            public string  cFileName;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 14)]
            public string  cAlternateFileName;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr FindFirstFileExW(
            string lpFileName,
            int fInfoLevelId,           // FindExInfoBasic = 1 (skip short name = faster)
            out WIN32_FIND_DATAW lpFindFileData,
            int fSearchOp,              // FindExSearchNameMatch = 0
            IntPtr lpSearchFilter,
            uint dwAdditionalFlags);    // FIND_FIRST_EX_LARGE_FETCH = 2

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern bool FindNextFileW(IntPtr hFindFile, out WIN32_FIND_DATAW lpFindFileData);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FindClose(IntPtr hFindFile);

        private static readonly IntPtr INVALID_HANDLE = new(-1);
        private const uint FIND_FIRST_EX_LARGE_FETCH = 2;
        private const uint FILE_ATTRIBUTE_DIRECTORY   = 0x10;
        private const uint FILE_ATTRIBUTE_HIDDEN      = 0x02;
        private const uint FILE_ATTRIBUTE_SYSTEM      = 0x04;
        private const uint FILE_ATTRIBUTE_REPARSE     = 0x400;

        private static readonly ConcurrentBag<SearchResult> _index = new();
        private static volatile bool _indexing = false;
        private static CancellationTokenSource? _indexCts;

        public static int IndexedCount => _index.Count;
        public static bool IsIndexing  => _indexing;

        public static event Action<int>?    IndexProgressChanged;
        public static event Action<string>? IndexStatusChanged;
        public static event Action?         IndexCompleted;

        public static void StartIndexing(bool includeHidden = true, bool includeSystem = true)
        {
            if (_indexing) return;
            _indexCts?.Cancel();
            _indexCts = new CancellationTokenSource();
            var token = _indexCts.Token;

            // Swap out index: replace with a fresh one
            while (_index.TryTake(out _)) { }
            _indexing = true;

            Task.Run(() =>
            {
                try
                {
                    var queue = new ConcurrentQueue<string>();
                    foreach (string drive in Directory.GetLogicalDrives())
                    {
                        uint dt = NativeMethods.GetDriveType(drive);
                        if (dt == NativeMethods.DRIVE_CDROM || dt == NativeMethods.DRIVE_REMOTE) continue;
                        queue.Enqueue(drive.TrimEnd('\\'));
                    }

                    // Worker count: IO-heavy, so use more threads than CPU cores
                    int workers = Math.Max(4, Math.Min(Environment.ProcessorCount * 2, 32));
                    int activeWorkers = workers;
                    int totalCount = 0;

                    var workerTasks = new Task[workers];
                    for (int w = 0; w < workers; w++)
                    {
                        workerTasks[w] = Task.Run(() =>
                        {
                            while (!token.IsCancellationRequested)
                            {
                                if (!queue.TryDequeue(out string? dir))
                                {
                                    // Wait a moment for other workers to enqueue more dirs
                                    Thread.Sleep(2);
                                    if (queue.IsEmpty && Volatile.Read(ref activeWorkers) <= 1) break;
                                    continue;
                                }

                                // Core: call FindFirstFileExW with LARGE_FETCH flag.
                                // This is the key — the kernel batches thousands of entries
                                // per call instead of context-switching for each one.
                                string pattern = dir + "\\*";
                                IntPtr h = FindFirstFileExW(
                                    pattern,
                                    1,      // FindExInfoBasic — skips 8.3 short name (faster)
                                    out WIN32_FIND_DATAW data,
                                    0,      // FindExSearchNameMatch
                                    IntPtr.Zero,
                                    FIND_FIRST_EX_LARGE_FETCH);

                                if (h == INVALID_HANDLE) continue;

                                try
                                {
                                    do
                                    {
                                        if (token.IsCancellationRequested) return;

                                        string name = data.cFileName;
                                        if (name == "." || name == "..") continue;

                                        uint attrs = data.dwFileAttributes;
                                        if (!includeHidden && (attrs & FILE_ATTRIBUTE_HIDDEN) != 0) continue;
                                        if (!includeSystem && (attrs & FILE_ATTRIBUTE_SYSTEM) != 0) continue;

                                        bool isDir = (attrs & FILE_ATTRIBUTE_DIRECTORY) != 0;
                                        string fullPath = dir + "\\" + name;

                                        long size = 0;
                                        string ext = "";
                                        if (!isDir)
                                        {
                                            size = ((long)data.nFileSizeHigh << 32) | data.nFileSizeLow;
                                            int dot = name.LastIndexOf('.');
                                            ext = dot >= 0 ? name[dot..].ToLowerInvariant() : "";
                                        }

                                        DateTime lastWrite = DateTime.FromFileTimeUtc(
                                            ((long)data.ftLastWriteTime.dwHighDateTime << 32) |
                                            (uint)data.ftLastWriteTime.dwLowDateTime).ToLocalTime();

                                        _index.Add(new SearchResult
                                        {
                                            Name            = name,
                                            FullPath        = fullPath,
                                            IsDirectory     = isDir,
                                            Size            = size,
                                            LastWriteTime   = lastWrite,
                                            Attributes      = (FileAttributes)attrs,
                                            AttributeString = BuildAttributeString((FileAttributes)attrs),
                                            Extension       = ext,
                                        });

                                        if (isDir) queue.Enqueue(fullPath);

                                        int c = Interlocked.Increment(ref totalCount);
                                        if (c % 5000 == 0) IndexProgressChanged?.Invoke(c);

                                    } while (FindNextFileW(h, out data));
                                }
                                finally { FindClose(h); }
                            }

                            Interlocked.Decrement(ref activeWorkers);
                        }, token);
                    }

                    Task.WaitAll(workerTasks);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { IndexStatusChanged?.Invoke($"Index error: {ex.Message}"); }
                finally
                {
                    _indexing = false;
                    IndexStatusChanged?.Invoke($"Index complete – {_index.Count:N0} items");
                    IndexCompleted?.Invoke();
                }
            }, token);
        }

        public static void StopIndexing() => _indexCts?.Cancel();

        /// <summary>
        /// Searches the in-memory index. Supports wildcards (*?) and
        /// special prefix filters: ext:, path:, size>, size<
        /// </summary>
        public static List<SearchResult> Search(
            string query,
            int maxResults = 10000,
            bool caseSensitive = false)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<SearchResult>(_index);

            var results = new List<SearchResult>(256);
            query = query.Trim();

            // Parse special filters
            bool hasExtFilter = false, hasPathFilter = false;
            bool hasSizeGt = false, hasSizeLt = false;
            string extFilter = "", pathFilter = "";
            long sizeLimit = 0;

            var parts = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string nameQuery = "";

            foreach (var part in parts)
            {
                if (part.StartsWith("ext:", StringComparison.OrdinalIgnoreCase))
                { hasExtFilter = true; extFilter = part[4..].TrimStart('.').ToLower(); }
                else if (part.StartsWith("path:", StringComparison.OrdinalIgnoreCase))
                { hasPathFilter = true; pathFilter = part[5..]; }
                else if (part.StartsWith("size>", StringComparison.OrdinalIgnoreCase))
                { hasSizeGt = true; sizeLimit = ParseSizeFilter(part[5..]); }
                else if (part.StartsWith("size<", StringComparison.OrdinalIgnoreCase))
                { hasSizeLt = true; sizeLimit = ParseSizeFilter(part[5..]); }
                else nameQuery = part;
            }

            var comp = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

            foreach (var item in _index)
            {
                if (results.Count >= maxResults) break;

                // Extension filter
                if (hasExtFilter && !item.Extension.Equals("." + extFilter, comp)) continue;
                // Path filter
                if (hasPathFilter && !item.FullPath.Contains(pathFilter, comp)) continue;
                // Size filters
                if (hasSizeGt && item.Size <= sizeLimit) continue;
                if (hasSizeLt && item.Size >= sizeLimit) continue;
                // Name query (wildcard)
                if (!string.IsNullOrEmpty(nameQuery) && !MatchWildcard(item.Name, nameQuery, comp)) continue;

                results.Add(item);
            }

            return results;
        }

        private static bool MatchWildcard(string str, string pattern, StringComparison comp)
        {
            if (!pattern.Contains('*') && !pattern.Contains('?'))
                return str.Contains(pattern, comp);

            // Simple wildcard matching
            return WildcardMatch(str, pattern, comp);
        }

        private static bool WildcardMatch(string str, string pat, StringComparison comp)
        {
            int si = 0, pi = 0, lastStar = -1, lastMatch = 0;
            while (si < str.Length)
            {
                if (pi < pat.Length && (pat[pi] == '?' ||
                    string.Compare(str, si, pat, pi, 1, comp) == 0))
                { si++; pi++; }
                else if (pi < pat.Length && pat[pi] == '*')
                { lastStar = pi++; lastMatch = si; }
                else if (lastStar >= 0)
                { pi = lastStar + 1; si = ++lastMatch; }
                else return false;
            }
            while (pi < pat.Length && pat[pi] == '*') pi++;
            return pi == pat.Length;
        }

        private static long ParseSizeFilter(string s)
        {
            s = s.Trim().ToUpperInvariant();
            if (s.EndsWith("GB")) return long.Parse(s[..^2]) * 1024 * 1024 * 1024;
            if (s.EndsWith("MB")) return long.Parse(s[..^2]) * 1024 * 1024;
            if (s.EndsWith("KB")) return long.Parse(s[..^2]) * 1024;
            long.TryParse(s, out long v);
            return v;
        }
    }
}
