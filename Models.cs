// ============================================================
// Models.cs - Data models for disk, partition, and file metadata
// All structures are READ-ONLY representations for inspection only.
// ============================================================

using System;
using System.Collections.Generic;

namespace DiskInspector.Models
{
    public class PhysicalDisk
    {
        public int DiskIndex { get; set; }
        public string DevicePath { get; set; } = "";
        public string Model { get; set; } = "";
        public string SerialNumber { get; set; } = "";
        public string MediaType { get; set; } = "";
        public ulong TotalSize { get; set; }
        public uint BytesPerSector { get; set; }
        public string PartitionStyle { get; set; } = "";
        public Guid DiskGuid { get; set; }
        public List<PartitionInfo> Partitions { get; set; } = new();
        // Extended geometry
        public long Cylinders { get; set; }
        public uint TracksPerCylinder { get; set; }
        public uint SectorsPerTrack { get; set; }
        public string FirmwareRevision { get; set; } = "";
        public string BusType { get; set; } = "";         // SATA, NVMe, USB, etc.
        public bool IsRemovable { get; set; }
    }

    public class PartitionInfo
    {
        public int PartitionNumber { get; set; }
        public string PartitionType { get; set; } = "";
        public Guid PartitionTypeGuid { get; set; }
        public Guid PartitionId { get; set; }
        public ulong StartingOffset { get; set; }
        public ulong PartitionLength { get; set; }
        public bool IsBootable { get; set; }
        public bool IsActive { get; set; }
        public bool IsHidden { get; set; }
        public bool IsSystem { get; set; }
        public bool IsRecovery { get; set; }
        public bool IsReadOnly { get; set; }
        public string? DriveLetter { get; set; }
        public string MbrType { get; set; } = "";
        public VolumeInfo? Volume { get; set; }
    }

    public class VolumeInfo
    {
        public string DriveLetter { get; set; } = "";
        public string VolumeName { get; set; } = "";
        public string VolumeLabel { get; set; } = "";
        public string FileSystem { get; set; } = "";
        public ulong TotalBytes { get; set; }
        public ulong FreeBytes { get; set; }
        public ulong UsedBytes => TotalBytes > FreeBytes ? TotalBytes - FreeBytes : 0;
        public double UsedPercent => TotalBytes > 0 ? (double)UsedBytes / TotalBytes * 100 : 0;
        public uint SectorsPerCluster { get; set; }
        public uint BytesPerSector { get; set; }
        public uint ClusterSize => SectorsPerCluster * BytesPerSector;
        public uint MaxComponentLength { get; set; }
        public uint FileSystemFlags { get; set; }
        public string DriveType { get; set; } = "";
        public string VolumeGuid { get; set; } = "";      // \\?\Volume{GUID}\
        // NTFS-specific
        public NtfsVolumeData? NtfsData { get; set; }
    }

    public class NtfsVolumeData
    {
        public long VolumeSerialNumber { get; set; }
        public long NumberSectors { get; set; }
        public long TotalClusters { get; set; }
        public long FreeClusters { get; set; }
        public uint BytesPerSector { get; set; }
        public uint BytesPerCluster { get; set; }
        public uint BytesPerMftRecord { get; set; }
        public long MftStartLcn { get; set; }
        public long Mft2StartLcn { get; set; }
        public long MftZoneStart { get; set; }
        public long MftZoneEnd { get; set; }
        public long MftValidDataLength { get; set; }
        public long MftOffset => MftStartLcn * BytesPerCluster;
    }

    public class FileSystemEntry
    {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
        public bool IsDirectory { get; set; }
        public long Size { get; set; }
        public DateTime CreationTime { get; set; }
        public DateTime LastWriteTime { get; set; }
        public DateTime LastAccessTime { get; set; }
        public System.IO.FileAttributes Attributes { get; set; }
        public bool IsHidden => (Attributes & System.IO.FileAttributes.Hidden) != 0;
        public bool IsSystem => (Attributes & System.IO.FileAttributes.System) != 0;
        public bool IsReparsePoint => (Attributes & System.IO.FileAttributes.ReparsePoint) != 0;
        public bool IsSymLink { get; set; }
        public bool IsJunction { get; set; }
        public string? LinkTarget { get; set; }
        public string AttributeString { get; set; } = "";
        public string Extension => System.IO.Path.GetExtension(Name).ToLowerInvariant();
    }

