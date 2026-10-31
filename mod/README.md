# mod

Standalone *Cult of the Lamb* mod. No BepInEx is used: Doorstop loads
`RemotePlayEnhanced/Bootstrap.dll`, which creates the mod component after the
first scene loads.

## Installation

Extract the release zip into the game folder:

```
Cult of the Lamb/
  winhttp.dll                 Doorstop 3 proxy (public domain)
  doorstop_config.ini         targetAssembly=RemotePlayEnhanced\Bootstrap.dll
  RemotePlayEnhanced/
    Bootstrap.dll
    RemotePlayEnhanced.dll
    0Harmony.dll              original Harmony 2
    config.cfg
    capabilities.json
    RemotePlayEnhanced.log
```

The first launch creates `config.cfg` with defaults. The desktop app can write
the same file and reinstall the bundle.

If BepInEx is already installed, remove its `winhttp.dll` and
`doorstop_config.ini` first (or use a clean game folder). Two Doorstop proxies
cannot occupy the same `winhttp.dll`.

This build does **not** load BepInEx plugins, so Thunderstore mods that depend
on BepInEx will not work alongside it.

## Milestones 4–8 + standalone loader (current)

### Four players

The two-player cap is a single allocation inside `CoopManager`'s static
constructor:

```csharp
AllPlayerGameObjects = new GameObject[2];
```

Everything downstream indexes that array, so at startup the mod replaces it
with one sized from `PlayerCount`. Once vanilla co-op is active, the mod calls
the game's own `HandleCoopCreation` routine once a second; that routine walks
the unclaimed slots, checks the matching Rewired player for joysticks and
spawns a guest per controller. It exists in the shipped assembly but has no
callers, so the pause-menu flow cannot reach it on its own.

The first guest is still added through the pause menu (the vanilla assign-input
flow). Guests 3 and 4 hot-join. All art and animation come from the existing
player prefab; the game assigns the Goat skin to every guest because
`PlayerFarming.isLamb` is false.

### Dynamic split screen

While players are close the game keeps its normal single camera. When the
maximum pairwise spread crosses `SplitDistance`, the director picks a layout:

- **2 viewports** when the players form two clusters that each fit a half;
  vertical or horizontal depending on which way the clusters separate.
- **3 viewports** for three spread players: one full-height half plus two
  right-hand quadrants.
- **4 viewports** (quadrants) when every player is far enough apart.

It falls back to one view when the spread drops below `MergeDistance`, so the
view does not flap at the threshold. Viewport rects and orthographic size are
interpolated over `TransitionMs`.

The main camera stays the only object tagged `MainCamera`; up to three
additional untagged cameras are created from its settings. While split, the
game's `CameraFollowTarget` is disabled and all cameras are driven from the
mod's `LateUpdate`. On merge, the original rect is restored and `ForceSnapTo`
hands the current position back before the follower is re-enabled.
Conversations (`IN_CONVERSATION`), menus (`GameManager.InMenu`) and having
fewer than two active players all force the merged view.

The split-screen decision logic (`SplitScreenMath.cs`) is pure and unit-tested;
the Unity layer only converts positions and applies the plan.

### Dialogue no-freeze

Vanilla freezes every player through two paths, both taking a
`shouldPlayerBeInactive` flag as their first argument:

- `GameManager.OnConversationNew` → `PlayerFarming.SetInactive` on every player
- `CoopManager.OnConversationNew` → `SetStateForAllPlayers(Inactive, ...)`

Harmony prefixes clear that flag for ordinary conversations. The matching
`OnConversationEnd` handlers get the same treatment, so closing a conversation
does not reset a player who is still walking.

Scripted conversations keep their original lock. They are recognised from the
conversation itself: a letterbox already on screen, or any line whose
`SetZoom` flag is set. Barks are ignored entirely.

