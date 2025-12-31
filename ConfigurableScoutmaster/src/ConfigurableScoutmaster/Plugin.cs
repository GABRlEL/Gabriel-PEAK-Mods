using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace ConfigurableScoutmaster;

// Default multipliers are 1.0 => vanilla behavior unchanged.

[BepInAutoPlugin]
public partial class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; } = null!;

    private const string LogTag = "ConfigurableScoutmaster";
// User-friendly status override (mirrors Dynamite mod style)
    internal enum DamageTypeOverride
    {
        Vanilla = -1,
        Injury = 0,
        Hunger = 1,
        Cold = 2,
        Poison = 3,
        Crab = 4,
        Curse = 5,
        Drowsy = 6,
        Weight = 7,
        Hot = 8,
        Thorns = 9,
        Spores = 10,
        Web = 11,
    }

private Harmony? _harmony;

    // Multipliers (defaults = 1 => no behavioral changes)
    internal static ConfigEntry<float> TargetIntervalMult { get; private set; } = null!;
    internal static ConfigEntry<float> ChancePerCheckMult { get; private set; } = null!;
    internal static ConfigEntry<float> AttackHeightDeltaMult { get; private set; } = null!;
    internal static ConfigEntry<float> MaxAggroHeightMult { get; private set; } = null!;
    internal static ConfigEntry<float> MinIsolationDistanceMult { get; private set; } = null!;
    // Perception multipliers (defaults = 1 => no behavioral changes)
    internal static ConfigEntry<float> CloseDistanceMult { get; private set; } = null!;
    internal static ConfigEntry<float> TargetLookAwayAngleMult { get; private set; } = null!;
    internal static ConfigEntry<float> AnyoneCanSeeAngleMult { get; private set; } = null!;
    internal static ConfigEntry<float> SeenGainRatesMult { get; private set; } = null!;
    internal static ConfigEntry<float> SeenCounterToEnableSprintMult { get; private set; } = null!;

