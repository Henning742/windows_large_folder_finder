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
   the tree keeps its shape. Every row shows a size, including the plain ones: a folder that did
   not match shows the sum of the folders below it that did, so a drive whose matches add up to two
   terabytes says so instead of showing an empty cell. Every folder has a triangle to expand or
   collapse it, and *Expand all* / *Collapse all* do the whole tree at once.
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
8. **Web page...** writes the whole list out as one HTML file with a few thumbnails of what is
   inside each folder, and the thumbnails themselves as files beside it - see *The web page*.

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

## The web page

**Web page...** writes everything on screen into one `.html` file, with the thumbnails in a folder
beside it: no internet and no program needed to open it. It is for the moment the list has to go to
somebody else - or to a later version of yourself - and the question is still the one the app
answers: *which of these folders is worth a look inside?*

The page is small, because the pictures are not carried inside it. Every thumbnail is written as a
file of its own in a `<page name>.files` folder next to the page, and the page points at it, so a
report over hundreds of folders opens as quickly as a report over three. Copy the page and that
folder together to send the report on; opening the page on its own still shows every folder, size
and note, with the places for the pictures left empty.

The page is a tree of every row on the left and a section per folder that matched on the right:

- The tree is the same one the window shows, parents and all, and every row carries its size. A
  folder that did not match shows the sum of the folders below it, so the way to a match still says
  how much is down there. Clicking a row opens what it leads to, so a click anywhere lands on a
  folder with something in it.
- Each section says the full path, the size, the file counts and the note you typed, and shows a
  few pictures of what is inside.
- Everything folds away: the sections, and the branches of the tree itself. *Expand all* and
  *Collapse all* at the top do the lot.

The pictures are picked at random from the pictures and the recordings directly inside a folder, so
two runs over the same folder do not have to look the same. A picture file is carried as it is; a
recording is decoded with the first of the schematics ticked in *Decode settings* that manages it,
and the caption says which one that was - a thumbnail never leaves you guessing how it was read. A
folder with nothing showable says so rather than showing nothing.

A run is bounded, so that a list of hundreds of folders ends and the file stays sendable:

| Bound | How much |
|---|---|
| Pictures per folder | 6 |
| Candidates looked for per folder | 40 |
| Files looked at per folder | 4,000 |
| One picture | 4 MB |
| All the pictures together | 48 MB |

Whatever a bound leaves out is said under the folder. The rest costs almost nothing: the folders of
a scan are read from the index the app already has in memory, so only imported lists make it walk
the disk again. Twelve folders with five pictures each come out as about 20 KB of page and sixty
files beside it in well under a second; the progress bar, the estimate and *Cancel* cover a list big
enough to take real time.

The notes travel with the page, but writing one does not count as exporting them: the *not exported
yet* marker stays up until the CSV is written.

## Reading data files

A recording - `.dat`, `.raw`, `.bin`, `.data` - is not a picture, it is numbers. To show it, the
app has to be told how the numbers are laid out, and that is what a *schematic* is:

| Setting | What it says |
|---|---|
| Frame size | How many pixels across and down one frame is, before cropping. |
| Header | How many bytes sit in front of every frame and are not pixels. |
| Data type | What the values are: 8 bit grey, 8 bit colour, 16 bit, 14 bits inside 16 bits, a 16 bit frame with a packed 8 bit picture on its right hand side, or packed UYVY colour. |
| Crop | Rows and columns to throw away, for the odd edge line some cameras add: `1,1,0,0`. |
| Split column | Where the packed 8 bit part of a 16 bit frame starts. 0 means the middle. |
| Frame to show | Which frame of the recording to look at. 0 is the first one. |

Stretching is not one of them, because it is not a property of the recording - it is how the
picture is *looked at*. The *Stretch* tick in the preview pane pins the darkest 2% of the frame to
black and the brightest 2% to white, and it starts where it makes sense: **16 bit data starts
stretched**, because its values fill only the first percent or two of the range and the picture
would be black otherwise; **8 bit data starts as it is**, because it already uses the whole range.
Ticking it the other way is a look rather than a setting, so it goes back to the start the moment
another file or another schematic is picked. When several schematics are on show and they disagree
- a 16 bit one next to an 8 bit one - the tick starts on if any of them is 16 bit, because that is
the one that would otherwise be black; the tile captions say which ones were stretched.

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
| 8 bit grayscale 640 x 512 | One byte per pixel, no header. |
| 16 bit grayscale 640 x 512 | The headerless 16 bit dump an ordinary frame grabber writes. |
| 16 bit infrared 644 x 514, cropped | 16 bit grey with the odd edge lines cropped off: `1,1,4,0`. |
| 14 bit inside 16 bit 640 x 514 (64 byte header) | 16 bit values that only use their lower 14 bits. |
| 16 bit + packed 8 bit 960 x 514 (64 byte header) | The wide recording: the 16 bit rows carry a packed 8 bit picture on their right hand side. |
| Colour 1920 x 540 (UYVY) | Packed colour. |