For ordinary conversations, the mod anchors on the first non-player speaker
seen (falling back to the player's position when the conversation began) and
calls `MMConversation.Close` once the player moves beyond `ExitRadius`.
`DialogueRules.cs` holds the distance/exception policy and is unit-tested.

### Bulk tithes

Pressing the configured key (default `T`) collects from every active follower
within `TitheRadius` of the main player. It does not require the
`Possessions_ExtortTithes` doctrine and does not use the interaction menu.

Vanilla pays through `interaction_FollowerInteraction.ExtortMoneyRoutine`, which
spawns 3–6 pickups whose callback adds
`InventoryItem.ITEM_TYPE.BLACK_GOLD`. The bulk path marks
`FollowerBrainStats.PaidTithes` only after `Inventory.AddItem` succeeds, pays the
same 3–6 black gold, and spawns one black-gold visual per follower when
`ResourceCustomTarget.Create` resolves. Already-paid followers are skipped; the
game resets the flag daily. A cooldown (default 2 s) stops key spam, and menus,
conversations and letterboxed cutscenes are ignored.

`BulkTitheRules.cs` holds the radius and cooldown policy and is unit-tested.

### Standalone loader

`Bootstrap.dll` implements both Doorstop entry points:

- `Doorstop.Loader.Main` for Doorstop 3;
- `Doorstop.Entrypoint.Start` for Doorstop 4.

It installs an `AssemblyResolve` handler so the mod, Harmony and any future
dependency load from the mod folder, then waits for
`RuntimeInitializeOnLoadMethod(AfterSceneLoad)` before creating the
`RemotePlayEnhanced` GameObject. A `bootstrap.log` records each step.

The mod itself is a plain `MonoBehaviour`. Feature code is unchanged; it uses a
small local logger and cfg reader instead of a loader framework.

No game assembly is referenced: every game type is resolved by name at runtime.
Unity reference assemblies come from the public `UnityEngine.Modules` NuGet
package, so CI still needs no copyrighted DLLs. If a game update renames a
target, the feature degrades and says so in `capabilities.json` instead of
failing startup.

Known limits:

- the bundled Doorstop proxy is Windows x64; Linux and macOS are not packaged;
- no Thunderstore/BepInEx plugin compatibility — those mods need BepInEx;
- bulk tithes is a keyboard shortcut on the host; remote gamepad players use the
  vanilla Extort interaction when the doctrine is available;
- scripted-conversation detection is conservative: a zoomed follower chat keeps
  the vanilla lock rather than risk breaking a cutscene;
- auto-exit tracks the main player; if a guest could start a conversation in a
  future build, the anchor logic would need to follow that guest;
- the vanilla health HUD only has two heart groups, so guests 3 and 4 share the
  second group until the HUD is cloned per player (`per-player-hud` in the
  capability report);
- clone cameras copy the camera component but not post-processing components,
  so colour grading may differ in split viewports;
- clone cameras are not clamped by `CameraLimiter` yet, so edges may show
  outside the room;
- `Camera.main` keeps viewport 0's rect, so mouse-to-world aim is only exact
  there; gamepad players are unaffected.

## Settings

Written to `RemotePlayEnhanced/config.cfg`, and settable by the Tauri app:

| Section | Key | Meaning |
|---|---|---|
| General | `PlayerCount` | 2–4 local players |
| Features | `AutoHotJoin` | fill free slots when extra gamepads connect |
| Features | `DynamicSplitScreen` | split/merge by player spread |
| Features | `DialogueNoFreeze` | movement during conversations |
| Features | `BulkTithes` | collect every follower in range |
| Camera | `MergeDistance` / `SplitDistance` | hysteresis thresholds |
| Camera | `TransitionMs` | viewport blend time (default 250) |
| Camera | `StateDwellMs` | minimum time between split layouts (default 500) |
| Camera | `Padding` | world units kept around a cluster |
| Camera | `MaxSplitSize` | largest orthographic half-height per viewport |
| Dialogue | `ExitRadius` | walk-away distance that ends a conversation |
| Tithes | `Radius` | collection radius |
| Tithes | `Key` | bulk-collection key (default `T`) |
| Tithes | `Cooldown` | seconds between bulk collections (default 2) |

`SplitDistance` must exceed `MergeDistance`; that is the hysteresis that stops
the view flapping at the boundary. If the configured values violate that, the
mod clamps `SplitDistance` at startup and the log records a warning.

A feature switched off is not installed at startup (split screen, dialogue
patches and tithe resolution all check the cfg in `Awake`).

## Capability report

`RemotePlayEnhanced/capabilities.json` is rewritten on every start. Each feature
is `active`, `degraded` or `unavailable`, with the reason, so the desktop app
can show what actually works on the installed game version. The report also
records `unityVersion`, `assemblyVersion` and `knownBuild`; the last is false
when the game is not the verified 2022.3.62f2 build, in which case the mod logs
a warning and tries every target best-effort.

## COTLMP

COTLMP is a BepInEx plugin, so it cannot be loaded by this standalone build. If
a `BepInEx/plugins/COTLMP/` or `COTLMP/` folder is present, the mod logs a
warning and keeps co-op patches disabled.

## Building

```bash
dotnet build mod/RemotePlayEnhanced.csproj -c Release
dotnet build mod/bootstrap/RemotePlayEnhanced.Bootstrap.csproj -c Release
```

The mod build copies `0Harmony.dll` from the `Lib.Harmony` package. The app's
release workflow assembles `winhttp.dll`, `doorstop_config.ini` and the three
DLLs into the standalone bundle.
