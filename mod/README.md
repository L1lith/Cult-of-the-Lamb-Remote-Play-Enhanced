# mod

BepInEx 5 plugin for *Cult of the Lamb*.

## Milestone 3 (current)

The plugin now lifts the two-player cap.

The cap is a single allocation inside `CoopManager`'s static constructor:

```csharp
AllPlayerGameObjects = new GameObject[2];
```

Everything downstream indexes that array, so at startup the plugin replaces it
with one sized from `PlayerCount`. Once vanilla co-op is active, the plugin
calls the game's own `HandleCoopCreation` routine once a second; that routine
walks the unclaimed slots, checks the matching Rewired player for joysticks and
spawns a guest per controller. It exists in the shipped assembly but has no
callers, so the pause-menu flow cannot reach it on its own.

The first guest is still added through the pause menu (the vanilla
assign-input flow). Guests 3 and 4 hot-join. All art and animation come from
the existing player prefab; the game assigns the Goat skin to every guest
because `PlayerFarming.isLamb` is false.

Nothing here references `Assembly-CSharp` or `UnityEngine`: game types are
resolved by name at runtime, which keeps CI free of copyrighted DLLs. If a game
update renames a target, the feature degrades and says so in
`capabilities.json` instead of failing startup.

Not implemented yet: split screen (milestone 4), dialogue no-freeze
(milestone 6), bulk tithes (milestone 7). The vanilla health HUD only has two
heart groups, so guests 3 and 4 share the second group's display until the HUD
is cloned per player; `capabilities.json` reports this as `per-player-hud`.

## Settings

Written to `BepInEx/config/com.l1lith.cotl.remoteplayenhanced.cfg`, and settable
by the Tauri app:

| Section | Key | Meaning |
|---|---|---|
| General | `PlayerCount` | 2–4 local players |
| Features | `AutoHotJoin` | fill free slots when extra gamepads connect |
| Features | `DynamicSplitScreen` | split/merge by player spread |
| Features | `DialogueNoFreeze` | movement during conversations |
| Features | `BulkTithes` | collect every follower in range |
| Camera | `MergeDistance` / `SplitDistance` | hysteresis thresholds |
| Dialogue | `ExitRadius` | walk-away distance that ends a conversation |
| Tithes | `Radius` | collection radius |

`SplitDistance` must exceed `MergeDistance`; that is the hysteresis that stops
the view flapping at the boundary.

## Capability report

`BepInEx/plugins/RemotePlayEnhanced/capabilities.json` is rewritten on every
start. Each feature is `active`, `degraded` or `unavailable`, with the reason,
so the desktop app can show what actually works on the installed game version.

## COTLMP

If `BepInEx/plugins/COTLMP/` exists, the mod logs a warning and keeps co-op
patches disabled. Both mods patch `CoopManager`; running them together corrupts
state.
