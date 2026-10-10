---
date: 2026-10-10T12:00:00+03:00
planner: Claude
git_commit: a9c9bc90959306950311cb605ef842ec0873a3a0
branch: main
repository: Thumbly
plan_type: "implementation"
topic: "Folder icons: assign, track, crop, remove background"
tags: [implementation, planning, folder-icons, desktop-ini, catalog, crop, background-removal, winforms]
status: complete
last_updated: 2026-10-10
last_updated_by: Claude
---
# Folder Icons Implementation Plan

**Date**: 2026-10-10 12:00 +03:00
**Planner**: Claude
**Git Commit**: a9c9bc90959306950311cb605ef842ec0873a3a0
**Branch**: main
**Repository**: Thumbly

## Overview

Thumbly today turns an image into a `.ico`. This plan adds a second job: set an image as the icon of a
Windows folder, keep a copy of every icon in the central `.ico` folder, track every assigned folder in a
catalog with a list view (status, reapply, remove), add an Explorer right-click entry on folders, and add an
optional "adjust" dialog with a square crop and a flood-fill background remover. Everything stays inside
.NET Framework 4.8 and GDI+: no NuGet packages, the exe stays a single small file, nothing runs that is
slower than a few hundred milliseconds.

Terminology: the Thumbly tracking file is called the **catalog** throughout, to avoid confusion with the
Windows Registry (which only the context-menu feature touches).

## Overview - Action List

* Phase 1 — Folder icon core (no UI)
  - `FolderIconService`: write/remove `desktop.ini` + in-folder ico, folder attribute, Explorer notify
  - `FolderCatalog`: entries, load/save, status check
  - `FolderAssigner`: image + folder → ico (central + in-folder) + catalog entry
  - `Program`: `--folder <path>` argument
  - Unit tests for all three classes
* Phase 2 — Assignment in the drop view + Explorer context menu
  - Drop zone accepts folders; armed-folder state; Browse link; Escape
  - Image + folder in one drop; Ctrl+V onto an armed folder
  - `ShellIntegration` (HKCU context menu) + settings checkbox
  - Status messages and reveal
* Phase 3 — Folders view
  - `FoldersPanel` list view with icon, name, path, status
  - Toolbar: Add folder, Rescan, filter; footer: count, Reapply, Remove
  - Row context menu: Reveal folder, Reveal icon, Change image, Reapply, Remove icon, Locate, Forget
  - Drop image onto a row; view toggle in MainForm; remembered window size
* Phase 4 — Image pipeline: crop + flood-fill background removal
  - `IconSource` (image path, normalized crop, remove-background, tolerance)
  - `ImagePipeline.Prepare`: downscale, crop, remove background → bitmap for `IcoWriter`
  - `BackgroundRemover`: edge-seeded flood fill with tolerance and one-pixel feather
  - Unit tests
* Phase 5 — Adjust dialog
  - `AdjustForm`: square crop overlay, zoom slider, remove-background checkbox + tolerance, 256/48/16 previews
  - Setting "Always adjust before applying"; Shift on drop forces it; "Adjust…" row action
  - README update

## Current State Analysis

- `src/MainForm.cs` is a single drop zone. `GetDroppedFiles` (`MainForm.cs:146`) filters dropped paths with
  `Converter.IsSupported`, so folders are silently dropped today. State is `_lastPreview`, `_lastResult`,
  `_hover`. Top bar is one `LinkLabel` ("Settings"), bottom is a clickable status label.
- `src/Converter.cs` does file → ico via `WriteIcon` (`Converter.cs:56`) and `UniquePath` (never overwrites).
  `LoadImage` reads through a `MemoryStream` so source files are never locked.
- `src/IcoWriter.cs` pads to a square (`ToSquare`) and resamples each size with GDI+ (`Resize`), writing
  PNG-compressed layers. It takes any `Image`, so a cropped/cleaned bitmap can be fed in unchanged.
- `src/Settings.cs` is a hand-written key=value file in `%AppData%\Thumbly\settings.txt`. The comment at
  `Settings.cs:8` records the deliberate choice to avoid JSON dependencies on net48. The catalog follows the
  same approach.
- `src/Program.cs` treats any argument as a file to convert headlessly (`Program.cs:12`). A `--folder` switch
  must be recognised before that branch.
- `src/SettingsForm.cs` is a `TableLayoutPanel` with dir rows, size checkboxes and two toggles. New settings
  slot in as more rows.
- Tests (`tests/`) are xUnit, each class works in its own temp folder created in the constructor and deleted
  in `Dispose` (`ConverterAndSettingsTests.cs:10-15`). New tests follow this pattern exactly, and never touch
  anything outside their own temp folder (or, for the context-menu test, outside a test-only HKCU subkey).
- The project is `AnyCPU`, `net48`, no package references. Keep it that way.

### Windows facts the design relies on

