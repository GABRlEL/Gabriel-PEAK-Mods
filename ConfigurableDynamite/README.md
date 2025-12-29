# ConfigurableDynamite (PEAK)

A small BepInEx + Harmony mod that makes PEAK’s **dynamite** configurable.

**Important:** The default config is designed to be **100% vanilla**.  
Nothing changes unless you edit the config entries.

---

## What this mod does

### Fuse / ignition
- Keeps vanilla “walk near dynamite → fuse lights” by default.
- Lets you:
  - disable proximity ignition,
  - override the proximity distance,
  - override the fuse length,
  - optionally ignite **only when you finish using it** (after the use timer),
  - optionally ignite **when you throw it**.

### Explosion
- Optionally scales explosion **damage** and **range**.
- Optionally changes **damage/status type** (poison/curse/burn/freeze/etc).
- Optionally controls whether the explosion can:
  - cook items,
  - launch items,
  - and how strongly items get launched.

### Item handling
- Optional: allow **unlit** dynamite to be pocketed (lit dynamite stays unpocketable).

---

## Installation

1. Install **BepInEx** for PEAK.
2. Build the mod (or use your compiled DLL) and place the DLL here:

`PEAK/BepInEx/plugins/ConfigurableDynamite/ConfigurableDynamite.dll`

3. Start the game once. The config file will be created at:

`PEAK/BepInEx/config/<mod_guid>.cfg`

(Exact filename depends on the template’s GUID.)

---

## How it works (high-level)

- **Dynamite configuration**
  - On `Dynamite.Awake()`, the mod applies fuse-related overrides (distance / fuse time / disable proximity ignition).
  - If you override fuse length, the mod also ensures the internal “fuel” timer is reset to full when the dynamite is unlit and instance data syncs.

- **Manual ignition mode (when AutoFuseEnabled = false)**
  - **StartFuseOnUse**: the mod marks the dynamite when you begin primary use, then ignites the fuse on `FinishCastPrimary()` (when the use timer completes). Cancelling use clears the marker.
  - **StartFuseOnThrow**: the mod marks the item when it is thrown, then ignites after `SetItemInstanceDataRPC()` runs (so the network sync can’t overwrite the lit state).

- **Explosion detection + overrides**
  - Dynamite explosions spawn an `AOE` component.
  - The mod toggles a “spawning dynamite explosion” flag during `Dynamite.RPC_Explode` and then applies overrides to the spawned `AOE` (range, damage, item cooking/launching, damage type, etc.).
  - A marker component is attached so the overrides only apply once and only to dynamite-spawned AOEs.

---

## Configuration reference

All settings live in the BepInEx config file.  
Defaults are chosen to keep vanilla gameplay.

### Section: `Fuse`

#### `AutoFuseEnabled` (bool, default: `true`)
- **Vanilla behavior**: `true`  
- If `true`: fuse lights automatically when a player gets close.
- If `false`: disables proximity ignition (manual ignition mode).

#### `AutoFuseDistance` (float, default: `-1`)
- Distance (Unity units) required to auto-light the fuse.
- `-1` = keep the game’s original distance.
- Only used when `AutoFuseEnabled = true`.

#### `FuseLengthSeconds` (float, default: `-1`)
- Sets the fuse duration in seconds.
- `-1` = keep vanilla fuse time.
- Only values `> 0` override.

#### `StartFuseOnUse` (bool, default: `false`)
- **Only active when** `AutoFuseEnabled = false`.
- If enabled: fuse lights **after** the primary-use timer completes (on cast finish), not on button press.

#### `StartFuseOnThrow` (bool, default: `false`)
- **Only active when** `AutoFuseEnabled = false`.
- If enabled: fuse lights when thrown (ignite happens after instance data sync to avoid network overwrite).

---

### Section: `Explosion`

#### `DamageMultiplier` (float, default: `1`)
- Multiplies `AOE.statusAmount`.
- `1` = vanilla.

#### `RangeMultiplier` (float, default: `1`)
- Multiplies `AOE.range`.
- `1` = vanilla.

#### `DamageType` (enum, default: `Vanilla`)
Controls which **status/damage type** the dynamite explosion applies via `AOE.statusType`.

- `Vanilla` (no change)
- `Injury` (regular damage)
- `Hunger`
- `Cold` (freeze)
- `Poison`
- `Crab`
- `Curse`
- `Drowsy`
- `Weight`
- `Hot` (burn)
- `Thorns`
- `Spores` (shroom)
- `Web`

> Note: this changes the AOE’s `statusType`. If the base game adds additional effects outside of `AOE.statusType/statusAmount`, those are not modified.

#### `ItemCooking` (enum, default: `Vanilla`)
Controls `AOE.cooksItems` for dynamite explosions.
- `Vanilla` (no change)
- `Enabled`
- `Disabled`

#### `ItemLaunching` (enum, default: `Vanilla`)
Controls `AOE.canLaunchItems` for dynamite explosions.
- `Vanilla` (no change)
- `Enabled`
- `Disabled`

#### `ItemLaunchDistanceMultiplier` (float, default: `1`)
- Multiplies `AOE.itemKnockbackMultiplier` for dynamite explosions.
- `1` = vanilla.

---

### Section: `Item`

#### `UnlitDynamiteCanBePocketed` (bool, default: `false`)
- If `true`: unlit dynamite becomes pocketable.
- Once lit: it becomes unpocketable again (safety behavior).
- If `false`: vanilla behavior (dynamite is not pocketable).

---

## Example configs

### 1) Manual ignition: only light on throw, 3s fuse
```ini
[Fuse]
AutoFuseEnabled = false
StartFuseOnThrow = true
FuseLengthSeconds = 3
```

### 2) “Freeze dynamite” with bigger radius
```ini
[Explosion]
DamageType = Cold
RangeMultiplier = 1.5
```

### 3) Disable item launching (no loot scatter)
```ini
[Explosion]
ItemLaunching = Disabled
```

---

## Multiplayer notes

PEAK is networked; the host/authority often decides what “really happened”.
For best results, install the mod on **all players**, especially the host.

---

## Troubleshooting

- If you change the config and nothing happens:
  - confirm you edited the correct `.cfg` file under `BepInEx/config`,
  - restart the game after editing (BepInEx typically loads config on startup),
  - check the BepInEx console/log for “Plugin ConfigurableDynamite is loaded!”.

