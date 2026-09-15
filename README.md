# NTFS Folder Finder

A small Windows desktop app that answers one question: *which folders on these drives are big
**and** full of files?*

It reads the master file table (MFT) of an NTFS volume directly, so it finds the answer in
seconds instead of walking millions of directories. No indexing service, no background
daemon, no dependencies to install.

## What it does

1. Press **Scan...**. A dialog opens with everything a run needs: the mounted NTFS volumes as a
   list of ticks (*Select all* ticks the lot, *Refresh* looks for drives that were plugged in since
   the app started, and a refresh keeps the ticks that are still there) and the two rules. The
   dialog keeps what you set, so it is ready the next time it is opened.
2. Press **Scan** in the dialog. The app reads each ticked volume's master file table in turn and
   applies two rules:
   - the folder is bigger than *N* MB (200 MB by default), and
   - more than *N* files sit **directly** inside it (200 by default).
   The progress bar fills while it runs, and the line next to it says how much longer the whole
   run is going to take, worked out from how far it has got. Importing a report estimates the same
   way.
3. Matched folders appear in the left pane as a folder tree, sorted by full path. Folders that
   matched are in **bold**; the plain rows above them are the folders on the way there, shown so
   the tree keeps its shape. Every folder has a triangle to expand or collapse it, and *Expand
   all* / *Collapse all* do the whole tree at once.
4. Click one and its contents appear on the right; select a file to see a preview. Images are
   shown inline, text files are shown as text, and a raw recording (`.dat`, `.raw`, `.bin` and
   friends) is decoded into a picture - see *Reading data files*. Everything else shows its
   metadata. The app picks that first file for you, so the pane is never empty: *On opening a
   folder, select* at the top of the right pane chooses between the first file, the middle file, a
   random file, or nothing at all. Folders are skipped when it picks, so you land on something
   with a preview.
5. A data file is read with the *schematic* chosen next to it: the frame size, the header in front
   of each frame and the kind of numbers the pixels are. **Decode settings...** keeps that list of
   schematics and the file suffixes to try them on. Tick *Show every ticked schematic at once* and
   the file is drawn through all of them, side by side.
6. Type a note about a folder in the *Comment* box under the path. It shows up straight away in
   the *Comment* column of the result list. A note only leaves the app when a report is written,
   so while any note has not been exported yet the window title says so and a small *not exported
   yet* marker sits next to the box. Starting a scan, importing a report, clearing the list and
   closing the app all ask first while that marker is up.
7. **Export** writes the list to a `.csv` file, with a small `.meta.json` file next to it that
   records the volumes, the rules and how the scan went. The notes are written to the CSV's
   comment column and the marker goes away. **Import** reads a report back, notes included.

The status bar keeps a note of which drives and which rules the results on screen came from.

## Requirements

| | |
|---|---|
| To run the app | Windows 10 or 11, x64. The published build is self contained, so .NET does **not** have to be installed. |
| To scan a drive | Administrator rights. Windows refuses raw volume reads to unelevated processes. |
| To build from source | The .NET 8 SDK. |

The app starts unelevated and shows a *Restart as administrator* button in the main window when it
detects that it does not have the rights it needs. The *Scan...* dialog says the same thing in a
note, so the reminder is there while the drives are being picked too.

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

## Reading data files

A recording - `.dat`, `.raw`, `.bin`, `.data` - is not a picture, it is numbers. To show it, the
app has to be told how the numbers are laid out, and that is what a *schematic* is:

| Setting | What it says |
|---|---|
| Frame size | How many pixels across and down one frame is, before cropping. |
| Header | How many bytes sit in front of every frame and are not pixels. |
| Data type | What the values are: 8 bit grey, 8 bit colour, 16 bit, 14 bits inside 16 bits, a 16 bit frame with a packed 8 bit picture on its right hand side, or packed UYVY colour. |
| Stretch | For 16 bit data: pin the darkest 2% of the frame to black and the brightest 2% to white, which is what makes the picture visible at all. Untick it and the whole 16 bit range is spread over the greys, which is right when the signal really does fill it. |
| Crop | Rows and columns to throw away, for the odd edge line some cameras add: `1,1,0,0`. |
| Split column | Where the packed 8 bit part of a 16 bit frame starts. 0 means the middle. |
| Frame to show | Which frame of the recording to look at. 0 is the first one. |
| White speckle | Replace the white dots some cameras leave behind with the neighbouring sample. |

**Decode settings...** in the preview pane holds both halves of it: the file suffixes the preview
will try to decode, and the list of schematics. Suffixes are typed in freely - `.raw .bin` and
`raw, bin` mean the same thing - and a suffix only takes effect when nothing else already knows
the extension, so listing `.csv` by accident cannot take the text preview away from a CSV file.