// Movement/Chase multipliers (defaults = 1 => no behavioral changes)
internal static ConfigEntry<float> TeleportInsteadOfChaseDistanceMult { get; private set; } = null!;
internal static ConfigEntry<float> WalkTowardTargetDistanceMult { get; private set; } = null!;
internal static ConfigEntry<float> SprintIfDistanceMult { get; private set; } = null!;
internal static ConfigEntry<float> LoseTargetAfterTeleportChanceMult { get; private set; } = null!;
    // Teleportation multipliers (defaults = 1 => no behavioral changes)
    internal static ConfigEntry<float> TeleportCloseCooldownMult { get; private set; } = null!;
    internal static ConfigEntry<float> TeleportCloseMinDistanceMult { get; private set; } = null!;
    internal static ConfigEntry<float> TeleportCloseMaxDistanceMult { get; private set; } = null!;

    internal static ConfigEntry<float> TeleportSamplingMaxDistanceMult { get; private set; } = null!;
    internal static ConfigEntry<float> TeleportSamplingMaxHeightDifferenceMult { get; private set; } = null!;
    internal static ConfigEntry<float> TeleportSamplingSamplesMult { get; private set; } = null!;

    internal static ConfigEntry<float> TeleportFarSeenTimeMult { get; private set; } = null!;
    internal static ConfigEntry<float> TeleportFarCooldownMult { get; private set; } = null!;
    internal static ConfigEntry<float> TeleportFarPositionX { get; private set; } = null!;
    internal static ConfigEntry<float> TeleportFarPositionY { get; private set; } = null!;
    internal static ConfigEntry<float> TeleportFarPositionZ { get; private set; } = null!;


    // Throw behavior multipliers (defaults = 1 => no behavioral changes)
    internal static ConfigEntry<float> ThrowWindupMult { get; private set; } = null!;
    internal static ConfigEntry<float> ThrowLocalShakeAmplitudeMult { get; private set; } = null!;
    internal static ConfigEntry<float> ThrowLocalShakeDurationMult { get; private set; } = null!;
    internal static ConfigEntry<float> ThrowGlobalShakeAmplitudeMult { get; private set; } = null!;
    internal static ConfigEntry<float> ThrowGlobalShakeDurationMult { get; private set; } = null!;
    internal static ConfigEntry<float> ThrowUpwardBiasMult { get; private set; } = null!;
    internal static ConfigEntry<float> ThrowForceMult { get; private set; } = null!;
    internal static ConfigEntry<float> ThrowDurationMult { get; private set; } = null!;
    internal static ConfigEntry<float> ThrowInjuryAmountMult { get; private set; } = null!;
    internal static ConfigEntry<DamageTypeOverride> ThrowDamageType { get; private set; } = null!;
    internal static ConfigEntry<float> ThrowPostChillSecondsMult { get; private set; } = null!;
    internal static ConfigEntry<float> ThrowDirectionSamplesMult { get; private set; } = null!;
    internal static ConfigEntry<float> ThrowDirectionSampleRadiusMult { get; private set; } = null!;
    internal static ConfigEntry<float> ThrowDirectionDownRayLengthMult { get; private set; } = null!;

    // Visual/UX multipliers (defaults = 1 => no behavioral changes)
    internal static ConfigEntry<float> VisualStrengthFarDistanceMult { get; private set; } = null!;
    internal static ConfigEntry<float> VisualStrengthNearDistanceMult { get; private set; } = null!;
    internal static ConfigEntry<float> VisualStrengthLerpSpeedMult { get; private set; } = null!;
    internal static ConfigEntry<float> VisualGrainMultiplierMult { get; private set; } = null!;

    // Rule toggles
    internal static ConfigEntry<bool> DisableScoutmasterStatusImmunity { get; private set; } = null!;
    internal static ConfigEntry<float> ScoutmasterInjuryDamageMultiplier { get; private set; } = null!;
    internal static ConfigEntry<float> ScoutmasterOtherStatusDamageMultiplier { get; private set; } = null!;






    // Cache original (unmodified) Scoutmaster field values per instance to avoid double-scaling.
    private static readonly ConditionalWeakTable<object, ScoutmasterOriginals> ScoutmasterOriginalsByInstance = new();

    private sealed class ScoutmasterOriginals
    {
        public float AttackHeightDelta;
        public float MaxAggroHeight;
    }

    private void Awake()
    {
        Log = Logger;

        // Config: multiplier-based, so players can tweak values they already know.
        // Defaults are 1.0 (no change).
        var section = "Scoutmaster.Targeting";
        TargetIntervalMult = Config.Bind(section, "TargetIntervalMultiplier", 1.0f,
            "Multiplier for Scoutmaster target interval (base game: 30 seconds). 1.0 = vanilla.");
        ChancePerCheckMult = Config.Bind(section, "ChancePerCheckMultiplier", 1.0f,
            "Multiplier for Scoutmaster chance per check (base game: 0.10). Clamped to [0,1]. 1.0 = vanilla.");
        AttackHeightDeltaMult = Config.Bind(section, "AttackHeightDeltaMultiplier", 1.0f,
            "Multiplier for Scoutmaster attackHeightDelta (base game: 100). 1.0 = vanilla.");
        MaxAggroHeightMult = Config.Bind(section, "MaxAggroHeightMultiplier", 1.0f,
            "Multiplier for Scoutmaster maxAggroHeight (base game: 825). 1.0 = vanilla.");
        MinIsolationDistanceMult = Config.Bind(section, "MinIsolationDistanceMultiplier", 1.0f,
            "Multiplier for Scoutmaster minIsolationDistance (base game: 15). 1.0 = vanilla.");

        var sectionPerception = "Scoutmaster.Perception";
        CloseDistanceMult = Config.Bind(sectionPerception, "CloseDistanceMultiplier", 1.0f,
            "Multiplier for Scoutmaster close distance check (base game: 10). 1.0 = vanilla.");
        TargetLookAwayAngleMult = Config.Bind(sectionPerception, "TargetLookAwayAngleMultiplier", 1.0f,
            "Multiplier for target look-away angle (degrees) used in perception (base game: 70). 1.0 = vanilla.");
        AnyoneCanSeeAngleMult = Config.Bind(sectionPerception, "AnyoneCanSeeAngleMultiplier", 1.0f,
            "Multiplier for anyone-can-see angle (degrees) used in perception (base game: 80). 1.0 = vanilla.");
        SeenGainRatesMult = Config.Bind(sectionPerception, "SeenGainRatesMultiplier", 1.0f,
            "Multiplier applied to all seen gain/decay rates (base game rates: 1.0 / 0.3 / 0.15 gains, 0.1 decay). 1.0 = vanilla.");
        SeenCounterToEnableSprintMult = Config.Bind(sectionPerception, "SeenCounterToEnableSprintMultiplier", 1.0f,
            "Multiplier for seenCounter threshold enabling sprint logic (base game: 1.0). 1.0 = vanilla.");


var sectionMovement = "Scoutmaster.Movement";
TeleportInsteadOfChaseDistanceMult = Config.Bind(sectionMovement, "TeleportInsteadOfChaseDistanceMultiplier", 1.0f,
    "Multiplier for distance at which Scoutmaster teleports instead of chasing (base game: 80). 1.0 = vanilla.");
WalkTowardTargetDistanceMult = Config.Bind(sectionMovement, "WalkTowardTargetDistanceMultiplier", 1.0f,
    "Multiplier for distance threshold to walk toward target (base game: 5). 1.0 = vanilla.");
SprintIfDistanceMult = Config.Bind(sectionMovement, "SprintIfDistanceMultiplier", 1.0f,
    "Multiplier for distance threshold to sprint toward target (base game: 15). 1.0 = vanilla.");
LoseTargetAfterTeleportChanceMult = Config.Bind(sectionMovement, "LoseTargetAfterTeleportChanceMultiplier", 1.0f,
    "Multiplier for chance to lose target after teleport (base game: 0.10). Clamped to [0,1]. 1.0 = vanilla.");

        // Teleportation tuning. Multipliers default to 1.0 => vanilla behavior.
        var sectionTpClose = "Scoutmaster.TeleportClose";
        TeleportCloseCooldownMult = Config.Bind(sectionTpClose, "TeleportCloseCooldownMultiplier", 1.0f,
            "Multiplier for teleport-close cooldown gate (base game: 5 seconds). 1.0 = vanilla.");
        TeleportCloseMinDistanceMult = Config.Bind(sectionTpClose, "TeleportCloseMinDistanceMultiplier", 1.0f,
            "Multiplier for teleport-close MIN distance to target (base game: 50). 1.0 = vanilla.");
        TeleportCloseMaxDistanceMult = Config.Bind(sectionTpClose, "TeleportCloseMaxDistanceMultiplier", 1.0f,
            "Multiplier for teleport-close MAX distance to target (base game: 70). 1.0 = vanilla.");

        var sectionTpSampling = "Scoutmaster.TeleportSampling";
        TeleportSamplingMaxDistanceMult = Config.Bind(sectionTpSampling, "TeleportSamplingMaxDistanceMultiplier", 1.0f,
            "Multiplier for generic teleport sampling maxDistanceToTarget (base game: 45). 1.0 = vanilla.");
        TeleportSamplingMaxHeightDifferenceMult = Config.Bind(sectionTpSampling, "TeleportSamplingMaxHeightDifferenceMultiplier", 1.0f,
            "Multiplier for generic teleport sampling maxHeightDifference (base game: 15). 1.0 = vanilla.");
        TeleportSamplingSamplesMult = Config.Bind(sectionTpSampling, "TeleportSamplingSamplesMultiplier", 1.0f,
            "Multiplier for generic teleport sampling raycast samples (base game: 50). Rounded, clamped to >= 1. 1.0 = vanilla.");

        var sectionTpFar = "Scoutmaster.TeleportFar";
        TeleportFarSeenTimeMult = Config.Bind(sectionTpFar, "TeleportFarSeenTimeMultiplier", 1.0f,
            "Multiplier for time being seen before triggering far teleport (base game: 0.5 seconds). 1.0 = vanilla.");
        TeleportFarCooldownMult = Config.Bind(sectionTpFar, "TeleportFarCooldownMultiplier", 1.0f,
            "Multiplier for far teleport cooldown gate (base game: 5 seconds). 1.0 = vanilla.");
        TeleportFarPositionX = Config.Bind(sectionTpFar, "TeleportFarPositionX", 0.0f,
            "Absolute X position for far teleport (base game: 0).");
        TeleportFarPositionY = Config.Bind(sectionTpFar, "TeleportFarPositionY", 0.0f,
            "Absolute Y position for far teleport (base game: 0).");
        TeleportFarPositionZ = Config.Bind(sectionTpFar, "TeleportFarPositionZ", 5000.0f,
            "Absolute Z position for far teleport (base game: 5000).");






        var sectionThrow = "Scoutmaster.Throw";
        ThrowWindupMult = Config.Bind(sectionThrow, "ThrowWindupMultiplier", 1.0f,
            "Multiplier for throw windup (base game: 3.2 seconds). 1.0 = vanilla.");
        ThrowLocalShakeAmplitudeMult = Config.Bind(sectionThrow, "ThrowLocalShakeAmplitudeMultiplier", 1.0f,
            "Multiplier for local (victim) perlin shake amplitude during throw windup (base game: 15). 1.0 = vanilla.");
        ThrowLocalShakeDurationMult = Config.Bind(sectionThrow, "ThrowLocalShakeDurationMultiplier", 1.0f,
            "Multiplier for local (victim) perlin shake duration during throw windup (base game: 0.5). 1.0 = vanilla.");
        ThrowGlobalShakeAmplitudeMult = Config.Bind(sectionThrow, "ThrowGlobalShakeAmplitudeMultiplier", 1.0f,
            "Multiplier for global perlin shake amplitude during throw windup (base game: 3). 1.0 = vanilla.");
        ThrowGlobalShakeDurationMult = Config.Bind(sectionThrow, "ThrowGlobalShakeDurationMultiplier", 1.0f,
            "Multiplier for global perlin shake duration during throw windup (base game: 3). 1.0 = vanilla.");
        ThrowUpwardBiasMult = Config.Bind(sectionThrow, "ThrowUpwardBiasMultiplier", 1.0f,
            "Multiplier for throw upward bias (base game: 0.3). 1.0 = vanilla.");
        ThrowForceMult = Config.Bind(sectionThrow, "ThrowForceMultiplier", 1.0f,
            "Multiplier for throw force (base game: 1500). 1.0 = vanilla.");
        ThrowDurationMult = Config.Bind(sectionThrow, "ThrowDurationMultiplier", 1.0f,
            "Multiplier for throw duration/param passed to grabbing.Throw (base game: 3). 1.0 = vanilla.");
        ThrowInjuryAmountMult = Config.Bind(sectionThrow, "ThrowInjuryAmountMultiplier", 1.0f,
            "Multiplier for injury amount applied on throw (base game: 0.25). 1.0 = vanilla.");
        ThrowDamageType = Config.Bind(sectionThrow, "DamageType", DamageTypeOverride.Vanilla,
            "Controls the status/damage type applied to the victim when Scoutmaster throws them. Vanilla keeps the game's original setting.\nCold = freeze, Hot = burn, Spores = shroom.");
ThrowPostChillSecondsMult = Config.Bind(sectionThrow, "ThrowPostChillSecondsMultiplier", 1.0f,
            "Multiplier for post-throw chill duration (base game: 2 seconds). 1.0 = vanilla.");
        ThrowDirectionSamplesMult = Config.Bind(sectionThrow, "ThrowDirectionSamplesMultiplier", 1.0f,
            "Multiplier for throw direction sample count used by RotateToMostEvilThrowDirection (base game: 10). 1.0 = vanilla.");
        ThrowDirectionSampleRadiusMult = Config.Bind(sectionThrow, "ThrowDirectionSampleRadiusMultiplier", 1.0f,
            "Multiplier for throw direction sample radius used by RotateToMostEvilThrowDirection (base game: 10). 1.0 = vanilla.");
        ThrowDirectionDownRayLengthMult = Config.Bind(sectionThrow, "ThrowDirectionDownRayLengthMultiplier", 1.0f,
            "Multiplier for down-ray length used by RotateToMostEvilThrowDirection (base game: 1000). 1.0 = vanilla.");


var sectionVisuals = "Scoutmaster.Visuals";
VisualStrengthFarDistanceMult = Config.Bind(sectionVisuals, "StrengthDistanceFarMultiplier", 1.0f,
    "Multiplier for the far distance used in Scoutmaster visual strength mapping (base game: 50). 1.0 = vanilla.");
VisualStrengthNearDistanceMult = Config.Bind(sectionVisuals, "StrengthDistanceNearMultiplier", 1.0f,
    "Multiplier for the near distance used in Scoutmaster visual strength mapping (base game: 5). 1.0 = vanilla.");
VisualStrengthLerpSpeedMult = Config.Bind(sectionVisuals, "StrengthLerpSpeedMultiplier", 1.0f,
    "Multiplier for the visual strength lerp speed (base game: 0.5). 1.0 = vanilla.");
VisualGrainMultiplierMult = Config.Bind(sectionVisuals, "GrainMultiplier", 1.0f,
    "Multiplier applied to the grain strength when photosensitivity is off (base game: 1). 1.0 = vanilla.");

        var sectionRules = "Scoutmaster.Rules";
        DisableScoutmasterStatusImmunity = Config.Bind(sectionRules, "DisableStatusImmunity", false,
            "When true, the Scoutmaster is no longer immune to status effects (vanilla: immune). Default false preserves vanilla.");
        ScoutmasterInjuryDamageMultiplier = Config.Bind(sectionRules, "ScoutmasterInjuryDamageMultiplier", 0.0f,
            "Multiplier applied to Injury status amounts the Scoutmaster receives when DisableStatusImmunity is true. Default 0 prevents spawn fall-damage from making the Scoutmaster go limp. 1.0 = vanilla injury amounts.");


        
        ScoutmasterOtherStatusDamageMultiplier = Config.Bind(sectionRules, "ScoutmasterOtherStatusDamageMultiplier", 1.0f,
            "Multiplier applied to ALL non-Injury status amounts the Scoutmaster receives when DisableStatusImmunity is true. Default 1.0 keeps vanilla status amounts (when immunity is disabled).");

        try
        {
            _harmony = new Harmony(Info.Metadata.GUID);
_harmony.PatchAll();
            Log.LogInfo($"Plugin {Info.Metadata.Name} ({Info.Metadata.Version}) is loaded and patched.");
        }
        catch (Exception e)
        {
            Log.LogError($"Failed to apply Harmony patches ({Info.Metadata.Version}): {e}");
        }
    }

    // --- Helpers used by transpilers/postfixes ---

    internal static bool ShouldBlockScoutmasterStatus(bool isScoutmaster)
    {
        if (!isScoutmaster) return false;
        // Default (false) => preserve vanilla immunity (block status). When enabled, Scoutmaster can receive status.
        bool disableImmunity = DisableScoutmasterStatusImmunity?.Value ?? false;
        return !disableImmunity;
    }


    internal static float GetTargetIntervalSeconds()
    {
        // Base is 30 seconds.
        float mult = TargetIntervalMult?.Value ?? 1.0f;
        // Avoid <= 0 intervals which can destabilize coroutines/logic.
        return Mathf.Max(0.01f, 30.0f * mult);
    }

    internal static float GetChancePerCheck()
    {
        // Base is 0.10
        float mult = ChancePerCheckMult?.Value ?? 1.0f;
        return Mathf.Clamp01(0.10f * mult);
    }

    internal static float GetMinIsolationDistance()
    {
        // Base is 15
        float mult = MinIsolationDistanceMult?.Value ?? 1.0f;
        return Mathf.Max(0.0f, 15.0f * mult);
    }

    internal static float GetAttackHeightDeltaMultiplier() => AttackHeightDeltaMult?.Value ?? 1.0f;
    internal static float GetMaxAggroHeightMultiplier() => MaxAggroHeightMult?.Value ?? 1.0f;

    internal static float GetCloseDistance()
    {
        // Base is 10
        float mult = CloseDistanceMult?.Value ?? 1.0f;
        return Mathf.Max(0.0f, 10.0f * mult);
    }

    internal static float GetTargetLookAwayAngle()
    {
        // Base is 70 degrees
        float mult = TargetLookAwayAngleMult?.Value ?? 1.0f;
        return Mathf.Clamp(70.0f * mult, 0.0f, 180.0f);
    }

    internal static float GetAnyoneCanSeeAngle()
    {
        // Base is 80 degrees
        float mult = AnyoneCanSeeAngleMult?.Value ?? 1.0f;
        return Mathf.Clamp(80.0f * mult, 0.0f, 180.0f);
    }

    internal static float GetSeenGainRate_1()  // base gain 1.0
    {
        float mult = SeenGainRatesMult?.Value ?? 1.0f;
        return Mathf.Max(0.0f, 1.0f * mult);
    }

    internal static float GetSeenGainRate_03() // base gain 0.3
    {
        float mult = SeenGainRatesMult?.Value ?? 1.0f;
        return Mathf.Max(0.0f, 0.3f * mult);
    }

    internal static float GetSeenGainRate_015() // base gain 0.15
    {
        float mult = SeenGainRatesMult?.Value ?? 1.0f;
        return Mathf.Max(0.0f, 0.15f * mult);
    }

    internal static float GetSeenDecayRate_01() // base decay 0.1
    {
        float mult = SeenGainRatesMult?.Value ?? 1.0f;
        return Mathf.Max(0.0f, 0.1f * mult);
    }

    internal static float GetSeenCounterToEnableSprint()
    {
        // Base threshold is 1.0
        float mult = SeenCounterToEnableSprintMult?.Value ?? 1.0f;
        return Mathf.Max(0.0f, 1.0f * mult);
    }


