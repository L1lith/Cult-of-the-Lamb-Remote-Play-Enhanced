# Remote Play Enhanced desktop app

Tauri 2 shell + Astro/SolidJS frontend for configuring the standalone mod and
keeping the loader installed.

## What it does

- Finds Cult of the Lamb through the Steam library folders
  (`steamapps/libraryfolders.vdf`), with a manual folder override.
- Installs the standalone Doorstop loader and the mod bundle:
  - `winhttp.dll` + `doorstop_config.ini` in the game folder;
  - `RemotePlayEnhanced/` with the bootstrap, mod, Harmony and config.
- Reports whether the loader and mod are present.
- Writes `RemotePlayEnhanced/config.cfg`.
- Reads `RemotePlayEnhanced/capabilities.json` and shows each feature as
  active, degraded or unavailable, with the reason the mod reported.
- Launches the game through `steam://rungameid/1313140`.
- Checks GitHub Releases for app updates with the Tauri updater.

It deliberately does **not** install Thunderstore mods: those are BepInEx
plugins and cannot run without BepInEx.

## Development

```bash
cd app
npm install
npm run dev          # browser preview; config is stored in localStorage
npm run tauri dev    # real shell; needs Rust + Tauri system dependencies
```

The browser preview is intentional: the frontend is usable without Rust. The
Tauri-only actions (deploy, launch, updates) return a clear error there.

### Building the loader bundle

The app expects this tree under `src-tauri/resources/bundle/`:

```
bundle/
  winhttp.dll                 copied from ../../loader/
  doorstop_config.ini         copied from ../../loader/
  RemotePlayEnhanced/
    Bootstrap.dll             mod/bootstrap/bin/Release/
    RemotePlayEnhanced.dll    mod/bin/Release/
    0Harmony.dll              copied by the mod build
    LICENSE-Doorstop.txt      copied from ../../loader/
    LICENSE-Harmony.txt       copied from ../../loader/
```

Assemble it locally with:

```bash
dotnet build mod/RemotePlayEnhanced.csproj -c Release
dotnet build mod/bootstrap/RemotePlayEnhanced.Bootstrap.csproj -c Release

mkdir -p app/src-tauri/resources/bundle/RemotePlayEnhanced
cp loader/winhttp.dll loader/doorstop_config.ini app/src-tauri/resources/bundle/
cp mod/bin/Release/RemotePlayEnhanced.dll \
   mod/bin/Release/0Harmony.dll \
   mod/bootstrap/bin/Release/Bootstrap.dll \
   app/src-tauri/resources/bundle/RemotePlayEnhanced/
cp loader/LICENSE-Doorstop.txt loader/LICENSE-Harmony.txt \
   app/src-tauri/resources/bundle/RemotePlayEnhanced/
```

`src-tauri/resources/**/*.dll` is gitignored. CI assembles the same tree from
the build artifacts.

## Tests

```bash
npm test
cargo test --manifest-path src-tauri/core/Cargo.toml
```

The JavaScript tests cover config parsing, formatting, validation and
capability reports. The Rust `rpe-core` crate covers the same config model plus
Steam library parsing and standalone bundle deployment, with no external
crates.

## Config model

The app and the mod share one INI file:
`RemotePlayEnhanced/config.cfg`.

| Section | Keys |
|---|---|
| General | `PlayerCount` |
| Features | `AutoHotJoin`, `DynamicSplitScreen`, `DialogueNoFreeze`, `BulkTithes` |
| Camera | `MergeDistance`, `SplitDistance`, `TransitionMs`, `StateDwellMs`, `Padding`, `MaxSplitSize` |
| Dialogue | `ExitRadius` |
| Tithes | `Radius`, `Key`, `Cooldown` |

Unknown keys are reported in the UI before saving, because saving replaces the
file. The mod reloads settings at startup, so restart the game after saving.

## Loader layout

```
Cult of the Lamb/
  winhttp.dll                 Doorstop 3 proxy (public domain)
  doorstop_config.ini         targetAssembly=RemotePlayEnhanced\Bootstrap.dll
  RemotePlayEnhanced/
    Bootstrap.dll             Doorstop entry point
    RemotePlayEnhanced.dll    mod
    0Harmony.dll              original Harmony 2
    config.cfg
    capabilities.json
    RemotePlayEnhanced.log
```

If another `winhttp.dll` proxy is already present (for example from BepInEx),
deployment refuses rather than overwriting it. Remove the old loader first, or
use a clean game folder.

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
    resources/bundle/  loader + mod DLLs (assembled by CI/local)
```