- A folder shows a custom icon when it contains a `desktop.ini` with
  `[.ShellClassInfo]` / `IconResource=<path>,<index>` and the folder has the ReadOnly or System attribute.
  Explorer's own "Customize folder" uses ReadOnly, so Thumbly does too. `desktop.ini` itself must be
  Hidden+System or Explorer shows it.
- `IconResource` accepts a path relative to the folder, so `IconResource=thumbly-icon.ico,0` keeps working
  after the folder is moved or renamed.
- `GetPrivateProfileString` / `WritePrivateProfileString` (kernel32) read and merge INI files, preserving
  other sections (`LocalizedResourceName`, `InfoTip`, etc.) that an existing `desktop.ini` may contain.
  Writing `null` as the value deletes a key; `null` as the key deletes a section.
- `SHChangeNotify(SHCNE_UPDATEITEM, SHCNF_PATHW, folder, null)` (shell32) tells Explorer the folder changed.
  Explorer's icon cache can still lag; the status line tells the user to press F5 if needed.
- OneDrive and some sync tools clear the ReadOnly attribute on folders. This is why the catalog has a status
  check and a Reapply action.

## Desired End State

- Dropping a folder onto Thumbly arms it; dropping or pasting an image then sets that folder's icon.
  Dropping an image and a folder together does it in one step. Right-clicking a folder in Explorer offers
  "Set icon with Thumbly" (when enabled in settings).
- Every assignment writes: `<IconDirectory>\<FolderName>.ico` (central copy, unique-named), a hidden+system
  `thumbly-icon.ico` inside the folder, a merged `desktop.ini`, the ReadOnly attribute, and a catalog entry
  in `%AppData%\Thumbly\folders.txt`.
- A "Folders" view lists all catalog entries with a live status (OK / Folder missing / Needs reapply) and
  actions; Rescan refreshes statuses; Reapply fixes drift; Remove icon restores the default folder look
  without touching anything Thumbly did not create.
- Optionally an Adjust dialog crops to a square and removes a flat background before the ico is built, with
  small-size previews. The crop and tolerance are stored so Adjust can be reopened later.
- `dotnet build -c Release` has no warnings, `dotnet test` passes, exe remains a single file with no
  dependencies.

### Key Discoveries

- `MainForm.GetDroppedFiles` (`src/MainForm.cs:146`) is the single filter point for drops; extending it to
  classify images vs directories covers drop, and `PasteFromClipboard` (`src/MainForm.cs:160`) covers paste.
- `IcoWriter.Write(Image, Stream, sizes)` (`src/IcoWriter.cs:15`) is already the right seam for a prepared
  bitmap; nothing in it needs to change for crop/background removal.
- `Converter.UniquePath` (`src/Converter.cs:78`) gives central-copy naming for free.
- `Settings.Load/Save` (`src/Settings.cs:31,61`) is a switch over keys; new keys need a case each and a line
  each. Unknown keys are ignored, so older settings files keep loading.

## What We're NOT Doing

- No ML background removal (ONNX Runtime, BiRefNet). The pipeline exposes one seam
  (`BackgroundRemover.Remove(Bitmap, int)`) so a later optional implementation can slot in, nothing more.
- No single-instance handling: launching from the context menu while Thumbly is open opens a second window.
- No automatic disk search to relocate moved folders; the user points at the new location with Locate.
- No setting to point `desktop.ini` at the central copy instead of the in-folder copy. The in-folder copy is
  the only mode; the central copy is a library/backup.
- No changes to the existing image → ico flow, its naming, or its settings.
- No third-party packages, no change of target framework or platform.
- No icon for the Thumbly exe itself or installer work.

## Implementation Approach

Composition of small classes with no dependencies on each other beyond data records:

| Class | File | Responsibility |
|---|---|---|
| `FolderIconService` | `src/FolderIconService.cs` | Everything Windows-specific: desktop.ini merge, in-folder ico, attributes, `SHChangeNotify`. Pure functions over a folder path. |
| `FolderCatalog` + `FolderEntry` | `src/FolderCatalog.cs` | The tracking file. Load/save, add/update/remove, `Check(entry)` → status. |
| `IconSource` | `src/IconSource.cs` | Record: image path, normalized crop, remove-background flag, tolerance. Serialised into the catalog entry. |
| `ImagePipeline` | `src/ImagePipeline.cs` | `Prepare(Image, IconSource) → Bitmap`: downscale, crop, remove background. |
| `BackgroundRemover` | `src/BackgroundRemover.cs` | Flood fill from the edges, tolerance, feather. |
| `FolderAssigner` | `src/FolderAssigner.cs` | Orchestrates: load image → pipeline → central ico → in-folder ico → service → catalog. Also `Reapply`, `RemoveIcon`. |
| `ShellIntegration` | `src/ShellIntegration.cs` | Add/remove the HKCU context-menu key; `IsInstalled`. |
| `FoldersPanel` | `src/FoldersPanel.cs` | The list view (UserControl). Raises events; does no file work itself. |
| `AdjustForm` | `src/AdjustForm.cs` | The crop/background dialog. |
| `MainForm` | `src/MainForm.cs` | View toggle, armed-folder state, drop classification. |

