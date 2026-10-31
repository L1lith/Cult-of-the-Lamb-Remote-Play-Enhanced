# Cult of the Lamb — Remote Play Enhanced

Design decisions and technical plan.
Status: **agreed; game internals verified against the installed build; milestone 3 implemented.**

## 1. What this is

A BepInEx mod for *Cult of the Lamb* that turns the game's local co-op into a
4-player, single-screen experience for **Steam Remote Play Together**, plus a
Tauri desktop app that installs, configures and updates it.

Remote Play Together streams the host's screen and delivers each remote player's
input to the host as a virtual gamepad. **There is no network layer and no
server.** Every player is local from the game's point of view, so the entire mod
runs inside the host process.

Steam caps Remote Play Together at 4 players. That is our limit.

## 2. Decisions

| # | Decision | Choice |
|---|---|---|
| 1 | Multiplayer source | Fusion of built-in co-op and COTLMP behaviour, **local only** |
| 2 | COTLMP | Behavioural reference only. **Never loaded alongside this mod** |
| 3 | Player count | 4 |
| 4 | Extra players | Guests — share the host's cult, no own followers, not persisted separately |
| 5 | Camera | **True dynamic split screen** (not zoom-to-fit) |
| 6 | Dialogue | **No movement freeze at all**; auto-exit if the talking player walks away |
| 7 | Tithes | Always available, no doctrine gate, radius-based |
| 8 | Version support | Attempt install on any game version; degrade, never crash |
| 9 | Builds | GitHub Actions produces releases; local build for testing |
| 10 | Toolchain | C# mod (BepInEx + Harmony), Tauri + Rust shell, SolidJS UI |

### Why COTLMP is not a dependency

COTLMP ships as two compiled DLLs (`COTLMP.dll`, `COTLMPServer.dll`) plus asset
bundles, with no source. Its networking cannot be removed, so it cannot be
"partially used": it owns a TCP/UDP client-server stack, a server browser,
handshake/heartbeat logic and remote player prefabs. Two mods patching
`CoopManager` will corrupt each other's state. The mod therefore:

- does **not** reference, load, or IL-merge COTLMP;
- detects `BepInEx/plugins/COTLMP/` at startup and refuses to activate with a
  clear log + in-game message, rather than producing undefined behaviour.

We reimplement locally the only parts worth keeping: extra player avatars and
the on-screen partner indicators.

## 3. Verified build facts

Established by inspecting the installed copy, not assumed.

| Fact | Value |
|---|---|
| Engine | Unity **2022.3.62f2**, built-in render pipeline |
| Assembly | `Assembly-CSharp.dll`, ~15.9 MB Mono build |
| Content system | Addressables — 3,421 bundles under `StreamingAssets/aa/StandaloneWindows64/` |
| Catalog | `StreamingAssets/aa/catalog.json`, 17,025 internal IDs |
| Executable | Stock Unity player, **no SteamStub** (see `docs/` history) |
| COTLMP | Present under `BepInEx/plugins/COTLMP/`, compiled only |
| Player cap | `CoopManager..cctor` allocates `AllPlayerGameObjects = new GameObject[2]`; this is the cap |
| Hot-join routine | `CoopManager.HandleCoopCreation` exists (private static) but has **no callers** in this build |

Asset locations that matter:

| Asset | Where |
|---|---|
| Player base atlas `player-main` (4096×4096) | `Cult Of The Lamb_Data/resources.assets` |
| Emission maps, `crown` | bundle `feec4c3c4756515edb46c48464103089.bundle` |
| `player-main.skel` (8.5 MB), `player-main.atlas` | TextAssets in `resources.assets` |

## 4. Character architecture

This section drives how guest avatars are built, so it is recorded in detail.

**There is one player skeleton, not two.** `Assets/Art/Spine/Player/` contains
`player-main` and `crown`; there is no Goat-specific skeleton anywhere in the
catalog. The Lamb and the Goat are **Spine skins on the same skeleton**, which
is why they share an atlas.

Naming convention inside the atlas:

