# ScreenRescue

Version **0.5.1** adds an optional **Black out everything outside the usable area** setting. Apply and save fits ordinary app windows and covers the unused margins on the selected physical monitor with solid black windows. There is no window over the usable rectangle, so normal mouse interaction there is unchanged. This is masking, not GPU desktop scaling.

The blackout option is enabled by default and is saved with the selected bounds. Uncheck it and apply to keep window fitting without black masks. Pause now, the tray Pause action, Ctrl+Alt+Shift+F12, and Quit remove the masks. A display-layout change also removes them and pauses fitting. Selecting a new on-monitor area temporarily removes the old masks; applying saves the new area. The full selection is kept visible, including a taskbar that falls inside it; a taskbar outside the selection is hidden by the masks.

Masks cover and block clicks in the unused area without taking keyboard focus. They use topmost windows, rather than changing the NVIDIA driver, resolution or monitor layout. Other always-on-top/system surfaces or exclusive fullscreen apps can appear above them; brief flashes are possible. **Software cannot force physically damaged/stuck pixels to turn black.** The masks hide normal displayed content and do not repair the panel.

Validation includes exact mask coverage and disjointness at negative monitor coordinates, settings persistence, and injected UI checks for applying/disabling masks, Pause/emergency cleanup, display-change cleanup and failure cleanup. Actual live mask windows still require confirmation in an interactive desktop session.

Version 0.5.0 removes the virtual-monitor/whole-desktop capture feature, source-monitor selector, driver-setup link, mouse hook and mouse-release shortcut. It does not install or require any display driver.

The remaining app is explicitly an **optional ordinary-window fitting helper**. It does not resize the entire desktop, taskbar or icons and is not a replacement for NVIDIA Control Panel desktop resizing. It cannot bypass GPU or driver restrictions. A genuine GPU resizing backend has not been implemented or validated.

## Remove the previously added virtual monitor

First quit every older ScreenRescue copy so its desktop viewer and mouse hook are stopped. Exit VDD Control too.

On the machine used during development, the added virtual display adapter was verified as:

- Device description: Virtual Display Driver
- Manufacturer: MikeTheTech
- Device instance: ROOT\DISPLAY\0000
- Driver package: oem32.inf

The NVIDIA RTX 3050 is a separate PCI device using oem10.inf. Do not remove the NVIDIA device or driver.

To remove only the identified virtual display device, run this in an Administrator PowerShell window:

```powershell
pnputil /remove-device "ROOT\DISPLAY\0000"
```

This removes the device, not the separately installed VDD Control application or its staged driver package. Do not click Install in VDD Control again unless you deliberately want the virtual display back. Device identifiers are specific to this PC: on another PC, verify the device using `pnputil /enum-devices /class Display` first.

Administrator access is required. ScreenRescue's own executable never elevates or removes drivers. Removing the virtual display can cause Windows to move any applications currently located on it back to the physical monitors.

## Optional window fitting

1. Select any connected monitor, including the primary.
2. Drag on the preview to draw a usable rectangle; drag inside to move it or on its edges/corners to resize. The on-monitor selection overlay is also available, with Esc to cancel.
3. Click Apply and save to enable ordinary window fitting. Default bounds keep the bottom 60% and exclude the taskbar work area.
4. Pause now, the tray pause action or Ctrl+Alt+Shift+F12 stops future corrections. Quit exits the helper. Closing settings keeps it in the tray.

Automatic startup and starting hidden are opt-in. Settings are saved atomically in `%LOCALAPPDATA%\ScreenRescue\settings.json`. Old desktop-view profiles load paused rather than silently starting ordinary window fitting; obsolete fields disappear on the next save.

Only visible unowned captioned top-level windows are managed. Minimized, cloaked, tool, shell, child and ScreenRescue windows are skipped. Maximized windows are restored and fitted on a later scan; true maximize behavior is not overridden. Apps enforcing a minimum size, higher-privilege apps, borderless apps, games and exclusive fullscreen may not fit. Other monitors are left alone. Pausing does not undo previous moves.

## Build and test

Requires Windows 10/11 and the .NET 8 SDK. No third-party runtime packages are used.

```powershell
dotnet build ScreenRescue.sln -c Release
dotnet run --project tests/ScreenRescue.Core.Tests -c Release
dotnet run --project tests/ScreenRescue.Windows.Tests -c Release -- --headless
dotnet publish src/ScreenRescue -c Release -r win-x64 --self-contained true -o publish
```

The core checks cover geometry, drawing/moving/resizing, invalid bounds, persistence and migration away from the removed desktop mode. Headless WPF checks exercise the real settings controls with injected startup/window services, without changing the desktop. For interactive native-window checks, run the Windows tests without `-- --headless`; they create isolated test windows on the available displays. Live tests require an interactive desktop and no other ScreenRescue instance.
