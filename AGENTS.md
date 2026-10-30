# Agent Guidelines

## Testing Rules

- **Never run `[<Explicit>]` tests or tests/commands that have side effects on the computer without explicit user approval.**
  - Standard `dotnet test` runs only pure unit tests (`[<Explicit>]` tests are skipped by default).
  - Do not pass `--filter` targeting `[<Explicit>]` tests, and do not invoke functions or scripts that create OS mutexes, change display topology (`SetDisplayConfig` / `DisplaySwitch.exe`), or otherwise mutate system state unless the user explicitly approves it first.
- All new tests that interact with the OS or hardware (e.g., named mutexes, display topology changes, WMI/PnP queries, notifications) must be marked with `[<Test; Explicit("...")>]`.

## Code Style & Formatting

- Written in pure functional F# (`.NET 10.0`, `net10.0-windows`) without `mutable` variables in domain logic.
- Format F# code with Fantomas (`dotnet fantomas .`) after making changes.
- Manage NuGet dependencies with Paket (`paket.dependencies`, `paket.references`, `dotnet paket install`).
