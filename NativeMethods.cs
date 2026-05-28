// ============================================================
// NativeMethods.cs - Windows API P/Invoke declarations
// READ-ONLY low-level disk and volume access
// ============================================================

using System;
using System.Runtime.InteropServices;

namespace DiskInspector.Core
{
    internal static class NativeMethods
    {
        // ---- File / Volume Access ----

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ReadFile(
            IntPtr hFile,
            byte[] lpBuffer,
            uint nNumberOfBytesToRead,
            out uint lpNumberOfBytesRead,
            IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetFilePointerEx(
            IntPtr hFile,
            long liDistanceToMove,
            out long lpNewFilePointer,
            uint dwMoveMethod);

        // ---- DeviceIoControl overloads ----

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeviceIoControl(
            IntPtr hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            uint nInBufferSize,
            IntPtr lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeviceIoControl(
            IntPtr hDevice,
            uint dwIoControlCode,
            byte[]? lpInBuffer,
            uint nInBufferSize,
            byte[]? lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped);

        // ---- Volume Information ----

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetVolumeInformation(
            string lpRootPathName,
            System.Text.StringBuilder? lpVolumeNameBuffer,
            int nVolumeNameSize,
            out uint lpVolumeSerialNumber,
            out uint lpMaximumComponentLength,
            out uint lpFileSystemFlags,
            System.Text.StringBuilder? lpFileSystemNameBuffer,
            int nFileSystemNameSize);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetDiskFreeSpaceEx(
            string lpDirectoryName,
            out ulong lpFreeBytesAvailable,
            out ulong lpTotalNumberOfBytes,
            out ulong lpTotalNumberOfFreeBytes);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern uint GetDriveType(string lpRootPathName);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetDiskFreeSpace(
            string lpRootPathName,
            out uint lpSectorsPerCluster,
            out uint lpBytesPerSector,
            out uint lpNumberOfFreeClusters,
            out uint lpTotalNumberOfClusters);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetVolumeNameForVolumeMountPoint(
            string lpszVolumeMountPoint,
            System.Text.StringBuilder lpszVolumeName,
            uint cchBufferLength);

        // ---- Reparse Points ----

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr FindFirstFile(
            string lpFileName,
            out WIN32_FIND_DATA lpFindFileData);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool FindClose(IntPtr hFindFile);

        // ---- Process / Token ----

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool OpenProcessToken(
            IntPtr ProcessHandle,
            uint DesiredAccess,
            out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool GetTokenInformation(
            IntPtr TokenHandle,
            TOKEN_INFORMATION_CLASS TokenInformationClass,
            IntPtr TokenInformation,
            uint TokenInformationLength,
            out uint ReturnLength);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll")]
        public static extern uint GetLastError();

        // ============================================================
        // Constants
        // ============================================================

        public const uint GENERIC_READ        = 0x80000000;
        public const uint FILE_SHARE_READ     = 0x00000001;
        public const uint FILE_SHARE_WRITE    = 0x00000002;
        public const uint FILE_SHARE_DELETE   = 0x00000004;
        public const uint OPEN_EXISTING       = 3;
        public const uint FILE_FLAG_BACKUP_SEMANTICS   = 0x02000000;
        public const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
        public static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        // IOCTL
        public const uint IOCTL_DISK_GET_DRIVE_LAYOUT_EX       = 0x00070050;
        public const uint IOCTL_DISK_GET_DRIVE_GEOMETRY_EX     = 0x000700A0;
        public const uint IOCTL_STORAGE_GET_DEVICE_NUMBER      = 0x002D1080;
        public const uint IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS = 0x00560000;
        public const uint FSCTL_GET_REPARSE_POINT              = 0x000900A8;
        public const uint FSCTL_GET_NTFS_VOLUME_DATA           = 0x00090064;

        // Drive types
        public const uint DRIVE_UNKNOWN     = 0;
        public const uint DRIVE_NO_ROOT_DIR = 1;
        public const uint DRIVE_REMOVABLE   = 2;
        public const uint DRIVE_FIXED       = 3;
        public const uint DRIVE_REMOTE      = 4;
        public const uint DRIVE_CDROM       = 5;
        public const uint DRIVE_RAMDISK     = 6;

        // Token
        public const uint TOKEN_QUERY = 0x0008;

        // ============================================================
        // Structs
        // ============================================================

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WIN32_FIND_DATA
        {
            public uint dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint dwReserved0;
            public uint dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string cAlternateFileName;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISK_GEOMETRY
        {
            public long Cylinders;
            public uint MediaType;
            public uint TracksPerCylinder;
            public uint SectorsPerTrack;
            public uint BytesPerSector;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct STORAGE_DEVICE_NUMBER
        {
            public uint DeviceType;
            public uint DeviceNumber;
            public uint PartitionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NTFS_VOLUME_DATA_BUFFER
        {
            public long   VolumeSerialNumber;
            public long   NumberSectors;
            public long   TotalClusters;
            public long   FreeClusters;
            public long   TotalReserved;
            public uint   BytesPerSector;
            public uint   BytesPerCluster;
            public uint   BytesPerFileRecordSegment;
            public uint   ClustersPerFileRecordSegment;
            public long   MftValidDataLength;
            public long   MftStartLcn;
            public long   Mft2StartLcn;
            public long   MftZoneStart;
            public long   MftZoneEnd;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct TOKEN_ELEVATION
        {
            public uint TokenIsElevated;
        }

        public enum TOKEN_INFORMATION_CLASS
        {
            TokenUser = 1, TokenGroups, TokenPrivileges, TokenOwner,
            TokenPrimaryGroup, TokenDefaultDacl, TokenSource, TokenType,
            TokenImpersonationLevel, TokenStatistics, TokenRestrictedSids,
            TokenSessionId, TokenGroupsAndPrivileges, TokenSessionReference,
            TokenSandBoxInert, TokenAuditPolicy, TokenOrigin,
            TokenElevationType, TokenLinkedToken, TokenElevation,
            TokenHasRestrictions, TokenAccessInformation, TokenVirtualizationAllowed,
            TokenVirtualizationEnabled, TokenIntegrityLevel, TokenUIAccess,
            TokenMandatoryPolicy, TokenLogonSid, MaxTokenInfoClass
        }
    }
}
