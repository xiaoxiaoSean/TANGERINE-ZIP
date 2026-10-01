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
  <a href="https://github.com/xiaoxiaoSean/TANGERINE-ZIP/releases/latest">Download the latest release</a> ·
  <a href="RELEASE_NOTES_v2.1.0.md">What's new in v2.1.0</a> ·
  <a href="dev_doc1/README.md">Developer documentation</a>
</p>

## Features

- Create, browse, and extract ZIP, 7z, TAR, GZ, BZ2, XZ, LZ4, ZSTD, ISO, and WIM archives. Browse and extract RAR archives; creating RAR archives requires the official `rar.exe` beside the application.
- Preview text and images inside supported archives. Search entries, inspect their details, and extract a single entry or an entire archive.
- Test archive integrity, inspect or verify hashes, recover readable files from damaged archives, and change filename encoding for archives with garbled names.
- Configure compression for ZIP, 7z, and RAR, including supported advanced options and split volumes. Edit entries in eligible ZIP, 7z, and TAR archives with backup protection.
- Use the Windows 11 Explorer context menu, customizable colors and mouse effects, and the built-in `help`, `compress`, `extract`, and `list` commands.
- Under **System settings → Default apps**, register supported archive formats, try automatic current-user defaults, or remove this app's defaults and registrations. Windows may block automatic association changes on protected versions.

Some operations depend on the archive format and its contents. The application explains unavailable options in the interface.

## Download and first run

Download one Windows x64 executable from the [latest release](https://github.com/xiaoxiaoSean/TANGERINE-ZIP/releases/latest):

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

- [v2.1.0 release notes](RELEASE_NOTES_v2.1.0.md)
- [v2.0.0 release notes](RELEASE_NOTES_v2.0.0.md)
- [Developer documentation and format details](dev_doc1/README.md)
- [Command-line reference](dev_doc1/command.md)
- [MIT license](LICENSE)
