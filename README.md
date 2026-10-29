# TimeTracker

**Where did my day go?** TimeTracker quietly notes which program you're working in and shows
how your time splits across programs — for a day, a week, a month or all time.
Works offline, and nothing ever leaves your PC.

[Українська версія](README.uk.md)

## Features

- **Day timeline with a row per program.** See not just totals but *when* you were in Blender and when in the browser.
  Hover a stretch to see the exact time.
- **Week, month and all time.** A column per day (or per month for long histories); click a column to open that period.
  A separate chart shows the hours you usually work.
- **Honest numbers.** Time when you don't touch the keyboard or mouse for a while (2 minutes by default) isn't counted,
  and neither are the lock screen, sleep or TimeTracker itself.
- **Hide what doesn't matter.** Remove a program from the statistics in one click (and undo it if you change your mind);
  it disappears from past days as well.
- **Lives in the tray.** Closing the window doesn't stop tracking. Pause and resume from the tray icon,
  and let it start together with Windows if you like.
- **Export to CSV** by day and program, opens correctly in Excel.
- **Automatic updates**, which you can turn off.
- Light and dark themes. The interface is in Ukrainian.

## Download and install

1. Download [TimeTracker-windows-x64.zip](https://github.com/DimaN221221/timetracker/releases/latest/download/TimeTracker-windows-x64.zip).
2. Unzip it into any folder you like (for example, *Documents\TimeTracker*).
3. Run `TimeTracker.exe`. Nothing else needs to be installed.

Requires Windows 10 or 11 (64-bit).
If Windows shows *“Windows protected your PC”*, click **More info → Run anyway**: the app is not signed with a paid
certificate, which is why the warning appears.

All versions and their change notes are on the [Releases](https://github.com/DimaN221221/timetracker/releases) page.

## Using it

Just leave it running: tracking starts immediately, and the statistics fill in as you work.

- **Close the window** — TimeTracker keeps running in the tray (next to the clock). Click the icon to open it again.
- **Pause** — the button at the top of the window or the tray menu. Useful when someone else uses your PC.
- **Quit completely** — right-click the tray icon → **Вихід**.
- **Export** — the download button at the top saves the open period as a CSV file.

In **Settings** (the gear button) you can:

- start TimeTracker together with Windows (it starts minimized to the tray);
- choose the theme;
- change after how many minutes without activity time stops counting — increase it if you often watch videos or lectures;
- return hidden programs to the statistics;
- turn automatic updates on or off, or check for an update right now;
- open the folder with your data.

## Updates

TimeTracker checks for a new version every few hours. When one is ready, a notice appears in the window:
click **Перезапустити й оновити** (Restart and update), or the update installs itself the next time you quit the app.

## Privacy

TimeTracker records the program, its window title and the time. Everything is stored on your computer
in `%LOCALAPPDATA%\TimeTracker`; nothing is sent anywhere. The only time it goes online is to check GitHub for updates,
and you can turn that off.

## Uninstalling

1. In Settings, turn off starting with Windows.
2. Quit the app: tray icon → **Вихід**.
3. Delete the folder you unzipped TimeTracker into. To remove your statistics as well, delete `%LOCALAPPDATA%\TimeTracker`.

---

Building the app yourself or curious how it works? See [DEVELOPMENT.md](DEVELOPMENT.md).