Phases are ordered so each one is usable on its own: after Phase 1 the feature works from the command line
and tests; after Phase 2 it works from the drop window and Explorer; Phase 3 adds management; Phases 4–5 add
the nice-to-haves.

---

## Phase 1: Folder icon core (no UI)

### Overview

The Windows mechanics, the catalog, and the orchestration, fully unit-tested, plus a `--folder` argument so
the result can be exercised before any UI exists.

### Changes Required

#### 1. `FolderIconService`

**File**: `src/FolderIconService.cs` (new)
**Changes**: Static class. Public API:

```csharp
public static class FolderIconService
{
    public const string IconFileName = "thumbly-icon.ico";
    const string ShellSection = ".ShellClassInfo";
    const string ThumblySection = "Thumbly";

    /// Copies icoSource into the folder as hidden+system IconFileName, writes/merges desktop.ini,
    /// sets ReadOnly. Returns whether ReadOnly was newly set (so Remove can undo exactly that).
    public static bool Apply(string folder, string icoSource, string id);

    /// Deletes IconResource/IconFile/IconIndex keys and the [Thumbly] section, deletes the in-folder ico,
    /// deletes desktop.ini when it has no other keys left, clears ReadOnly when clearReadOnly.
    public static void Remove(string folder, bool clearReadOnly);

    /// True when desktop.ini points at IconFileName, the file exists, and ReadOnly or System is set.
    public static bool IsApplied(string folder, out string reason);

    /// The Id stored in [Thumbly], or null.
    public static string ReadId(string folder);

    public static void NotifyExplorer(string folder); // SHChangeNotify(SHCNE_UPDATEITEM, SHCNF_PATHW | SHCNF_FLUSH)
}
```

Implementation notes:
- Use `WritePrivateProfileString`/`GetPrivateProfileString` P/Invokes (kernel32, `CharSet.Unicode`).
  Before writing, if `desktop.ini` exists and is Hidden/System/ReadOnly, clear those attributes, write, then
  set Hidden+System again. If it does not exist, create it with `File.WriteAllText(path, "", Encoding.Unicode)`
  first so the profile API sees a Unicode file (otherwise it writes ANSI, which breaks non-ASCII values in
  other keys later).
- Keys written: `[.ShellClassInfo] IconResource=thumbly-icon.ico,0`; `[Thumbly] Id=<id>`. Delete legacy
  `IconFile`/`IconIndex` if present so they do not win over `IconResource`.
- Copy the ico with `File.Copy(src, dst, overwrite: true)` after clearing attributes on an existing dst.
- `Apply` returns `true` only if ReadOnly was not set before, so a folder the user had already made ReadOnly
  stays ReadOnly after Remove.
- "Empty desktop.ini" detection for Remove: read the file back and treat it as empty when no line contains
  `=` after Thumbly's keys are removed; then delete it. Otherwise leave it in place.
- `NotifyExplorer` swallows exceptions; it is best effort.

#### 2. `IconSource`

**File**: `src/IconSource.cs` (new)

```csharp
public class IconSource
{
    public string ImagePath;        // the file the ico is built from (the saved copy when SaveImageCopy is on)
    public RectangleF Crop;         // normalized 0..1, Empty = whole image
    public bool RemoveBackground;
    public int Tolerance = 24;      // 0..128, max RGB channel distance
    public string Serialize();      // "crop=x,y,w,h;bg=1;tol=24" (InvariantCulture)
    public static IconSource Parse(string imagePath, string text);
}
```

Phase 1 only uses `ImagePath`; `Crop`/`RemoveBackground` become live in Phase 4. Defining it now keeps the
catalog format stable.

#### 3. `FolderCatalog`

**File**: `src/FolderCatalog.cs` (new)
**Format**: INI-style text in `%AppData%\Thumbly\folders.txt`, one section per entry, same hand-written
reading style as `Settings`:

```
[6f2a9c1d3e4b]
Folder=D:\Work\Projects
Icon=C:\Users\me\Pictures\Thumbly\Icons\Projects.ico
Image=C:\Users\me\Pictures\Thumbly\Images\hero.png
Source=crop=0.1,0,0.8,0.8;bg=1;tol=24
Applied=2026-10-10T12:34:56+03:00
SetReadOnly=true
```

```csharp
public enum FolderStatus { Ok, FolderMissing, NeedsReapply }

public class FolderEntry
{
    public string Id;            // 12 lowercase hex chars
    public string Folder;
    public string IconPath;      // central copy
    public IconSource Source;
    public DateTimeOffset Applied;
    public bool SetReadOnly;
}

public class FolderCatalog
{
    public static string DefaultPath => ...\Thumbly\folders.txt;
    public List<FolderEntry> Entries { get; }
    public static FolderCatalog Load(string path = null);
    public void Save(string path = null);
    public FolderEntry Find(string folder);          // case-insensitive, trailing-slash-insensitive
    public void Upsert(FolderEntry e);               // by Id
    public void Remove(string id);
    public static FolderStatus Check(FolderEntry e, out string reason);
}
```

