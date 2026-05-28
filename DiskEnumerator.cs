// ============================================================
// DiskEnumerator.cs – Physical disk, partition, and volume enumeration.
// ALL OPERATIONS ARE READ-ONLY.
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using DiskInspector.Models;

namespace DiskInspector.Core
{
    public static class DiskEnumerator
    {
        private static readonly Dictionary<string, string> GptTypeNames = new(StringComparer.OrdinalIgnoreCase)
        {
            { "C12A7328-F81F-11D2-BA4B-00A0C93EC93B", "EFI System Partition" },
            { "E3C9E316-0B5C-4DB8-817D-F92DF00215AE", "Microsoft Reserved (MSR)" },
            { "EBD0A0A2-B9E5-4433-87C0-68B6B72699C7", "Microsoft Basic Data" },
            { "5808C8AA-7E8F-42E0-85D2-E1E90434CFB3", "LDM Metadata" },
            { "AF9B60A0-1431-4F62-BC68-3311714A69AD", "LDM Data" },
            { "DE94BBA4-06D1-4D40-A16A-BFD50179D6AC", "Windows Recovery Environment" },
            { "E75CAF8F-F680-4CEE-AFA3-B001E56EFC2D", "Storage Spaces" },
            { "558D43C5-A1AC-43C0-AAC8-D1472B2923D1", "Storage Replica" },
            { "0FC63DAF-8483-4772-8E79-3D69D8477DE4", "Linux Data" },
            { "A19D880F-05FC-4D3B-A006-743F0F84911E", "Linux RAID" },
            { "4F68BCE3-E8CD-4DB1-96E7-FBCAF984B709", "Linux Root (x86-64)" },
            { "8DA63339-0007-60C0-C436-083AC8230908", "Linux /home" },
            { "21686148-6449-6E6F-744E-656564454649", "BIOS Boot Partition" },
        };

        private static readonly Dictionary<byte, string> MbrTypeNames = new()
        {
            { 0x00, "Empty" },
            { 0x01, "FAT12" },
            { 0x04, "FAT16 <32MB" },
            { 0x05, "Extended (CHS)" },
            { 0x06, "FAT16B" },
            { 0x07, "NTFS / exFAT / HPFS" },
            { 0x0B, "FAT32 (CHS)" },
            { 0x0C, "FAT32 (LBA)" },
            { 0x0E, "FAT16 (LBA)" },
            { 0x0F, "Extended (LBA)" },
            { 0x17, "Hidden NTFS / exFAT" },
            { 0x27, "Windows Recovery (OEM)" },
            { 0x42, "Windows Dynamic Disk" },
            { 0x82, "Linux Swap" },
            { 0x83, "Linux Native" },
            { 0x84, "Hibernation (S4)" },
            { 0x8E, "Linux LVM" },
            { 0xDE, "Dell Diagnostics" },
            { 0xEE, "GPT Protective MBR" },
            { 0xEF, "EFI System Partition (FAT)" },
            { 0xFD, "Linux RAID Auto" },
        };