    public class MftRecord
    {
        public ulong RecordNumber { get; set; }
        public bool IsInUse { get; set; }
        public bool IsDirectory { get; set; }
        public ushort SequenceNumber { get; set; }
        public ushort HardLinkCount { get; set; }
        public uint Flags { get; set; }
        public string FileName { get; set; } = "";
        public string FilePath { get; set; } = "";
        public ulong ParentRecordNumber { get; set; }
        public long FileSize { get; set; }
        public long AllocatedSize { get; set; }
        public DateTime CreationTime { get; set; }
        public DateTime LastModifiedTime { get; set; }
        public DateTime MftModifiedTime { get; set; }
        public DateTime LastAccessTime { get; set; }
        public List<MftAttribute> Attributes { get; set; } = new();
        public byte[]? RawBytes { get; set; }
        public string StatusDescription { get; set; } = "";
    }

    public class MftAttribute
    {
        public uint TypeCode { get; set; }
        public string TypeName { get; set; } = "";
        public uint Length { get; set; }
        public bool IsNonResident { get; set; }
        public string? AttributeName { get; set; }
        public byte[]? ResidentData { get; set; }
        public string ParsedDescription { get; set; } = "";
        // Non-resident specifics
        public long StartVcn { get; set; }
        public long LastVcn { get; set; }
        public long AllocatedSize { get; set; }
        public long DataSize { get; set; }
    }

public class SearchResult
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public bool IsDirectory { get; set; }
    public long Size { get; set; }
    public DateTime LastWriteTime { get; set; }
    public System.IO.FileAttributes Attributes { get; set; }
    public string AttributeString { get; set; } = "";
    public string Extension { get; set; } = "";
    public bool IsHidden => (Attributes & System.IO.FileAttributes.Hidden) != 0;
    public bool IsSystem => (Attributes & System.IO.FileAttributes.System) != 0;
}

    public static class NtfsMetaFiles
    {
        public static readonly string[] Names = {
            "$MFT", "$MFTMirr", "$LogFile", "$Volume", "$AttrDef",
            ".", "$Bitmap", "$Boot", "$BadClus", "$Secure",
            "$UpCase", "$Extend"
        };

        public static string Describe(string name) => name switch
        {
            "$MFT"     => "Master File Table – index of every file and directory on the volume",
            "$MFTMirr" => "Partial mirror of the first 4 MFT records for crash recovery",
            "$LogFile" => "NTFS journal / transaction log for crash recovery",
            "$Volume"  => "Volume information: label, version, dirty flag",
            "$AttrDef" => "Attribute type definitions: names, sizes, flags",
            "."        => "Root directory of the volume (MFT record 5)",
            "$Bitmap"  => "Cluster allocation bitmap (1 bit per cluster)",
            "$Boot"    => "Volume boot record and bootstrap loader code",
            "$BadClus" => "Bad cluster list – clusters marked as unusable",
            "$Secure"  => "Security descriptors (ACLs) for all files on the volume",
            "$UpCase"  => "Unicode uppercase translation table (128KB)",
            "$Extend"  => "Extension directory: $Quota, $ObjId, $Reparse, $UsnJrnl",
            _          => "Unknown NTFS metadata file"
        };
    }

    public class LocalUser
    {
        public string Username { get; set; } = "";
        public string SID { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Description { get; set; } = "";
        public string ProfilePath { get; set; } = "";
        public bool IsDisabled { get; set; }
        public bool IsLocked { get; set; }
        public bool IsBuiltin { get; set; }
        public List<string> Groups { get; set; } = new();
    }

    public class ServiceInfo
    {
        public string Name { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Status { get; set; } = "";
        public string StartType { get; set; } = "";
        public string ImagePath { get; set; } = "";
        public string Account { get; set; } = "";
    }

    public class StartupEntry
    {
        public string Name { get; set; } = "";
        public string Command { get; set; } = "";
        public string Location { get; set; } = "";
        public string User { get; set; } = "";
    }
}
