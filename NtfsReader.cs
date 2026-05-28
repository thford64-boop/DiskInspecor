// ============================================================
// NtfsReader.cs – Raw NTFS MFT parsing (read-only, requires admin).
// Reads MFT records directly from the volume device.
// ============================================================

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using DiskInspector.Models;

namespace DiskInspector.Core
{
    /// <summary>
    /// Reads NTFS structures directly via raw volume device access.
    /// Requires administrator privileges. All operations read-only.
    /// </summary>
    public static class NtfsReader
    {
        private const int MFT_RECORD_SIZE = 1024;
        private const uint MFT_SIGNATURE  = 0x454C4946; // "FILE"

        // NTFS Attribute type codes
        private static readonly Dictionary<uint, string> AttrTypeNames = new()
        {
            { 0x10, "$STANDARD_INFORMATION" },
            { 0x20, "$ATTRIBUTE_LIST"       },
            { 0x30, "$FILE_NAME"            },
            { 0x40, "$OBJECT_ID"            },
            { 0x50, "$SECURITY_DESCRIPTOR"  },
            { 0x60, "$VOLUME_NAME"          },
            { 0x70, "$VOLUME_INFORMATION"   },
            { 0x80, "$DATA"                 },
            { 0x90, "$INDEX_ROOT"           },
            { 0xA0, "$INDEX_ALLOCATION"     },
            { 0xB0, "$BITMAP"               },
            { 0xC0, "$REPARSE_POINT"        },
            { 0xD0, "$EA_INFORMATION"       },
            { 0xE0, "$EA"                   },
            { 0xF0, "$PROPERTY_SET"         },
            { 0x100,"$LOGGED_UTILITY_STREAM"},
            { 0xFFFFFFFF, "$END"            },
        };

        /// <summary>
        /// Opens a volume device handle for raw reading.
        /// volumePath e.g. "C:\\" or "C:" or "\\\\.\\C:"
        /// </summary>
        private static IntPtr OpenVolume(string volumePath)
        {
            // Normalize to \\.\X: form
            string devPath = volumePath.TrimEnd('\\', '/');
            if (!devPath.StartsWith("\\\\.\\"))
                devPath = "\\\\.\\" + devPath.TrimStart('\\');

            return NativeMethods.CreateFile(
                devPath,
                NativeMethods.GENERIC_READ,
                NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                IntPtr.Zero,
                NativeMethods.OPEN_EXISTING,
                NativeMethods.FILE_FLAG_BACKUP_SEMANTICS,
                IntPtr.Zero);
        }

        /// <summary>
        /// Retrieves NTFS volume data (MFT location, cluster sizes, etc.).
        /// </summary>
        public static NtfsVolumeData? GetVolumeData(string rootPath)
        {
            IntPtr handle = OpenVolume(rootPath);
            if (handle == NativeMethods.INVALID_HANDLE_VALUE) return null;

            try
            {
                int sz = Marshal.SizeOf<NativeMethods.NTFS_VOLUME_DATA_BUFFER>();
                byte[] buf = new byte[sz + 64];

                bool ok = NativeMethods.DeviceIoControl(
                    handle, NativeMethods.FSCTL_GET_NTFS_VOLUME_DATA,
                    null, 0, buf, (uint)buf.Length, out uint returned, IntPtr.Zero);

                if (!ok || returned < sz) return null;

                GCHandle gcHandle = GCHandle.Alloc(buf, GCHandleType.Pinned);
                try
                {
                    var raw = Marshal.PtrToStructure<NativeMethods.NTFS_VOLUME_DATA_BUFFER>(
                        gcHandle.AddrOfPinnedObject());

                    return new NtfsVolumeData
                    {
                        VolumeSerialNumber = raw.VolumeSerialNumber,
                        NumberSectors      = raw.NumberSectors,
                        TotalClusters      = raw.TotalClusters,
                        FreeClusters       = raw.FreeClusters,
                        BytesPerSector     = raw.BytesPerSector,
                        BytesPerCluster    = raw.BytesPerCluster,
                        BytesPerMftRecord  = raw.BytesPerFileRecordSegment,
                        MftStartLcn        = raw.MftStartLcn,
                        Mft2StartLcn       = raw.Mft2StartLcn,
                        MftZoneStart       = raw.MftZoneStart,
                        MftZoneEnd         = raw.MftZoneEnd,
                        MftValidDataLength = raw.MftValidDataLength,
                    };
                }
                finally { gcHandle.Free(); }
            }
            finally { NativeMethods.CloseHandle(handle); }
        }

