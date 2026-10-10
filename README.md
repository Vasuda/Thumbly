# Thumbly

Drag an image in, get a Windows `.ico` out. Single 24 KB exe, no installer, no runtime download,
no server. Targets .NET Framework 4.8, which ships with Windows 10/11.

## Use

- Run `Thumbly.exe`, drop one or more images onto the window (PNG, JPG, BMP, GIF, TIFF).
- Or press **Ctrl+V** to convert an image or copied file from the clipboard.
- Or drop files directly onto `Thumbly.exe` in Explorer (or add a shortcut to `shell:sendto`):
  they are converted silently without opening a window.
- Click the status line to reveal the last saved `.ico` in Explorer.

Non-square images are centered on a transparent square canvas, then resampled into every chosen
size and stored as PNG-compressed layers (the same layout the online converters produce).
Output files take the source file's name; existing files are never overwritten, a ` (2)` suffix is
added instead.

## Settings (link in the top-right, or Ctrl+,)

| Setting | Default |
|---|---|
| `.ico` folder | `Pictures\Thumbly\Icons` |
| Image folder (copy of the source) | `Pictures\Thumbly\Images` |
| Sizes | 16, 32, 48, 64, 128, 256 (16 to 256 selectable) |
| Save a copy of the source image | on |
| Reveal in Explorer after saving | off |

Stored as plain text in `%AppData%\Thumbly\settings.txt`.

## Build

```bash
dotnet build -c Release
```

Exe lands in `src\bin\Release\net48\Thumbly.exe`. Tests:

```bash
dotnet test
```

## Layout

- `src/IcoWriter.cs` – square-pad, resize, and write the ICO container.
- `src/Converter.cs` – file in, `.ico` (+ optional image copy) out, unique naming.
- `src/Settings.cs` – key=value settings file.
- `src/MainForm.cs`, `src/SettingsForm.cs` – the two windows.
- `src/Program.cs` – GUI entry point and headless mode for command-line files.
- `tests/` – xUnit tests for the writer, converter, and settings.

## Notes

- WebP and AVIF are not decodable by GDI+, so they are not accepted. Save them as PNG first.
