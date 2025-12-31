# Configurable Scoutmaster (PEAK mod)

This mod makes the **Scoutmaster** configurable via a BepInEx config file: targeting, perception, movement, teleporting, throwing, visuals, and a few “rules” toggles.

Due to the Multiplayer nature of Scoutmaster, some features in this mod are **untested**.

This mod will only change gameplay, when configured.

## Requirements
- BepInEx (whatever variant you already use for PEAK mods)
- Harmony (bundled as a dependency in most mod templates)

## Install
1. Build / download the mod **DLL**.
2. Copy it into: `BepInEx/plugins/`
3. Launch the game once to generate the config file.

## Config file location
After first launch, BepInEx writes a config into `BepInEx/config/`.

Tip: open the config directory and search for **`[Scoutmaster.`** — this mod’s config is the one that contains sections like:
- `[Scoutmaster.Targeting]`
- `[Scoutmaster.TeleportClose]`
- …etc

## How the settings work
Most settings are **multipliers**:
- `1.0` = vanilla
- `0.5` = half of the vanilla value
- `2.0` = double the vanilla value

A few settings are:
- **absolute coordinates** (TeleportFarPositionX/Y/Z)
- an **enum override** (Throw → `DamageType`)

---

# Configuration reference

## Scoutmaster.Targeting
- **AttackHeightDeltaMultiplier** (default: `1.0`) — Multiplier for Scoutmaster attackHeightDelta (base game: 100). 1.0 = vanilla.
- **ChancePerCheckMultiplier** (default: `1.0`) — Multiplier for Scoutmaster chance per check (base game: 0.10). Clamped to [0,1]. 1.0 = vanilla.
- **MaxAggroHeightMultiplier** (default: `1.0`) — Multiplier for Scoutmaster maxAggroHeight (base game: 825). 1.0 = vanilla.
- **MinIsolationDistanceMultiplier** (default: `1.0`) — Multiplier for Scoutmaster minIsolationDistance (base game: 15). 1.0 = vanilla.
- **TargetIntervalMultiplier** (default: `1.0`) — Multiplier for Scoutmaster target interval (base game: 30 seconds). 1.0 = vanilla.

## Scoutmaster.Perception
- **AnyoneCanSeeAngleMultiplier** (default: `1.0`) — Multiplier for anyone-can-see angle (degrees) used in perception (base game: 80). 1.0 = vanilla.
- **CloseDistanceMultiplier** (default: `1.0`) — Multiplier for Scoutmaster close distance check (base game: 10). 1.0 = vanilla.
- **SeenCounterToEnableSprintMultiplier** (default: `1.0`) — Multiplier for seenCounter threshold enabling sprint logic (base game: 1.0). 1.0 = vanilla.
- **SeenGainRatesMultiplier** (default: `1.0`) — Multiplier applied to all seen gain/decay rates (base game rates: 1.0 / 0.3 / 0.15 gains, 0.1 decay). 1.0 = vanilla.
- **TargetLookAwayAngleMultiplier** (default: `1.0`) — Multiplier for target look-away angle (degrees) used in perception (base game: 70). 1.0 = vanilla.

## Scoutmaster.Movement
- **LoseTargetAfterTeleportChanceMultiplier** (default: `1.0`) — Multiplier for chance to lose target after teleport (base game: 0.10). Clamped to [0,1]. 1.0 = vanilla.
- **SprintIfDistanceMultiplier** (default: `1.0`) — Multiplier for distance threshold to sprint toward target (base game: 15). 1.0 = vanilla.
- **TeleportInsteadOfChaseDistanceMultiplier** (default: `1.0`) — Multiplier for distance at which Scoutmaster teleports instead of chasing (base game: 80). 1.0 = vanilla.
- **WalkTowardTargetDistanceMultiplier** (default: `1.0`) — Multiplier for distance threshold to walk toward target (base game: 5). 1.0 = vanilla.

