# QuickStash

A lightweight overlay for taking notes while you game. Press a hotkey and a note box pops up over your game, already on the right topic, with a screenshot of the moment ready to attach. Type, press Enter, press Esc, and you're back in the game.

<!-- TODO: add a short GIF here: hotkey → type → Enter → pin → back to game -->

## Features

- **One hotkey** (default `Ctrl+Shift+Space`) opens and closes a Steam-overlay-style panel on top of the game.
- **Automatic topics.** A topic such as "TLD" can be linked to a game's executable (`tld.exe`). The linked topic is selected automatically when you open the overlay over that game. For a game it hasn't seen yet, it offers to create a topic.
- **Screenshot on open.** The game window is captured just before the overlay appears. The screenshot is attached to your note if you keep the Attach box ticked, and discarded otherwise. `Ctrl+V` pastes any image from the clipboard instead.
- **Plain-text notes.** Enter saves and Shift+Enter adds a new line. Notes are listed newest first with thumbnails, and they expand, edit, delete and open images at full size.
- **Search across all topics** as you type (`Ctrl+F`).
- **Pinned notes.** A pinned note is a small, semi-transparent, click-through card that stays on top of the game. While the overlay is open you can drag it. It is restored where you left it after a restart. `Ctrl+Shift+P` unpins everything.
- **Small footprint.** It runs as a tray icon and uses no GPU (see [Why software rendering](#why-software-rendering)). While idle in the tray it gives unused memory back to Windows, so Task Manager typically shows 10–40 MB.
- **Local and private.** All data is one SQLite file plus PNGs in `%AppData%\QuickStash`. There's no account, network access or telemetry.

## Requirements

- Windows 10 version 2004 or later, or Windows 11 (x64).
- **The game must run in borderless windowed mode, not exclusive fullscreen** (see [below](#borderless-windowed-mode-required)).
- The self-contained download needs nothing else. The smaller `-fx` download needs the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).

## Install

1. Download the latest `QuickStash-x.y.z-win-x64.zip` from the **Releases** page.
2. Extract it anywhere, for example `C:\Tools\QuickStash`, and run `QuickStash.exe`.
3. Look for the blue note icon in the system tray. Right-click it for Settings and Exit.

> **"Windows protected your PC"?** The executable is not code-signed, so Microsoft SmartScreen warns about an unknown publisher. Click **More info → Run anyway**, or build from source.

To start QuickStash automatically, enable **Start with Windows** in Settings.

## Usage

| Where | Key | Action |
|---|---|---|
| Anywhere | `Ctrl+Shift+Space` | Open / close the overlay (configurable) |
| Anywhere | `Ctrl+Shift+P` | Unpin all pinned notes (configurable) |
| Overlay | `Esc` | Close the viewer, editor or search first, then the overlay |
| Note box | `Enter` / `Shift+Enter` | Save note / new line |
| Note box | `Ctrl+V` | Paste an image from the clipboard as the attachment |
| Overlay | `Ctrl+F` | Search all notes |
| Overlay | `Ctrl+T` | Filter topics (`↑` `↓` to move, `Enter` to open) |
| Overlay | `Ctrl+N` | New topic |

Clicking a note expands it. Clicking a thumbnail shows the image full size. Hover a note to pin, edit or delete it.

### Borderless windowed mode required

In **exclusive fullscreen**, the game owns the display, and Windows cannot draw other windows on top of it. Opening the overlay would force the game to minimize or switch modes. Most modern games offer **Borderless**, **Windowed Fullscreen** or **Fullscreen (Windowed)** in their video settings. Pick that one. Screenshots are also taken from the composed desktop image, which is only available in borderless or windowed mode.

### Anti-cheat

QuickStash never touches the game process:

- It doesn't inject code, hook game functions or read game memory.
- It uses only standard desktop APIs: a registered global hotkey (`RegisterHotKey`), a copy of the screen area where the game window is (`BitBlt`), normal always-on-top windows, and a notification when the foreground window changes (`SetWinEventHook`, out-of-context).

These are the same mechanisms used by screenshot tools and Discord or Steam style overlays. Anti-cheat policies differ, though, so check your game's rules if you're unsure.

## Data

Everything lives in `%AppData%\QuickStash`:

| File | Contents |
|---|---|
| `quickstash.db` | SQLite database (topics, process links, notes, pin positions) |
| `images\*.png` | Attached screenshots and pasted images |
| `settings.json` | Settings (hand-editable) |
| `quickstash.log` | Small diagnostic log, trimmed automatically |

To back up, copy the folder. To keep data elsewhere, for example a portable setup, set the environment variable `QUICKSTASH_DATA` to another folder.

## Build from source

Requirements: the .NET 8 SDK or newer, on Windows.

```bash
dotnet build
dotnet test
dotnet run --project src/QuickStash
```

This is a self-contained single-file build, the same as a release:

```bash
dotnet publish src/QuickStash -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

## Architecture

```
src/QuickStash/
  App.xaml(.cs)            Composition root: creates services, wires events, tray menu, settings apply
  Interop/NativeMethods.cs ALL Win32 P/Invoke (hotkeys, tray icon, foreground window, window styles, monitors, capture)
  Models/                  Topic, Note, AppSettings, HotkeyGesture
  Data/                    Database (single SQLite connection + schema migrations), TopicRepository, NoteRepository
  Services/                Overlay lifecycle, hotkeys, tray icon, capture, images, pins, settings, startup, log
  ViewModels/              OverlayViewModel, NoteItemViewModel, TopicItemViewModel, SearchResultViewModel,
                           PinnedNoteViewModel, SettingsViewModel (CommunityToolkit.Mvvm)
  Views/                   OverlayWindow, PinnedNoteWindow, SettingsWindow, HotkeyBox, attached behaviors
  Themes/Dark.xaml         Steam-like dark theme (colors, controls, scrollbars, menus)
tests/QuickStash.Tests/    xUnit tests for storage, search, hotkey parsing
```

**MVVM.** Views bind to view models built on CommunityToolkit.Mvvm. Code-behind only handles window mechanics: native styles, placement, focus and keyboard routing. Small attached behaviors (`TextBoxBehavior`, `FocusBehavior`) handle Enter-to-save, Shift+Enter, image paste and focusing inline editors.

**Win32 interop in one place.** `NativeMethods` is the only class that calls native code. The tray icon uses `Shell_NotifyIcon` directly rather than pulling in WinForms. A hidden `MessageWindow` receives `WM_HOTKEY`, tray callbacks, and the `TaskbarCreated` broadcast (to re-add the icon after Explorer restarts).

**Opening the overlay in under 200 ms.** The overlay window is created once at startup and warmed up by showing it off-screen once. After that, opening it only does four things:
1. `GetForegroundWindow` and `GetWindowThreadProcessId` find the game and its executable name.
2. The game's visible bounds (`DWMWA_EXTENDED_FRAME_BOUNDS`) are copied from the screen into a frozen in-memory bitmap. Nothing is written to disk unless the note is saved.
3. The view model picks the topic linked to that executable.
4. The window is centered on the game's monitor (DPI-aware, physical pixels), shown and focused.

Typical timings on a desktop PC are about 15 ms for the capture and 30–90 ms from hotkey to visible overlay. Closing the overlay hides it, drops the screenshot, compacts the large-object heap and trims the working set, so the app idles small. The first open after a trim takes about 100 ms while pages come back, still well under the 200 ms target.

**Pinned notes.** Each pinned note is its own small layered window with `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`:
- While the overlay is closed, `WS_EX_TRANSPARENT` is added, so every click goes through to the game.
- While the overlay is open, the flag is removed, so the note can be dragged. The drag is implemented manually and never activates the window, so the overlay doesn't lose focus.
- Positions are stored in physical pixels in the `Notes` table and checked against connected monitors on restore.
- Pinned notes are raised again whenever the foreground window changes, because some games push themselves to the top.
- By default they're excluded from screen capture (`WDA_EXCLUDEFROMCAPTURE`), so they don't appear in the auto screenshot or on stream. This is configurable.

**Storage.** `Microsoft.Data.Sqlite` with WAL mode and a schema version in `PRAGMA user_version`:

```
Topics         (Id, Name UNIQUE NOCASE, LastUsed)
TopicProcesses (TopicId → Topics ON DELETE CASCADE, ProcessName)   -- PK (TopicId, ProcessName)
Notes          (Id, TopicId → Topics ON DELETE CASCADE, Text, ImagePath, IsPinned, PinX, PinY, CreatedAt, UpdatedAt)
```

Timestamps are ISO-8601 UTC. `ImagePath` is relative to the data folder. Search uses a small custom SQL function, `qs_match`, that requires every word of the query to appear in the note text, ignoring case. Unlike SQLite's built-in `LIKE`, it handles Unicode case (Ö/ö), and characters such as `%` and `_` are matched literally.

### Why software rendering

The overlay and pinned notes are transparent layered windows. For those, WPF reads rendered frames back from the GPU to the CPU anyway. Turning GPU rendering off (`RenderMode.SoftwareOnly`) skips creating a Direct3D device. That saves about 50 MB of memory and means QuickStash never competes with the game for the GPU.

## Dependencies

- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet): MVVM source generators
- [Microsoft.Data.Sqlite](https://learn.microsoft.com/dotnet/standard/data/sqlite/): SQLite
- Tests only: xUnit

There's no Electron, web view or WinForms.

## License

MIT. See [LICENSE](LICENSE).
