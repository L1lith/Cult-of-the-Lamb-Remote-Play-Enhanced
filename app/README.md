# Remote Play Enhanced desktop app

Tauri 2 shell + Astro/SolidJS frontend for configuring the mod and keeping it
installed.

## What it does

- Finds Cult of the Lamb through the Steam library folders
  (`steamapps/libraryfolders.vdf`), with a manual folder override.
- Reports whether BepInEx 5 and the mod DLL are installed.
- Writes the mod's BepInEx config
  (`BepInEx/config/com.l1lith.cotl.remoteplayenhanced.cfg`).
- Deploys the bundled `RemotePlayEnhanced.dll` into
  `BepInEx/plugins/RemotePlayEnhanced/`.
- Reads `capabilities.json` and shows each feature as active, degraded or
  unavailable, with the reason the plugin reported.
- Launches the game through `steam://rungameid/1313140`.
- Checks GitHub Releases for app updates with the Tauri updater.

## Development

```bash
cd app
npm install
npm run dev          # browser preview; config is stored in localStorage
npm run tauri dev    # real shell; needs Rust + Tauri system dependencies
```

The browser preview is intentional: the frontend is usable without Rust. The
Tauri-only actions (deploy, launch, updates) return a clear error there.

### Mod DLL for local packaging

Build the mod, then copy it next to the Tauri resources:

```bash
dotnet build ../mod/RemotePlayEnhanced.csproj -c Release
cp ../mod/bin/Release/RemotePlayEnhanced.dll src-tauri/resources/
```

`src-tauri/resources/*.dll` is gitignored. CI downloads the mod artifact into
that folder before packaging.

## Tests

```bash
npm test
cargo test --manifest-path src-tauri/core/Cargo.toml
```

The JavaScript tests cover config parsing, formatting, validation and
capability reports. The Rust `rpe-core` crate covers the same config model plus
Steam library parsing and DLL deployment, with no external crates.

## Config model

The app does not add a second source of truth. The mod already owns the BepInEx
config file, so the app parses and rewrites that file and keeps the same keys:

| Section | Keys |
|---|---|
| General | `PlayerCount` |
| Features | `AutoHotJoin`, `DynamicSplitScreen`, `DialogueNoFreeze`, `BulkTithes` |
| Camera | `MergeDistance`, `SplitDistance`, `TransitionMs`, `StateDwellMs`, `Padding`, `MaxSplitSize` |
| Dialogue | `ExitRadius` |
| Tithes | `Radius`, `Key`, `Cooldown` |

Unknown keys are reported in the UI before saving, because saving replaces the
file. The mod reloads settings at startup, so restart the game after saving.

## Updater

`src-tauri/tauri.conf.json` points at the repository's `latest.json` release
asset. Replace `REPLACE_WITH_TAURI_UPDATER_PUBLIC_KEY` with the public half of
the Tauri signing key, and set `TAURI_SIGNING_PRIVATE_KEY` plus
`TAURI_SIGNING_PRIVATE_KEY_PASSWORD` in the release workflow.

## Layout

```
app/
  src/                 Astro page, SolidJS UI, pure config/bridge modules
  tests/               node:test suites
  src-tauri/
    src/               Tauri commands
    core/              dependency-free Rust crate (config, Steam, deploy)
    capabilities/      Tauri v2 permission set
    icons/             app icons
    resources/         bundled mod DLL (CI/local)
```