        public static List<PhysicalDisk> EnumeratePhysicalDisks()
        {
            var disks = new List<PhysicalDisk>();
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_DiskDrive ORDER BY Index");

                foreach (ManagementObject drive in searcher.Get())
                {
                    var disk = new PhysicalDisk
                    {
                        DiskIndex      = Convert.ToInt32(drive["Index"]),
                        DevicePath     = drive["DeviceID"]?.ToString() ?? "",
                        Model          = drive["Model"]?.ToString()?.Trim() ?? "Unknown",
                        SerialNumber   = drive["SerialNumber"]?.ToString()?.Trim() ?? "",
                        MediaType      = drive["MediaType"]?.ToString() ?? "Unknown",
                        TotalSize      = Convert.ToUInt64(drive["Size"] ?? 0UL),
                        BytesPerSector = Convert.ToUInt32(drive["BytesPerSector"] ?? 512u),
                        FirmwareRevision = drive["FirmwareRevision"]?.ToString()?.Trim() ?? "",
                        Cylinders      = Convert.ToInt64(drive["TotalCylinders"] ?? 0L),
                        TracksPerCylinder = Convert.ToUInt32(drive["TracksPerCylinder"] ?? 0u),
                        SectorsPerTrack = Convert.ToUInt32(drive["SectorsPerTrack"] ?? 0u),
                    };

                    // Determine bus type from MediaType / InterfaceType
                    string iface = drive["InterfaceType"]?.ToString() ?? "";
                    disk.BusType = iface switch
                    {
                        "IDE"      => drive["MediaType"]?.ToString()?.Contains("SSD") == true ? "SATA SSD" : "SATA",
                        "SCSI"     => "SCSI",
                        "USB"      => "USB",
                        "1394"     => "FireWire",
                        "NVMe"     => "NVMe",
                        _          => iface.Length > 0 ? iface : "Unknown"
                    };

                    disk.Partitions = GetPartitionsForDisk(disk.DiskIndex, out string style, out Guid diskGuid);
                    disk.PartitionStyle = style;
                    disk.DiskGuid = diskGuid;

                    // Try to get NTFS volume data for each mounted volume
                    foreach (var part in disk.Partitions)
                    {
                        if (part.Volume != null && part.Volume.FileSystem == "NTFS")
                        {
                            part.Volume.NtfsData = NtfsReader.GetVolumeData(part.DriveLetter + "\\");
                        }
                    }

                    disks.Add(disk);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WMI disk enumeration error: {ex.Message}");
            }
            return disks;
        }

        private static List<PartitionInfo> GetPartitionsForDisk(
            int diskIndex, out string partitionStyle, out Guid diskGuid)
        {
            var partitions = new List<PartitionInfo>();
            partitionStyle = "Unknown";
            diskGuid = Guid.Empty;

            try
            {
                using var diskSearcher = new ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_DiskDrive.DeviceID='\\\\\\\\.\\\\PHYSICALDRIVE{diskIndex}'}} " +
                    "WHERE AssocClass=Win32_DiskDriveToDiskPartition");

                foreach (ManagementObject part in diskSearcher.Get())
                {
                    var pi = new PartitionInfo
                    {
                        PartitionNumber = Convert.ToInt32(part["Index"] ?? 0),
                        StartingOffset  = Convert.ToUInt64(part["StartingOffset"] ?? 0UL),
                        PartitionLength = Convert.ToUInt64(part["Size"] ?? 0UL),
                        IsBootable      = Convert.ToBoolean(part["Bootable"] ?? false),
                        IsActive        = Convert.ToBoolean(part["BootPartition"] ?? false),
                    };

                    string typeStr = part["Type"]?.ToString() ?? "";
                    pi.PartitionType = typeStr;
                    pi.IsHidden   = typeStr.Contains("Hidden", StringComparison.OrdinalIgnoreCase);
                    pi.IsSystem   = typeStr.Contains("EFI", StringComparison.OrdinalIgnoreCase)
                                 || typeStr.Contains("System", StringComparison.OrdinalIgnoreCase);
                    pi.IsRecovery = typeStr.Contains("Recovery", StringComparison.OrdinalIgnoreCase)
                                 || typeStr.Contains("Diagnostic", StringComparison.OrdinalIgnoreCase);

                    if (typeStr.Contains("GPT")) partitionStyle = "GPT";
                    else if (partitionStyle != "GPT") partitionStyle = "MBR";

                    try
                    {
                        using var ldSearcher = new ManagementObjectSearcher(
                            $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{part["DeviceID"]}'}} " +
                            "WHERE AssocClass=Win32_LogicalDiskToPartition");

                        foreach (ManagementObject ld in ldSearcher.Get())
                        {
                            string letter = ld["DeviceID"]?.ToString() ?? "";
                            pi.DriveLetter = letter;
                            pi.Volume = GetVolumeInfo(letter + "\\");
                        }
                    }
                    catch { }

                    partitions.Add(pi);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Partition enum error disk {diskIndex}: {ex.Message}");
            }