That is one per kind of recording rather than one per camera, and every layout the decoder knows is
a *Duplicate* and a drop down away.

Only one frame is read per look - the frame the schematic asks for - and it is read where it sits
in the file, so a multi gigabyte recording opens as quickly as a small one. The picture is drawn
at the size it comes out of the decoder; the pane fits it to the space available.

Trying the schematics out one at a time meant going back to the dialog for every folder, so the
preview pane has *Show every ticked schematic at once*. With it on, the file is read through every
ticked schematic and the pictures are drawn side by side, each labelled with its name and size. A
schematic that cannot read the file gets a tile of its own that says why - usually that the file is
shorter than one frame of that size - so one glance says which schematics fit the recording. The
*Stretch* tick applies to every tile at once, and each tile says whether it was stretched.

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
  full. The same is done for `$MFT` itself, and going round in passes, because an extension record
  can only be found once the extent that covers it is known. A list that does not fit in its record
  - the layout of a very fragmented table - is read through its own data runs like any other
  attribute. What is left is a warning: a list that points at records which are missing or damaged.
- **Damage is skipped rather than given up on.** A hole between the runs of the table, or a stretch
  of it the volume will not read, no longer ends the scan: the reader looks ahead for the first
  record that does come back and carries on there, so folders on the far side are still found. Each
  read is also tried more than once, a failed read is asked for in shorter and shorter pieces, and
  what could not be read is reported - how many records, and whether the runs stopped short or the
  volume refused - so an incomplete list says which of the two it was. Records whose name could not
  be read and records too damaged to read are counted in the report as well, instead of quietly
  going missing.
- Deleted records are ignored. Hard links show up once per name. Compressed and sparse files
  count their logical size.
- The folder tree is kept in memory so the preview pane is instant. Budget roughly 100 to 200 MB
  of RAM per million files - and that is per scanned drive, since the tree of every drive that was
  scanned stays around so its folders can still be opened afterwards. Scan the drives one at a
  time if memory is tight; the result list and the report are the same either way.
- Decoding is meant for looking, not for converting: one frame at a time, no files written, and a
  schematic whose frame works out to more than 256 MB is refused rather than read. Showing ten
  schematics at once costs about a fifth of a second on a 960 x 514 recording.
- A web page carries its pictures as files in a folder beside it, so the page and that folder travel
  together. The bounds in *The web page* are what keeps a run in hand; a folder with more in it than
  a bound allows is marked as such instead of quietly dropping the rest.

## Project layout

| Path | What it is |
|---|---|
| `src/DataFinder.Core` | The scanning engine. Targets plain `net8.0` with no Windows-only code, so it builds and runs anywhere - including in the Linux CI job. |
| `src/DataFinder.Core/Ntfs` | Boot sector, data run list decoding, MFT record parsing, the record reader and the folder tree. |
| `src/DataFinder.Core/Preview/Raw` | The data file decoder: the schematics, the frame reader and the pixel conversions, plus the set of schematics that ships with the app. |
| `src/DataFinder.Core/Results` | The report formats: the tree the results are drawn as, the CSV that is written with the JSON file next to it, the older text list, and the web page with the code that gathers its thumbnails and writes them beside it. |
| `src/DataFinder.App` | The WPF windows (`net8.0-windows`) - the main window, the *Scan...* dialog and the *Decode settings* dialog - plus the view models and services. Deliberately thin: it displays what the core produces. |
| `tests/DataFinder.Core.Tests` | xUnit tests, including a synthetic MFT record builder and a whole NTFS volume built in memory, so parsing and scanning are tested without a real drive. |
| `build.yml`, `app.manifest` | The CI workflow and the app manifest (unelevated start, per-monitor DPI, long path aware). |

## Tests

```bash
dotnet test tests/DataFinder.Core.Tests/DataFinder.Core.Tests.csproj -c Release
```

225 tests cover the boot sector geometry, data run list decoding (including signed offsets and
sparse runs, multi-extent attributes and run lists that contain zero bytes), MFT record parsing
(update sequence fix-ups, DOS name filtering, hard links, corrupt records, attribute list
entries), resolving an `$ATTRIBUTE_LIST` across extension records (split `$DATA`, split
`$FILE_NAME`, cycles, missing records), reading a table whole when an extension record only comes
into reach after an extent has been merged, carrying on past a hole between its runs and past a
stretch the volume will not read, a list of the table's own that is not in a record, reading an
attribute straight through its runs, the folder tree and rule evaluation, the human readable
size parser, the CSV and JSON report round trip (quoting, column lookup, the relative path between
the two files), the tree that the results are drawn as, the choice of file to select on its own,
the estimate of how much longer a scan will take, and the data file decoder: every layout, the
stretch (including the one 8 bit data gets when it is asked for, and the crop it is worked out
from), the header and frame skipping, packed colour, the schematics the app ships with, and the
list of file suffixes the preview decodes. The web page has its own set: the tree and the sizes it
rolls up, the escaping, the pictures named as files beside the page, and the PNG writer, which is checked by
unpacking what it wrote the way a browser would.
