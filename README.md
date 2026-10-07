# Flat Hab for Hardspace: Shipbreaker

A [BepInEx](https://github.com/BepInEx/BepInEx) mod that replaces the 3D habitat navigation in
*Hardspace: Shipbreaker* with a plain menu. No more flying the camera from the bed to the
window to the desk to start a shift. Pick what you want to do and go.

```
 HAB
 What would you like to do?

 1  Start Shift
 2  Employee Terminal
 3  Workstation
 4  Posters: Bed
 5  Posters: Suit area
 6  Posters: Kitchen
 7  Look around (3D view)
```

## Features

- A single flat menu in the hab with every station you'd normally walk to:
  - **Wake up**, in the morning
  - **Answer the comms terminal**, on story nights
  - **Start Shift**
  - **Employee Terminal**
  - **Workstation**
  - **Posters**
  - **Leave the Hab**, at the end of the game
- The 3D hab buttons are hidden while the menu is active.
- Camera fly-throughs are replaced with instant cuts (configurable).
- The game's own navigation code does the work. Story beats, triggers, and progression behave as in the unmodded game.
- **F6** switches between the menu and the original 3D hab at any time.
- Works with mouse, keyboard (number keys, arrows/W/S plus Enter), and controller (stick/D-pad plus Confirm).

## Requirements

- Hardspace: Shipbreaker for Windows. Tested on the Steam release, which is a Mono/Unity 2020.3 build.
- [BepInEx 5](https://github.com/BepInEx/BepInEx/releases) (x64). The install script installs BepInEx 5.4.23.5 for you.

## Installation

Download `FlatHab-<version>.zip` from the [latest release](https://github.com/Structed/shipbreaker-flat-hab/releases/latest) and extract it.

### With the script

Close the game, open PowerShell in the extracted folder and run:

```powershell
powershell -ExecutionPolicy Bypass -File .\Install-FlatHab.ps1
```

The script finds the game in your Steam libraries. If BepInEx isn't installed yet, it downloads BepInEx 5.4.23.5 and checks it against a pinned SHA-256 hash. Then it copies the mod to `BepInEx\plugins\FlatHab\`. If your game is elsewhere, add `-GameDir "<game folder>"`.

### Manually

1. Extract [BepInEx_win_x64_5.4.23.5.zip](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5) into the game folder, next to `Shipbreaker.exe`.
2. Copy `FlatHab.dll` to `<game folder>\BepInEx\plugins\FlatHab\`.
3. Start the game. `BepInEx\LogOutput.log` should contain `Flat Hab 0.1.0 loaded`.

To install a version you built yourself, run `.\Install-FlatHab.ps1` from the repository root. It picks up `src\FlatHab\bin\Release\FlatHab.dll` automatically.

## Configuration

After the first launch, the settings are in `BepInEx\config\structed.shipbreaker.flathab.cfg`:

| Setting | Default | Description |
| --- | --- | --- |
| `General.StartInFlatMode` | `true` | Start with the flat menu instead of the 3D hab. |
| `General.ToggleKey` | `F6` | Switch between the flat menu and the 3D hab. |
| `General.InstantCameraCuts` | `true` | Replace hab camera blends with instant cuts. |
| `Appearance.BackgroundOpacity` | `0.65` | How much the hab behind the menu is darkened (0 to 1). |
| `Appearance.MenuScale` | `1` | Menu size multiplier (0.5 to 2.5). |
| `Debug.VerboseLogging` | `true` | Log the hab layout and every menu action to `BepInEx\LogOutput.log`. |

## Uninstalling

```powershell
powershell -ExecutionPolicy Bypass -File .\Install-FlatHab.ps1 -Uninstall                 # removes the mod only
powershell -ExecutionPolicy Bypass -File .\Install-FlatHab.ps1 -Uninstall -RemoveBepInEx  # also removes BepInEx (and any other BepInEx mods)
```

To uninstall by hand, delete `BepInEx\plugins\FlatHab\`. To remove BepInEx entirely, also delete `BepInEx\`, `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`, and `changelog.txt` from the game folder.

## Building

You need the [.NET SDK](https://dotnet.microsoft.com/download) (6 or newer) and a copy of the game with BepInEx installed. The project compiles against the game's own assemblies, so no game files are included in this repository. You only need to build if you want to change the mod; players can use the release zip.

```powershell
dotnet build src\FlatHab -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\Hardspace Shipbreaker"
```

- `GameDir` defaults to `C:\Program Files (x86)\Steam\steamapps\common\Hardspace Shipbreaker`.
- Add `-p:DeployToGame=true` to copy the built DLL straight into `BepInEx\plugins\FlatHab\`.

## How it works

The hab is built from "locations". Each location is a viewpoint with a virtual camera, made up of a set of navigation options (buttons) that blend the camera to another location or to a screen such as the terminal or the job board.

Flat Hab uses this same structure:

- It hooks `Hab3DController.Awake` to find every location, and works out which ones are viewpoints, screens, or poster editors.
- In flat mode, it hides the viewpoint button UI and stops it from reacting to input.
- When you pick a menu entry, the mod finds a navigation option that reaches the right location and calls the game's own `Navigate`. Once the camera arrives, it runs the action: opening the screen, holding Start Shift, starting the story sequence, and so on. Location triggers, blocking conditions, and progression flags are all handled by the game.
- The menu only appears when the game would normally let you navigate. It stays out of the way during wake-up fades, story scenes, screens, the pause menu, and going to sleep.

## Known limitations

- The menu uses Unity's IMGUI and is English only.
- Only tested with the Steam version. Other storefront builds may work if they are also Mono builds.
- Some special cases depend on game state and have seen less testing, such as the end-game "Leave the Hab" option. If something gets stuck, press **F6** to fall back to the 3D hab. Please open an issue with your `BepInEx\LogOutput.log` attached.

## Disclaimer

This is an unofficial fan mod. It is not affiliated with or endorsed by Blackbird Interactive or Focus Entertainment. *Hardspace: Shipbreaker* is a trademark of its respective owners. This repository contains no game code or assets.

## License

[MIT](LICENSE)