`Check`: folder missing → `FolderMissing`; else `FolderIconService.IsApplied` false → `NeedsReapply` with its
reason ("icon file missing", "desktop.ini changed", "attribute cleared"); central ico missing →
`NeedsReapply` ("central copy missing"); else `Ok`.

#### 4. `FolderAssigner`

**File**: `src/FolderAssigner.cs` (new)

```csharp
public class AssignResult { public FolderEntry Entry; public string IconPath; }

public static class FolderAssigner
{
    /// Builds the ico from source, saves the central copy as <folderName>.ico (UniquePath), applies it to
    /// the folder, upserts the catalog entry (reusing an existing entry's Id for the same folder) and saves.
    public static AssignResult Assign(string folder, IconSource source, Settings settings, FolderCatalog catalog);

    /// Rebuilds from entry.Source into entry.IconPath (overwrite) and re-applies. Falls back to copying the
    /// existing central ico when the source image is gone.
    public static void Reapply(FolderEntry entry, Settings settings, FolderCatalog catalog);

    /// FolderIconService.Remove(entry.Folder, entry.SetReadOnly) when the folder exists; catalog.Remove.
    /// The central copy is kept.
    public static void RemoveIcon(FolderEntry entry, FolderCatalog catalog);
}
```

In Phase 1 `Assign` builds the bitmap with `Converter.LoadImage` + `IcoWriter.Write` directly; Phase 4 swaps
in `ImagePipeline.Prepare`. When `settings.SaveImageCopy` is on, the source image is first copied into
`ImageDirectory` (via `UniquePath`) and `source.ImagePath` is pointed at the copy, so later Reapply/Adjust
survive the original being deleted.

#### 5. `Program`

**File**: `src/Program.cs`
**Changes**: Recognise `--folder <path>` before the headless branch:

```csharp
int fi = Array.IndexOf(args, "--folder");
if (fi >= 0 && fi + 1 < args.Length)
{
    Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
    Application.Run(new MainForm(armedFolder: args[fi + 1]));
    return 0;
}
```

`MainForm` gets an optional constructor parameter in this phase that only stores the value; Phase 2 makes it
behave.

#### 6. Tests

**Files**: `tests/FolderIconServiceTests.cs`, `tests/FolderCatalogTests.cs`, `tests/FolderAssignerTests.cs`
(new), all using the temp-folder-per-class pattern.

- `Apply` creates `desktop.ini` (hidden+system) with `IconResource=thumbly-icon.ico,0` and `[Thumbly] Id`,
  copies the ico hidden+system, sets ReadOnly, returns `true`.
- `Apply` on a folder that already has `desktop.ini` with `InfoTip=hello` keeps `InfoTip`.
- `Apply` on an already-ReadOnly folder returns `false`; `Remove(clearReadOnly:false)` leaves it ReadOnly.
- `Remove` deletes the ico and `desktop.ini` when nothing else is in it, clears ReadOnly, and keeps
  `desktop.ini` when `InfoTip` is present.
- `IsApplied` false after deleting the ico, after clearing ReadOnly, after editing `IconResource`.
- Catalog: round-trip of two entries including `Source` text, `Find` is case-insensitive and ignores a
  trailing backslash, `Upsert` replaces by Id, unknown keys are ignored, missing file loads empty.
- `Check` returns each status for the matching situation.
- `Assigner.Assign`: writes `<folderName>.ico` to the icons dir, `thumbly-icon.ico` into the folder, one
  catalog entry; assigning the same folder twice reuses the Id and writes `Projects (2).ico` as the new
  central copy; `RemoveIcon` leaves the central copy in place.

### Success Criteria

#### Automated Verification

- [ ] `dotnet build -c Release` succeeds with no warnings
- [ ] `dotnet test` passes (existing 2 test classes + 3 new)
- [ ] `src/bin/Release/net48/` contains only `Thumbly.exe`, `.pdb`, `.exe.config` (no new DLLs)

#### Manual Verification

- [ ] Call `FolderAssigner.Assign` (temporary test or `--folder` + Phase 2) on a scratch folder in `%TEMP%`:
      Explorer shows the icon on that folder after at most an F5
- [ ] Moving the scratch folder keeps the icon
- [ ] `RemoveIcon` restores the default folder icon and leaves the folder otherwise unchanged

---

## Phase 2: Assignment in the drop view + Explorer context menu

### Overview

The drop window learns about folders. A folder can be armed, then an image completes the assignment; an
image and a folder in one drop assign immediately. A context-menu entry launches Thumbly with the folder
armed.

### Changes Required

#### 1. Drop classification and armed state

**File**: `src/MainForm.cs`
**Changes**:

