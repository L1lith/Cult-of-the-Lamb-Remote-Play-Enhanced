# Cult of the Lamb — Remote Play Enhanced

A standalone *Cult of the Lamb* mod that turns local co-op into a
**4-player, dynamic split-screen** experience for **Steam Remote Play Together**,
with a Tauri desktop app for installing, configuring and updating it.

No BepInEx is used. The mod brings its own public-domain Doorstop proxy,
bootstrap and Harmony runtime. That also means it does **not** load BepInEx
plugins such as Thunderstore mods — the trade was made deliberately: standalone
loading over third-party plugin compatibility. The bundled Doorstop proxy is
Windows x64; Linux and macOS would need their own proxy and launcher.

> **Status:** milestones 3–8 implemented (4-player expansion, dynamic split
> screen, conversation/menu merge, dialogue no-freeze, bulk tithes, desktop app)
> plus the standalone loader. Release CI and version-tolerance hardening are in
> place; the next game update should be validated in-game.
> Read [`docs/DESIGN.md`](docs/DESIGN.md) first — it is the source of truth for
> architecture, verified findings, the split-screen algorithm, and
> version-tolerance strategy.

## How it works, in one paragraph

Steam Remote Play Together streams the host's screen and sends each remote
player's input back as a virtual gamepad. There is no network layer and no
server; every player is local to the host process. The mod therefore only has to
do three things: let the game accept more than two local players, split the view
when they spread out and merge it when they regroup, and stop dialogues from
freezing everyone else.

## Verified against the installed build

- **One player skeleton, not two.** The Lamb and Goat are Spine skins on the
  shared `player-main` skeleton, which is why they share one atlas. Guest
  avatars are therefore a skin assignment, not a new art set.
- **Co-op is list-based.** `SetStateForAllPlayers`, `PositionAllPlayers` and
  `DoAllPlayersHaveJoysticks` already exist; the 2-player limit sits in the
  spawn/UI layer, not the core.
- **The camera is scriptable.** `CameraFollowTarget` accepts multiple targets and
  exposes zoom/offset setters, so split screen can drive supported APIs rather
  than fighting the camera.
- **No SteamStub.** The executable is a stock Unity player; storing this is why
  no unpacking step exists.

Architecture detail, asset locations and the atlas vocabulary are in
[`docs/DESIGN.md`](docs/DESIGN.md) §3–§5 and §13.

## Layout

```
docs/DESIGN.md        architecture and decisions (source of truth)
loader/               Doorstop 3 proxy, config and public-domain license
mod/                  standalone mod — C#, Harmony 2
  src/
  bootstrap/          Doorstop entry point
app/                  Tauri + Rust shell, SolidJS UI
  src/
  src-tauri/
.github/workflows/    CI: build mod, build app, publish releases
```

Installed layout:

```
Cult of the Lamb/
  winhttp.dll
  doorstop_config.ini
  RemotePlayEnhanced/
    Bootstrap.dll
    RemotePlayEnhanced.dll
    0Harmony.dll
    config.cfg
    capabilities.json
    RemotePlayEnhanced.log
```

## Features

| Feature | Behaviour |
|---|---|
| 4 local players | Guests sharing the host's cult; no separate saves |
| Dynamic split screen | 1 → 2 → 4 viewports by player spread, with hysteresis |
| No dialogue freeze | Free movement during conversations; auto-exit when you walk away |
| Bulk tithes | Collect every follower in radius at once; no doctrine requirement |

COTLMP is **not** a dependency. If a `COTLMP` folder is present the mod disables
its co-op patches so the two cannot fight over `CoopManager`.

## Building

```bash
# Mod + bootstrap (needs the .NET SDK; Unity stubs come from NuGet)
dotnet build mod/RemotePlayEnhanced.csproj -c Release
dotnet build mod/bootstrap/RemotePlayEnhanced.Bootstrap.csproj -c Release

# App frontend (works in a browser without Rust)
cd app
npm install
npm run dev

# App shell (needs Rust and the Tauri system dependencies)
npm run tauri dev
```

For a packaged app, assemble the standalone bundle under
`app/src-tauri/resources/bundle/` first. See
[`app/README.md`](app/README.md).

Tests:

```bash
cd app && npm test
cargo test --manifest-path app/src-tauri/core/Cargo.toml
```

Releases are built by `.github/workflows/mod.yml` and
`.github/workflows/app.yml`:

- pushing a `mod-v*` tag builds and publishes a standalone zip containing the
  Doorstop proxy, bootstrap, mod and Harmony;
- pushes to `main` create draft app releases with updater artifacts;
- pull requests build both without publishing.

The app workflow builds the mod and bootstrap, tests both the JavaScript config
model and the dependency-free Rust core, then packages the Windows/macOS/Linux
matrix with `tauri-action`. Updater signing keys are repo secrets.