        /// <summary>
        /// Reads MFT records from a volume, yielding parsed metadata.
        /// maxRecords = 0 means read all.
        /// </summary>
        public static List<MftRecord> ReadMftRecords(
            string volumeRoot,
            NtfsVolumeData volData,
            int maxRecords = 0,
            IProgress<(int done, int total)>? progress = null)
        {
            var results = new List<MftRecord>();
            IntPtr handle = OpenVolume(volumeRoot);
            if (handle == NativeMethods.INVALID_HANDLE_VALUE)
                return results;

            try
            {
                long mftOffset = volData.MftStartLcn * volData.BytesPerCluster;
                long totalRecords = volData.MftValidDataLength / MFT_RECORD_SIZE;
                if (maxRecords > 0 && totalRecords > maxRecords)
                    totalRecords = maxRecords;

                // Seek to MFT start
                NativeMethods.SetFilePointerEx(handle, mftOffset, out _, 0);

                byte[] buf = new byte[MFT_RECORD_SIZE];
                int count = 0;

                for (long i = 0; i < totalRecords; i++)
                {
                    if (!NativeMethods.ReadFile(handle, buf, MFT_RECORD_SIZE, out uint read, IntPtr.Zero)
                        || read < MFT_RECORD_SIZE)
                        break;

                    var rec = ParseMftRecord(buf, (ulong)i);
                    if (rec != null)
                    {
                        results.Add(rec);
                        count++;
                    }

                    if (i % 500 == 0)
                        progress?.Report(((int)i, (int)totalRecords));
                }

                progress?.Report(((int)totalRecords, (int)totalRecords));
            }
            finally
            {
                NativeMethods.CloseHandle(handle);
            }

            return results;
        }

        /// <summary>Reads a single MFT record by record number.</summary>
        public static MftRecord? ReadMftRecord(string volumeRoot, NtfsVolumeData volData, ulong recordNumber)
        {
            IntPtr handle = OpenVolume(volumeRoot);
            if (handle == NativeMethods.INVALID_HANDLE_VALUE) return null;

            try
            {
                long offset = volData.MftStartLcn * volData.BytesPerCluster
                            + (long)recordNumber * MFT_RECORD_SIZE;

                NativeMethods.SetFilePointerEx(handle, offset, out _, 0);
                byte[] buf = new byte[MFT_RECORD_SIZE];

                if (!NativeMethods.ReadFile(handle, buf, MFT_RECORD_SIZE, out uint read, IntPtr.Zero)
                    || read < MFT_RECORD_SIZE)
                    return null;

                var rec = ParseMftRecord(buf, recordNumber);
                if (rec != null) rec.RawBytes = buf;
                return rec;
            }
            finally { NativeMethods.CloseHandle(handle); }
        }

        /// <summary>Parses raw 1024-byte MFT record buffer.</summary>
        public static MftRecord? ParseMftRecord(byte[] buf, ulong recordNumber)
        {
            if (buf.Length < 48) return null;

            uint sig = BitConverter.ToUInt32(buf, 0);
            if (sig == 0) return null; // Unformatted

            bool isFile = sig == MFT_SIGNATURE; // "FILE"
            bool isBaad = sig == 0x44414142;   // "BAAD"

            if (!isFile && !isBaad) return null;

            // Apply fixup array
            byte[] working = (byte[])buf.Clone();
            ApplyFixup(working);

            ushort attrOffset  = BitConverter.ToUInt16(working, 20);
            ushort flags       = BitConverter.ToUInt16(working, 22);
            ushort seqNum      = BitConverter.ToUInt16(working, 16);
            ushort hardlinks   = BitConverter.ToUInt16(working, 18);

            bool inUse    = (flags & 0x01) != 0;
            bool isDir    = (flags & 0x02) != 0;

            var rec = new MftRecord
            {
                RecordNumber    = recordNumber,
                IsInUse         = inUse,
                IsDirectory     = isDir,
                SequenceNumber  = seqNum,
                HardLinkCount   = hardlinks,
                Flags           = flags,
                StatusDescription = isBaad ? "BAAD (fixup mismatch)" : inUse ? "In Use" : "Deleted",
                RawBytes        = working,
            };

            // Parse attributes
            if (attrOffset < MFT_RECORD_SIZE - 4)
            {
                ParseAttributes(working, attrOffset, rec);
            }

            return rec;
        }

