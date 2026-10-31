# Cult of the Lamb — Remote Play Enhanced

A BepInEx mod that turns *Cult of the Lamb*'s local co-op into a **4-player,
dynamic split-screen** experience for **Steam Remote Play Together**, with a
Tauri desktop app for installing, configuring and updating it.

> **Status:** design agreed; game internals verified against the installed build;
> milestones 3–6 (4-player expansion, dynamic split screen, conversation/menu
> merge, dialogue no-freeze) implemented in `mod/`. Bulk tithes are not
> implemented yet.
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

## Planned layout

```
docs/DESIGN.md        architecture and decisions (source of truth)
mod/                  BepInEx plugin — C#, Harmony
  src/
app/                  Tauri + Rust shell, SolidJS UI
  src/
  src-tauri/
.github/workflows/    CI: build mod, build app, publish releases
```

## Features

| Feature | Behaviour |
|---|---|
| 4 local players | Guests sharing the host's cult; no separate saves |
| Dynamic split screen | 1 → 2 → 4 viewports by player spread, with hysteresis |
| No dialogue freeze | Free movement during conversations; auto-exit when you walk away |
| Bulk tithes | Collect every follower in radius at once; no doctrine requirement |

COTLMP is **not** a dependency and is never loaded alongside this mod; see the
design document for why.

## Building

Releases are built by GitHub Actions. Local building requires the .NET SDK for
the mod and the Rust toolchain plus webkit2gtk for the app.