            return partitions;
        }

        public static VolumeInfo? GetVolumeInfo(string rootPath)
        {
            try
            {
                if (!rootPath.EndsWith("\\")) rootPath += "\\";
                var sbLabel = new StringBuilder(261);
                var sbFS    = new StringBuilder(261);

                bool ok = NativeMethods.GetVolumeInformation(
                    rootPath, sbLabel, sbLabel.Capacity,
                    out uint serial, out uint maxComp, out uint fsFlags,
                    sbFS, sbFS.Capacity);

                if (!ok) return null;

                NativeMethods.GetDiskFreeSpaceEx(rootPath,
                    out ulong freeBytesAvail, out ulong totalBytes, out ulong totalFree);
                NativeMethods.GetDiskFreeSpace(rootPath,
                    out uint spc, out uint bps, out _, out _);

                uint driveType = NativeMethods.GetDriveType(rootPath);
                string dtName = driveType switch
                {
                    NativeMethods.DRIVE_FIXED     => "Fixed",
                    NativeMethods.DRIVE_REMOVABLE => "Removable",
                    NativeMethods.DRIVE_REMOTE    => "Network",
                    NativeMethods.DRIVE_CDROM     => "CD-ROM",
                    NativeMethods.DRIVE_RAMDISK   => "RAM Disk",
                    _                             => "Unknown"
                };

                // Get Volume GUID
                var sbGuid = new StringBuilder(64);
                NativeMethods.GetVolumeNameForVolumeMountPoint(rootPath, sbGuid, 64);

                return new VolumeInfo
                {
                    DriveLetter        = rootPath.TrimEnd('\\'),
                    VolumeLabel        = sbLabel.ToString(),
                    FileSystem         = sbFS.ToString(),
                    TotalBytes         = totalBytes,
                    FreeBytes          = totalFree,
                    SectorsPerCluster  = spc,
                    BytesPerSector     = bps,
                    MaxComponentLength = maxComp,
                    FileSystemFlags    = fsFlags,
                    DriveType          = dtName,
                    VolumeGuid         = sbGuid.ToString(),
                };
            }
            catch { return null; }
        }

        public static List<VolumeInfo> EnumerateVolumes()
        {
            var volumes = new List<VolumeInfo>();
            foreach (string drive in Directory.GetLogicalDrives())
            {
                var vi = GetVolumeInfo(drive);
                if (vi != null) volumes.Add(vi);
            }
            return volumes;
        }

        public static string GetGptTypeName(Guid guid)
        {
            string key = guid.ToString("D").ToUpperInvariant();
            return GptTypeNames.TryGetValue(key, out string? name) ? name : "Unknown GPT Type";
        }

        public static string GetMbrTypeName(byte typeByte) =>
            MbrTypeNames.TryGetValue(typeByte, out string? name)
                ? $"0x{typeByte:X2} – {name}"
                : $"0x{typeByte:X2} – Unknown";

        public static string FormatSize(ulong bytes)
        {
            const double KB = 1024, MB = KB * 1024, GB = MB * 1024, TB = GB * 1024;
            if (bytes >= (ulong)TB) return $"{bytes / TB:F2} TB";
            if (bytes >= (ulong)GB) return $"{bytes / GB:F2} GB";
            if (bytes >= (ulong)MB) return $"{bytes / MB:F2} MB";
            if (bytes >= (ulong)KB) return $"{bytes / KB:F1} KB";
            return $"{bytes} B";
        }

        public static string FormatSize(long bytes) =>
            bytes < 0 ? "N/A" : FormatSize((ulong)bytes);

        /// <summary>Checks whether the current process is elevated (admin).</summary>
        public static bool IsElevated()
        {
            try
            {
                if (!NativeMethods.OpenProcessToken(NativeMethods.GetCurrentProcess(),
                    NativeMethods.TOKEN_QUERY, out IntPtr token))
                    return false;

                try
                {
                    uint size = (uint)Marshal.SizeOf<NativeMethods.TOKEN_ELEVATION>();
                    IntPtr buf = Marshal.AllocHGlobal((int)size);
                    try
                    {
                        if (NativeMethods.GetTokenInformation(token,
                            NativeMethods.TOKEN_INFORMATION_CLASS.TokenElevation, buf, size, out _))
                        {
                            var elev = Marshal.PtrToStructure<NativeMethods.TOKEN_ELEVATION>(buf);
                            return elev.TokenIsElevated != 0;
                        }
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
                finally { NativeMethods.CloseHandle(token); }
            }
            catch { }
            return false;
        }
    }
}
