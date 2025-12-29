# Configurable Scorpions (PEAK) — README

This mod lets you tweak **scorpion AI + attack behavior** via a BepInEx config file.

**Vanilla-first design:** every config entry defaults to the game's vanilla behavior.  
If you never touch the config, the mod should behave like **no mod is installed**.

---

## What this mod changes (when configured)

You can configure:

- **Targeting / aggression**
  - Disable chasing players (no player target acquisition)
  - Make scorpions aggressive **only when held**
  - Change **aggro range**
  - Toggle **line-of-sight** requirement
  - Allow / block targeting players that are already poisoned

- **Movement**
  - Movement speed
  - Turn rate
  - Sleep distance / wake radius (when scorpions go dormant / wake up)

- **Attack shape + tempo**
  - Attack start distance / attack distance / attack angle
  - Attack cooldown
  - Sting windup time
  - Special: **HeldStingTimeMultiplier** adjusts the *entire held sting sequence* (animation + damage timing)

- **Damage / status**
  - Choose the damage/status type (with an easy “Vanilla” option)
  - Enable/disable the **initial tick** and the **over-time tick** independently
  - Separate multipliers for initial vs over-time tick (+ a global multiplier)

---

## Installation

1. Build the plugin (or use a prebuilt DLL if you have one).
2. Drop the plugin DLL into:
   - `BepInEx/plugins/`
3. Run the game once to generate the config file.
4. Edit the config file and restart the game.

Config file path is typically:
- `BepInEx/config/com.github.GABRlEL.ConfigurableScorpions.cfg`

---

## Multiplayer: who needs the mod?

PEAK uses Photon ownership. Scorpion **AI decisions** run on the client that **owns the scorpion** (`photonView.IsMine`).
Also, scorpions are items: when someone **picks up** a scorpion, ownership is commonly transferred to the holder.

Damage/status is applied on the **victim’s local client** (`character.IsLocal`).

Practical rules of thumb:

- **Targeting/movement/attack-start behavior** is controlled by the **scorpion owner**.
- **Damage type + damage amounts** are applied by **each client to themselves** when they are stung.
- For consistent gameplay in co-op, it’s best if **everyone uses the same config**.

---

## Vanilla fidelity notes (important)

This mod avoids re-implementing vanilla logic unless a setting forces it.

### Targeting override
The mod calls the game’s original `Mob.Targeting()` when all of these are vanilla:
- `AggroDistanceMultiplier = 1`
- `RequireLineOfSight = true`
- `TargetPoisonedPlayers = false`
- and the other aggression toggles are at defaults

If only **AggroDistanceMultiplier** is changed, the mod temporarily scales `aggroDistance`, calls vanilla targeting, then restores it.

Only when you change **LoS** and/or **Target poisoned** does it run a custom targeting loop, because vanilla hardcodes those checks.

---

## Configuration reference

### [Targeting]

#### EnablePathfindingToPlayer (bool, default: `true`)
If `false`, scorpions won’t acquire a player target while on the ground (they will patrol instead).

#### OnlyAggressiveWhenHeld (bool, default: `false`)
If `true`, scorpions only target/attack while held as an item. On the ground they patrol.

#### AggroDistanceMultiplier (float, default: `1`)
Multiplies the scorpion’s aggro distance.

- `1` = vanilla
- `0` = effectively disables range-based acquisition

#### RequireLineOfSight (bool, default: `true`)
If `false`, scorpions can target through terrain (skips the terrain line check).

#### TargetPoisonedPlayers (bool, default: `false`)
Vanilla behavior avoids targeting players already afflicted with poison-over-time.  
Set to `true` to allow targeting poisoned players.

---

### [Movement]

#### MovementSpeedMultiplier (float, default: `1`)
Multiplies `Mob.movementSpeed`.

#### TurnRateMultiplier (float, default: `1`)
Multiplies `Mob.turnRate`.

#### SleepDistanceMultiplier (float, default: `1`)
Scales the distance check used for entering sleep.

#### WakeRadiusMultiplier (float, default: `1`)
Scales the distance check used for waking up.

---

### [Attack]

#### StingDelaySeconds (float, default: `-1`)
Override for the windup time until the sting happens.

- `-1` = keep vanilla
- `>= 0` = override

#### HeldStingTimeMultiplier (float, default: `1`)
Applies **only while the scorpion is held**.

This multiplier adjusts the **entire sting sequence** while held:
- the moment the attack animation starts
- the windup duration
- and when damage is applied

`1` keeps vanilla held behavior.

#### AttackStartDistance (float, default: `-1`)
Override `Mob.attackStartDistance`.

#### AttackDistance (float, default: `-1`)
Override `Mob.attackDistance`.

#### AttackAngle (float, default: `-1`)
Override `Mob.attackAngle`.

#### AttackCooldownSeconds (float, default: `-1`)
Override the internal attack cooldown.

- `-1` = keep vanilla
- `>= 0` = override

---

### [Attack] Damage & status

#### DamageType (enum, default: `Vanilla`)
Easy-to-type values (like the dynamite mod).  
Recommended: keep `Vanilla` unless you intentionally want different status effects.

Allowed values:
- `Vanilla` (uses the game’s default)
- `Injury`, `Hunger`, `Cold`, `Poison`, `Crab`, `Curse`, `Drowsy`, `Weight`, `Hot`, `Thorns`, `Spores`, `Web`

#### DamageMultiplier (float, default: `1`)
Global multiplier applied to both initial and over-time ticks.

#### EnableInitialDamageTick (bool, default: `true`)
If `false`, disables the single “instant” sting tick.

#### EnableOvertimeDamageTick (bool, default: `true`)
If `false`, disables the over-time tick.

#### InitialDamageMultiplier (float, default: `1`)
Extra multiplier for the initial tick.

Total initial scaling = `DamageMultiplier * InitialDamageMultiplier`.

#### OvertimeDamageMultiplier (float, default: `1`)
Extra multiplier for the over-time tick.

Total overtime scaling = `DamageMultiplier * OvertimeDamageMultiplier`.

---

## Troubleshooting

### “Nothing changes”
- Make sure you edited the correct config file in `BepInEx/config/`.
- Restart the game after editing the config.

### Multiplayer feels inconsistent
- Ensure all players are using the same config.
- Remember: AI runs on the scorpion owner; damage is applied locally on the victim.

---

## License / attribution
This project is a gameplay mod for PEAK. It patches runtime behavior via Harmony and does not redistribute game assets.
