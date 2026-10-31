# mod

BepInEx 5 plugin for *Cult of the Lamb*.

## Milestones 4–6 (current)

### Four players

The two-player cap is a single allocation inside `CoopManager`'s static
constructor:

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
plugin's `LateUpdate`. On merge, the original rect is restored and
`ForceSnapTo` hands the current position back before the follower is
re-enabled. Conversations (`IN_CONVERSATION`), menus (`GameManager.InMenu`) and
having fewer than two active players all force the merged view.

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

For ordinary conversations, the plugin anchors on the first non-player speaker
seen (falling back to the player's position when the conversation began) and
calls `MMConversation.Close` once the player moves beyond `ExitRadius`.
`DialogueRules.cs` holds the distance/exception policy and is unit-tested.

No game assembly is referenced: every game type is resolved by name at runtime.
Unity reference assemblies come from the public `UnityEngine.Modules` NuGet
package, so CI still needs no copyrighted DLLs. If a game update renames a
target, the feature degrades and says so in `capabilities.json` instead of
failing startup.

Not implemented yet: bulk tithes (milestone 7). Known limits:

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
| Camera | `TransitionMs` | viewport blend time (default 250) |
| Camera | `StateDwellMs` | minimum time between split layouts (default 500) |
| Camera | `Padding` | world units kept around a cluster |
| Camera | `MaxSplitSize` | largest orthographic half-height per viewport |
| Dialogue | `ExitRadius` | walk-away distance that ends a conversation |
| Tithes | `Radius` | collection radius |

`SplitDistance` must exceed `MergeDistance`; that is the hysteresis that stops
the view flapping at the boundary. If the configured values violate that, the
camera clamps `SplitDistance` at runtime and the log records a warning.

## Capability report

`BepInEx/plugins/RemotePlayEnhanced/capabilities.json` is rewritten on every
start. Each feature is `active`, `degraded` or `unavailable`, with the reason,
so the desktop app can show what actually works on the installed game version.

## COTLMP

If `BepInEx/plugins/COTLMP/` exists, the mod logs a warning and keeps co-op
patches disabled. Both mods patch `CoopManager`; running them together corrupts
state.