- Replace `GetDroppedFiles` with `ClassifyDrop(DragEventArgs) → (string[] images, string[] folders)`:
  images are `Converter.IsSupported`, folders are `Directory.Exists`. `OnDragEnter` accepts when either is
  non-empty.
- New fields: `string _armedFolder;` `FolderCatalog _catalog = FolderCatalog.Load();`
- `OnDragDrop`:
  - images and exactly one folder → `AssignTo(folder, images[0])` (extra images are ignored with a status note).
  - one or more folders only → arm the first (`Arm(folder)`); more than one → status "Drop one folder at a time."
  - images only, nothing armed → existing `ConvertFiles`.
  - images only, armed → `AssignTo(_armedFolder, images[0])`.
  - bitmap data, armed → save it as PNG into `ImageDirectory` named `<folderName>-<timestamp>.png` then
    assign; not armed → existing `ConvertBitmap`.
- `Arm(folder)`: sets `_armedFolder`, hint becomes
  `"Drop an image for\n<folder name>\nor paste · Browse"` where "Browse" is a link (replace `_hint` `Label`
  with a `LinkLabel`; `LinkArea` set to the word Browse). Browse opens an `OpenFileDialog` filtered to
  `Converter.SupportedExtensions`. Dashed border colour turns to a warmer accent (`Color.Gold`) while armed.
  Title becomes `"Thumbly – <folder name>"`.
- `Disarm()`: on Escape (`OnKeyDown`) and after a successful assignment; restores hint, border, title.
- `AssignTo(folder, imagePath)`: `FolderAssigner.Assign(folder, new IconSource { ImagePath = imagePath },
  _settings, _catalog)` inside try/catch; preview shows the image; status:
  `"Applied to <folder name>. Press F5 in Explorer if it has not updated.  (click to reveal)"`; reveal opens
  Explorer selecting the folder (not the ico). `_settings.OpenFolderAfterSave` reveals immediately as today.
- Constructor `MainForm(string armedFolder = null)`: calls `Arm` when non-null and the directory exists,
  otherwise status error "Folder not found: …".
- `PasteFromClipboard`: when armed and the clipboard has a supported file or an image, assign instead of
  convert.

Keep the existing non-armed behaviour identical.

#### 2. `ShellIntegration`

**File**: `src/ShellIntegration.cs` (new)

```csharp
public static class ShellIntegration
{
    const string KeyPath = @"Software\Classes\Directory\shell\Thumbly";
    public static bool IsInstalled(RegistryKey root = null);          // root defaults to Registry.CurrentUser
    public static void Install(string exePath, RegistryKey root = null);
    // (Default) = "Set icon with Thumbly"; Icon = "<exe>,0"; command\(Default) = "\"<exe>\" --folder \"%1\""
    public static void Uninstall(RegistryKey root = null);            // DeleteSubKeyTree(KeyPath, throwOnMissing:false)
}
```

The `root` parameter exists so tests can pass a throwaway subkey (`HKCU\Software\ThumblyTests-<guid>`) and
delete only that.

On startup, if `IsInstalled()` and the stored command does not contain the current
`Application.ExecutablePath`, re-`Install` silently so a moved exe keeps working.

#### 3. Settings

**Files**: `src/Settings.cs`, `src/SettingsForm.cs`
**Changes**: add `bool ContextMenu` (default false) with a checkbox
"Add 'Set icon with Thumbly' to the folder right-click menu". `MainForm.OpenSettings` compares old/new and
calls `ShellIntegration.Install/Uninstall` accordingly, reporting a registry error in the status line rather
than crashing.

#### 4. Tests

**File**: `tests/ShellIntegrationTests.cs` (new): install into a test root key writes the three values with
the exact command string; `IsInstalled` true; uninstall removes it; uninstall on a missing key does not throw.
The test deletes its own root key in `Dispose`.

### Success Criteria

#### Automated Verification

- [ ] `dotnet build -c Release` with no warnings
- [ ] `dotnet test` passes
- [ ] Still no new DLLs in the output folder

#### Manual Verification

- [ ] Drop a folder: window arms, hint names the folder, Escape disarms
- [ ] Drop an image onto the armed window: folder shows the icon in Explorer; `.ico` appears in the icons
      folder; entry appears in `%AppData%\Thumbly\folders.txt`
- [ ] Select an image and a folder in Explorer, drag both: assigned in one step
- [ ] Ctrl+V with an image on the clipboard while armed assigns
- [ ] Dropping an image with nothing armed converts exactly as before
- [ ] Enable the context menu in settings; right-click a folder → "Set icon with Thumbly" opens Thumbly
      armed; disable → entry gone
- [ ] `Thumbly.exe --folder "C:\missing"` shows the not-found status rather than crashing

---

## Phase 3: Folders view

### Overview

A second view inside the main window listing every catalog entry with status and actions.

### Changes Required

#### 1. `FoldersPanel`

**File**: `src/FoldersPanel.cs` (new), a `UserControl` with `Dock = Fill`, dark colours matching `MainForm`.