## Scoutmaster.TeleportClose
- **TeleportCloseCooldownMultiplier** (default: `1.0`) — Multiplier for teleport-close cooldown gate (base game: 5 seconds). 1.0 = vanilla.
- **TeleportCloseMaxDistanceMultiplier** (default: `1.0`) — Multiplier for teleport-close MAX distance to target (base game: 70). 1.0 = vanilla.
- **TeleportCloseMinDistanceMultiplier** (default: `1.0`) — Multiplier for teleport-close MIN distance to target (base game: 50). 1.0 = vanilla.

## Scoutmaster.TeleportSampling
- **TeleportSamplingMaxDistanceMultiplier** (default: `1.0`) — Multiplier for generic teleport sampling maxDistanceToTarget (base game: 45). 1.0 = vanilla.
- **TeleportSamplingMaxHeightDifferenceMultiplier** (default: `1.0`) — Multiplier for generic teleport sampling maxHeightDifference (base game: 15). 1.0 = vanilla.
- **TeleportSamplingSamplesMultiplier** (default: `1.0`) — Multiplier for generic teleport sampling raycast samples (base game: 50). Rounded, clamped to >= 1. 1.0 = vanilla.

## Scoutmaster.TeleportFar
- **TeleportFarCooldownMultiplier** (default: `1.0`) — Multiplier for far teleport cooldown gate (base game: 5 seconds). 1.0 = vanilla.
- **TeleportFarPositionX** (default: `0.0`) — Absolute X position for far teleport (base game: 0).
- **TeleportFarPositionY** (default: `0.0`) — Absolute Y position for far teleport (base game: 0).
- **TeleportFarPositionZ** (default: `5000.0`) — Absolute Z position for far teleport (base game: 5000).
- **TeleportFarSeenTimeMultiplier** (default: `1.0`) — Multiplier for time being seen before triggering far teleport (base game: 0.5 seconds). 1.0 = vanilla.

## Scoutmaster.Throw
- **DamageType** (default: `Vanilla`) — Controls the status/damage type applied to the victim when Scoutmaster throws them. Vanilla keeps the game's original setting.
Cold = freeze, Hot = burn, Spores = shroom.
- **ThrowDirectionDownRayLengthMultiplier** (default: `1.0`) — Multiplier for down-ray length used by RotateToMostEvilThrowDirection (base game: 1000). 1.0 = vanilla.
- **ThrowDirectionSampleRadiusMultiplier** (default: `1.0`) — Multiplier for throw direction sample radius used by RotateToMostEvilThrowDirection (base game: 10). 1.0 = vanilla.
- **ThrowDirectionSamplesMultiplier** (default: `1.0`) — Multiplier for throw direction sample count used by RotateToMostEvilThrowDirection (base game: 10). 1.0 = vanilla.
- **ThrowDurationMultiplier** (default: `1.0`) — Multiplier for throw duration/param passed to grabbing.Throw (base game: 3). 1.0 = vanilla.
- **ThrowForceMultiplier** (default: `1.0`) — Multiplier for throw force (base game: 1500). 1.0 = vanilla.
- **ThrowGlobalShakeAmplitudeMultiplier** (default: `1.0`) — Multiplier for global perlin shake amplitude during throw windup (base game: 3). 1.0 = vanilla.
- **ThrowGlobalShakeDurationMultiplier** (default: `1.0`) — Multiplier for global perlin shake duration during throw windup (base game: 3). 1.0 = vanilla.
- **ThrowInjuryAmountMultiplier** (default: `1.0`) — Multiplier for injury amount applied on throw (base game: 0.25). 1.0 = vanilla.
- **ThrowLocalShakeAmplitudeMultiplier** (default: `1.0`) — Multiplier for local (victim) perlin shake amplitude during throw windup (base game: 15). 1.0 = vanilla.
- **ThrowLocalShakeDurationMultiplier** (default: `1.0`) — Multiplier for local (victim) perlin shake duration during throw windup (base game: 0.5). 1.0 = vanilla.
- **ThrowPostChillSecondsMultiplier** (default: `1.0`) — Multiplier for post-throw chill duration (base game: 2 seconds). 1.0 = vanilla.
- **ThrowUpwardBiasMultiplier** (default: `1.0`) — Multiplier for throw upward bias (base game: 0.3). 1.0 = vanilla.
- **ThrowWindupMultiplier** (default: `1.0`) — Multiplier for throw windup (base game: 3.2 seconds). 1.0 = vanilla.