| Suffix | Character |
|---|---|
| *(none)* | **Lamb** — `images/Body`, `Face/EYE`, `images/Antler` |
| `_GOAT` | **Goat** — `images/Body_GOAT`, `Face/EYE_GOAT`, `images/Antler_GOAT` (70 regions) |
| `_Moth`, `_Rags`, `_Warrior`, `_Traveller`, `_Twins`, `_RidingHood` | Follower skins, not leaders |
| `_BW` | Ghost / black-and-white variant |
| `_HURT`, `_RITUAL`, `_SERMON` | State variants of the same part |

Atlas composition — 955 regions total:

| Prefix | Count | Contents |
|---|---|---|
| `images/` | 584 | body parts: body, arm, leg, head, antlers, robes, feet |
| `Face/` | 137 | eyes, eyebrows, mouths |
| `Weapons/` | 100 | weapons |
| `effects/` | 61 | combat / VFX overlays |
| `Tools/` | 42 | tools |
| `Items/` | 26 | items |
| `Curses/` | 5 | curse icons |

### 4.1 Consequence for guest avatars

Because the skeleton is shared and parts are skinned, a guest avatar is:

1. an instance of the existing player prefab,
2. with a different **Spine skin** assigned,

not a new prefab hierarchy, and not a new art set. That makes 2 → 4 players
substantially cheaper than it first appeared, and it means guest avatars inherit
every animation and equipment attachment for free.

It also means guests cannot diverge visually beyond what a skin can express
(that is a deliberate constraint, not a defect).

### 4.2 Asset-tooling gotchas

Recorded because any future asset work in this project hits them:

- Spine atlas `xy` is the **top-left** corner in image coordinates, not
  bottom-left. Interpreting it as bottom-left yields fully transparent crops.
- `rotate: true` regions are packed **90° clockwise**; they must be rotated
  counter-clockwise to be upright.
- The base atlas lives in `resources.assets`, while its emission maps live in an
  Addressables bundle. Do not assume a texture and its emission share a file.

## 5. What the game already provides

Confirmed in `Assembly-CSharp.dll`.

| Symbol | Use |
|---|---|
| `CoopManager`, `CoopActive`, `CoopPlayerFrozen` | Co-op subsystem; the freeze flag |
| `AllPlayerGameObjects` | Static `GameObject[]`; length 2 is the real player cap |
| `HandleCoopCreation`, `SpawnCoopPlayer`, `CreateCoopPlayer` | Complete guest spawn path, driven by slot index |
| `UICoopAssignController.ConfirmSpawnButtonPress` | Menu path; hardcodes slot 1 and Rewired players 0/1 |
| `CoopBlockersEnabled`, `CoopBlockerController` | Tether walls — removed |
| `SetStateForAllPlayers`, `PositionAllPlayers`, `DoAllPlayersHaveJoysticks`, `ApplyAllBindingsAllPlayers`, `HapticAllPlayers` | Engine is already written against a **player list**, not two hardcoded players |
| `CameraFollowTarget`, `CameraSetZoom`, `CameraSetTargetZoom`, `CameraSetOffset`, `CameraSetConversationMode`, `PanToAll`, `SetTargetCameraRoutine` | Camera is scriptable and multi-target aware |
| `CameraManager`, `CameraZoomRoutine`, `CameraLimiter`, `CameraLimitBounds`, `CacheCameraZoomLimiter` | Singleton camera + the max-zoom clamp |
| `CoopIndicatorLamb`, `CoopIndicatorGoat`, `CoopIndicatorXOffset/YOffset` | Existing partner indicators |
| `FollowerInteractions_AlreadyPaidTithes`, `get/set_PaidTithes` | Tithe state, prevents double collection |
| `DoctrineUpgradeSystem_Possessions_ExtortTithes` | Existing Extort Tithes doctrine (not required by us) |
| `MMTools.DialogueWheel`, `AdvanceDialogue`, `GetAdvanceDialogueButtonDown` | Dialogue state machine |

Two findings shape the plan:

- The `AllPlayers` helpers mean the 2-player limit lives in the **spawn/UI
  layer**, not the core. This is the strongest evidence that 4 players is
  tractable.
- `CameraFollowTarget` already accepts multiple targets and exposes zoom/offset
  setters, so the split-screen director drives supported APIs rather than
  fighting the camera.

## 6. Dynamic split screen

### 6.1 State machine

Hysteresis prevents flapping at the boundary.