internal static float GetTeleportInsteadOfChaseDistance()
{
    // Base is 80
    float mult = TeleportInsteadOfChaseDistanceMult?.Value ?? 1.0f;
    return Mathf.Max(0.0f, 80.0f * mult);
}

internal static float GetWalkTowardTargetDistance()
{
    // Base is 5
    float mult = WalkTowardTargetDistanceMult?.Value ?? 1.0f;
    return Mathf.Max(0.0f, 5.0f * mult);
}

internal static float GetSprintIfDistance()
{
    // Base is 15
    float mult = SprintIfDistanceMult?.Value ?? 1.0f;
    return Mathf.Max(0.0f, 15.0f * mult);
}

internal static float GetLoseTargetAfterTeleportChance()
{
    // Base is 0.10
    float mult = LoseTargetAfterTeleportChanceMult?.Value ?? 1.0f;
    return Mathf.Clamp01(0.10f * mult);
}

    internal static float GetTeleportCloseCooldownSeconds()
    {
        float mult = TeleportCloseCooldownMult?.Value ?? 1.0f;
        return Mathf.Max(0.01f, 5.0f * mult);
    }

    internal static float GetTeleportFarCooldownSeconds()
    {
        float mult = TeleportFarCooldownMult?.Value ?? 1.0f;
        return Mathf.Max(0.01f, 5.0f * mult);
    }

    internal static float GetTeleportFarSeenTimeSeconds()
    {
        float mult = TeleportFarSeenTimeMult?.Value ?? 1.0f;
        return Mathf.Max(0.0f, 0.5f * mult);
    }

    internal static Vector3 GetTeleportFarPosition()
    {
        float x = TeleportFarPositionX?.Value ?? 0.0f;
        float y = TeleportFarPositionY?.Value ?? 0.0f;
        float z = TeleportFarPositionZ?.Value ?? 5000.0f;
        return new Vector3(x, y, z);
    }

    internal static int GetTeleportSamplingSamples()
    {
        float mult = TeleportSamplingSamplesMult?.Value ?? 1.0f;
        int samples = Mathf.RoundToInt(50.0f * mult);
        return Mathf.Clamp(samples, 1, 512);
    }




    
    // --- Throw helpers ---

    internal static float GetThrowWindupSeconds()
    {
        float mult = ThrowWindupMult?.Value ?? 1.0f;
        return Mathf.Max(0.0f, 3.2f * mult);
    }

    internal static float GetThrowLocalShakeAmplitude()
    {
        float mult = ThrowLocalShakeAmplitudeMult?.Value ?? 1.0f;
        return Mathf.Max(0.0f, 15.0f * mult);
    }

    internal static float GetThrowLocalShakeDuration()
    {
        float mult = ThrowLocalShakeDurationMult?.Value ?? 1.0f;
        return Mathf.Max(0.0f, 0.5f * mult);
    }

    internal static float GetThrowGlobalShakeAmplitude()
    {
        float mult = ThrowGlobalShakeAmplitudeMult?.Value ?? 1.0f;
        return Mathf.Max(0.0f, 3.0f * mult);
    }

    internal static float GetThrowGlobalShakeDuration()
    {
        float mult = ThrowGlobalShakeDurationMult?.Value ?? 1.0f;
        return Mathf.Max(0.0f, 3.0f * mult);
    }

    internal static float GetThrowUpwardBias()
    {
        float mult = ThrowUpwardBiasMult?.Value ?? 1.0f;
        return 0.3f * mult;
    }

    internal static float GetThrowForce()
    {
        float mult = ThrowForceMult?.Value ?? 1.0f;
        return Mathf.Max(0.0f, 1500.0f * mult);
    }

    internal static float GetThrowDuration()
    {
        float mult = ThrowDurationMult?.Value ?? 1.0f;
        return Mathf.Max(0.0f, 3.0f * mult);
    }

    internal static float GetThrowInjuryAmount()
    {
        float mult = ThrowInjuryAmountMult?.Value ?? 1.0f;
        return Mathf.Max(0.0f, 0.25f * mult);
    }


    
    internal static int GetThrowStatusTypeValue()
    {
        // Default: vanilla behaviour (Injury). When set, applies the chosen status type.
        DamageTypeOverride val = ThrowDamageType?.Value ?? DamageTypeOverride.Vanilla;
        if (val == DamageTypeOverride.Vanilla)
        {
            return 0; // Injury
        }

        return (int)val;
    }


    internal static float GetThrowPostChillSeconds()
    {
        float mult = ThrowPostChillSecondsMult?.Value ?? 1.0f;
        return Mathf.Max(0.0f, 2.0f * mult);
    }

    internal static int GetThrowDirectionSamples()
    {
        float mult = ThrowDirectionSamplesMult?.Value ?? 1.0f;
        int v = Mathf.RoundToInt(10.0f * mult);
        return Mathf.Clamp(v, 1, 512);
    }

    internal static float GetThrowDirectionSampleRadius()
    {
        float mult = ThrowDirectionSampleRadiusMult?.Value ?? 1.0f;
        return Mathf.Max(0.0f, 10.0f * mult);
    }

    internal static float GetThrowDirectionDownRayLength()
    {
        float mult = ThrowDirectionDownRayLengthMult?.Value ?? 1.0f;
        return Mathf.Max(0.0f, 1000.0f * mult);
    }


// --- Visual UX helpers ---

internal static float GetVisualStrengthFarDistance()
{
    float mult = VisualStrengthFarDistanceMult?.Value ?? 1.0f;
    return Mathf.Max(0.0f, 50.0f * mult);
}

internal static float GetVisualStrengthNearDistance()
{
    float mult = VisualStrengthNearDistanceMult?.Value ?? 1.0f;
    return Mathf.Max(0.0f, 5.0f * mult);
}

internal static float GetVisualStrengthLerpSpeed()
{
    float mult = VisualStrengthLerpSpeedMult?.Value ?? 1.0f;
    return Mathf.Max(0.0f, 0.5f * mult);
}