Layout:
- Top `FlowLayoutPanel`: `Button "Add folder…"` (FolderBrowserDialog → raises `ArmRequested(string folder)`),
  `Button "Rescan"`, `TextBox` filter (filters by folder name/path substring on `TextChanged`).
- Middle `ListView` (`View.Details`, `FullRowSelect`, `SmallImageList` 32×32 built from each entry's central
  ico via `new Icon(path, 32, 32).ToBitmap()`, fallback to a generic folder glyph). Columns: Folder (name),
  Path, Status. Sorted by folder name. Row `Tag` = `FolderEntry`.
  Status text: `OK`, `Folder missing`, `Needs reapply – <reason>`; the latter two in `Color.Salmon`/`Color.Gold`.
- Bottom `FlowLayoutPanel`: `Label` "N folders · M problems", `Button "Reapply"`, `Button "Remove icon"`
  (both act on the selection, enabled when something is selected).
- `ContextMenuStrip` on the list: Reveal folder, Reveal icon, Change image…, Reapply, Remove icon, Locate…
  (only for Folder missing), Forget. "Adjust…" is added in Phase 5.
- The list accepts drops: an image dropped onto a row (hit-test by `ListView.GetItemAt`) raises
  `AssignRequested(folder, imagePath)`. The panel raises events and `MainForm` performs the work, so the
  panel has no dependency on `FolderAssigner`.

