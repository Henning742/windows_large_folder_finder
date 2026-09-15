# NTFS Folder Finder

A small Windows desktop app that answers one question: *which folders on these drives are big
**and** full of files?*

It reads the master file table (MFT) of an NTFS volume directly, so it finds the answer in
seconds instead of walking millions of directories. No indexing service, no background
daemon, no dependencies to install.

## What it does

1. Tick one or more mounted NTFS volumes in the *Drives to scan* box. *Select all* ticks the lot.
   The ticks are remembered when you press *Refresh*, and the first run ticks the first drive, so
   the one-drive case is still a single click.
2. Press **Scan**. The app reads each volume's master file table in turn and applies two rules:
   - the folder is bigger than *N* MB (200 MB by default), and
   - more than *N* files sit **directly** inside it (200 by default).
3. Matched folders appear in the left pane as a folder tree, sorted by full path. Folders that
   matched are in **bold**; the plain rows above them are the folders on the way there, shown so
   the tree keeps its shape. Every folder has a triangle to expand or collapse it, and *Expand
   all* / *Collapse all* do the whole tree at once.
4. Click one and its contents appear on the right; select a file to see a preview. Images are
   shown inline, text files are shown as text, everything else shows its metadata. The app picks
   that first file for you, so the pane is never empty: *On opening a folder, select* at the top
   of the right pane chooses between the first file, the middle file, a random file, or nothing
   at all. Folders are skipped when it picks, so you land on something with a preview.
5. Type a note about a folder in the *Comment* box under the path. It is saved with the report.
6. **Export** writes the list to a `.csv` file, with a small `.meta.json` file next to it that
   records the volumes, the rules and how the scan went. **Import** reads a report back, notes
   included.

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

## The result files

**Export** writes two files side by side, and both are named after the report:

```
folders-D-20260915-1030.csv        the list itself, one folder per row
folders-D-20260915-1030.meta.json  where it came from and what the rules were
```

### The CSV

The first row is a header, and the columns are:

| Column | Meaning |
|---|---|
| `Path` | The folder's full path. |
| `Comment` | Whatever you typed in the *Comment* box for that folder. |
| `SizeBytes` / `Size` | The size the rules were tested against: the number, and the readable form. |
| `DirectFiles` | Files sitting directly inside the folder. |
| `Subfolders` | Folders sitting directly inside it. |
| `TotalFiles` | Files below it, at any depth. |
| `TotalSizeBytes` | Size of the folder and everything below it. |
| `Exists` | `yes`, or `no` for an imported folder that is gone. |

```
Path,Comment,SizeBytes,Size,DirectFiles,Subfolders,TotalFiles,TotalSizeBytes,Exists
D:\data\set1,checked 2026-09-14,314572800,300 MB,412,3,1590,1073741824,yes
```

Rows follow the order of the tree, so a folder comes right before the folders inside it. Quotes,
commas and line breaks inside a path or a note are quoted the way RFC 4180 asks for, and the file
is UTF-8 with a byte order mark so Excel reads notes in any language correctly.

### The JSON

Everything that does not belong in a spreadsheet lives in the JSON file: the volumes that were
read (all of them, when several drives were scanned), the rules, how long the scan took, how many
records it read, whether the master file tables were read in full, and any warnings. Its
`resultsFile` field points at the CSV **by relative path**, so the two files can be moved or
archived together:

```json
{
  "format": "NTFS Folder Finder results",
  "formatVersion": 1,
  "generatedAt": "2026-09-15T10:30:12+08:00",
  "resultsFile": "folders-D-20260915-1030.csv",
  "rules": { "minSizeBytes": 209715200, "minDirectFileCount": 200, "sizeIncludesSubfolders": true },
  "volumes": [ { "driveLetter": "D", "rootPath": "D:\\", "label": "Work" } ],
  "folderCount": 2,
  "scan": { "elapsedSeconds": 12.4, "recordsRead": 812345, "mftReadCompleted": true, "warnings": [] }
}
```

### Importing

A report is read back by column name, so a file that was edited or reordered still imports, and a
plain list of paths with no header at all works too. Blank rows, rows that start with `#` and
repeated paths are dropped. Importing recovers the paths and the notes only, so the app measures
each folder straight from the file system to fill in the size and file-count columns. A folder that
no longer exists is kept in the list and marked `not found`.

Reports written by older versions (one folder per line, text after `#` is a comment) are still
readable; they simply have no notes.

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
- When an `$ATTRIBUTE_LIST` spreads a file's attributes over several MFT records, the app follows
  the list and merges those records, so split `$DATA` and `$FILE_NAME` attributes are counted in
  full. The same is done for `$MFT` itself, so a fragmented master file table is read completely.
  The app only warns if such a list points at records that are missing or damaged.
- Deleted records are ignored. Hard links show up once per name. Compressed and sparse files
  count their logical size.
- The folder tree is kept in memory so the preview pane is instant. Budget roughly 100 to 200 MB
  of RAM per million files - and that is per scanned drive, since the tree of every drive that was
  scanned stays around so its folders can still be opened afterwards. Scan the drives one at a
  time if memory is tight; the result list and the report are the same either way.

## Project layout

| Path | What it is |
|---|---|
| `src/DataFinder.Core` | The scanning engine. Targets plain `net8.0` with no Windows-only code, so it builds and runs anywhere - including in the Linux CI job. |
| `src/DataFinder.Core/Ntfs` | Boot sector, data run list decoding, MFT record parsing, the record reader and the folder tree. |
| `src/DataFinder.Core/Results` | The report formats: the CSV that is written, the JSON file next to it, the older text list, and the tree the results are drawn as. |
| `src/DataFinder.App` | The WPF window (`net8.0-windows`), view models and services. Deliberately thin: it displays what the core produces. |
| `tests/DataFinder.Core.Tests` | xUnit tests, including a synthetic MFT record builder so the parser is tested without a real drive. |
| `build.yml`, `app.manifest` | The CI workflow and the app manifest (unelevated start, per-monitor DPI, long path aware). |

## Tests

```bash
dotnet test tests/DataFinder.Core.Tests/DataFinder.Core.Tests.csproj -c Release
```

92 tests cover the boot sector geometry, data run list decoding (including signed offsets and
sparse runs, multi-extent attributes and run lists that contain zero bytes), MFT record parsing
(update sequence fix-ups, DOS name filtering, hard links, corrupt records, attribute list
entries), resolving an `$ATTRIBUTE_LIST` across extension records (split `$DATA`, split
`$FILE_NAME`, cycles, missing records), the folder tree and rule evaluation, the human readable
size parser, the CSV and JSON report round trip (quoting, column lookup, the relative path between
the two files), the tree that the results are drawn as, and the choice of file to select on its own.