The schematics can be added to, changed, duplicated and removed, and the set that comes with the
app can be brought back at any time. Changes apply as they are typed: the picture in the preview
pane follows along while a width is being worked out. Like the scan choices, they last as long as
the window is open rather than being written to disk. Those built-in ones are the recordings the
reference scripts were written for:

| Schematic | Layout |
|---|---|
| 8 bit grayscale 640 x 512 / 1280 x 720 | One byte per pixel, no header. |
| 8 bit colour 1920 x 1080 | Three bytes per pixel, red green blue. |
| 16 bit infrared 644 x 514, cropped | 16 bit grey, stretched, with `1,1,4,0` cropped off. |
| 16 bit grayscale 640 x 480 / 640 x 512, stretched | The usual headerless 16 bit dump, stretched. |
| 16 bit grayscale 640 x 512, as it is | The same, with the stretch off. |
| 14 bit inside 16 bit 640 x 514 (64 byte header) | 16 bit values that only use their lower 14 bits. |
| 16 bit + packed 8 bit 960 x 514 (64 byte header) | The wide recording: the 16 bit rows carry a packed 8 bit picture on their right hand side. |
| Colour 1920 x 540 (UYVY) | Packed colour, with and without the white speckle filter. |

Only one frame is read per look - the frame the schematic asks for - and it is read where it sits
in the file, so a multi gigabyte recording opens as quickly as a small one. The picture is drawn
at the size it comes out of the decoder; the pane fits it to the space available.

Trying the schematics out one at a time meant going back to the dialog for every folder, so the
preview pane has *Show every ticked schematic at once*. With it on, the file is read through every
ticked schematic and the pictures are drawn side by side, each labelled with its name and size. A
schematic that cannot read the file gets a tile of its own that says why - usually that the file is
shorter than one frame of that size - so one glance says which schematics fit the recording.

Two details differ from the reference Python script on purpose. Packed colour is converted with the
ordinary BT.601 coefficients and the usual red, green, blue order; the script used a sign turned
around in the green channel and left the colours in the order the data happened to be in. And its
`resize` step is left out, because the preview scales the picture to the pane instead.

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
- Decoding is meant for looking, not for converting: one frame at a time, no files written, and a
  schematic whose frame works out to more than 256 MB is refused rather than read. Showing ten
  schematics at once costs about a fifth of a second on a 960 x 514 recording.

## Project layout

| Path | What it is |
|---|---|
| `src/DataFinder.Core` | The scanning engine. Targets plain `net8.0` with no Windows-only code, so it builds and runs anywhere - including in the Linux CI job. |
| `src/DataFinder.Core/Ntfs` | Boot sector, data run list decoding, MFT record parsing, the record reader and the folder tree. |
| `src/DataFinder.Core/Preview/Raw` | The data file decoder: the schematics, the frame reader and the pixel conversions, plus the set of schematics that ships with the app. |
| `src/DataFinder.Core/Results` | The report formats: the CSV that is written, the JSON file next to it, the older text list, and the tree the results are drawn as. |
| `src/DataFinder.App` | The WPF windows (`net8.0-windows`) - the main window, the *Scan...* dialog and the *Decode settings* dialog - plus the view models and services. Deliberately thin: it displays what the core produces. |
| `tests/DataFinder.Core.Tests` | xUnit tests, including a synthetic MFT record builder so the parser is tested without a real drive. |
| `build.yml`, `app.manifest` | The CI workflow and the app manifest (unelevated start, per-monitor DPI, long path aware). |

## Tests

```bash
dotnet test tests/DataFinder.Core.Tests/DataFinder.Core.Tests.csproj -c Release
```

177 tests cover the boot sector geometry, data run list decoding (including signed offsets and
sparse runs, multi-extent attributes and run lists that contain zero bytes), MFT record parsing
(update sequence fix-ups, DOS name filtering, hard links, corrupt records, attribute list
entries), resolving an `$ATTRIBUTE_LIST` across extension records (split `$DATA`, split
`$FILE_NAME`, cycles, missing records), the folder tree and rule evaluation, the human readable
size parser, the CSV and JSON report round trip (quoting, column lookup, the relative path between
the two files), the tree that the results are drawn as, the choice of file to select on its own,
the estimate of how much longer a scan will take, and the data file decoder: every layout, the
stretch, the crop, the header and frame skipping, packed colour, the white speckle filter, the
schematics the app ships with, and the list of file suffixes the preview decodes.
