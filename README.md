# DiskInspector — Build Instructions

## Requirements
- Windows 10/11 x64
- .NET 8 SDK → https://dotnet.microsoft.com/download/dotnet/8.0

---

## Build a single .EXE (self-contained, no install needed)

Open **Developer PowerShell** or any terminal in the project folder:

```powershell
# Debug build (fast)
dotnet build

# ✅ RELEASE — single EXE, self-contained, no .NET install required on target machine
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

# Output: bin\Release\net8.0-windows\win-x64\publish\DiskInspector.exe
```

To trim unused code (smaller file, ~50-70 MB instead of ~120 MB):
```powershell
dotnet publish -c Release -r win-x64 --self-contained true ^
    -p:PublishSingleFile=true -p:PublishTrimmed=true ^
    -p:TrimmerRootAssembly=DiskInspector
```

---

## Run

Double-click `DiskInspector.exe`.  
**Right-click → Run as Administrator** to unlock:
- Raw MFT reading (NTFS tab)
- Raw physical drive hex viewing
- Hidden/protected OS partition details
- NTFS volume data (cluster map, MFT offset, etc.)

The app manifest requests elevation automatically, so Windows will UAC-prompt on launch.

---

## Features

### 🔍 Search (Everything-style)
- Background indexer crawls all local drives on startup
- Instant filter-as-you-type on 500k+ files
- Filter syntax:
  - `*.dll`          — wildcard name search
  - `ext:exe`        — filter by extension
  - `path:System32`  — path contains filter
  - `size>100MB`     — larger than
  - `size<1KB`       — smaller than
  - Combined: `*.log path:Windows size>10MB`
- Double-click result → jumps to File Browser
- Right-click → Copy path / Open in Explorer

### 📁 File Browser
- Full tree navigation including hidden/system/protected dirs
- Shows: symlinks, junctions, reparse points (with targets)
- Color coding: blue=dirs, orange=system, grey=hidden, yellow=encrypted
- Attribute flags panel per file
- Path bar (type + Enter to navigate)

### 💽 Disks & Partitions
- All physical disks via WMI (HDDs, SSDs, NVMe, USB)
- Per-disk: model, serial, firmware, bus type, geometry
- Per-partition: type, offset, size, flags (boot/active/hidden/system/recovery)
- Per-volume: label, FS, used/free, cluster size, volume GUID, FS capability flags
- NTFS-specific: MFT location, cluster map, MFT zone, record size
- Visual usage bar per volume

### 🗂 NTFS / MFT Viewer
- Direct raw MFT read (requires admin)
- Parses all attribute types: $STANDARD_INFORMATION, $FILE_NAME, $DATA, $VOLUME_*, etc.
- Shows deleted records (red) and NTFS metadata files $MFT–$Extend (yellow)
- Per-record: timestamps (100ns precision), hard link count, parent record number
- Full attribute list with resident data parsing
- Raw 1024-byte hex dump per record

### 🔬 Hex Viewer
- Read any file, volume (\\.\C:), or physical drive (\\.\PhysicalDrive0)
- Configurable offset and length
- Offset navigation in bytes; useful for reading boot sectors, MBR, VBR, etc.
- Classic hex+ASCII columnar dump

---

## Project Structure

```
DiskInspector/
├── DiskInspector.csproj
├── app.manifest              ← UAC elevation + DPI awareness
├── Program.cs
├── Models/
│   └── Models.cs             ← All data structures
├── Core/
│   ├── NativeMethods.cs      ← P/Invoke (kernel32, advapi32)
│   ├── DiskEnumerator.cs     ← WMI disk/partition/volume enumeration
│   ├── NtfsReader.cs         ← Raw MFT parser + hex dump
│   └── FileSystemBrowser.cs  ← Directory listing + background indexer
└── UI/
    └── MainForm.cs           ← Full WinForms dark UI
```