Behaviour:
- `Refresh(FolderCatalog)` rebuilds rows and statuses via `FolderCatalog.Check`.
- Remove icon asks `MessageBox` confirmation when more than one row is selected ("Remove the icon from N
  folders?"). Forget never prompts. Both leave the central copy alone.
- Locate…: FolderBrowserDialog; if the chosen folder's `FolderIconService.ReadId` equals the entry's Id,
  update `Folder` and re-check; if it does not match, ask "This folder has no Thumbly icon from this entry.
  Link it anyway and reapply?".
- Change image…: OpenFileDialog → `AssignRequested` for that folder (keeps Id; in Phase 5 resets the crop).

#### 2. View toggle

**File**: `src/MainForm.cs`
**Changes**:
- Top bar becomes two `LinkLabel`s: left-aligned "Folders" / "Drop" toggle, right-aligned "Settings".
- `ShowFolders()`: hides `_dropZone`, shows `_foldersPanel` (created lazily), calls `Refresh(_catalog)`,
  resizes the window to `_settings.FoldersViewSize` (default 720×460), `MinimumSize` 560×320.
  `ShowDrop()`: reverse, restores the previous small size. Size changes while in the Folders view are saved
  to settings on view switch and on close.
- `_foldersPanel.AssignRequested += (folder, image) => AssignTo(folder, image)` then `Refresh`.
  `ArmRequested += folder => { ShowDrop(); Arm(folder); }`.
- Keyboard: Ctrl+L toggles views.
- Status line in the Folders view shows the last action result.

#### 3. Settings

**File**: `src/Settings.cs`: `Size FoldersViewSize` stored as `FoldersViewSize=720,460`. Not shown in the
settings form.

### Success Criteria

#### Automated Verification

- [ ] `dotnet build -c Release` with no warnings
- [ ] `dotnet test` passes (new automated coverage limited to the `FoldersViewSize` settings round-trip)

#### Manual Verification

- [ ] Folders view lists entries with icons and OK status after Phase 2 assignments
- [ ] Rename the in-folder ico by hand → Rescan shows "Needs reapply – icon file missing"; Reapply fixes it
- [ ] Clear the ReadOnly attribute (`attrib -r <folder>`) → "Needs reapply – attribute cleared"; Reapply fixes
- [ ] Rename the folder → "Folder missing"; Locate… relinks; status OK
- [ ] Remove icon on a folder with a pre-existing `desktop.ini` `InfoTip` keeps the InfoTip
- [ ] Multi-select Reapply and Remove work; Forget removes only the row
- [ ] Drop an image onto a row reassigns that folder
- [ ] Window size in the Folders view is remembered across restarts; the Drop view keeps its small size

---

## Phase 4: Image pipeline — crop and flood-fill background removal

### Overview

A `Prepare` step between loading the image and `IcoWriter`, with a normalized crop and an edge-seeded
flood fill. Pure functions over bitmaps, fully tested, still no UI.

### Changes Required

#### 1. `ImagePipeline`

**File**: `src/ImagePipeline.cs` (new)

```csharp
public static class ImagePipeline
{
    public const int MaxSide = 1024;

    /// 1. crop (normalized rect, Empty = whole), 2. downscale so the longer side is at most MaxSide,
    /// 3. optionally remove background. Always returns a new 32bppArgb bitmap the caller disposes.
    public static Bitmap Prepare(Image source, IconSource s);

    public static Rectangle ToPixels(RectangleF normalized, Size size); // clamps to the image, min 1×1
}
```

Downscaling first means the flood fill touches at most 1 MP regardless of source size, and `IcoWriter`
never needs more than 256 px anyway. Extract a shared helper `IcoWriter.DrawScaled(src, srcRect, dstSize)`
so `Resize` and `Prepare` use the same `ImageAttributes` wrap mode and quality settings.

#### 2. `BackgroundRemover`

**File**: `src/BackgroundRemover.cs` (new)

```csharp
public static class BackgroundRemover
{
    /// Flood-fills from every edge pixel: connected pixels within `tolerance` (0..128, max RGB channel
    /// distance) of that edge pixel's colour become transparent. Then a 3×3 box blur of the alpha mask
    /// softens the boundary by one pixel. Opaque pixels not connected to an edge are untouched.
    /// Operates in place on a 32bppArgb bitmap.
    public static void Remove(Bitmap bmp, int tolerance);
}
```

Implementation: `LockBits` once, work on an `int[]` of pixels and a `byte[]` mask, iterative BFS with a
`Queue<int>` over pixel indexes (no recursion), compare against the seed colour of the edge pixel that
started each region (so a white background and a dark shadow at a corner are separate regions).
Already-transparent pixels count as background. The feather pass only modifies pixels whose 3×3
neighbourhood mixes background and foreground. Expected cost: well under 100 ms for 1 MP.

#### 3. `FolderAssigner`

**File**: `src/FolderAssigner.cs`
**Changes**: `Assign`/`Reapply` build via `using (var prepared = ImagePipeline.Prepare(img, source))
IcoWriter.Write(prepared, fs, settings.Sizes)`. `IconSource.Serialize/Parse` now carries real values.

#### 4. Tests

**Files**: `tests/ImagePipelineTests.cs`, `tests/BackgroundRemoverTests.cs` (new)

- `ToPixels` clamps and never yields zero size; `Prepare` with `Crop = (0.25,0.25,0.5,0.5)` on a 4-quadrant
  bitmap returns only the centre colours; `Prepare` on 3000×3000 returns ≤1024 per side; `Crop.Empty`
  returns the full image scaled.
- `Remove`: solid orange square on white → corners alpha 0, centre alpha 255; a white pixel fully enclosed by
  orange stays opaque (flood fill, not colour key); tolerance 0 removes only exact matches; background
  already transparent stays transparent; a 1024×1024 image completes in under 500 ms (loose bound, guards
  against accidental O(n²)).
- `IconSource` serialize/parse round-trip with InvariantCulture decimals.

### Success Criteria

#### Automated Verification

- [ ] `dotnet build -c Release` with no warnings
- [ ] `dotnet test` passes including the new pipeline tests

#### Manual Verification

- [ ] Hand-edit a catalog entry's `Source=` to `bg=1;tol=24`, Reapply from the Folders view: the folder icon
      loses its flat background

---

## Phase 5: Adjust dialog

### Overview

The UI for Phase 4: a modal with a square crop overlay, zoom, background removal toggle and tolerance, and
256/48/16 px previews. Hooked into assignment (optionally always, or with Shift) and into the Folders view.

### Changes Required

#### 1. `AdjustForm`

**File**: `src/AdjustForm.cs` (new). `AdjustForm(Image image, IconSource current)`; `IconSource Result`.

Layout (ClientSize about 820×520, resizable, dark theme as `MainForm`):
- Left: a custom `Panel` (`DoubleBuffered`) drawing the image fitted to the panel, a dimmed overlay outside
  the crop square, the square with a 2 px accent border and 8 px corner handles. Mouse: drag inside moves,
  drag a corner resizes keeping 1:1 (anchored at the opposite corner), clamped to the image; mouse wheel
  resizes around the centre. Default crop: the largest centred square (or `current.Crop` when set).
  Beneath it a `TrackBar` "Zoom" (crop side as % of the shorter image side, 10–100) bound two-way to the square.
- Right column: `CheckBox` "Remove background", `TrackBar` "Tolerance" 0–128 (enabled only when checked,
  value label beside it), then three `PictureBox`es labelled 256, 48, 16 drawn on a checkerboard so
  transparency is visible, then a `Label` with image dimensions and the crop in pixels.
- Bottom: `Button` "Reset", `Button` "Apply" (OK), `Button` "Cancel".

Previews recompute on `MouseUp`, on `TrackBar` `ValueChanged` (debounced with a 150 ms `Timer`), and on the
checkbox: `ImagePipeline.Prepare(image, draft)` then `IcoWriter.Resize` to 256/48/16. With the 1024 cap in
`Prepare` this is tens of milliseconds, so no background thread is needed.

#### 2. Hooks

**Files**: `src/MainForm.cs`, `src/FoldersPanel.cs`, `src/Settings.cs`, `src/SettingsForm.cs`
**Changes**:
- Setting `bool AdjustBeforeApply` (default false), checkbox "Always adjust (crop / background) before
  applying a folder icon".
- `AssignTo(folder, imagePath, bool forceAdjust = false)`: when `forceAdjust || _settings.AdjustBeforeApply`,
  load the image, show `AdjustForm`, cancel aborts (status "Cancelled."), otherwise use `Result`.
  `OnDragDrop` passes `forceAdjust: (ModifierKeys & Keys.Shift) != 0`. Browse in the armed view passes
  `false`; the hint gains a line "hold Shift to adjust first".
- `FoldersPanel` context menu gains "Adjust…" → `AdjustRequested(FolderEntry)`; `MainForm` loads
  `entry.Source.ImagePath` (error status if missing), shows `AdjustForm(image, entry.Source)`, then
  `FolderAssigner.Reapply` with the updated `Source` and refreshes the list.
- The headless image → ico path and the non-armed drop path are untouched; Adjust is a folder-icon feature only.

#### 3. README

**File**: `README.md`
**Changes**: new "Folder icons" section (arming, drop both, context menu, catalog file location, F5 note,
what Remove icon does), the Folders view, the Adjust dialog, and the new settings rows in the table.
Layout section lists the new files.

### Success Criteria

#### Automated Verification

- [ ] `dotnet build -c Release` with no warnings
- [ ] `dotnet test` passes
- [ ] Output folder still contains no new DLLs; exe size stays under about 100 KB

#### Manual Verification

- [ ] Shift-drop an image onto an armed folder opens Adjust; crop square drags, resizes from corners and via
      wheel; zoom slider and square stay in sync
- [ ] Remove background on a logo-on-white image clears the background in the 16 px preview without halo;
      a photo shows the expected poor result and the user can uncheck it
- [ ] Apply writes the icon; Adjust… from the Folders view reopens with the same crop and tolerance
- [ ] Cancel leaves the folder untouched
- [ ] "Always adjust" setting makes plain drops open the dialog; off by default keeps drops instant

---

## Testing Strategy

### Unit Tests

- `FolderIconService`: desktop.ini creation and merge, attribute handling, idempotent Apply, exact Remove,
  `IsApplied` reasons. All inside the test's temp folder.
- `FolderCatalog`: text round-trip, lookups, status mapping.
- `FolderAssigner`: file outputs, Id reuse, unique naming, central copy kept on remove.
- `ShellIntegration`: against a test-only HKCU subkey that the test creates and deletes.
- `ImagePipeline` / `BackgroundRemover`: geometry, colours, flood vs key, tolerance, cap, timing bound.
- `Settings`: new keys round-trip; an old settings file without them loads defaults.

### Integration Tests

None automated; WinForms UI is verified manually. Every orchestration path is covered one layer down.

### Manual Testing Steps

1. Create `%TEMP%\ThumblyPlay\Folder A` and `Folder B` as scratch folders (delete them yourself afterwards;
   nothing in the plan deletes user folders).
2. Phase 2 flow: arm Folder A, drop an image; verify icon, central ico, catalog line.
3. Drag image + Folder B together; verify.
4. Move Folder A elsewhere; icon persists; Folders view says Folder missing; Locate… relinks.
5. `attrib -r` on Folder B; Rescan; Reapply.
6. Remove icon on both; folders look default; central icos still in the icons folder.
7. Context menu on/off; launch from Explorer.
8. Shift-drop with a logo on white; Adjust; verify 16 px preview; Apply; reopen Adjust from the list.

## Performance Considerations

- All image work is capped at 1024 px by `ImagePipeline.Prepare` before the flood fill and the ico build,
  so the dialog previews and assignment stay in the tens of milliseconds regardless of source size.
- The Folders view loads one 32 px icon per entry from the central ico; for a few hundred entries this is
  negligible. `Check` does a handful of file-existence tests and one INI read per entry.
- `SHChangeNotify` is fire-and-forget. No background threads are introduced anywhere.
- Exe size grows by a few tens of KB of IL; no native or managed dependencies are added.

## Migration Notes

- `settings.txt` gains keys `ContextMenu`, `AdjustBeforeApply`, `FoldersViewSize`; older files load with
  defaults because unknown keys are ignored and missing keys keep defaults.
- `folders.txt` is new; a missing file means an empty catalog.
- Nothing in existing user folders is touched until the user assigns an icon. Remove undoes exactly what
  Apply did (keys, file, attribute-if-set) and nothing else.

## References

- Current drop handling: `src/MainForm.cs:123-160`
- ico build seam: `src/IcoWriter.cs:15`; naming: `src/Converter.cs:78`
- Settings style to mirror: `src/Settings.cs:31-82`
- Test pattern to mirror: `tests/ConverterAndSettingsTests.cs:10-15`
- WorldForge normalized-crop sidecar (the model for `IconSource.Crop`):
  `S:\Coding\WorldForgeGit\backend\Service\Logic\ThumbnailService.cs:128-178`
- ImagineStudio matting (the possible future ML slot-in, out of scope here):
  `S:\Coding\AiImageEditor\src-tauri\src\matting\mod.rs`
