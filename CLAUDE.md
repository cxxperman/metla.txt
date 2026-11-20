# Working rules for this repo

This is an app of the dev.txt™ ecosystem (Windows, C#/WPF), made from the newapp.txt template. The launcher core.txt installs it, shows its settings and hosts the local hub. Shared design and the hub protocol live in the kit.txt repo.

## Ecosystem rules

1. Colours, radii, font sizes and durations come from `Tokens.cs`. Never write them as numbers in code.
2. The app works without the hub and without the other apps. The hub being absent is normal, not an error.
3. Everything the app tells the outside world goes through the hub (`HubLink`), never through a port of its own.
4. Buttons, switches, blocks, rows, status dots and the brand name come from `Ui.cs`. Do not draw your own.
5. Everyday settings are described in `AppHub.Settings()` and published as `<app>.settings`; core.txt draws them. State is always sent whole. After applying `settings.set`, publish the full state again, even if nothing changed.
6. Each product has its own pixel sprite: rows of `.`, `#`, `E`; one flat orange, dark upright eye, transparent background.

## Shared files: copied, never edited

`src/Tokens.cs`, `src/Ui.cs`, `src/HubLink.cs`, `src/Family.cs` and `src/CoreApp.cs` are copies from kit.txt. Do not edit them here, not even to silence a warning. If one needs a change, say so: it is made in kit.txt and brought back with `tools/sync.ps1 <path to kit.txt>`.

## Language level: C# 5

The app is built by the compiler that ships with Windows (`csc.exe` from .NET Framework 4). It understands C# 5 only:

- no `?.`, no `$"..."`, no `nameof`, no expression-bodied members (`=>` after a member), no `out var`, no pattern matching, no tuples;
- use `delegate { }` or plain lambdas, `string.Format` or `+`, explicit `out` variables;
- WPF is written in code, no XAML; no NuGet packages and no other dependencies.

## Naming

- In the interface and in text the product is lowercase with the signature: `metla.txt™`. Use `Ui.Brand(...)` for it.
- Files and processes carry the plain name: `Metla.exe`. Never `Metla.txt.exe`.
- Hub app id, topics and commands: lowercase Latin with dots, e.g. `metla.settings`, `settings.set`.

## Text

All interface text is in Russian, plain and short, addressed as «вы». Code comments are in Russian too, like the rest of the code.

## How to verify

Build, then run both checks, every time:

```
powershell -ExecutionPolicy Bypass -File build.ps1
./Metla.exe --selftest                 # must exit with 0
./Metla.exe --demo --shot shot.png     # then open shot.png and look at it
```

- Zero compiler errors is not enough: look at the picture. Compare it with core.txt's `docs/screenshot.png`: same backdrop, glass, type and spacing.
- New behaviour gets a check in `SelfTest.Run()`.
- Hub behaviour is tested against the reference hub on a spare port: `node <kit.txt>/hub/hub.js <port>`, then the app with `METLA_HUB_PORT=<port>`, `METLA_DATA=<temp folder>` and `--headless`. Never test against port 42080: that is the user's real core.txt.
- The build copies sources to a short temp folder because `csc.exe` fails on long paths. Keep it that way.

## The user's machine

- Never kill, restart or reconfigure apps that are already running (core.txt, Kapsula, Aura, Discord, Steam). Stop only processes you started, by their PID; no `taskkill /IM`.
- Do not leave windows on the screen: use `--shot` and `--headless` for checks.
- `--demo` and `--selftest` must not touch the real settings folder, the registry or the network.
- No features, numbers or claims in README or interface text that the code does not back.

## This app deletes things

- Nothing is deleted without a confirmation sheet that shows what and how much. Never add a path that deletes without it, and never a hub command that cleans.
- Every deletion goes through `Safe.Delete(root, path)` in `src/Safe.cs`. No `File.Delete`, `Directory.Delete` or recursive delete anywhere else (the self-test's own clean-up of its temp tree is the only exception).
- A change to `Safe.cs`, `Scanner` or `Cleaner` gets a check in `SelfTest.Tree()`; survivors are asserted by name.
- Never run a real clean, empty the real recycle bin, toggle a real autostart entry or launch a real uninstaller while developing. Use `--selftest`, `--demo` and `--scan-only` (read-only).
- The only registry write in the app is `Startup.Set` (the `StartupApproved` mark under HKCU). Keep it that way.
- Not offered on purpose: services, registry cleaning, boosters, drivers, scheduled tasks. See README, «Чего здесь нет и почему».
- dev.txt apps (`DevApps` in `src/Startup.cs`) are installed, removed and put into autostart by core.txt. While core.txt is installed, metla.txt shows no autostart switch and no «Удалить» for them, only the way to core.txt. Keep it that way (kit.txt `hub/PROTOCOL.md`, «Кто за что отвечает»).