internal static float ApplyGrainMultiplier(float baseGrain)
{
    float mult = VisualGrainMultiplierMult?.Value ?? 1.0f;
    return baseGrain * Mathf.Max(0.0f, mult);
}


    // --- Harmony patches (reflection-based to avoid hard namespace assumptions) ---

    [HarmonyPatch]
    private static class Patch_Scoutmaster_LookForTarget
    {
        private static MethodBase? TargetMethod()
        {
            var t = AccessTools.TypeByName("Scoutmaster");
            return t == null ? null : AccessTools.Method(t, "LookForTarget");
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);

            var mInterval = AccessTools.Method(typeof(Plugin), nameof(GetTargetIntervalSeconds));
            var mChance = AccessTools.Method(typeof(Plugin), nameof(GetChancePerCheck));

            bool replacedInterval = false;
            bool replacedChance = false;

            for (int i = 0; i < codes.Count; i++)
            {
                var ci = codes[i];

                // Replace literal 30f with our computed interval.
                if (!replacedInterval && ci.opcode == OpCodes.Ldc_R4 && ci.operand is float f30 && Approximately(f30, 30f))
                {
                    codes[i] = new CodeInstruction(OpCodes.Call, mInterval);
                    replacedInterval = true;
                    continue;
                }

                // Replace literal 0.1f with our computed chance.
                if (!replacedChance && ci.opcode == OpCodes.Ldc_R4 && ci.operand is float f01 && Approximately(f01, 0.1f))
                {
                    codes[i] = new CodeInstruction(OpCodes.Call, mChance);
                    replacedChance = true;
                    continue;
                }

                // Handle potential int literal for 30 followed by conv.r4
                if (!replacedInterval && TryGetInt32Constant(ci, out int i30) && i30 == 30)
                {
                    if (i + 1 < codes.Count && codes[i + 1].opcode == OpCodes.Conv_R4)
                    {
                        codes[i] = new CodeInstruction(OpCodes.Call, mInterval);
                        codes[i + 1] = new CodeInstruction(OpCodes.Nop);
                        replacedInterval = true;
                    }
                }
            }

            if (!replacedInterval)
                Log?.LogWarning($"[{LogTag}] LookForTarget: did not find 30f constant to replace (target interval).");
            if (!replacedChance)
                Log?.LogWarning($"[{LogTag}] LookForTarget: did not find 0.1f constant to replace (chance per check).");

            return codes;
        }
    }

    [HarmonyPatch]
    private static class Patch_Scoutmaster_VerifyTarget
    {
        private static MethodBase? TargetMethod()
        {
            var t = AccessTools.TypeByName("Scoutmaster");
            return t == null ? null : AccessTools.Method(t, "VerifyTarget");
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            var mIso = AccessTools.Method(typeof(Plugin), nameof(GetMinIsolationDistance));

            bool replaced = false;

            // Replace any literal 15f used as the isolation distance threshold.
            for (int i = 0; i < codes.Count; i++)
            {
                var ci = codes[i];

                if (ci.opcode == OpCodes.Ldc_R4 && ci.operand is float f15 && Approximately(f15, 15f))
                {
                    codes[i] = new CodeInstruction(OpCodes.Call, mIso);
                    replaced = true;
                }
                else if (TryGetInt32Constant(ci, out int i15) && i15 == 15)
                {
                    if (i + 1 < codes.Count && codes[i + 1].opcode == OpCodes.Conv_R4)
                    {
                        codes[i] = new CodeInstruction(OpCodes.Call, mIso);
                        codes[i + 1] = new CodeInstruction(OpCodes.Nop);
                        replaced = true;
                    }
                }
            }

            if (!replaced)
                Log?.LogWarning($"[{LogTag}] VerifyTarget: did not find 15f constant to replace (min isolation distance).");

            return codes;
        }
    }

    
    [HarmonyPatch]
    private static class Patch_Scoutmaster_CalcVars
    {
        private static MethodBase? TargetMethod()
        {
            var t = AccessTools.TypeByName("Scoutmaster");
            return t == null ? null : AccessTools.Method(t, "CalcVars");
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);

            var mCloseDist = AccessTools.Method(typeof(Plugin), nameof(GetCloseDistance));
            var mLookAway = AccessTools.Method(typeof(Plugin), nameof(GetTargetLookAwayAngle));
            var mGain1 = AccessTools.Method(typeof(Plugin), nameof(GetSeenGainRate_1));
            var mGain03 = AccessTools.Method(typeof(Plugin), nameof(GetSeenGainRate_03));
            var mGain015 = AccessTools.Method(typeof(Plugin), nameof(GetSeenGainRate_015));
            var mDecay01 = AccessTools.Method(typeof(Plugin), nameof(GetSeenDecayRate_01));

            bool replacedClose = false;
            bool replacedLookAway = false;
            bool replacedGain1 = false;
            bool replacedGain03 = false;
            bool replacedGain015 = false;
            bool replacedDecay01 = false;

            bool StoresSeenCounterSoon(int idx)
            {
                // Look ahead for a stfld targetHasSeenMeCounter so we only touch the "seen" math.
                const int window = 16;
                for (int j = idx; j < Mathf.Min(codes.Count, idx + window); j++)
                {
                    if (codes[j].opcode == OpCodes.Stfld && codes[j].operand is FieldInfo fi &&
                        fi.Name.IndexOf("targetHasSeenMeCounter", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
                return false;
            }

            for (int i = 0; i < codes.Count; i++)
            {
                var ci = codes[i];

                // closeDistance: replace literal 10f in the distance check.
                if (!replacedClose && ci.opcode == OpCodes.Ldc_R4 && ci.operand is float f10 && Approximately(f10, 10f))
                {
                    codes[i] = new CodeInstruction(OpCodes.Call, mCloseDist);
                    replacedClose = true;
                    continue;
                }

                // Handle potential int literal 10 followed by conv.r4
                if (!replacedClose && TryGetInt32Constant(ci, out int i10) && i10 == 10)
                {
                    if (i + 1 < codes.Count && codes[i + 1].opcode == OpCodes.Conv_R4)
                    {
                        codes[i] = new CodeInstruction(OpCodes.Call, mCloseDist);
                        codes[i + 1] = new CodeInstruction(OpCodes.Nop);
                        replacedClose = true;
                        continue;
                    }
                }

                // targetLookAwayAngle: replace literal 70f in the Vector3.Angle(...) check.
                if (!replacedLookAway && ci.opcode == OpCodes.Ldc_R4 && ci.operand is float f70 && Approximately(f70, 70f))
                {
                    codes[i] = new CodeInstruction(OpCodes.Call, mLookAway);
                    replacedLookAway = true;
                    continue;
                }

                // Seen gain/decay rates: only patch the ones used to update targetHasSeenMeCounter.
                if (ci.opcode == OpCodes.Ldc_R4 && ci.operand is float f && StoresSeenCounterSoon(i))
                {
                    if (!replacedGain1 && Approximately(f, 1f))
                    {
                        codes[i] = new CodeInstruction(OpCodes.Call, mGain1);
                        replacedGain1 = true;
                        continue;
                    }

                    if (!replacedGain03 && Approximately(f, 0.3f))
                    {
                        codes[i] = new CodeInstruction(OpCodes.Call, mGain03);
                        replacedGain03 = true;
                        continue;
                    }

                    if (!replacedGain015 && Approximately(f, 0.15f))
                    {
                        codes[i] = new CodeInstruction(OpCodes.Call, mGain015);
                        replacedGain015 = true;
                        continue;
                    }

                    if (!replacedDecay01 && Approximately(f, 0.1f))
                    {
                        codes[i] = new CodeInstruction(OpCodes.Call, mDecay01);
                        replacedDecay01 = true;
                        continue;
                    }
                }
            }

            if (!replacedClose)
                Log?.LogWarning($"[{LogTag}] CalcVars: did not find 10f constant to replace (close distance).");
            if (!replacedLookAway)
                Log?.LogWarning($"[{LogTag}] CalcVars: did not find 70f constant to replace (target look-away angle).");
            if (!replacedGain1 || !replacedGain03 || !replacedGain015 || !replacedDecay01)
                Log?.LogWarning($"[{LogTag}] CalcVars: did not replace all seen rates (1.0/0.3/0.15/0.1). " +
                                $"Replaced: 1.0={replacedGain1}, 0.3={replacedGain03}, 0.15={replacedGain015}, 0.1={replacedDecay01}.");

            return codes;
        }
    }

    [HarmonyPatch]
    private static class Patch_Scoutmaster_AnyoneCanSeePos
    {
        private static MethodBase? TargetMethod()
        {
            var t = AccessTools.TypeByName("Scoutmaster");
            return t == null ? null : AccessTools.Method(t, "AnyoneCanSeePos");
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);

            var mAngle = AccessTools.Method(typeof(Plugin), nameof(GetAnyoneCanSeeAngle));

            bool replaced = false;

            for (int i = 0; i < codes.Count; i++)
            {
                var ci = codes[i];

                // Replace literal 80f with our computed angle.
                if (!replaced && ci.opcode == OpCodes.Ldc_R4 && ci.operand is float f80 && Approximately(f80, 80f))
                {
                    codes[i] = new CodeInstruction(OpCodes.Call, mAngle);
                    replaced = true;
                    continue;
                }

                // Handle potential int literal 80 followed by conv.r4
                if (!replaced && TryGetInt32Constant(ci, out int i80) && i80 == 80)
                {
                    if (i + 1 < codes.Count && codes[i + 1].opcode == OpCodes.Conv_R4)
                    {
                        codes[i] = new CodeInstruction(OpCodes.Call, mAngle);
                        codes[i + 1] = new CodeInstruction(OpCodes.Nop);
                        replaced = true;
                        continue;
                    }
                }
            }

            if (!replaced)
                Log?.LogWarning($"[{LogTag}] AnyoneCanSeePos: did not find 80f constant to replace (anyone-can-see angle).");

            return codes;
        }
    }

    
[HarmonyPatch]
private static class Patch_Scoutmaster_Chase
{
    private static MethodBase? TargetMethod()
    {
        var t = AccessTools.TypeByName("Scoutmaster");
        return t == null ? null : AccessTools.Method(t, "Chase");
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var codes = new List<CodeInstruction>(instructions);

        var mSeenThreshold = AccessTools.Method(typeof(Plugin), nameof(GetSeenCounterToEnableSprint));
        var mWalkDist = AccessTools.Method(typeof(Plugin), nameof(GetWalkTowardTargetDistance));
        var mSprintDist = AccessTools.Method(typeof(Plugin), nameof(GetSprintIfDistance));
        var mLoseChance = AccessTools.Method(typeof(Plugin), nameof(GetLoseTargetAfterTeleportChance));

int replacedSeenThreshold = 0;
        int replacedWalk = 0;
        int replacedSprint = 0;
        int replacedLoseChance = 0;

bool RecentLoadsField(int idx, string fieldNamePart, int lookback = 3)
        {
            for (int k = idx - 1; k >= 0 && k >= idx - lookback; k--)
            {
                if (codes[k].opcode == OpCodes.Ldfld && codes[k].operand is FieldInfo fi &&
                    fi.Name.IndexOf(fieldNamePart, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        bool RecentDistanceCompute(int idx, int lookback = 10)
        {
            for (int k = idx - 1; k >= 0 && k >= idx - lookback; k--)
            {
                var op = codes[k].opcode;
                if ((op == OpCodes.Call || op == OpCodes.Callvirt) && codes[k].operand is MethodInfo mi)
                {
                    var dt = mi.DeclaringType;
                    if (dt != null && dt.FullName == "UnityEngine.Vector3" &&
                        (mi.Name == "Distance" || mi.Name == "get_magnitude" || mi.Name == "get_sqrMagnitude"))
                        return true;

                    if (mi.Name.IndexOf("Distance", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            return false;
        }

        bool RecentRandomValue(int idx, int lookback = 8)
        {
            for (int k = idx - 1; k >= 0 && k >= idx - lookback; k--)
            {
                var op = codes[k].opcode;
                if ((op == OpCodes.Call || op == OpCodes.Callvirt) && codes[k].operand is MethodInfo mi)
                {
                    var dt = mi.DeclaringType;
                    if (dt != null && dt.FullName == "UnityEngine.Random" && mi.Name == "get_value")
                        return true;
                    if (mi.Name.IndexOf("Random", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        mi.Name.IndexOf("value", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            return false;
        }

        for (int i = 0; i < codes.Count; i++)
        {
            var ci = codes[i];

            // --- Seen threshold patch: only "targetHasSeenMeCounter > 1f" comparisons ---
            if (ci.opcode == OpCodes.Ldc_R4 && ci.operand is float f1 && Approximately(f1, 1f))
            {
                if (i - 1 >= 0 && codes[i - 1].opcode == OpCodes.Ldfld && codes[i - 1].operand is FieldInfo fi &&
                    fi.Name.IndexOf("targetHasSeenMeCounter", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    codes[i] = new CodeInstruction(OpCodes.Call, mSeenThreshold);
                    replacedSeenThreshold++;
                    continue;
                }
            }
            // --- Walk toward target threshold (base 5) ---
            // Avoid patching tpCounter > 5f (teleport cooldown) by excluding nearby tpCounter loads.
            if (ci.opcode == OpCodes.Ldc_R4 && ci.operand is float f5 && Approximately(f5, 5f) && RecentDistanceCompute(i) && !RecentLoadsField(i, "tpCounter"))
            {
                codes[i] = new CodeInstruction(OpCodes.Call, mWalkDist);
                replacedWalk++;
                continue;
            }
            if (TryGetInt32Constant(ci, out int i5) && i5 == 5)
            {
                if (i + 1 < codes.Count && codes[i + 1].opcode == OpCodes.Conv_R4 && RecentDistanceCompute(i) && !RecentLoadsField(i, "tpCounter"))
                {
                    codes[i] = new CodeInstruction(OpCodes.Call, mWalkDist);
                    codes[i + 1] = new CodeInstruction(OpCodes.Nop);
                    replacedWalk++;
                    continue;
                }
            }

            // --- Sprint if distance threshold (base 15) ---
            if (ci.opcode == OpCodes.Ldc_R4 && ci.operand is float f15 && Approximately(f15, 15f) && RecentDistanceCompute(i))
            {
                codes[i] = new CodeInstruction(OpCodes.Call, mSprintDist);
                replacedSprint++;
                continue;
            }
            if (TryGetInt32Constant(ci, out int i15) && i15 == 15)
            {
                if (i + 1 < codes.Count && codes[i + 1].opcode == OpCodes.Conv_R4 && RecentDistanceCompute(i))
                {
                    codes[i] = new CodeInstruction(OpCodes.Call, mSprintDist);
                    codes[i + 1] = new CodeInstruction(OpCodes.Nop);
                    replacedSprint++;
                    continue;
                }
            }

            // --- Lose target after teleport chance (base 0.10) ---
            // Only patch the Random.value compare cases.
            if (ci.opcode == OpCodes.Ldc_R4 && ci.operand is float f01 && Approximately(f01, 0.1f) && RecentRandomValue(i))
            {
                codes[i] = new CodeInstruction(OpCodes.Call, mLoseChance);
                replacedLoseChance++;
                continue;
            }
        }

        if (replacedSeenThreshold == 0)
            Log?.LogWarning($"[{LogTag}] Chase: did not find targetHasSeenMeCounter > 1f threshold to replace.");
        else if (replacedSeenThreshold < 2)
            Log?.LogWarning($"[{LogTag}] Chase: replaced {replacedSeenThreshold} seen-threshold occurrences (expected 2).");

        if (replacedWalk == 0)
            Log?.LogWarning($"[{LogTag}] Chase: did not find 5f walk-toward-target distance to replace (pattern may have changed).");
        if (replacedSprint == 0)
            Log?.LogWarning($"[{LogTag}] Chase: did not find 15f sprint-if-distance threshold to replace (pattern may have changed).");
        if (replacedLoseChance == 0)
            Log?.LogWarning($"[{LogTag}] Chase: did not find 0.1f lose-target-after-teleport chance to replace (pattern may have changed).");


        return codes;
    }
}



[HarmonyPatch]
private static class Patch_Scoutmaster_Update
{
    private static MethodBase? TargetMethod()
    {
        var t = AccessTools.TypeByName("Scoutmaster");
        return t == null ? null : AccessTools.Method(t, "Update");
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var codes = new List<CodeInstruction>(instructions);
        var mTpInstead = AccessTools.Method(typeof(Plugin), nameof(GetTeleportInsteadOfChaseDistance));
        int replaced = 0;

        bool RecentDistanceCompute(int idx, int lookback = 10)
        {
            for (int k = idx - 1; k >= 0 && k >= idx - lookback; k--)
            {
                var op = codes[k].opcode;
                if ((op == OpCodes.Call || op == OpCodes.Callvirt) && codes[k].operand is MethodInfo mi)
                {
                    var dt = mi.DeclaringType;
                    if (dt != null && dt.FullName == "UnityEngine.Vector3" &&
                        (mi.Name == "Distance" || mi.Name == "get_magnitude" || mi.Name == "get_sqrMagnitude"))
                        return true;

                    if (mi.Name.IndexOf("Distance", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            return false;
        }

        for (int i = 0; i < codes.Count; i++)
        {
            var ci = codes[i];
            if (ci.opcode == OpCodes.Ldc_R4 && ci.operand is float f && Approximately(f, 80f) && RecentDistanceCompute(i))
            {
                codes[i] = new CodeInstruction(OpCodes.Call, mTpInstead);
                replaced++;
            }
            else if (TryGetInt32Constant(ci, out int v) && v == 80)
            {
                // Sometimes 80 is emitted as int followed by conv.r4; patch that too.
                if (i + 1 < codes.Count && codes[i + 1].opcode == OpCodes.Conv_R4 && RecentDistanceCompute(i))
                {
                    codes[i] = new CodeInstruction(OpCodes.Call, mTpInstead);
                    codes[i + 1] = new CodeInstruction(OpCodes.Nop);
                    replaced++;
                }
            }
        }

        if (replaced == 0)
            Log?.LogWarning($"[{LogTag}] Update: did not find teleport-instead-of-chase distance (80f) to replace (pattern may have changed).");

        return codes;
    }
}

[HarmonyPatch]
private static class Patch_Scoutmaster_Teleport
{
    private static MethodBase? TargetMethod()
    {
        var t = AccessTools.TypeByName("Scoutmaster");
        if (t == null) return null;

        // Prefer the Teleport(Character, float, float, float) overload.
        foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
        {
            if (!string.Equals(m.Name, "Teleport", StringComparison.Ordinal)) continue;
            var ps = m.GetParameters();
            if (ps.Length != 4) continue;
            if (ps[1].ParameterType != typeof(float) || ps[2].ParameterType != typeof(float) || ps[3].ParameterType != typeof(float))
                continue;

            // ps[0] should be Character in-game; avoid hard reference by name.
            if (!string.Equals(ps[0].ParameterType.Name, "Character", StringComparison.Ordinal))
                continue;

            return m;
        }

        return null;
    }

    private static void Prefix(ref float minDistanceToTarget, ref float maxDistanceToTarget, ref float maxHeightDifference)
    {
        // Category split:
        // - Teleport close to target: when called with (50, 70)
        // - Generic teleport sampling: otherwise (defaults: maxDist=45, maxHeight=15)
        bool isCloseTeleport = Approximately(minDistanceToTarget, 50f) && Approximately(maxDistanceToTarget, 70f);

        if (isCloseTeleport)
        {
            float minMult = TeleportCloseMinDistanceMult?.Value ?? 1.0f;
            float maxMult = TeleportCloseMaxDistanceMult?.Value ?? 1.0f;

            minDistanceToTarget = Mathf.Max(0.0f, minDistanceToTarget * minMult);
            maxDistanceToTarget = Mathf.Max(0.0f, maxDistanceToTarget * maxMult);

            // Keep max >= min (avoid invalid ring).
            if (maxDistanceToTarget < minDistanceToTarget + 0.01f)
                maxDistanceToTarget = minDistanceToTarget + 0.01f;
        }
        else
        {
            float maxDistMult = TeleportSamplingMaxDistanceMult?.Value ?? 1.0f;
            maxDistanceToTarget = Mathf.Max(0.01f, maxDistanceToTarget * maxDistMult);
        }

        float heightMult = TeleportSamplingMaxHeightDifferenceMult?.Value ?? 1.0f;
        maxHeightDifference = Mathf.Max(0.01f, maxHeightDifference * heightMult);
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var codes = new List<CodeInstruction>(instructions);

        var mSamples = AccessTools.Method(typeof(Plugin), nameof(GetTeleportSamplingSamples));
        var mCooldown = AccessTools.Method(typeof(Plugin), nameof(GetTeleportCloseCooldownSeconds));

        int replacedSamples = 0;
        int replacedCooldown = 0;

        for (int i = 0; i < codes.Count; i++)
        {
            var ci = codes[i];

            // Patch tpCounter < 5f gate in Teleport(...) (close-teleport cooldown).
            if (ci.opcode == OpCodes.Ldc_R4 && ci.operand is float f5 && Approximately(f5, 5f))
            {
                if (i - 1 >= 0 && codes[i - 1].opcode == OpCodes.Ldfld && codes[i - 1].operand is FieldInfo fi &&
                    fi.Name.IndexOf("tpCounter", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    codes[i] = new CodeInstruction(OpCodes.Call, mCooldown);
                    replacedCooldown++;
                    continue;
                }
            }

            // Patch attempt-loop count in Teleport(...) (base game: 50).
            if (replacedSamples == 0 && TryGetInt32Constant(ci, out int n) && n == 50)
            {
                codes[i] = new CodeInstruction(OpCodes.Call, mSamples);
                replacedSamples++;
                continue;
            }
        }

        if (replacedCooldown == 0)
            Log?.LogWarning($"[{LogTag}] Teleport: did not find tpCounter < 5f gate to replace (pattern may have changed).");
        if (replacedSamples == 0)
            Log?.LogWarning($"[{LogTag}] Teleport: did not find int 50 constant to replace (attempt-loop count).");

        return codes;
    }
}

[HarmonyPatch]
private static class Patch_Scoutmaster_EvasiveBehaviour
{
    private static MethodBase? TargetMethod()
    {
        var t = AccessTools.TypeByName("Scoutmaster");
        return t == null ? null : AccessTools.Method(t, "EvasiveBehaviour");
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var codes = new List<CodeInstruction>(instructions);

        var mSeenTime = AccessTools.Method(typeof(Plugin), nameof(GetTeleportFarSeenTimeSeconds));

        int replaced = 0;

        for (int i = 0; i < codes.Count; i++)
        {
            var ci = codes[i];
            if (ci.opcode == OpCodes.Ldc_R4 && ci.operand is float f && Approximately(f, 0.5f))
            {
                if (i - 1 >= 0 && codes[i - 1].opcode == OpCodes.Ldfld && codes[i - 1].operand is FieldInfo fi &&
                    fi.Name.IndexOf("sinceAnyoneCanSeeMe", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    codes[i] = new CodeInstruction(OpCodes.Call, mSeenTime);
                    replaced++;
                }
            }
        }

        if (replaced == 0)
            Log?.LogWarning($"[{LogTag}] EvasiveBehaviour: did not find sinceAnyoneCanSeeMe > 0.5f threshold to replace.");

        return codes;
    }
}

[HarmonyPatch]
private static class Patch_Scoutmaster_TeleportFarAway
{
    private static MethodBase? TargetMethod()
    {
        var t = AccessTools.TypeByName("Scoutmaster");
        return t == null ? null : AccessTools.Method(t, "TeleportFarAway");
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var codes = new List<CodeInstruction>(instructions);

        var mCooldown = AccessTools.Method(typeof(Plugin), nameof(GetTeleportFarCooldownSeconds));
        var mFarPos = AccessTools.Method(typeof(Plugin), nameof(GetTeleportFarPosition));
        var v3Ctor = AccessTools.Constructor(typeof(Vector3), new[] { typeof(float), typeof(float), typeof(float) });

        int replacedCooldown = 0;
        int replacedPos = 0;

        for (int i = 0; i < codes.Count; i++)
        {
            var ci = codes[i];

            // Patch tpCounter < 5f gate
            if (ci.opcode == OpCodes.Ldc_R4 && ci.operand is float f5 && Approximately(f5, 5f))
            {
                if (i - 1 >= 0 && codes[i - 1].opcode == OpCodes.Ldfld && codes[i - 1].operand is FieldInfo fi &&
                    fi.Name.IndexOf("tpCounter", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    codes[i] = new CodeInstruction(OpCodes.Call, mCooldown);
                    replacedCooldown++;
                    continue;
                }
            }

            // Patch new Vector3(0,0,5000) used for far teleport destination.
            if (v3Ctor != null && ci.opcode == OpCodes.Newobj && ci.operand is ConstructorInfo ctor && ctor == v3Ctor)
            {
                if (i >= 3 &&
                    codes[i - 3].opcode == OpCodes.Ldc_R4 && codes[i - 3].operand is float fx && Approximately(fx, 0f) &&
                    codes[i - 2].opcode == OpCodes.Ldc_R4 && codes[i - 2].operand is float fy && Approximately(fy, 0f) &&
                    codes[i - 1].opcode == OpCodes.Ldc_R4 && codes[i - 1].operand is float fz && Approximately(fz, 5000f))
                {
                    codes[i - 3] = new CodeInstruction(OpCodes.Nop);
                    codes[i - 2] = new CodeInstruction(OpCodes.Nop);
                    codes[i - 1] = new CodeInstruction(OpCodes.Nop);
                    codes[i] = new CodeInstruction(OpCodes.Call, mFarPos);
                    replacedPos++;
                    continue;
                }
            }
        }

        if (replacedCooldown == 0)
            Log?.LogWarning($"[{LogTag}] TeleportFarAway: did not find tpCounter < 5f gate to replace.");
        if (replacedPos == 0)
            Log?.LogWarning($"[{LogTag}] TeleportFarAway: did not find new Vector3(0,0,5000) to replace.");

        return codes;
    }
}


    [HarmonyPatch]
    private static class Patch_Scoutmaster_IThrow
    {
        private static MethodBase? TargetMethod()
        {
            var t = AccessTools.TypeByName("Scoutmaster");
            if (t == null) return null;

            // IThrow is a coroutine (IEnumerator). Patching the method itself only patches the wrapper that creates the iterator.
            // We must patch the compiler-generated state machine's MoveNext() to affect the actual throw logic/constants.
            var m = AccessTools.Method(t, "IThrow");
            return m == null ? null : AccessTools.EnumeratorMoveNext(m);
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);

            var mWindup = AccessTools.Method(typeof(Plugin), nameof(GetThrowWindupSeconds));
            var mLocalAmp = AccessTools.Method(typeof(Plugin), nameof(GetThrowLocalShakeAmplitude));
            var mLocalDur = AccessTools.Method(typeof(Plugin), nameof(GetThrowLocalShakeDuration));
            var mGlobalAmp = AccessTools.Method(typeof(Plugin), nameof(GetThrowGlobalShakeAmplitude));
            var mGlobalDur = AccessTools.Method(typeof(Plugin), nameof(GetThrowGlobalShakeDuration));
            var mUpBias = AccessTools.Method(typeof(Plugin), nameof(GetThrowUpwardBias));
            var mForce = AccessTools.Method(typeof(Plugin), nameof(GetThrowForce));
            var mDuration = AccessTools.Method(typeof(Plugin), nameof(GetThrowDuration));
            var mInjury = AccessTools.Method(typeof(Plugin), nameof(GetThrowInjuryAmount));
            var mThrowStatusType = AccessTools.Method(typeof(Plugin), nameof(GetThrowStatusTypeValue));
            var mChill = AccessTools.Method(typeof(Plugin), nameof(GetThrowPostChillSeconds));

            // Resolve method handles we want to patch around.
            MethodInfo? addPerlinShake = null;
            var tGamefeel = AccessTools.TypeByName("GamefeelHandler");
            if (tGamefeel != null)
                addPerlinShake = AccessTools.Method(tGamefeel, "AddPerlinShake", new[] { typeof(float), typeof(float) });

            MethodInfo? grabbingThrow = null;
            var tGrabbing = AccessTools.TypeByName("CharacterGrabbing");
            if (tGrabbing != null)
                grabbingThrow = AccessTools.Method(tGrabbing, "Throw", new[] { typeof(Vector3), typeof(float) });

            MethodInfo? addStatus = null;
            var tAff = AccessTools.TypeByName("CharacterAfflictions");
            if (tAff != null)
                addStatus = AccessTools.Method(tAff, "AddStatus");

            FieldInfo? chillField = null;
            var tScout = AccessTools.TypeByName("Scoutmaster");
            if (tScout != null)
                chillField = AccessTools.Field(tScout, "chillForSeconds");

            bool replacedWindup = false;
            bool replacedUpBias = false;
            bool replacedForce = false;
            bool replacedChill = false;
            bool replacedThrowStatusType = false;

            for (int i = 0; i < codes.Count; i++)
            {
                var ci = codes[i];

                // Patch AddPerlinShake calls (local and global) by matching their literal args.
                if (addPerlinShake != null &&
                    (ci.opcode == OpCodes.Callvirt || ci.opcode == OpCodes.Call) &&
                    ci.operand is MethodInfo miShake && miShake == addPerlinShake &&
                    i >= 2)
                {
                    if (codes[i - 2].opcode == OpCodes.Ldc_R4 && codes[i - 2].operand is float a &&
                        codes[i - 1].opcode == OpCodes.Ldc_R4 && codes[i - 1].operand is float b)
                    {
                        // local victim shake: (15, 0.5)
                        if (Approximately(a, 15f) && Approximately(b, 0.5f))
                        {
                            codes[i - 2] = new CodeInstruction(OpCodes.Call, mLocalAmp);
                            codes[i - 1] = new CodeInstruction(OpCodes.Call, mLocalDur);
                            continue;
                        }

                        // global shake: (3, 3)
                        if (Approximately(a, 3f) && Approximately(b, 3f))
                        {
                            codes[i - 2] = new CodeInstruction(OpCodes.Call, mGlobalAmp);
                            codes[i - 1] = new CodeInstruction(OpCodes.Call, mGlobalDur);
                            continue;
                        }
                    }
                }

                // Windup: replace 3.2f in "while (c < 3.2f)".
                if (!replacedWindup && ci.opcode == OpCodes.Ldc_R4 && ci.operand is float f32 && Approximately(f32, 3.2f))
                {
                    codes[i] = new CodeInstruction(OpCodes.Call, mWindup);
                    replacedWindup = true;
                    continue;
                }

                // Upward bias: vector.y = 0.3f
                if (!replacedUpBias && ci.opcode == OpCodes.Ldc_R4 && ci.operand is float f03 && Approximately(f03, 0.3f))
                {
                    codes[i] = new CodeInstruction(OpCodes.Call, mUpBias);
                    replacedUpBias = true;
                    continue;
                }

                // Throw force multiplier: patch the literal 1500f.
                if (!replacedForce && ci.opcode == OpCodes.Ldc_R4 && ci.operand is float f1500 && Approximately(f1500, 1500f))
                {
                    codes[i] = new CodeInstruction(OpCodes.Call, mForce);
                    replacedForce = true;
                    continue;
                }

                // Throw duration: patch the float argument to CharacterGrabbing.Throw(..., 3f).
                if (grabbingThrow != null &&
                    (ci.opcode == OpCodes.Callvirt || ci.opcode == OpCodes.Call) &&
                    ci.operand is MethodInfo miThrow && miThrow == grabbingThrow &&
                    i >= 1)
                {
                    if (codes[i - 1].opcode == OpCodes.Ldc_R4 && codes[i - 1].operand is float f3 && Approximately(f3, 3f))
                    {
                        codes[i - 1] = new CodeInstruction(OpCodes.Call, mDuration);
                        continue;
                    }
                }

                // Injury amount and status type: patch AddStatus(..., Injury, 0.25f, ...).
if (addStatus != null &&
    (ci.opcode == OpCodes.Callvirt || ci.opcode == OpCodes.Call) &&
    ci.operand is MethodInfo miAdd && miAdd == addStatus &&
    i >= 3)
{
    int floatIdx = -1;

    // Look back a few instructions for the 0.25f literal (it should be the amount argument).
    for (int back = 1; back <= 6 && i - back >= 0; back++)
    {
        var prev = codes[i - back];
        if (prev.opcode == OpCodes.Ldc_R4 && prev.operand is float f025 && Approximately(f025, 0.25f))
        {
            codes[i - back] = new CodeInstruction(OpCodes.Call, mInjury);
            floatIdx = i - back;
            break;
        }
    }

    // Replace the status-type constant (vanilla: Injury = 0) with a configurable value.
    // We only replace a 0 int constant immediately preceding the amount, to avoid touching the bool "fromRPC" flag.
    if (!replacedThrowStatusType && floatIdx >= 0 && mThrowStatusType != null)
    {
        for (int k = floatIdx - 1; k >= 0 && k >= floatIdx - 10; k--)
        {
            if (TryGetInt32Constant(codes[k], out int v) && v == 0)
            {
                codes[k] = new CodeInstruction(OpCodes.Call, mThrowStatusType);
                replacedThrowStatusType = true;
                break;
            }
        }
    }
}

                // Post-throw chill: chillForSeconds = 2f;
                if (!replacedChill && chillField != null && ci.opcode == OpCodes.Stfld && ci.operand is FieldInfo fi && fi == chillField && i >= 1)
                {
                    if (codes[i - 1].opcode == OpCodes.Ldc_R4 && codes[i - 1].operand is float f2 && Approximately(f2, 2f))
                    {
                        codes[i - 1] = new CodeInstruction(OpCodes.Call, mChill);
                        replacedChill = true;
                        continue;
                    }
                }
            }

            return codes;
        }
    }

    [HarmonyPatch]
    private static class Patch_Scoutmaster_RotateToMostEvilThrowDirection
    {
        private static MethodBase? TargetMethod()
        {
            var t = AccessTools.TypeByName("Scoutmaster");
            return t == null ? null : AccessTools.Method(t, "RotateToMostEvilThrowDirection");
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);

            var mSamples = AccessTools.Method(typeof(Plugin), nameof(GetThrowDirectionSamples));
            var mRadius = AccessTools.Method(typeof(Plugin), nameof(GetThrowDirectionSampleRadius));
            var mDownLen = AccessTools.Method(typeof(Plugin), nameof(GetThrowDirectionDownRayLength));

            MethodInfo? getCircularDirections = null;
            var tHelpers = AccessTools.TypeByName("HelperFunctions");
            if (tHelpers != null)
                getCircularDirections = AccessTools.Method(tHelpers, "GetCircularDirections", new[] { typeof(int) });

            bool afterDirs = false;
            bool replacedRadius = false;
            bool replacedDown = false;

            for (int i = 0; i < codes.Count; i++)
            {
                var ci = codes[i];

                if (getCircularDirections != null &&
                    (ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt) &&
                    ci.operand is MethodInfo mi && mi == getCircularDirections)
                {
                    // Replace the argument (ldc.i4.s 10 / ldc.i4 10) with our computed sample count.
                    if (i >= 1 &&
                                                TryGetInt32Constant(codes[i - 1], out int v) && v == 10)
                    {
                        codes[i - 1] = new CodeInstruction(OpCodes.Call, mSamples);
                    }
                    afterDirs = true;
                    continue;
                }

                if (afterDirs && !replacedRadius && ci.opcode == OpCodes.Ldc_R4 && ci.operand is float f10 && Approximately(f10, 10f))
                {
                    codes[i] = new CodeInstruction(OpCodes.Call, mRadius);
                    replacedRadius = true;
                    continue;
                }

                if (afterDirs && !replacedDown && ci.opcode == OpCodes.Ldc_R4 && ci.operand is float f1000 && Approximately(f1000, 1000f))
                {
                    codes[i] = new CodeInstruction(OpCodes.Call, mDownLen);
                    replacedDown = true;
                    continue;
                }
            }

            return codes;
        }
    }



[HarmonyPatch]
private static class Patch_Scoutmaster_DoVisuals
{
    private static MethodBase? TargetMethod()
    {
        var t = AccessTools.TypeByName("Scoutmaster");
        return t == null ? null : AccessTools.Method(t, "DoVisuals");
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var codes = new List<CodeInstruction>(instructions);

        var mFar = AccessTools.Method(typeof(Plugin), nameof(GetVisualStrengthFarDistance));
        var mNear = AccessTools.Method(typeof(Plugin), nameof(GetVisualStrengthNearDistance));
        var mSpeed = AccessTools.Method(typeof(Plugin), nameof(GetVisualStrengthLerpSpeed));
        var mGrain = AccessTools.Method(typeof(Plugin), nameof(ApplyGrainMultiplier));

        var setFloat = AccessTools.Method(typeof(Material), "SetFloat", new[] { typeof(int), typeof(float) });
        var inverseLerp = AccessTools.Method(typeof(Mathf), "InverseLerp", new[] { typeof(float), typeof(float), typeof(float) });

        FieldInfo? grainField = null;
        FieldInfo? strengthField = null;
        var tScout = AccessTools.TypeByName("Scoutmaster");
        if (tScout != null)
        {
            grainField = AccessTools.Field(tScout, "GRAINMULTID");
            strengthField = AccessTools.Field(tScout, "STRENGTHID");
        }

        int replacedFar = 0;
        int replacedNear = 0;
        int replacedSpeed = 0;
        int insertedGrainMult = 0;

        for (int i = 0; i < codes.Count; i++)
        {
            var ci = codes[i];

            // Patch endpoints of Mathf.InverseLerp(50f, 5f, distanceToTarget)
            if (inverseLerp != null &&
                (ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt) &&
                ci.operand is MethodInfo miInv && miInv == inverseLerp &&
                i >= 3)
            {
                if (codes[i - 3].opcode == OpCodes.Ldc_R4 && codes[i - 3].operand is float a && Approximately(a, 50f))
                {
                    codes[i - 3] = new CodeInstruction(OpCodes.Call, mFar);
                    replacedFar++;
                }

                if (codes[i - 2].opcode == OpCodes.Ldc_R4 && codes[i - 2].operand is float b && Approximately(b, 5f))
                {
                    codes[i - 2] = new CodeInstruction(OpCodes.Call, mNear);
                    replacedNear++;
                }

                continue;
            }

            // Patch lerp speed constant used in Time.deltaTime * 0.5f
            if (replacedSpeed == 0 && ci.opcode == OpCodes.Ldc_R4 && ci.operand is float f05 && Approximately(f05, 0.5f))
            {
                codes[i] = new CodeInstruction(OpCodes.Call, mSpeed);
                replacedSpeed++;
                continue;
            }

            // Insert grain multiplier right before mat.SetFloat(GRAINMULTID, ...)
            if (setFloat != null &&
                (ci.opcode == OpCodes.Callvirt || ci.opcode == OpCodes.Call) &&
                ci.operand is MethodInfo miSet && miSet == setFloat)
            {
                if (IsNearestIdField(codes, i, grainField, strengthField))
                {
                    // Stack: mat, id, value -> call ApplyGrainMultiplier(value)
                    codes.Insert(i, new CodeInstruction(OpCodes.Call, mGrain));
                    i++; // skip inserted instruction
                    insertedGrainMult++;
                }
            }
        }

        if (replacedFar == 0 || replacedNear == 0)
            Log?.LogWarning($"[{LogTag}] DoVisuals: did not replace both InverseLerp endpoints (50f/5f). Replaced: far={replacedFar}, near={replacedNear}.");
        if (replacedSpeed == 0)
            Log?.LogWarning($"[{LogTag}] DoVisuals: did not replace 0.5f lerp speed constant.");
        if (insertedGrainMult == 0)
            Log?.LogWarning($"[{LogTag}] DoVisuals: did not insert grain multiplier before SetFloat(GRAINMULTID, ...).");

        return codes;
    }

    private static bool IsNearestIdField(List<CodeInstruction> codes, int callIndex, FieldInfo? grainField, FieldInfo? strengthField)
    {
        const int lookback = 12;
        for (int k = callIndex - 1; k >= 0 && k >= callIndex - lookback; k--)
        {
            if ((codes[k].opcode == OpCodes.Ldfld || codes[k].opcode == OpCodes.Ldsfld) && codes[k].operand is FieldInfo fi)
            {
                // Instance fields are loaded via ldarg.0 + ldfld; require that to avoid false matches.
                if (codes[k].opcode == OpCodes.Ldfld)
                {
                    if (k - 1 < 0 || codes[k - 1].opcode != OpCodes.Ldarg_0) continue;
                }
                if (grainField != null && fi == grainField) return true;
                if (strengthField != null && fi == strengthField) return false;
            }
        }
        return false;
    }
}

[HarmonyPatch]
    private static class Patch_Scoutmaster_Start
    {
        private static MethodBase? TargetMethod()
        {
            var t = AccessTools.TypeByName("Scoutmaster");
            return t == null ? null : AccessTools.Method(t, "Start");
        }

        private static void Postfix(object __instance)
        {
            try
            {
                var t = __instance.GetType();

                var fAttackHeightDelta = AccessTools.Field(t, "attackHeightDelta");
                var fMaxAggroHeight = AccessTools.Field(t, "maxAggroHeight");

                if (fAttackHeightDelta == null || fMaxAggroHeight == null)
                {
                    Log?.LogWarning($"[{LogTag}] Start: could not access fields attackHeightDelta/maxAggroHeight (names may have changed).");
                    return;
                }

                // Read originals once per instance (avoid double-multiplying).
                if (!ScoutmasterOriginalsByInstance.TryGetValue(__instance, out var orig))
                {
                    orig = new ScoutmasterOriginals
                    {
                        AttackHeightDelta = SafeGetFloat(fAttackHeightDelta, __instance),
                        MaxAggroHeight = SafeGetFloat(fMaxAggroHeight, __instance),
                    };
                    ScoutmasterOriginalsByInstance.Add(__instance, orig);
                }

                float ahdMult = Mathf.Max(0f, GetAttackHeightDeltaMultiplier());
                float mahMult = Mathf.Max(0f, GetMaxAggroHeightMultiplier());

                fAttackHeightDelta.SetValue(__instance, orig.AttackHeightDelta * ahdMult);
                fMaxAggroHeight.SetValue(__instance, orig.MaxAggroHeight * mahMult);
            }
            catch (Exception e)
            {
                Log?.LogError($"[{LogTag}] Start postfix error: {e}");
            }
        }
    }

    private static float SafeGetFloat(FieldInfo fi, object instance)
    {
        object? v = fi.GetValue(instance);
        if (v is float f) return f;
        if (v is double d) return (float)d;
        if (v is int i) return i;
        // Worst case: don't throw, keep vanilla-ish 0.
        return 0f;
    }

    private static bool Approximately(float a, float b) => Mathf.Abs(a - b) < 0.0001f;

    private static bool TryGetInt32Constant(CodeInstruction ci, out int value)
    {
        // Handles all ldc.i4 variants (including short forms like ldc.i4.0).
        var op = ci.opcode;
        if (op == OpCodes.Ldc_I4_M1) { value = -1; return true; }
        if (op == OpCodes.Ldc_I4_0) { value = 0; return true; }
        if (op == OpCodes.Ldc_I4_1) { value = 1; return true; }
        if (op == OpCodes.Ldc_I4_2) { value = 2; return true; }
        if (op == OpCodes.Ldc_I4_3) { value = 3; return true; }
        if (op == OpCodes.Ldc_I4_4) { value = 4; return true; }
        if (op == OpCodes.Ldc_I4_5) { value = 5; return true; }
        if (op == OpCodes.Ldc_I4_6) { value = 6; return true; }
        if (op == OpCodes.Ldc_I4_7) { value = 7; return true; }
        if (op == OpCodes.Ldc_I4_8) { value = 8; return true; }

        if (op == OpCodes.Ldc_I4 && ci.operand is int i) { value = i; return true; }
        if (op == OpCodes.Ldc_I4_S)
        {
            if (ci.operand is sbyte sb) { value = sb; return true; }
            if (ci.operand is byte b) { value = b; return true; }
            if (ci.operand is int ii) { value = ii; return true; }
        }

        value = 0;
        return false;
    }
    [HarmonyPatch]
    private static class Patch_CharacterAfflictions_AddStatus
    {
        private static MethodBase? TargetMethod()
        {
            var t = AccessTools.TypeByName("CharacterAfflictions");
            return t == null ? null : AccessTools.Method(t, "AddStatus");
        }

        
        private static void Prefix(object __instance, object statusType, ref float amount, bool fromRPC, bool playEffects)
        {
            // Keep vanilla unless status immunity is explicitly disabled.
            if (!(DisableScoutmasterStatusImmunity?.Value ?? false)) return;
            if (fromRPC) return; // avoid double-scaling on RPC replays

            // __instance is CharacterAfflictions; access 'character' field dynamically to avoid hard reference.
            var ch = Traverse.Create(__instance).Field("character").GetValue();
            if (ch == null) return;

            bool isScout = Traverse.Create(ch).Field("isScoutmaster").GetValue<bool>();
            if (!isScout) return;

            // statusType is CharacterAfflictions.STATUSTYPE; compare by name to avoid enum binding issues
string stName = statusType?.ToString() ?? string.Empty;

if (string.Equals(stName, "Injury", StringComparison.Ordinal))
{
    float mult = (ScoutmasterInjuryDamageMultiplier?.Value ?? 0f);
    if (mult <= 0f)
    {
        amount = 0f;
        return;
    }

    amount *= mult;
    if (amount < 0f) amount = 0f;
    return;
}

float otherMult = (ScoutmasterOtherStatusDamageMultiplier?.Value ?? 1f);
if (Approximately(otherMult, 1f)) return;
if (otherMult <= 0f)
{
    amount = 0f;
    return;
}

amount *= otherMult;
if (amount < 0f) amount = 0f;
        }

private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);

            var tChar = AccessTools.TypeByName("Character");
            FieldInfo? isScoutField = null;
            if (tChar != null)
                isScoutField = AccessTools.Field(tChar, "isScoutmaster");

            var helper = AccessTools.Method(typeof(Plugin), nameof(ShouldBlockScoutmasterStatus), new[] { typeof(bool) });

            if (isScoutField == null || helper == null)
            {
                Log?.LogWarning($"[{LogTag}] AddStatus: could not find Character.isScoutmaster field or helper; leaving vanilla.");
                return codes;
            }

            bool patched = false;
            for (int i = 0; i < codes.Count - 1; i++)
            {
                var ci = codes[i];

                if (ci.opcode == OpCodes.Ldfld && ci.operand is FieldInfo fi && fi == isScoutField)
                {
                    // Only patch when this is used as the immediate condition of an 'if (isScoutmaster) ...' check.
                    var next = codes[i + 1];
                    if (next.opcode == OpCodes.Brfalse || next.opcode == OpCodes.Brfalse_S)
                    {
                        codes.Insert(i + 1, new CodeInstruction(OpCodes.Call, helper));
                        patched = true;
                        i++; // skip over inserted call
                    }
                }
            }

            if (!patched)
                Log?.LogWarning($"[{LogTag}] AddStatus: did not find scoutmaster immunity check to patch.");

            return codes;
        }
    }
}