### Throw → DamageType values
`DamageType` is an enum (default: `Vanilla`). Valid values:

- `Vanilla` (keep the game’s original setting)
- `Injury`, `Hunger`, `Cold`, `Poison`, `Crab`, `Curse`, `Drowsy`, `Weight`, `Hot`, `Thorns`, `Spores`, `Web`

## Scoutmaster.Visuals
- **GrainMultiplier** (default: `1.0`) — Multiplier applied to the grain strength when photosensitivity is off (base game: 1). 1.0 = vanilla.
- **StrengthDistanceFarMultiplier** (default: `1.0`) — Multiplier for the far distance used in Scoutmaster visual strength mapping (base game: 50). 1.0 = vanilla.
- **StrengthDistanceNearMultiplier** (default: `1.0`) — Multiplier for the near distance used in Scoutmaster visual strength mapping (base game: 5). 1.0 = vanilla.
- **StrengthLerpSpeedMultiplier** (default: `1.0`) — Multiplier for the visual strength lerp speed (base game: 0.5). 1.0 = vanilla.

## Scoutmaster.Rules
- **DisableStatusImmunity** (default: `false`) — When true, the Scoutmaster is no longer immune to status effects (vanilla: immune). Default false preserves vanilla.
- **ScoutmasterInjuryDamageMultiplier** (default: `0.0`) — Multiplier applied to Injury status amounts the Scoutmaster receives when DisableStatusImmunity is true. Default 0 prevents spawn fall-damage from making the Scoutmaster go limp. 1.0 = vanilla injury amounts.
- **ScoutmasterOtherStatusDamageMultiplier** (default: `1.0`) — Multiplier applied to ALL non-Injury status amounts the Scoutmaster receives when DisableStatusImmunity is true. Default 1.0 keeps vanilla status amounts (when immunity is disabled).

---

## Example config (defaults)
```ini
[Scoutmaster.Targeting]
AttackHeightDeltaMultiplier = 1.0
ChancePerCheckMultiplier = 1.0
MaxAggroHeightMultiplier = 1.0
MinIsolationDistanceMultiplier = 1.0
TargetIntervalMultiplier = 1.0

[Scoutmaster.Movement]
LoseTargetAfterTeleportChanceMultiplier = 1.0
SprintIfDistanceMultiplier = 1.0
TeleportInsteadOfChaseDistanceMultiplier = 1.0
WalkTowardTargetDistanceMultiplier = 1.0

[Scoutmaster.TeleportClose]
TeleportCloseCooldownMultiplier = 1.0
TeleportCloseMaxDistanceMultiplier = 1.0
TeleportCloseMinDistanceMultiplier = 1.0

[Scoutmaster.Throw]
DamageType = Vanilla
ThrowDirectionDownRayLengthMultiplier = 1.0
ThrowDirectionSampleRadiusMultiplier = 1.0
ThrowDirectionSamplesMultiplier = 1.0
ThrowDurationMultiplier = 1.0
ThrowForceMultiplier = 1.0
ThrowGlobalShakeAmplitudeMultiplier = 1.0
ThrowGlobalShakeDurationMultiplier = 1.0
ThrowInjuryAmountMultiplier = 1.0
ThrowLocalShakeAmplitudeMultiplier = 1.0
ThrowLocalShakeDurationMultiplier = 1.0
ThrowPostChillSecondsMultiplier = 1.0
ThrowUpwardBiasMultiplier = 1.0
ThrowWindupMultiplier = 1.0

[Scoutmaster.Rules]
DisableStatusImmunity = false
ScoutmasterInjuryDamageMultiplier = 0.0
ScoutmasterOtherStatusDamageMultiplier = 1.0
```

## Notes
- This mod patches constants with Harmony transpilers. If PEAK updates and the Scoutmaster implementation changes, some settings may stop applying until the mod is updated.
- If you’re tweaking aggressively, change a few values at a time so you can tell what actually caused an effect.
