# NTFS Folder Finder

A small Windows desktop app that answers one question: *which folders on this drive are big
**and** full of files?*

It reads the master file table (MFT) of an NTFS volume directly, so it finds the answer in
seconds instead of walking millions of directories. No indexing service, no background
daemon, no dependencies to install.

## What it does

1. Pick a mounted NTFS volume from the drop down.
2. Press **Scan**. The app reads the volume's master file table and applies two rules:
   - the folder is bigger than *N* MB (200 MB by default), and
   - more than *N* files sit **directly** inside it (200 by default).
3. Matched folders appear in the left pane, sorted by size.
4. Click one and its contents appear on the right; select a file to see a preview. Images are
   shown inline, text files are shown as text, everything else shows its metadata.
5. **Export** writes the list to a `.txt` file; **Import** reads one back.

Both rule values are editable in the *Rules* box, and both are re-applied instantly to the
next scan.

## Requirements

| | |
|---|---|
| To run the app | Windows 10 or 11, x64. The published build is self contained, so .NET does **not** have to be installed. |
| To scan a drive | Administrator rights. Windows refuses raw volume reads to unelevated processes. |
| To build from source | The .NET 8 SDK. |

The app starts unelevated and shows a *Restart as administrator* button when it detects that it
does not have the rights it needs.

## Building

```powershell
# on Windows
dotnet build DataFinder.sln -c Release
dotnet run --project src/DataFinder.App

# the same single-file executable the CI workflow produces
dotnet publish src/DataFinder.App/DataFinder.App.csproj -c Release -r win-x64 `
    --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=embedded -o publish
```

The output is a single `DataFinder.exe` (about 63 MB) that runs on any 64-bit Windows 10/11
machine. It is unsigned, so the first launch shows a SmartScreen warning unless the download is
signed or the file is unblocked.

### Building on Linux or macOS

`Directory.Build.props` sets `EnableWindowsTargeting`, so the whole solution - including the
WPF project and its XAML - compiles on Linux:

```bash
dotnet build DataFinder.sln -c Release
dotnet test tests/DataFinder.Core.Tests/DataFinder.Core.Tests.csproj -c Release
```

The window itself cannot be launched off Windows; the build and the tests are the useful part.

## Continuous integration

`.github/workflows/build.yml` runs two jobs on every push and pull request:

| Job | Runner | What it does |
|---|---|---|
| `core-tests` | `ubuntu-latest` | Builds and runs the unit tests for the scanning engine. |
| `windows-app` | `windows-latest` | Builds the whole solution, runs the tests again, then publishes the self-contained `DataFinder.exe` and uploads it as the `DataFinder-win-x64` artifact. |

The workflow lives at the repository root and runs every command from there, so there is
nothing to configure. Download the built `DataFinder.exe` from the *Artifacts* section of a
workflow run.

## The result file format

One folder per line, and everything after a `#` is a comment:

```
# NTFS Folder Finder results
# generated: 2026-09-14 14:30:12 +08:00
# volume: D: Work
# rules: size > 200 MB (whole folder) and more than 200 files directly inside
# folders: 2
# format: one folder per line. Text after '#' is a comment. A '#' inside a path is written as '\#'.
D:\data\set1
D:\data\set2\raw     # checked 2026-09-14
```

Blank lines, comments and duplicate paths are ignored when reading. A `#` that is really part of
a path is written as `\#` on export and read back as `#`.

Importing only recovers the folder paths, so the app measures each folder straight from the file
system to fill in the size and file-count columns. A folder that no longer exists is kept in the
list and marked `not found`.

## What "size" means

Two details are worth knowing, because they decide what shows up:

- **Sizes come from the MFT, not from the disk.** A folder's size is the sum of the real size of
  the files in it, which is the number Windows Explorer shows. It matches what you would get by
  sorting folders by size in Explorer, not the `Size on disk` column.
- **The size rule can include or ignore subfolders.** Tick *Folder size includes everything in
  its subfolders* (the default) and a folder qualifies on its total size. Clear it and only the
  files sitting directly in the folder count. The file-count rule always counts only the files
  directly inside the folder.

## Limits

- Only local NTFS volumes are offered. FAT32, exFAT, network shares and non-Windows file systems
  cannot be read this way.
- A **BitLocker locked** volume cannot be scanned; Windows refuses the read and the app reports it.
- If `$MFT` itself uses an `$ATTRIBUTE_LIST` (only on volumes with an extremely fragmented master
  file table) the app warns you: a handful of folders may be missing from the results.
- Deleted records are ignored. Hard links show up once per name. Compressed and sparse files
  count their logical size.
- The folder tree is kept in memory so the preview pane is instant. Budget roughly 100 to 200 MB
  of RAM per million files.

## Project layout

| Path | What it is |
|---|---|
| `src/DataFinder.Core` | The scanning engine. Targets plain `net8.0` with no Windows-only code, so it builds and runs anywhere - including in the Linux CI job. |
| `src/DataFinder.Core/Ntfs` | Boot sector, data run list decoding, MFT record parsing, the record reader and the folder tree. |
| `src/DataFinder.Core/Results` | The text import/export format. |
| `src/DataFinder.App` | The WPF window (`net8.0-windows`), view models and services. Deliberately thin: it displays what the core produces. |
| `tests/DataFinder.Core.Tests` | xUnit tests, including a synthetic MFT record builder so the parser is tested without a real drive. |
| `build.yml`, `app.manifest` | The CI workflow and the app manifest (unelevated start, per-monitor DPI, long path aware). |

## Tests

```bash
dotnet test tests/DataFinder.Core.Tests/DataFinder.Core.Tests.csproj -c Release
```

46 tests cover the boot sector geometry, data run list decoding (including signed offsets and
sparse runs), MFT record parsing (update sequence fix-ups, DOS name filtering, hard links,
corrupt records), the folder tree and rule evaluation, the human readable size parser, and the
text format round trip.

