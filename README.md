# Thumbly

Drag an image in, get a Windows `.ico` out, or set it as a folder's icon. No installer, no runtime download,
no server. Targets .NET Framework 4.8 (ships with Windows 10/11), 64-bit. `Thumbly.exe` runs on its own; the ONNX
Runtime DLLs next to it are only needed for AI background removal.

## Use

The window has two drop areas on the left, **Folder** and **Image**, and the list of folders with Thumbly icons
on the right. Drop things anywhere in the window: folders land in the folder area, images in the image area.
Ctrl+V pastes an image, an image file or a folder.

- **Image only → Create icon** writes a plain `.ico` into the icon folder (the original Thumbly behaviour).
- **Folder + image → Create & set icon** makes it that folder's icon.
- **Edit image…** (or Shift-drop, or Ctrl+E) opens the editor: crop, square lock, background removal, live
  previews. The image area always shows the image the way the icon will look.
- **Several images with no folder** are converted to `.ico` files straight away.
- Drop files directly onto `Thumbly.exe` in Explorer (or a `shell:sendto` shortcut) to convert them without
  opening a window.
- Click the status line to reveal the last result in Explorer. Ctrl+Enter is the same as the main button.

Non-square images are centered on a transparent square canvas unless cropped, then resampled into every chosen
size and stored as PNG-compressed layers. Output files take the source file's name; existing files are never
overwritten, a ` (2)` suffix is added instead.

## Folder icons

Every assignment writes:

| What | Where |
|---|---|
| Central copy of the icon | `.ico` folder, named after the folder (`Projects.ico`, `Projects (2).ico`, …) |
| The icon Explorer uses | hidden `thumbly-icon.ico` inside the folder, referenced by a relative path, so moving or renaming the folder keeps the icon |
| `desktop.ini` | merged, keeps any other settings it already had |
| Folder attribute | ReadOnly, which is what makes Explorer read `desktop.ini` |
| Tracking entry | `%AppData%\Thumbly\folders.txt` |

The icon is applied through the same shell call Explorer's own *Customize → Change Icon* uses, which also clears
Explorer's cached folder preview. Without that, folders Explorer had already shown kept their old preview.

**Right-click a folder in Explorer → Set icon with Thumbly** after enabling it in Settings. On Windows 11 the
entry sits under *Show more options*. It is a per-user registry entry, no admin rights needed.

### Folder list

Shows every folder with a Thumbly icon and its status: **OK**, **Folder missing**, or **Needs reapply** (for
example when a sync tool such as OneDrive cleared the folder attribute). Rescan (F5) re-checks.

- **Double-click a row** (or Enter, or *Edit…*) to load its folder and source image into the left side; edit
  and press Create & set icon to update it. Same image keeps the same `.ico`; a new image adds a new one.
- **Drop an image onto a row** to replace that folder's icon in one step.
- **Reapply** rebuilds the icon from its source and fixes whatever drifted.
- **Remove icon** (Del) restores the default folder icon. It removes only what Thumbly added, and clears the
  ReadOnly attribute only if Thumbly set it. The central copy is kept.
- **Forget** drops the row and leaves the folder's icon alone. **Locate moved folder…** relinks a moved folder.

### Editor

A crop frame over the image: drag inside to move, drag corners or edges to resize, drag outside the frame to
draw a new one, mouse wheel to zoom, arrow keys to nudge (Shift for bigger steps). **Square** locks the aspect
(on by default; off pads the result to a square). The frame can extend past the image edges; that part becomes
transparent. **Fit square** and **Whole image** are one-click starting points. Previews at 256, 48, 32 and 16 px
update while you drag. The crop and background settings are remembered per folder.

**Remove background** has two methods:

- **AI (BiRefNet-lite)**: for photos, hair, busy backgrounds. Needs the model file (see below). The mask is
  computed once per image in the background (a few seconds), so re-cropping afterwards is instant.
- **Flat colour**: instant flood fill from the image border, with a tolerance. Good for logos, clipart and
  renders on a plain background; poor on photos.

## AI background removal