| State | Enter when | Exit when |
|---|---|---|
| `MERGED` (1 viewport) | max pairwise spread ≤ `merge_distance` | spread ≥ `split_distance` |
| `SPLIT_2` (2 viewports) | `split_distance` ≤ spread, and a 2-cluster fit succeeds | spread ≤ `merge_distance`, or clusters can't fit |
| `SPLIT_4` (4 viewports) | a 2-cluster fit fails | a 2-cluster fit succeeds |

`split_distance > merge_distance` always. Both configurable.

### 6.2 Partitioning

Players are partitioned spatially, not arbitrarily:

1. Project player positions onto their principal axis (2D PCA).
2. Sort along that axis and split at the largest gap → two clusters.
3. If each cluster's spread fits its viewport at max zoom-out → `SPLIT_2`.
4. Otherwise each player gets their own viewport → `SPLIT_4`.

With 3 players this yields a 2+1 layout, which reads better than three thin
panes. With 4 players in two pairs, 2×2-style halves.

### 6.3 Layout

| State | Layout |
|---|---|
| `MERGED` | full rect |
| `SPLIT_2` | vertical split if clusters differ mainly in X, horizontal if mainly in Y |
| `SPLIT_4` | quadrants |

Transitions interpolate the `camera.rect` values over `transition_ms` (~250 ms)
so the change is not jarring. HUD stays as a single overlay for v1.

### 6.4 Camera implementation

- The main camera stays the only object tagged `MainCamera`. **This is critical**
  — anything tagged `MainCamera` hijacks every `Camera.main` world/screen
  conversion in the game.
- Up to 3 additional cameras are cloned from the main camera's settings
  (orthographic, culling mask, clear flags, near/far) and driven by our own
  `SplitViewFollower` component, **not** by the game's singleton
  `CameraFollowTarget`.
- Only one `AudioListener` may be active; clones have theirs disabled.
- `CameraLimiter` / `CameraLimitBounds` are reused with a configurable raised
  max zoom-out, so players are no longer confined by the vanilla camera bounds.
- While `CameraSetConversationMode` is active (cutscenes, scripted dialogue),
  the director is forced to `MERGED` and restored afterwards.

### 6.5 Known risks

| Risk | Mitigation |
|---|---|
| `Camera.main` assumptions elsewhere in the game | clones never use the MainCamera tag |
| HUD (`Screen Space - Overlay`) spans the whole window when split | accepted for v1; per-viewport HUD is a later option |
| Vanilla health HUD has two heart groups (`health_firstPlayer`, `health_secondPlayer`) | guests 3–4 reuse the second group; per-player HUD cloning is later work |
| Two–four full scene renders | stylised 2D; measure, expected fine |
| Cutscene camera takeover | force merge during conversation mode |
| Flapping at threshold | hysteresis + interpolation |

## 7. Dialogue behaviour

- The global movement freeze is **not applied**: no `CoopPlayerFrozen`, no
  `SetStateForAllPlayers(frozen)` around a conversation.
- Each frame, if the player who opened the dialogue moves further than
  `dialogue_exit_radius` from the follower, the conversation is closed
  gracefully.
- Configurable `dialogue_exit_radius` (default tuned in-game).
- **Exception:** scripted/mandatory conversations (tutorial, first follower,
  cutscene-driven) keep their original lock, since walking out of them would
  break quest state.

## 8. Bulk tithes

- Always available. No `ExtortTithes` doctrine requirement.
- On activation, every follower within `tithe_radius` of the player is collected
  at once, mirroring the blessing flow's visual treatment.
- Reuses `AlreadyPaidTithes` so no follower can be double-collected.
- Configurable `tithe_radius` and an optional cooldown to avoid spam.

## 9. Version tolerance

Requirement: install on any version; if a game update breaks us, rerun the app
and it reinstalls.

1. **Reflection-based patching.** All targets resolved by type/method name with
   signature fallbacks via `AccessTools`. No hardcoded RVAs, no binary patching.
2. **Per-feature isolation.** Each feature is a patch group. A missing target
   disables that feature and logs a warning; it never aborts startup.
3. **Capability report.** On launch the mod writes
   `remoteplay-enhanced/capabilities.json` listing every feature as
   `active | degraded | unavailable` with the reason.
4. **Build manifest.** `game-versions.json` records known-good assembly
   versions. Unknown version ⇒ best-effort mode with all targets attempted.
