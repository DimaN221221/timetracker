# TimeTracker for developers

How the app is built, released and laid out. The user guide is in the [README](README.md).

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

[DESIGN.md](DESIGN.md) is the design contract: tokens, layout and the states every screen must handle.

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
