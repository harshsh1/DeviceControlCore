# Device Control Core

A minimal console service demonstrating production control/orchestration patterns:
state management, error handling with rollback, background monitoring, externalized
configuration, and structured logging — built on .NET 8 and the Generic Host.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows, macOS, or Linux — no admin/elevated privileges required
- No external services; everything (update packages, OS settings, audit log) is simulated on the local filesystem

## Project structure

```
DeviceControlCore.sln
src/DeviceControlCore/
  Program.cs                  Generic Host bootstrap, DI wiring, Serilog, Ctrl+C shutdown
  appsettings.json             all tunables (intervals, timeouts, paths, log level)
  Models/                      SystemState, AuditRecord, PackageManifest, VersionState
  Options/ServiceOptions.cs    strongly-typed configuration bound from appsettings.json
  Services/
    StateService               the state machine (guarded, idempotent transitions)
    UpdateService               package validation, pre-install hook, rollback, version history
    PreInstallScriptRunner      shells out to a package's pre-install script
    DeviceMonitorService        10s keep-alive ping loop, ACK timeout -> Maintenance
    OsSettingsService           simulated OS setting change + audit trail
    ConsoleHostedService        CLI REPL + the start/stop worker loop
tests/DeviceControlCore.Tests/
  StateServiceTests.cs          state transition coverage
  UpdateServiceTests.cs         rollback decision paths
```

## Build

```
dotnet build
```

## Run

```
dotnet run --project src/DeviceControlCore
```

On startup this launches a background peripheral keep-alive monitor and an interactive
command loop. Runtime artifacts (`logs/`, `state/`, `audit/`) are created relative to the
working directory the app is launched from and are already `.gitignore`d.

Available commands: `start`, `stop`, `signal safety_interlock`, `update --package <path>`,
`device peripheral ack on|off`, `os set-timezone <tz>`, `status`, `exit`. Ctrl+C also shuts
down cleanly at any point (stops background loops, flushes logs, prints a final status line).

## Test

```
dotnet test
```

10 tests: state-transition coverage (valid/invalid/duplicate/recovery paths) and
`UpdateService`'s rollback decision paths (successful install, pre-install-failure rollback,
missing-manifest validation failure).

## Example command sequence (covers all 6 required behaviors)

First, create two test update packages (any location outside `src/` works):

`test-packages/v2-good/manifest.json`:
```json
{"name": "device-control-core", "version": "2.0.0"}
```

`test-packages/v3-bad/manifest.json`:
```json
{"name": "device-control-core", "version": "3.0.0"}
```

`test-packages/v3-bad/pre-install.bat` (Windows — omit/replace with `pre-install.sh` on Linux/macOS):
```bat
@echo off
exit /b 1
```

Then, with the app running:

```
status
start
os set-timezone Africa/Conakry
update --package test-packages/v2-good
status
update --package test-packages/v3-bad
status
device peripheral ack off
```
Wait ~12 seconds here (10s ping interval + 2s ACK timeout) — an `ALERT: Peripheral
keep-alive timeout` line appears and the system transitions to `Maintenance`.
```
status
device peripheral ack on
start
signal safety_interlock
status
stop
exit
```

What this exercises:
- **Update + rollback** — `update --package v2-good` succeeds and activates `2.0.0`;
  `update --package v3-bad` fails its pre-install script and rolls back, leaving
  `current_version` unchanged. Check `state/update/version-state.json` for the full history.
- **Signal → maintenance** — `signal safety_interlock` immediately transitions to
  `Maintenance`, logs an alert, and stops the running worker loop.
- **Peripheral keep-alive** — `device peripheral ack off` causes the next ping to time out,
  raising an alert and entering `Maintenance` automatically; toggling `ack on` again does
  *not* auto-recover (that's what `start` is for) — repeated timeouts while already in
  `Maintenance` don't spam duplicate alerts.
- **OS setting + audit** — `os set-timezone` writes the simulated OS state file and appends
  an audit record (who/when/old→new) to `audit/audit-log.jsonl`.
- **State machine + CLI** — every command above produces a log line; `status` reports the
  live `SystemState` at any point.
- **Graceful shutdown** — `exit` (or Ctrl+C at any point) stops the peripheral monitor and
  worker loop cleanly and prints a final status line before the process exits.

## Configuration

All tunables live in `src/DeviceControlCore/appsettings.json` under `DeviceControl`
(bound to `Options/ServiceOptions.cs`) and `Serilog` (console + rolling file sink, log
level configurable via `Serilog:MinimumLevel:Default`).
