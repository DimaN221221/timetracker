# TimeTracker

**Where did my day go?** TimeTracker quietly records which program is in front of you and shows
how your time splits across apps — per day, week, month or all time. Windows, offline, nothing leaves your PC.

[Українська версія](README.uk.md)

## Features

- **Day timeline with a lane per program** — see *when* you were in Blender and *when* in the browser,
  not just totals. Hover a stretch for exact times.
- **Week, month and all time** — stacked columns per day (or per month for long histories); click a column
  to drill in. An hour-of-day chart shows when you usually work.
- **Honest numbers** — time without keyboard/mouse input longer than a threshold (2 min by default) is not counted,
  neither are the lock screen, sleep or TimeTracker itself.
- **Exclusions** — hide a program with one click (with undo); it disappears from past statistics too.
- **Lives in the tray** — closing the window keeps tracking; pause/resume from the tray menu.
  Optional start with Windows.
- **CSV export** per day and program, ready for Excel.
- **Automatic updates** from GitHub Releases, can be turned off.
- Light and dark themes. Interface in Ukrainian.

## Download

Grab `TimeTracker-windows-x64.zip` from the [latest release](../../releases/latest), unzip it anywhere
and run `TimeTracker.exe`. It's a single self-contained file — no .NET install needed.
Requires Windows 10/11 x64 (uses the Edge WebView2 runtime, which is already part of Windows).

Data lives in `%LOCALAPPDATA%\TimeTracker` (`activity.db` — SQLite, `settings.json`).

## Building

Open `TimeTracker.sln` in Visual Studio 2022 (.NET 9 SDK) and press F5, or from the command line:

```bash
dotnet build TimeTracker.sln
```

```bash
build.bat
```

`build.bat` publishes the single-file `dist\TimeTracker.exe`. Quit the running app (tray → Вихід) first.

To work on the interface without rebuilding, generate a preview with fake data and open it in a browser
(`?theme=dark`, `?mode=month`, `?state=empty`, `?state=error`, `?paused=1`, `?update=ready`):

```bash
python tools/preview.py
```

## Releasing

Bump `<Version>` in `src/TimeTracker/TimeTracker.csproj`, commit, then push a matching tag
(for example `v2.2.0`). The [release workflow](.github/workflows/release.yml) builds the app on
GitHub Actions and attaches `TimeTracker-windows-x64.zip` and its `.sha256` to the release.

The in-app updater checks the latest release every few hours, downloads the zip, verifies the SHA-256 and
unpacks it to `%TEMP%`. When the app quits (or on *Restart and update*), the new exe starts from there with
`--apply-update`, waits for the old process to exit, copies itself over the installed exe and starts again.

If you fork the project, change `Repo` in `src/TimeTracker/Core/AppInfo.cs` so updates come from your repository.

## Project layout

```
src/TimeTracker/
  App.xaml.cs          startup, single instance, tray, status and update timers
  MainWindow.xaml.cs   WebView2 host and the page ↔ C# bridge (overview, settings, updates…)
  Core/                Tracker (foreground window, idle), Database (SQLite), Stats, Settings,
                       Autostart, Updater, AppInfo (name, version, repo)
  Host/                tray icon, embedded web files
  wwwroot/index.html   the whole interface (HTML/CSS/JS), compiled into the exe
tools/preview.py       interface preview in a browser with a fake host
DESIGN.md              design contract: tokens, layout, required states
```
