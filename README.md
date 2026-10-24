# ChokePoint for Teams

**Your line. Your rules. Their meeting.** Artificially inject network disruption into your Teams meetings.  Make Teams misbehave, on your own PC only, with one click.  Affects up-traffic only, so you don't miss a thing.

It is a focused spin-off of [ChokePoint](https://github.com/Esk11/ChokePoint), which does the same kind of thing for every app. ChokePoint for Teams does one job: pick how bad Teams' connection should be today.

## Features

- **Six levels of trouble, one click each.** No sliders to fiddle with, just pick a button:

  | Level | What the people on your call get |
  |---|---|
  | **No Effect** | Normal operation. Nothing is touched. |
  | **Minor Turbulence** | Noticeable jitter and lag on voice and video. |
  | **Moderate Disturbance** | Voice and video too flaky to hold a conversation. |
  | **Significant Disruption** | Even with video off, voice is unusable. |
  | **Major Disruption** | Calls time out, drop and try to reconnect. |
  | **Appear Offline** | All of Teams' upload traffic is blocked. |

- **Teams only.** Nothing else on your PC is touched.
- **Upload only.** Only what Teams sends out is affected. What it receives is never touched, so you can still hear everyone else.
- **Live view.** Shows whether Teams is detected, its upload as a live chart, and its download speed.
- **Back to normal.** The **Back to normal** button (or closing the app) puts everything back instantly.

## Install

1. Download the latest `ChokePointforTeams-Setup.exe` from Releases.
2. Run it and follow the steps.
3. Start ChokePoint for Teams and accept the Windows permission prompt. It needs administrator rights to watch and change Teams' traffic.

**Requirements:** 64-bit Windows 10 or 11, and the Microsoft Edge WebView2 Runtime (already on most PCs; the app tells you if it is missing).

## Using it

1. Open ChokePoint for Teams. The Teams card says **Waiting for Teams network activity** until Teams sends or receives something, for example when you start or join a call.
2. Click a level. The change starts straight away, and the chart and the level label take that level's colour.
3. Click **No Effect** or **Back to normal** to stop.
4. Minimises to System Tray

The app always starts at **No Effect**. The level you pick is never saved, so a restart is always a clean slate. The window position and profile configuration are stored in `%AppData%\ChokePointTeams\`.

## Good to know

- The levels are tuned by ear and are approximate. Real calls react differently depending on the network, the Teams version and the codec in use. The built-in defaults are in the `Profiles` table in `Limiter.cs`.
- To tune the installed app without reinstalling it, edit `%AppData%\ChokePointTeams\profiles.json` and restart the app. The file is created with the built-in values on first launch. It contains six entries in button order: No Effect, Minor, Moderate, Significant, Major and Offline. Each entry has `rateKBs` (use `-1` for no cap, `0` for blocked), `lossPct` (0-100), `jitterMs` (non-negative), and `blackoutSec` / `clearSec` (seconds, from 0 to 31536000). Keep all six entries; an invalid file produces a warning and the app uses its built-in values.
- Disturbance is made by slowing, delaying and dropping Teams' outgoing packets. The delay is random, so some packets can arrive out of order.
- It recognises the Teams apps `ms-teams` (new Teams), `teams` (classic Teams) and `msteams`. Teams running in a web browser is not affected.
- DNS and local (loopback) traffic are never touched.
- Everything happens on your PC. The people on the call will notice the effect, because it is *your* connection that is going bad. Nothing you do in the app is sent anywhere.
- Use it for fun and for testing how software copes with a bad connection. Be kind to the people on the other end, and do not use it during meetings where it could cause real trouble.

## Why does my antivirus warn me?

ChokePoint for Teams uses the open-source [WinDivert](https://reqrypt.org/windivert.html) packet driver to slow traffic down. Security software often flags programs that use WinDivert as "riskware", and the app is not code-signed yet, so Windows SmartScreen or your antivirus may warn about it. This is a common false alarm for this kind of tool, not proof of a problem. The full source code is here for you to read, and you can build the installer yourself.

## Build from source

You need the .NET SDK version the project targets (see `TargetFramework` in `ChokePointTeams\ChokePointTeams.csproj`). To build the installer you also need [Inno Setup 6](https://jrsoftware.org/isinfo.php).

1. Get WinDivert 2.2.2 (x64) from [reqrypt.org](https://reqrypt.org/windivert.html) and put `WinDivert.dll` and `WinDivert64.sys` in the `ChokePointTeams` project folder.
2. Run `build.bat` for a build folder, or `build_as_installer.bat` for a single installer. Results go to `Builds\`.

To run from a terminal, use a terminal opened as administrator and run `dotnet run` in the `ChokePointTeams` project folder.

## Third-party software

ChokePoint for Teams uses WinDivert (LGPL v3), TraceEvent (MIT), Microsoft Edge WebView2, .NET and the Bangers font (SIL OFL). Licenses and notices are in [`ChokePointTeams/THIRD-PARTY-NOTICES.txt`](ChokePointTeams/THIRD-PARTY-NOTICES.txt).

ChokePoint for Teams is not affiliated with, endorsed by or sponsored by Microsoft. Microsoft Teams is a trademark of Microsoft Corporation.

<!-- Add your license, e.g. MIT, and your support link here once decided. -->
