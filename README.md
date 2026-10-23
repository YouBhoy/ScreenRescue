# ScreenRescue

A Windows C# WPF .NET 8 tray app that fits ordinary desktop windows into the working portion of a damaged **secondary** monitor. It does not change the monitor resolution or repair panel damage.

## Use

1. Set the damaged monitor to **Extend these displays** in Windows Display Settings. Set its orientation to Portrait there if desired. Keep a working display as primary.
2. Run `ScreenRescue.exe`. Automatic fitting is off on first launch.
3. Select the secondary monitor by its Windows device name and resolution. Only secondary displays are offered. Use **Refresh displays** after changing the display layout.
4. Adjust Left, Top, Right and Bottom as percentages of the full monitor. Default `0, 40, 100, 100` keeps the bottom 60%; the preview follows the monitor's aspect ratio. Each dimension must span at least 5%. The effective region also excludes the taskbar's work area.
5. Check **Automatically fit windows on this display**, then **Apply and save**. Windows assigned to that display are clamped and, if resizable, shrunk into the usable area. Other monitors are left alone.
6. Close or hide the settings window to keep running in the tray. Double-click the tray icon to reopen settings. The tray menu offers pause, resume and quit. A separate Quit button is available in settings.

**Emergency pause: Ctrl+Alt+Shift+F12.** This always pauses; it never toggles back on. Pausing is saved so restarting stays paused. If registration fails because another app owns the shortcut, automatic fitting and hiding are disabled until ScreenRescue is restarted with the shortcut available. Tray Resume enables the saved bounds for this session; Apply and save persists enabled state for future launches.

Launch at sign-in is opt-in and uses only `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\ScreenRescue`. Start hidden is a separate option. Keep the executable at a stable location before enabling startup; disable startup before moving/deleting the app. No administrator permission is required.

## Build and test

Requires Windows 10/11 and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). No third-party runtime packages are used. From the repository root:

```powershell
dotnet build src/ScreenRescue -c Release
dotnet run --project tests/ScreenRescue.Core.Tests -c Release
dotnet run --project tests/ScreenRescue.Windows.Tests -c Release
dotnet run --project src/ScreenRescue -c Release
```

The core checks cover negative monitor coordinates, portrait bounds, taskbar exclusion, clamping, fixed-size windows, invalid percentages and config persistence. The Windows smoke checks open the settings window briefly, use a separate temporary config, exercise the real HWND pause handler, and verify primary/disconnected display guards. When a secondary display exists they also create an isolated child test window and verify native movement and maximize fitting, restricting correction to that window. Run them in an interactive Windows session with no other ScreenRescue instance.

Create a portable x64 folder including the .NET runtime:

```powershell
dotnet publish src/ScreenRescue -c Release -r win-x64 --self-contained true -o publish
```

Run `publish\ScreenRescue.exe`; keep all files in the folder together. For Windows on ARM, substitute `win-arm64`. Framework-dependent builds require the .NET 8 **Desktop Runtime**. GitHub Actions builds, runs core checks and uploads a self-contained x64 artifact on each push/PR. Download and extract that artifact from the Actions page.

## Behavior and limitations

- This is best-effort window management, not a Windows per-monitor work-area override. A 650 ms scan moves windows after they appear or change. Scans wait while the left mouse button is down, so dragging between monitors remains possible. Rejected moves are retried no faster than every two seconds. There may be a brief flash in the damaged area.
- Maximize is approximated: a maximized ordinary window on the selected monitor is asynchronously restored, then resized to the usable rectangle on a later scan. It is no longer in Windows' true maximized state; restoring does not preserve its earlier normal rectangle. Snap layouts, app-owned fullscreen and custom maximize behavior may override this. No hooks or DLLs are injected into other processes. See Microsoft's [window sizing documentation](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features).
- Only visible, unowned, captioned top-level windows are eligible. Minimized, cloaked, tool, shell, child and ScreenRescue's own windows are skipped. Dialogs, borderless apps, games, exclusive fullscreen, secure desktop and other virtual desktops are outside the intended scope. Fixed-size windows are repositioned without shrinking and may extend beyond the area. Apps enforcing a minimum size may also overflow.
- Windows selects a window's monitor based on its rectangle; windows mostly on another display are not rescued until Windows assigns them to the selected display. Disconnecting the selected monitor or making it primary suspends fitting. It resumes if the saved device name returns as secondary. Display device names can change after driver/hardware changes, so recheck the selection after reconnecting equipment.
- Elevated apps can reject changes from a normal process. Run both apps at the same privilege level if necessary; ScreenRescue does not request elevation. Mixed DPI uses PerMonitorV2 and physical desktop coordinates, including negative monitor positions, but needs real-hardware validation on your display arrangement.
- Pausing/quitting stops future corrections; previously moved windows are not restored. Windows display modes, global work area and resolution are never changed. Startup settings are retained until explicitly disabled.
- Configuration is written atomically to `%LOCALAPPDATA%\ScreenRescue\settings.json`. Invalid/unreadable settings show a warning and start paused. There is one running instance per Windows session. To reset, quit ScreenRescue and rename/delete that settings file.

## Manual acceptance checklist

- With a portrait secondary display, verify the bottom 60% preview and taskbar exclusion; try custom bounds and a monitor positioned left/above primary.
- Drag Notepad/File Explorer onto the target, release, resize and maximize. Confirm primary-display windows, owned dialogs and minimized windows stay untouched.
- Test mixed display scaling, reconnect, orientation changes, making the target primary and minimum-size/elevated apps.
- Pause via the keyboard while settings are hidden; confirm windows stop moving and restart remains paused. Reopen from tray, resume, quit and verify the shortcut is released.
- Enable startup from the final executable location, sign out/in, test start hidden, then disable startup. Check malformed config recovery without losing access to pause/quit.

Hardware-specific movement and sign-in behavior must be verified on the intended monitor setup; passing automated checks does not establish those behaviors for every app or driver.