Uses **BiRefNet-lite** in ONNX format, run on the CPU with Microsoft ONNX Runtime, the same model and processing
as ImagineStudio. Set the file in **Settings → AI model** and press **Check**.

- Download `onnx/model.onnx` from <https://huggingface.co/onnx-community/BiRefNet_lite-ONNX> (about 224 MB).
  Not `model_fp16.onnx`: it expects half-precision input.
- Or point at a copy you already have, such as ImagineStudio's `<models folder>\Matting\birefnet-lite.onnx`.

| | Approximate |
|---|---|
| ONNX Runtime files next to the exe | 16.5 MB |
| First mask for an image (incl. loading the model) | 5 s |
| Memory while the model is loaded | several hundred MB, released after 2 idle minutes |

**Licences.** BiRefNet is MIT (© 2024 ZhengPeng, <https://github.com/ZhengPeng7/BiRefNet>); the ONNX export is
published under MIT too. ONNX Runtime is MIT (Microsoft). MIT allows redistributing the model, including in this
repo, as long as the copyright and licence notice go with it. The file is larger than GitHub's 100 MB per-file
limit, so it cannot be committed normally; it would need Git LFS. `*.onnx` is git-ignored for that reason
(except the 238-byte test fixture).

To check a real model from the tests, set `THUMBLY_REAL_MODEL` to the `.onnx` file (optionally
`THUMBLY_REAL_IMAGE` and `THUMBLY_REAL_OUT`) and run `dotnet test --filter Category=RealModel`.

## Settings (link in the top-right, or Ctrl+,)

| Setting | Default |
|---|---|
| `.ico` folder | `Pictures\Thumbly\Icons` |
| Image folder (copy of the source) | `Pictures\Thumbly\Images` |
| Sizes | 16, 32, 48, 64, 128, 256 (16 to 256 selectable) |
| Save a copy of the source image | on |
| Reveal in Explorer after saving | off |
| Folder right-click menu entry | off |
| Always open the editor when an image is added | off |
| Create and set automatically when both a folder and an image are present | off |
| AI model (BiRefNet-lite `.onnx` for AI background removal) | empty |

Stored as plain text in `%AppData%\Thumbly\settings.txt`.

## Build

```bash
dotnet build -c Release
```

Exe lands in `src\bin\Release\net48\Thumbly.exe`, with the ONNX Runtime DLLs beside it. Copy the whole folder to
deploy; the exe alone works too, minus AI background removal. Tests:

```bash
dotnet test
```

## Layout

- `src/IcoWriter.cs` – square-pad, resize, write the ICO container, read a layer back.
- `src/Converter.cs` – file in, `.ico` (+ optional image copy) out, unique naming.
- `src/Settings.cs` – key=value settings file.
- `src/FolderIconService.cs` – desktop.ini, in-folder icon, folder attribute, Explorer refresh.
- `src/FolderCatalog.cs` – the tracking file and per-folder status.
- `src/FolderAssigner.cs` – image + folder in, central copy + folder icon + tracking entry out.
- `src/IconSource.cs`, `src/ImagePipeline.cs`, `src/BackgroundRemover.cs` – crop (with padding), downscale, flood-fill background removal.
- `src/AiMatting.cs`, `src/OnnxModel.cs` – AI background removal (mask cache; the only code touching ONNX Runtime).
- `src/ShellIntegration.cs` – the Explorer right-click entry.
- `src/MainForm.cs`, `src/DropSlot.cs`, `src/FoldersPanel.cs` – the main window, its drop areas and folder list.
- `src/EditorForm.cs`, `src/CropCanvas.cs` – the image editor and its crop frame.
- `src/SettingsForm.cs`, `src/Theme.cs` – settings dialog, shared dark colours.
- `src/Program.cs` – GUI entry point and headless mode for command-line files.
- `tests/` – xUnit tests, including crop gestures driven with real mouse events and a check of what Explorer
  actually draws for a folder; each class works in its own temp folder (and the context-menu tests in a throwaway
  registry key) and removes only that.

## Notes

- WebP and AVIF are not decodable by GDI+, so they are not accepted. Save them as PNG first.