5. **Repair path.** The Tauri app reads the capability report, shows which
   features are inactive, and offers *Reinstall / Repair* which redeploys the
   DLLs and rewrites config.

## 10. Tauri app

- Detects the game install (Steam library folders → `Cult Of The Lamb.exe`).
- Deploys / updates the mod DLL under `BepInEx/plugins/RemotePlayEnhanced/`.
- Feature toggles map 1:1 to mod patch groups; disabled features are never
  applied, not merely inert.
- Writes the mod config JSON; shows the capability report.
- Launches the game through Steam.
- Self-updates via the Tauri updater against GitHub Releases.

Stack: Tauri (Rust shell) + SolidJS (plain JS, JSX `.jsx`, no TypeScript),
functional style, per project preferences.

## 11. CI

- `mod.yml` — compiles the C# plugin, packages it with BepInEx metadata.
- `app.yml` — matrix build (Windows/macOS/Linux) with `tauri-action`, publishes
  releases, generates the updater manifest.
- Signing: Tauri updater keypair as repo secrets.

**Blocker to resolve:** CI needs the game's managed assemblies
(`Assembly-CSharp.dll`, `UnityEngine.*`) to compile against. These are
copyrighted and cannot be committed. Options: a private `libs` release the
workflow downloads, or a `COTL_MANAGED_ZIP` secret. To be decided.

## 12. Testing limits

This sandbox is Linux with no game runtime. Local verification means:

- compile the mod and the app,
- unit-test camera partitioning, hysteresis, layout selection, config parsing,
- run the Tauri UI headless (Xvfb) for a smoke test.

**Actual gameplay cannot be tested here.** In-game feel is verified on Windows,
aided by a debug overlay showing player spread, camera state, active viewports
and current thresholds.

## 13. Findings log

Chronological record of what investigation established, so later work does not
re-derive it.

| Finding | Impact |
|---|---|
| The game executable carries **no SteamStub**; the shipped `steam_api64.dll` is the genuine Valve binary | No unpacking step exists; DRM work is out of scope |
| COTLMP is compiled-only, with its own TCP/UDP stack and server browser | Confirms "fusion" must be a reimplementation, not a link |
| `AllPlayers` helpers exist in `Assembly-CSharp` | Player count is not hardcoded to 2 in the core |
| `CoopManager..cctor` allocates `AllPlayerGameObjects` with length 2 | The 2-player cap is one array allocation; milestone 3 replaces it |
| `HandleCoopCreation` has no callers in this build | Hot-join is menu-driven for player 2 only; the mod calls the routine itself |
| `ConfirmSpawnButtonPress` hardcodes slot 1 and Rewired players 0/1 | The vanilla menu cannot add a third or fourth guest |
| `PlayerFarming.SetSkin` treats every `isLamb == false` player as the Goat | Guests 2–4 share the Goat skin |
| `Health_Manager.Init` has one single-player and one two-player layout | health HUD for guests 3–4 is degraded until per-player HUD cloning |
| `CameraFollowTarget` supports multiple targets and zoom/offset setters | Split-screen director can use supported APIs |
| `CoopPlayerFrozen` / `SetStateForAllPlayers` are the freeze mechanism | Dialogue lock becomes per-player |
| Lamb and Goat are skins on one Spine skeleton sharing one atlas | Guest avatars are a skin assignment, not new art |
| Character atlas is 955 regions across `images/`, `Face/`, `Weapons/`, `effects/`, `Tools/`, `Items/`, `Curses/` | Skin authoring has a fixed, known vocabulary |
| Base atlas lives in `resources.assets`; emissions in a bundle | Asset tooling must consult both locations |

## 14. Milestones

1. ✅ Design agreed (this document).
2. ✅ Repo scaffold + mod loads under BepInEx, logs capability report.
3. ✅ Player expansion 2 → 4: slot array resized, guests 3–4 hot-join via `HandleCoopCreation`.
4. Camera director: `MERGED` + `SPLIT_2`, hysteresis, interpolation.
5. `SPLIT_4`, cutscene merge, pausing.
6. Dialogue no-freeze + auto-exit.
7. Bulk tithes.
8. Tauri app: install, toggles, capability display, launch.
9. CI releases + updater.
10. Version-tolerance hardening against an actual game update.