        private static void ApplyFixup(byte[] buf)
        {
            // Update Sequence Array starts at offset 4 (UsnOffset = offset 4)
            ushort usnOffset = BitConverter.ToUInt16(buf, 4);
            ushort usnCount  = BitConverter.ToUInt16(buf, 6);

            if (usnOffset + usnCount * 2 > buf.Length) return;

            // USN is at usnOffset, entries follow
            for (int i = 1; i < usnCount && i < 3; i++)
            {
                int sectorEnd = i * 512 - 2;
                if (sectorEnd + 1 >= buf.Length) break;
                buf[sectorEnd]     = buf[usnOffset + i * 2];
                buf[sectorEnd + 1] = buf[usnOffset + i * 2 + 1];
            }
        }

        private static void ParseAttributes(byte[] buf, int offset, MftRecord rec)
        {
            while (offset + 4 < buf.Length)
            {
                uint typeCode = BitConverter.ToUInt32(buf, offset);
                if (typeCode == 0xFFFFFFFF) break; // End marker

                if (offset + 8 >= buf.Length) break;
                uint attrLen = BitConverter.ToUInt32(buf, offset + 4);
                if (attrLen < 8 || attrLen > 1024 || offset + attrLen > buf.Length) break;

                byte nonResident = buf[offset + 8];
                byte nameLen     = buf[offset + 9];
                ushort nameOff   = BitConverter.ToUInt16(buf, offset + 10);

                string? attrName = null;
                if (nameLen > 0 && offset + nameOff + nameLen * 2 <= buf.Length)
                    attrName = Encoding.Unicode.GetString(buf, offset + nameOff, nameLen * 2);

                var attr = new MftAttribute
                {
                    TypeCode      = typeCode,
                    TypeName      = AttrTypeNames.TryGetValue(typeCode, out string? tn) ? tn : $"0x{typeCode:X}",
                    Length        = attrLen,
                    IsNonResident = nonResident != 0,
                    AttributeName = attrName,
                };

                if (nonResident == 0)
                {
                    // Resident attribute
                    ushort dataOff = BitConverter.ToUInt16(buf, offset + 20);
                    uint   dataLen = BitConverter.ToUInt32(buf, offset + 16);

                    if (dataOff + dataLen <= attrLen && dataLen <= 4096)
                    {
                        attr.ResidentData = new byte[dataLen];
                        Array.Copy(buf, offset + dataOff, attr.ResidentData, 0, dataLen);

                        // Parse known attribute types
                        attr.ParsedDescription = ParseAttributeData(typeCode, attr.ResidentData, rec);
                    }
                }
                else
                {
                    // Non-resident attribute — extract VCN range & sizes
                    if (offset + 64 <= buf.Length)
                    {
                        attr.StartVcn     = BitConverter.ToInt64(buf, offset + 16);
                        attr.LastVcn      = BitConverter.ToInt64(buf, offset + 24);
                        attr.AllocatedSize = BitConverter.ToInt64(buf, offset + 40);
                        attr.DataSize      = BitConverter.ToInt64(buf, offset + 48);
                        attr.ParsedDescription = $"Non-resident, VCN {attr.StartVcn}–{attr.LastVcn}, " +
                                                 $"Data: {DiskEnumerator.FormatSize(attr.DataSize)}, " +
                                                 $"Alloc: {DiskEnumerator.FormatSize(attr.AllocatedSize)}";
                    }
                }

                rec.Attributes.Add(attr);
                offset += (int)attrLen;
            }
        }

