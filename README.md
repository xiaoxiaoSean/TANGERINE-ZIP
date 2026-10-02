<p align="right">English · <a href="README.zh-CN.md">简体中文</a></p>

<p align="center">
  <img src="TANGERINE-ZIP/Resources/TZIP.png" alt="TANGERINE ZIP logo" width="110">
  &nbsp;&nbsp;&nbsp;
  <img src="README-assets/tangerine.png" alt="Tangerine illustration" width="86">
  &nbsp;&nbsp;&nbsp;
  <img src="TANGERINE-ZIP/Resources/Kiro.png" alt="Kiro, the TANGERINE ZIP mascot" width="110">
</p>

<h1 align="center">TANGERINE ZIP</h1>

<p align="center">A Windows archive manager with a WPF interface and command-line tools.</p>

<p align="center">
  <a href="https://github.com/xiaoxiaoSean/TANGERINE-ZIP/releases/tag/v2.1.5">Download tanevo v2.1.5 pre-release</a> ·
  <a href="RELEASE_NOTES_v2.1.5.md">What's new in v2.1.5</a> ·
  <a href="dev_doc1/README.md">Developer documentation</a>
</p>

## Features

- Create, browse, and extract ZIP, 7z, TAR, GZ, BZ2, XZ, LZ4, ZSTD, LZip, ISO, WIM, ARJ, ACE, ARC, and Unix compress (`.Z`) files. Browse and extract RAR archives; creating RAR archives requires the official `rar.exe` beside the application. ACE creation uses interoperable stored entries, so it does not reduce file size. Classic ARC creation accepts only flat ASCII names of at most 12 characters.
- ISO creation has its own filesystem and El Torito BIOS boot settings, including volume and manufacturer IDs, Joliet, identical-file sharing, boot image, emulation mode, load segment, and ISOLINUX boot information table. The current writer supports one BIOS boot entry; it does not create UEFI or hybrid USB boot images.
- Preview text and images inside supported archives. Search entries, inspect their details, and extract a single entry or an entire archive.
- Test archive integrity, inspect or verify hashes, recover readable files from damaged archives, and change filename encoding for archives with garbled names.
- Configure compression for ZIP, 7z, and RAR, including split volumes, solid mode, exclusions, RAR recovery records, and saved profiles. The always-visible **Archive tools → Create self-extracting EXE** dialog accepts any archive the app can extract and creates a 7z-based Windows EXE; it can use the currently open archive. Edit entries in eligible ZIP, 7z, and TAR archives with backup protection; add or replace files in ordinary ZIP, 7z, and RAR archives (RAR requires `rar.exe`).
- Batch extract and convert archives, edit ZIP comments, save passwords in Windows Credential Manager, and create verified archive snapshots with optional retention. Scan a selected archive with Microsoft Defender when that component is available.
- Use the Windows 11 Explorer context menu, customizable colors and mouse effects, and the built-in command-line tools. Run `help` to list commands and `help <command>` for options.
- Under **System settings → Default apps**, register supported archive formats, try automatic current-user defaults, or remove this app's defaults and registrations. Windows may block automatic association changes on protected versions.

Some operations depend on the archive format and its contents. Encrypted and split archives cannot be updated in place, and password-protected conversion outputs are limited to ZIP and 7z. Microsoft Defender scanning requires an available Defender installation; the app treats scanner errors as failures.

## Download and first run

Download one Windows x64 executable from the [tanevo v2.1.5 pre-release](https://github.com/xiaoxiaoSean/TANGERINE-ZIP/releases/tag/v2.1.5):

| File suffix | Edition | Requirement |
| --- | --- | --- |
| `-sc.exe` | Self-contained | No separate .NET installation required. |
| `-fd.exe` | Framework-dependent | Install the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0). |

Run the executable. On the first launch, choose a writable temporary directory when prompted. You can change it later under **Settings → More**. Keep the executable in a writable location because application settings are stored beside it. To create RAR archives, obtain `rar.exe` under its own license and place it beside `TANGERINE-ZIP.exe`; other formats do not require it.

## Command line

The same executable opens the graphical interface with no arguments and accepts these commands in a terminal:

```powershell
.\TANGERINE-ZIP.exe help
.\TANGERINE-ZIP.exe compress "C:\Backup\photos.zip" "C:\Photos"
.\TANGERINE-ZIP.exe list "C:\Backup\photos.zip"
.\TANGERINE-ZIP.exe extract "C:\Backup\photos.zip" "C:\Restored"
```

Set the temporary directory through the graphical interface before using `compress`, `list`, or `extract`. For all options and exit behavior, see the [command-line guide](dev_doc1/command.md).

## Build from source

On Windows, install the .NET 10 SDK and run:

```powershell
dotnet build TANGERINE-ZIP/TANGERINE-ZIP.csproj -c Release
```

The project includes two publish profiles: `FolderProfile` for the self-contained build and `FolderProfile1` for the framework-dependent build. Their `PublishDir` values contain a local absolute path; change that path or override `PublishDir` when publishing on another machine.

## Documentation and license

- [v2.1.5 pre-release notes](RELEASE_NOTES_v2.1.5.md)
- [v2.1.1 release notes](RELEASE_NOTES_v2.1.1.md)
- [v2.1.0 release notes](RELEASE_NOTES_v2.1.0.md)
- [v2.0.0 release notes](RELEASE_NOTES_v2.0.0.md)
- [Developer documentation and format details](dev_doc1/README.md)
- [Command-line reference](dev_doc1/command.md)
- [MIT license](LICENSE)
