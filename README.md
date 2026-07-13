# hsh-installers-poc

A proof-of-concept for a **self-updating local agent + browser client**, packaged
as installable (non-portable) [Velopack](https://velopack.io) releases for Windows,
macOS (Apple Silicon + Intel) and Linux, and published to **GitHub Releases** by a
GitHub Actions pipeline.

It deliberately has no real business logic — a **random number streamed over
SignalR** stands in for whatever live data the production agent would provide. The
point is to exercise the install → run → detect → update → cross-OS-publish loop
end to end. 

## What it does

- **Agent** (`agent/HshAgent`, .NET 8) — a tiny localhost web service (port `9740`)
  exposing:
  - `GET /version` — the running version.
  - `GET /update/status` / `POST /update` — Velopack self-update (download & apply
    in place, then Velopack's `Update.exe` relaunches the new version).
  - SignalR hub at `/hub` that pushes a `RandomNumber` every ~2 s.
- **Web client** (`web/`, Angular 22) — connects to the agent and:
  - **Agent offline** → detects your OS and shows the matching installer as the
    primary download, with the other platforms as smaller links underneath.
  - **Agent online & current** → shows the live random number + agent version.
  - **Newer version available** → a dismissible "update now" banner on top.
  - **Below the configured minimum** (`environment.minAgentVersion`) → hides the
    data and forces a background self-update (same mechanism as the banner).

## Repo layout

```
agent/HshAgent/            .NET 8 agent (web API + SignalR + Velopack self-update)
web/                       Angular 22 client
installer-assets/          per-OS icons + macOS installer text panes
scripts/pack.sh            per-RID Velopack build
scripts/generate-icons.sh  regenerates the placeholder icons
.github/workflows/release.yml   tag-triggered multi-OS publish
```

## Prerequisites

- **.NET SDK 8.0**
- **Node.js ≥ 24.15** (Angular 22's CLI requires it; `nvm use 24` works)
- **Velopack CLI** pinned to the package version: `dotnet tool install -g vpk --version 1.2.0`

## Run it locally

```bash
# 1) Agent
dotnet run --project agent/HshAgent
#   → http://localhost:9740/version , SignalR at /hub

# 2) Web client (separate terminal)
cd web && npm install && npm start
#   → http://localhost:4200
```

With the agent running you'll see the live number. Stop the agent to see the
download view. The update flows can be demoed without a real newer release:

- `http://localhost:4200/?demoUpdate=optional` → optional-update banner
- `http://localhost:4200/?demoUpdate=required` → forced-update view

> Self-update (`POST /update`) only does real work when the agent is running as an
> actual Velopack install — a `dotnet run` dev build reports `unsupported`, which
> is why the web app falls back to a download link there.

## Configuration (`agent/HshAgent/appsettings.json`)

| Key | Meaning |
| --- | --- |
| `Server:Port` | Agent HTTP/SignalR port (default `9740`). |
| `Update:Policy` | `Auto` (scheduled + on-demand), `OnDemandOnly`, or `Disabled`. |
| `Update:Channel` | Empty = auto-detect `os-arch` (e.g. `osx-arm64`). |
| `Update:CheckIntervalHours` | Background check cadence when policy is `Auto`. |
| `Update:Github:RepoUrl` | Repo whose Releases hold the Velopack feed. |
| `Update:Github:Prerelease` | Whether to consider GitHub pre-releases. |

The web client's `minAgentVersion` and download asset names live in
`web/src/environments/environment.ts`.

## Background running & autostart

The whole install is **per-user and never needs admin rights** — Velopack installs
to the user's app-data folder and the autostart hooks only touch per-user
mechanisms. A normal double-click on the installer is all a user does.

- **Windows** — the install hook writes the agent into the
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` registry key (the same
  per-user autostart Slack/Discord use), pointing at the stable
  `%LocalAppData%\HshAgent\current\HshAgent.exe` path. The installer's post-install
  launch is the running agent for the current session; the Run key starts it at
  every subsequent logon. Uninstalling removes the key. The exe is built as
  `WinExe`, so no console window appears. Hook activity is logged to
  `%AppData%\HshAgent\install.log`.
  (Earlier attempts — a Windows Service and a `schtasks` ONLOGON task — both fail
  with access-denied from a non-elevated installer; the Run key needs no
  permissions at all.)
- **macOS** — the first-run hook writes a launchd **LaunchAgent**
  (`~/Library/LaunchAgents/com.hsh.agent.plist`, `RunAtLoad` + crash-only
  `KeepAlive`) and loads it, so the agent starts immediately and at every login.
  Known limitation: dragging the app to the trash doesn't remove the plist
  (Velopack has no uninstall hook on macOS).
- **Linux** — autostart is **not implemented yet**; the AppImage runs only while
  started manually.

Autostart is at user **logon** (after a reboot, once the user signs in). Starting
before login would require an admin-installed system service, which this POC
deliberately avoids.

## Packaging

`scripts/pack.sh <version> <rid>` publishes a self-contained agent and runs
`vpk pack`. RIDs: `win-x64`, `osx-arm64`, `osx-x64`, `linux-x64`.

```bash
scripts/generate-icons.sh                 # (re)build placeholder icons — run once
scripts/pack.sh 0.1.0 osx-arm64           # → dist/releases/
```

Each run produces the installer, the full `*.nupkg`, and the Velopack feed files
(`releases.<channel>.json`, `assets.<channel>.json`, `RELEASES-<channel>`).
`--noPortable` is passed, so **only the installable artifact is produced** — no
portable zip. Branding is applied per-OS from `installer-assets/` (an `.icns` +
plain-text welcome/readme/conclusion panes on macOS, an `.ico` on Windows, a
`.png` on Linux).

> **`vpk` packages for the host OS.** Build each RID on its matching OS — Windows
> packs `Setup.exe`, macOS packs the `.pkg`, Linux packs the `.AppImage`. That's
> why the pipeline uses one runner per platform (and why you can't pack the Linux
> AppImage from macOS).

> **Linux is an AppImage**, not a `.deb`/`.rpm` — that's Velopack's installable,
> self-updating form on Linux. No autostart is registered on Linux yet (a systemd
> user service would be the natural follow-up).

## Publishing a release

Push a semver tag:

```bash
git tag v0.1.0 && git push origin v0.1.0
```

`.github/workflows/release.yml` then:

1. Builds & packs on `windows-latest`, `macos-latest` (both mac arches), and
   `ubuntu-latest`.
2. Collects every artifact and creates the GitHub Release for the tag, attaching
   the installers, the Velopack feed files, and a `hsh-web.zip` of the client.

The running agent reads that Release through Velopack's `GithubSource`
(`releases.<channel>.json` + nupkgs), and the web app's download links resolve via
GitHub's `/releases/latest/download/<asset>` redirect. Only the built-in
`GITHUB_TOKEN` is needed — no external tokens.

## Not done yet (production follow-ups)

- **Code signing / notarization** — packages ship unsigned (the `vpk` signing
  flags are wired but no certificates are configured): Windows Authenticode /
  Azure Trusted Signing, macOS Developer ID + notarization.
- Replace the placeholder "H" icon with a real logo (re-run `generate-icons.sh`
  from a 1024px source, or drop in your own `.icns`/`.ico`/`.png`).