        private static string ParseAttributeData(uint typeCode, byte[] data, MftRecord rec)
        {
            try
            {
                switch (typeCode)
                {
                    case 0x10: // $STANDARD_INFORMATION
                        if (data.Length >= 48)
                        {
                            DateTime created  = DateTime.FromFileTime(BitConverter.ToInt64(data, 0));
                            DateTime modified = DateTime.FromFileTime(BitConverter.ToInt64(data, 8));
                            DateTime mftMod   = DateTime.FromFileTime(BitConverter.ToInt64(data, 16));
                            DateTime accessed = DateTime.FromFileTime(BitConverter.ToInt64(data, 24));
                            uint fileAttrs    = BitConverter.ToUInt32(data, 32);

                            rec.CreationTime     = created;
                            rec.LastModifiedTime = modified;
                            rec.MftModifiedTime  = mftMod;
                            rec.LastAccessTime   = accessed;

                            return $"Created: {created:yyyy-MM-dd HH:mm:ss}  " +
                                   $"Modified: {modified:yyyy-MM-dd HH:mm:ss}  " +
                                   $"Attrs: 0x{fileAttrs:X4}";
                        }
                        break;

                    case 0x30: // $FILE_NAME
                        if (data.Length >= 66)
                        {
                            ulong parentRef = BitConverter.ToUInt64(data, 0) & 0x0000FFFFFFFFFFFF;
                            byte nameLen    = data[64];
                            byte nameSpace  = data[65];
                            long fileSize   = BitConverter.ToInt64(data, 40);
                            long allocSize  = BitConverter.ToInt64(data, 32);

                            rec.ParentRecordNumber = parentRef;
                            rec.AllocatedSize = allocSize;

                            if (nameLen > 0 && 66 + nameLen * 2 <= data.Length)
                            {
                                string fn = Encoding.Unicode.GetString(data, 66, nameLen * 2);
                                // Only set filename from POSIX/Win32 namespaces (0,1,3), not DOS (2)
                                if (nameSpace != 2 && rec.FileName.Length == 0)
                                {
                                    rec.FileName = fn;
                                    rec.FileSize = fileSize;
                                }
                                string nsName = nameSpace switch { 0 => "POSIX", 1 => "Win32", 2 => "DOS", 3 => "Win32&DOS", _ => $"NS{nameSpace}" };
                                return $"Name: '{fn}'  Parent: {parentRef}  " +
                                       $"Size: {DiskEnumerator.FormatSize(fileSize)}  NS: {nsName}";
                            }
                        }
                        break;

                    case 0x80: // $DATA (resident)
                        rec.FileSize = data.Length;
                        return $"Resident data, {data.Length} bytes";

                    case 0x60: // $VOLUME_NAME
                        return $"Volume name: '{Encoding.Unicode.GetString(data)}'";

                    case 0x70: // $VOLUME_INFORMATION
                        if (data.Length >= 8)
                        {
                            byte major = data[8], minor = data[9];
                            ushort volFlags = BitConverter.ToUInt16(data, 10);
                            return $"NTFS {major}.{minor}  Flags: 0x{volFlags:X4}" +
                                   ((volFlags & 0x02) != 0 ? " [DIRTY]" : "");
                        }
                        break;
                }
            }
            catch { }
            return $"{data.Length} bytes";
        }

        /// <summary>Returns a formatted hex+ASCII dump of raw bytes.</summary>
        public static string HexDump(byte[] data, int maxBytes = 512)
        {
            var sb = new StringBuilder();
            int limit = Math.Min(data.Length, maxBytes);

            for (int row = 0; row < limit; row += 16)
            {
                sb.Append($"{row:X8}  ");

                for (int col = 0; col < 16; col++)
                {
                    if (row + col < limit)
                        sb.Append($"{data[row + col]:X2} ");
                    else
                        sb.Append("   ");
                    if (col == 7) sb.Append(' ');
                }

                sb.Append(" |");
                for (int col = 0; col < 16 && row + col < limit; col++)
                {
                    byte b = data[row + col];
                    sb.Append(b >= 0x20 && b < 0x7F ? (char)b : '.');
                }
                sb.AppendLine("|");
            }

            if (data.Length > maxBytes)
                sb.AppendLine($"... ({data.Length - maxBytes} more bytes)");

            return sb.ToString();
        }
    }
}
