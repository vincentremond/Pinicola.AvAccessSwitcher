# Agent Guidelines

## Project Overview

`Pinicola.AvAccessSwitcher` is a headless Windows System Tray application written in pure functional F# (`.NET 10.0`, `net10.0-windows10.0.19041.0`, WinForms) for **AV Access KVM Switches / Docking Stations** (e.g., iDock C20). Because these docks emulate EDID, Windows keeps extending to a "ghost" monitor when the KVM switches to another PC. The app monitors USB PnP presence + CCD external display targets to switch display topology (`SetDisplayConfig` / `DisplaySwitch.exe`), optionally keeps the screen awake via `SendInput` cursor movement, and persists user settings in YAML.

## Repository Structure & F# Compile Order

F# compilation order in `.fsproj` matters — add new files in dependency order.

- `Pinicola.AvAccessSwitcher.slnx` — Solution file.
- `Pinicola.AvAccessSwitcher/` — Main `WinExe` project (`InternalsVisibleTo` granted to `Pinicola.AvAccessSwitcher.Tests`):
  1. `Measures.fs` — Units of measure (`Second`, `Minute`, `Millisecond`, `Frame`).
  2. `NativeDisplay.fs` — Windows CCD API (`QueryDisplayConfig`, `SetDisplayConfig` via `Vanara.PInvoke.User32`) and `DisplaySwitch.exe` fallback.
  3. `KvmDetector.fs` — WMI `Win32_PnPEntity` detection for AV Access KVM (`VID_1D6B&PID_B022` / `AV Access`).
  4. `CursorActivity.fs` — Idle cursor tracking and smooth `SendInput` screen-wake movement (aborts on user movement or locked desktop).
  5. `AppVersion.fs` — Parses `AssemblyInformationalVersion` (GitVersion) into `VersionInfo`.
  6. `AppLogging.fs` — Serilog daily rolling file logger (`%LOCALAPPDATA%\Pinicola.AvAccessSwitcher\logs\switcher-YYYYMMDD.log`) and log-folder opener.
  7. `AppSettings.fs` — YAML persistence (`YamlDotNet`) at `%LOCALAPPDATA%\Pinicola.AvAccessSwitcher\settings.yaml` (`AutoExtendOnReconnect`, `KeepScreenAwake`).
  8. `TrayApplication.fs` — Pure state machine (`TrayStateLogic.step`), `MailboxProcessor` actor, `NotifyIcon` tray UI, and Windows Toast notifications.
  9. `Program.fs` — WinForms entry point, `--allow-multiple` flag, and session-scoped single-instance OS mutex (`Local\Pinicola.AvAccessSwitcher.SingleInstance`).
- `Pinicola.AvAccessSwitcher.Tests/` — NUnit test project with one `*Tests.fs` module per source module.

## Testing Rules

- **Never run `[<Explicit>]` tests or tests/commands that have side effects on the computer without explicit user approval.**
  - Standard `dotnet test` runs only pure unit tests (`[<Explicit>]` tests are skipped by default).
  - Do not pass `--filter` targeting `[<Explicit>]` tests, and do not invoke functions or scripts that create OS mutexes, change display topology (`SetDisplayConfig` / `DisplaySwitch.exe`), move the cursor, show toast notifications, or otherwise mutate system state unless the user explicitly approves it first.
- All new tests that interact with the OS or hardware (e.g., named mutexes, display topology changes, WMI/PnP queries, cursor movement, notifications, launching processes) must be marked with `[<Test; Explicit("...")>]`.
- Keep domain logic unit-testable by injecting OS/Win32/IO side effects through `internal` `*With` functions or `*Dependencies` / `*Deps` records, rather than calling Win32/WMI/IO APIs directly inside logic functions.

## Build, Tooling & Dependencies

- **Local .NET tools** (`dotnet-tools.json`): `paket`, `fantomas`, `dotnet-gitversion`. Run `dotnet tool restore` if tools are missing.
- **Build**: `dotnet build` (or `.\build.ps1` / `dotnet build -c Release`).
- **Test**: `dotnet test` (pure unit tests only).
- **Formatting**: Always run `dotnet fantomas .` after editing F# files. Formatting rules live in `.editorconfig` (Stroustrup multiline brackets, single-item multiline records/lists/arrays).
- **Dependencies**: Manage NuGet packages strictly with **Paket** (`paket.dependencies`, `paket.references`, `dotnet paket install` / `dotnet paket restore`). Do not use `dotnet add package`. Test-only packages belong in the `group Tests` section of `paket.dependencies`.
- **Versioning & Releases**: Configured via `GitVersion.yml` using Conventional Commits (`feat:`, `fix:`, `chore:`, etc., and `!` / `BREAKING CHANGE:` for major bumps). Pushing to `main` triggers `.github/workflows/release.yml`, which publishes a self-contained single-file `win-x64` EXE and creates a GitHub Release.

## Code Style & Architecture Conventions

- Write pure functional F# (`.NET 10.0`, `net10.0-windows10.0.19041.0`) without `mutable` variables in domain logic (only at unavoidable .NET/Win32 interop boundaries such as `Mutex` `byref` out parameters).
- `WarningsAsErrors` enforces `FS0025` (incomplete pattern matches fail the build).
- Prefer `[<RequireQualifiedAccess>]` on modules and discriminated unions, and use units of measure from `Measures.fs` for time/rate constants.
- Keep mutable/async orchestration inside the `MailboxProcessor` actor in `TrayApplication.fs`, delegating state transitions to pure functions in `TrayStateLogic`.
- Use Serilog structured logging (`Log.Debug`, `Log.Information`, `Log.Warning`, `Log.Error`) with named templates (not interpolated strings).
