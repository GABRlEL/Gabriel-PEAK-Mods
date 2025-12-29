using System;
using System.Reflection;
using Photon.Pun;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Peak.Afflictions;
using UnityEngine;

namespace ConfigurableScorpions;

[BepInAutoPlugin]
public partial class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; } = null!;

    // Config helpers (override for "keep vanilla" defaults)
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


    // Config (defaults match vanilla behavior; changing values enables the mod's effects)
    internal static ConfigEntry<bool> CfgEnablePlayerTargeting { get; private set; } = null!;
    internal static ConfigEntry<bool> CfgOnlyAggressiveWhenHeld { get; private set; } = null!;
    internal static ConfigEntry<float> CfgAggroDistanceMultiplier { get; private set; } = null!;
    internal static ConfigEntry<bool> CfgRequireLineOfSight { get; private set; } = null!;
    internal static ConfigEntry<bool> CfgTargetPoisonedPlayers { get; private set; } = null!;
internal static ConfigEntry<float> CfgMovementSpeedMultiplier { get; private set; } = null!;
    internal static ConfigEntry<float> CfgTurnRateMultiplier { get; private set; } = null!;
    internal static ConfigEntry<float> CfgSleepDistanceMultiplier { get; private set; } = null!;
    internal static ConfigEntry<float> CfgWakeRadiusMultiplier { get; private set; } = null!;

    // Attack tuning (set to -1 to keep vanilla)
    internal static ConfigEntry<float> CfgAttackStartDistance { get; private set; } = null!;
    internal static ConfigEntry<float> CfgAttackDistance { get; private set; } = null!;
    internal static ConfigEntry<float> CfgAttackAngle { get; private set; } = null!;
    internal static ConfigEntry<float> CfgAttackCooldownSeconds { get; private set; } = null!;


        internal static ConfigEntry<float> CfgStingDelaySeconds { get; private set; } = null!;

    
    internal static ConfigEntry<float> CfgHeldStingTimeMultiplier { get; private set; } = null!;
// Damage configuration
    // - CfgDamageMultiplier is kept for backwards compatibility (global multiplier applied to both ticks)
    internal static ConfigEntry<float> CfgDamageMultiplier { get; private set; } = null!;
    internal static ConfigEntry<bool> CfgEnableInitialDamageTick { get; private set; } = null!;
    internal static ConfigEntry<bool> CfgEnableOvertimeDamageTick { get; private set; } = null!;
    internal static ConfigEntry<float> CfgInitialDamageMultiplier { get; private set; } = null!;
    internal static ConfigEntry<float> CfgOvertimeDamageMultiplier { get; private set; } = null!;
    internal static ConfigEntry<DamageTypeOverride> CfgDamageType { get; private set; } = null!;

    private Harmony? _harmony;

    private void Awake()
    {
        Log = Logger;

        CfgEnablePlayerTargeting = Config.Bind(
            "Targeting",
            "EnablePathfindingToPlayer",
            true,
            "Enable/disable player target acquisition for scorpions while they're on the ground. " +
            "If disabled, scorpions will only patrol (and won't chase players) unless they're being held.");

        CfgOnlyAggressiveWhenHeld = Config.Bind(
            "Targeting",
            "OnlyAggressiveWhenHeld",
            false,
            "If enabled, scorpions will only target/attack while held as an item. On the ground they will only patrol.");

        CfgAggroDistanceMultiplier = Config.Bind(
            "Targeting",
            "AggroDistanceMultiplier",
            1f,
            new ConfigDescription(
                "Multiplier applied to the scorpion's aggro radius (Mob.aggroDistance). 1 = vanilla.",
                new AcceptableValueRange<float>(0f, 20f)));

        CfgRequireLineOfSight = Config.Bind(
            "Targeting",
            "RequireLineOfSight",
            true,
            "If true (vanilla), scorpions only target players they have line-of-sight to (terrain blocks). If false, they can target through terrain.");

        CfgTargetPoisonedPlayers = Config.Bind(
            "Targeting",
            "TargetPoisonedPlayers",
            false,
            "If true, scorpions will target players even if they already have PoisonOverTime. false = vanilla.");

        CfgMovementSpeedMultiplier = Config.Bind(
            "Movement",
            "MovementSpeedMultiplier",
            1f,
            new ConfigDescription(
                "Multiplier applied to the scorpion's movement speed (Mob.movementSpeed). 1 = vanilla.",
                new AcceptableValueRange<float>(0f, 20f)));

        CfgTurnRateMultiplier = Config.Bind(
            "Movement",
            "TurnRateMultiplier",
            1f,
            new ConfigDescription(
                "Multiplier applied to the scorpion's turn rate (Mob.turnRate). 1 = vanilla.",
                new AcceptableValueRange<float>(0f, 20f)));

        CfgSleepDistanceMultiplier = Config.Bind(
            "Movement",
            "SleepDistanceMultiplier",
            1f,
            new ConfigDescription(
                "Multiplier for the distance used when deciding a scorpion can go to sleep (vanilla uses Mob.sleepDistance for both sleep and wake). 1 = vanilla.",
                new AcceptableValueRange<float>(0f, 20f)));

        CfgWakeRadiusMultiplier = Config.Bind(
            "Movement",
            "WakeRadiusMultiplier",
            1f,
            new ConfigDescription(
                "Multiplier for the distance used when deciding a sleeping scorpion should wake up (vanilla uses Mob.sleepDistance for both sleep and wake). 1 = vanilla.",
                new AcceptableValueRange<float>(0f, 20f)));


        CfgStingDelaySeconds = Config.Bind(
            "Attack",
            "StingDelaySeconds",
            -1f,
            "Override for the delay (in seconds) between starting the attack and the sting happening. " +
            "Set to -1 to keep the game's value.");

        
        CfgHeldStingTimeMultiplier = Config.Bind(
            "Attack",
            "HeldStingTimeMultiplier",
            1f,
            new ConfigDescription(
                "Multiplier applied to scorpion sting delay while the scorpion is held as an item. 1 = vanilla. " +
                "This multiplies the effective Mob.attackTime only while held (after any StingDelaySeconds override).",
                new AcceptableValueRange<float>(0f, 20f)));

// Attack tuning (set to -1 to keep vanilla)
        CfgAttackStartDistance = Config.Bind(
            "Attack",
            "AttackStartDistance",
            -1f,
            new ConfigDescription(
                "Override for the distance at which scorpions begin an attack (Mob.attackStartDistance). Set to -1 to keep vanilla.",
                new AcceptableValueRange<float>(-1f, 50f)));

        CfgAttackDistance = Config.Bind(
            "Attack",
            "AttackDistance",
            -1f,
            new ConfigDescription(
                "Override for the distance at which scorpions can hit their attack (Mob.attackDistance). Set to -1 to keep vanilla.",
                new AcceptableValueRange<float>(-1f, 50f)));

        CfgAttackAngle = Config.Bind(
            "Attack",
            "AttackAngle",
            -1f,
            new ConfigDescription(
                "Override for the attack cone angle in degrees (Mob.attackAngle). Set to -1 to keep vanilla.",
                new AcceptableValueRange<float>(-1f, 360f)));

        CfgAttackCooldownSeconds = Config.Bind(
            "Attack",
            "AttackCooldownSeconds",
            -1f,
            new ConfigDescription(
                "Override for the base attack cooldown in seconds (Mob private field attackCooldown). Set to -1 to keep vanilla.",
                new AcceptableValueRange<float>(-1f, 60f)));


        // Backwards compatible global multiplier.
        CfgDamageMultiplier = Config.Bind(
            "Attack",
            "DamageMultiplier",
            1f,
            new ConfigDescription(
                "Global multiplier for scorpion status damage (applies to BOTH the initial sting tick and the overtime tick). 1 = vanilla.",
                new AcceptableValueRange<float>(0f, 100f)));

        CfgEnableInitialDamageTick = Config.Bind(
            "Attack",
            "EnableInitialDamageTick",
            true,
            "Enable/disable the single (instant) status tick applied when the sting connects. true = vanilla.");

        CfgEnableOvertimeDamageTick = Config.Bind(
            "Attack",
            "EnableOvertimeDamageTick",
            true,
            "Enable/disable the over-time status application after the sting. true = vanilla.");

        CfgInitialDamageMultiplier = Config.Bind(
            "Attack",
            "InitialDamageMultiplier",
            1f,
            new ConfigDescription(
                "Multiplier for ONLY the initial (instant) sting status tick. 1 = vanilla.",
                new AcceptableValueRange<float>(0f, 100f)));

        CfgOvertimeDamageMultiplier = Config.Bind(
            "Attack",
            "OvertimeDamageMultiplier",
            1f,
            new ConfigDescription(
                "Multiplier for ONLY the over-time status tick rate (per second). 1 = vanilla.",
                new AcceptableValueRange<float>(0f, 100f)));

                CfgDamageType = Config.Bind(
                    "Attack",
                    "DamageType",
                    DamageTypeOverride.Vanilla,
                    "Controls the status/damage type a scorpion sting applies. Vanilla keeps the game's original setting (Poison).\n" +
                    "Cold = freeze, Hot = burn, Spores = shroom.\n" +
                    "Note: Non-Poison over-time is applied locally (not via the game's built-in PoisonOverTime affliction)."
                );
        try
        {
            _harmony = new Harmony(Info.Metadata.GUID);
            _harmony.PatchAll();
        }
        catch (Exception ex)
        {
            Log.LogError($"Failed to apply Harmony patches: {ex}");
        }

        Log.LogInfo($"Plugin {Name} is loaded!");
    }
}

/// <summary>
/// Patch scorpion behavior.
/// </summary>
[HarmonyPatch]
internal static class ScorpionPatches
{
    private static readonly MethodInfo? SetTargetChar = AccessTools.PropertySetter(typeof(Mob), "targetChar");

    // Mob.Targeting internals (private fields) so we can reproduce vanilla logic when we MUST override.
    private static readonly AccessTools.FieldRef<Mob, bool> AttackingRef = AccessTools.FieldRefAccess<Mob, bool>("attacking");
    private static readonly AccessTools.FieldRef<Mob, float> TimeLastCheckedForTargetRef = AccessTools.FieldRefAccess<Mob, float>("_timeLastCheckedForTarget");
    private static readonly AccessTools.FieldRef<Mob, float> TimeLastSwitchedTargetRef = AccessTools.FieldRefAccess<Mob, float>("_timeLastSwitchedTarget");
    private static readonly AccessTools.FieldRef<Mob, float> TargetSwitchCooldownRef = AccessTools.FieldRefAccess<Mob, float>("targetSwitchCooldown");
    private static readonly AccessTools.FieldRef<Mob, float> TargetCheckCooldownRef = AccessTools.FieldRefAccess<Mob, float>("targetCheckCooldown");
    private static readonly AccessTools.FieldRef<Mob, Character> TargetCharRef = AccessTools.FieldRefAccess<Mob, Character>("_targetChar");
    private static readonly AccessTools.FieldRef<Mob, Vector3> NormalRef = AccessTools.FieldRefAccess<Mob, Vector3>("normal");

    private static readonly AccessTools.FieldRef<Mob, float> AttackCooldownRef = AccessTools.FieldRefAccess<Mob, float>("attackCooldown");
    private static readonly AccessTools.FieldRef<Mob, float> InRangeForAttackTimeRef = AccessTools.FieldRefAccess<Mob, float>("inRangeForAttackTime");
    private static readonly AccessTools.FieldRef<Mob, float> TimeLastAttackedRef = AccessTools.FieldRefAccess<Mob, float>("_timeLastAttacked");
    private static readonly AccessTools.FieldRef<Mob, float> PostAttackRestRef = AccessTools.FieldRefAccess<Mob, float>("postAttackRest");
    private static readonly AccessTools.FieldRef<Mob, float> WhiffRefundRef = AccessTools.FieldRefAccess<Mob, float>("whiffRefund");

    private static readonly MethodInfo? MobInflictAttackMI = AccessTools.Method(typeof(Mob), "InflictAttack");

    private sealed class AttackAnimSyncState
    {
        public bool Initialized;
        public float PrefabAttackTime;
        public float BaseAnimSpeed = 1f;
        public bool TriggeredThisAttack;
    }

    private static readonly ConditionalWeakTable<Mob, AttackAnimSyncState> AttackAnimStates = new ConditionalWeakTable<Mob, AttackAnimSyncState>();

    private static AttackAnimSyncState GetAttackAnimState(Mob mob)
    {
        return AttackAnimStates.GetValue(mob, _ => new AttackAnimSyncState());
    }


    private static bool IsInAttackCooldown(Mob mob)
    {
        return Time.time < TimeLastAttackedRef(mob) + AttackCooldownRef(mob);
    }


    private static readonly AccessTools.FieldRef<Mob, MobItem> MobItemRef = AccessTools.FieldRefAccess<Mob, MobItem>("_mobItem");
    private static readonly AccessTools.FieldRef<Mob, Rigidbody> RigRef = AccessTools.FieldRefAccess<Mob, Rigidbody>("rig");

// Mob state (some builds expose MobState as a non-public nested enum). We avoid referencing the enum type directly
// so the plugin compiles even when MobState isn't publicly visible.
private static readonly FieldInfo? MobStateField = AccessTools.Field(typeof(Mob), "_mobState");
private static readonly MethodInfo? MobStateGetter = AccessTools.PropertyGetter(typeof(Mob), "mobState");

private static bool TryGetMobStateWalking(Mob mob, out bool isWalking)
{
    isWalking = false;

    try
    {
        object? stateObj = null;
        if (MobStateField != null)
        {
            stateObj = MobStateField.GetValue(mob);
        }
        else if (MobStateGetter != null)
        {
            stateObj = MobStateGetter.Invoke(mob, null);
        }

        if (stateObj == null)
        {
            return false;
        }

        // Enum.ToString() yields the member name ("Walking", "Dead", ...)
        isWalking = string.Equals(stateObj.ToString(), "Walking", StringComparison.Ordinal);
        return true;
    }
    catch
    {
        return false;
    }
}


    private static bool InTargetingCooldown(Mob mob)
    {
        float now = Time.time;
        return now < Mathf.Max(
            TimeLastCheckedForTargetRef(mob) + TargetCheckCooldownRef(mob),
            TimeLastSwitchedTargetRef(mob) + TargetSwitchCooldownRef(mob));
    }

    private static Vector3 Center(Mob mob)
    {
        return mob.transform.position + NormalRef(mob) * 0.2f;
    }

    private static bool IsNearCharacterWithinRadius(Mob mob, float radius)
    {
        Vector3 myCenter = Center(mob);
        foreach (Character allCharacter in Character.AllCharacters)
        {
            if (allCharacter != null && Vector3.Distance(myCenter, allCharacter.Center) < radius)
            {
                return true;
            }
        }
        return false;
    }


    private static bool IsHeldAsItem(Mob mob)
    {
        // Scorpions are items in PEAK.
        var item = mob.GetComponent<Item>();
        return item != null && item.itemState == ItemState.Held;
    }

    private static void ClearTarget(Mob mob)
    {
        try
        {
            SetTargetChar?.Invoke(mob, new object?[] { null });
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"Failed to clear scorpion target (safe to ignore): {ex.Message}");
        }
    }

    [HarmonyPatch(typeof(Mob), "Start")]
    [HarmonyPostfix]
    private static void MobStart_Postfix(Mob __instance)
    {
        if (__instance is not Scorpion)
        {
            return;
        }

        // Time until sting (vanilla = whatever the prefab uses)
        float overrideDelay = Plugin.CfgStingDelaySeconds.Value;
        if (overrideDelay >= 0f)
        {
            __instance.attackTime = overrideDelay;
        }

        float moveMult = Mathf.Max(0f, Plugin.CfgMovementSpeedMultiplier.Value);
        if (!Mathf.Approximately(moveMult, 1f))
        {
            __instance.movementSpeed *= moveMult;
        }

        float turnMult = Mathf.Max(0f, Plugin.CfgTurnRateMultiplier.Value);
        if (!Mathf.Approximately(turnMult, 1f))
        {
            __instance.turnRate *= turnMult;
        }

        // Attack tuning overrides (defaults keep vanilla)
        float atkStart = Plugin.CfgAttackStartDistance.Value;
        if (atkStart >= 0f)
        {
            __instance.attackStartDistance = atkStart;
        }

        float atkDist = Plugin.CfgAttackDistance.Value;
        if (atkDist >= 0f)
        {
            __instance.attackDistance = atkDist;
        }

        float atkAngle = Plugin.CfgAttackAngle.Value;
        if (atkAngle >= 0f)
        {
            __instance.attackAngle = atkAngle;
        }

        float atkCd = Plugin.CfgAttackCooldownSeconds.Value;
        if (atkCd >= 0f)
        {
            AttackCooldownRef(__instance) = atkCd;
        }
    }


    // Held sting timing (fix): when HeldStingTimeMultiplier is configured and the scorpion is HELD,
    // we run a minimal custom Attacking() implementation so the *visual sting sequence* (attack animation trigger)
    // lines up with the delayed/accelerated damage timing. Otherwise we run the game's original method.

    [HarmonyPatch(typeof(Mob), "Attacking")]
    [HarmonyPrefix]
    private static bool MobAttacking_Prefix(Mob __instance)
    {
        if (__instance is not Scorpion)
        {
            return true;
        }

        float mult = Plugin.CfgHeldStingTimeMultiplier.Value;
        bool held = IsHeldAsItem(__instance);

        // Only override when it's actually needed (held + multiplier != 1).
        if (!held || Mathf.Approximately(mult, 1f))
        {
            // Safety: ensure we don't leave animator speed modified.
            var st = GetAttackAnimState(__instance);
            if (st.Initialized && __instance.anim != null)
            {
                __instance.anim.speed = st.BaseAnimSpeed;
            }
            st.TriggeredThisAttack = false;
            return true;
        }

        var state = GetAttackAnimState(__instance);
        if (!state.Initialized)
        {
            // Snapshot the prefab defaults (attack animation is authored around these values).
            state.PrefabAttackTime = Mathf.Max(0.01f, __instance.attackTime);
            state.BaseAnimSpeed = (__instance.anim != null) ? __instance.anim.speed : 1f;
            state.Initialized = true;
        }

        Character? target = TargetCharRef(__instance);
        if (target != null)
        {
            // Start attack (vanilla behavior) if we're close enough and not in cooldown.
            if (!AttackingRef(__instance))
            {
                if (__instance.distanceToTarget < __instance.attackStartDistance && !IsInAttackCooldown(__instance) && __instance.photonView.IsMine)
                {
                    __instance.photonView.RPC("RPC_StartAttack", RpcTarget.All);
                }

                InRangeForAttackTimeRef(__instance) = 0f;
                state.TriggeredThisAttack = false;

                if (!AttackingRef(__instance))
                {
                    // Still not attacking (cooldown / not mine / etc.)
                    return false;
                }
            }

            float timer = InRangeForAttackTimeRef(__instance);

            // Effective timing while held.
            float effectiveAttackTime = Mathf.Max(0f, __instance.attackTime * mult);
            float animDuration = Mathf.Max(0.01f, state.PrefabAttackTime);

            // Delay the *start* of the sting animation so the damage still lands at the end of the animation.
            // If the effective attack time is shorter than the animation duration, we trigger immediately and speed the anim up.
            float triggerDelay = Mathf.Max(0f, effectiveAttackTime - animDuration);

            float dt = Time.deltaTime;
            float nextTimer = timer + dt;

            // Trigger the attack animation once, when we cross triggerDelay.
            if (!state.TriggeredThisAttack && nextTimer >= triggerDelay)
            {
                if (__instance.anim != null)
                {
                    float desiredSpeed = state.BaseAnimSpeed;

                    // If the effective attack time is shorter than the authored animation duration, speed up to fit.
                    if (effectiveAttackTime > 0.01f && effectiveAttackTime < animDuration)
                    {
                        desiredSpeed = state.BaseAnimSpeed * (animDuration / effectiveAttackTime);
                    }

                    __instance.anim.speed = desiredSpeed;
                    __instance.anim.SetTrigger("Attack");
                }

                state.TriggeredThisAttack = true;
            }
            else
            {
                // While waiting (before the sting starts), keep animation speed at its baseline.
                if (__instance.anim != null)
                {
                    __instance.anim.speed = state.BaseAnimSpeed;
                }
            }

            InRangeForAttackTimeRef(__instance) = nextTimer;

            // Apply sting when the effective windup is over.
            if (nextTimer > effectiveAttackTime)
            {
                if (__instance.distanceToTarget < __instance.attackDistance
                    && Vector3.Angle((__instance.transform.forward + Vector3.up).normalized, (target.Center - __instance.transform.position).normalized) < __instance.attackAngle)
                {
                    // Protected virtual call via reflection so Scorpion override runs.
                    MobInflictAttackMI?.Invoke(__instance, new object[] { target });
                    TimeLastAttackedRef(__instance) = Time.time;
                }
                else
                {
                    float whiffRefund = Mathf.Clamp01(WhiffRefundRef(__instance));
                    float atkCd = AttackCooldownRef(__instance);
                    float rest = PostAttackRestRef(__instance);
                    TimeLastAttackedRef(__instance) = Time.time - whiffRefund * Mathf.Max(atkCd, rest);
                }

                AttackingRef(__instance) = false;
                state.TriggeredThisAttack = false;

                if (__instance.anim != null)
                {
                    __instance.anim.speed = state.BaseAnimSpeed;
                }
            }
        }
        else
        {
            // No target – vanilla resets the timer.
            InRangeForAttackTimeRef(__instance) = 0f;
            state.TriggeredThisAttack = false;

            if (__instance.anim != null)
            {
                __instance.anim.speed = state.BaseAnimSpeed;
            }
        }

        return false; // skip original
    }
    [HarmonyPatch(typeof(Mob), "TestSleepMode")]
    [HarmonyPrefix]
    private static bool MobTestSleepMode_Prefix(Mob __instance)
    {
        if (__instance is not Scorpion)
        {
            return true;
        }

        float sleepMult = Mathf.Max(0f, Plugin.CfgSleepDistanceMultiplier.Value);
        float wakeMult = Mathf.Max(0f, Plugin.CfgWakeRadiusMultiplier.Value);

        // If both multipliers are vanilla, run the game's original method for maximum fidelity.
        if (Mathf.Approximately(sleepMult, 1f) && Mathf.Approximately(wakeMult, 1f))
        {
            return true;
        }

        float baseDist = __instance.sleepDistance;
        float sleepRadius = baseDist * sleepMult;
        float wakeRadius = baseDist * wakeMult;

        if (__instance.sleeping)
        {
            if (IsNearCharacterWithinRadius(__instance, wakeRadius))
            {
                __instance.sleeping = false;
                __instance.UpdateSleeping();
            }
        }
        else
        {
            MobItem? mobItem = MobItemRef(__instance);
            Rigidbody? rig = RigRef(__instance);

if (!TryGetMobStateWalking(__instance, out bool isWalking))
{
    // If we can't read the mob state (build differences), fall back to vanilla for safety.
    return true;
}

            if (isWalking
                && (mobItem == null || mobItem.itemState == ItemState.Ground)
                && rig != null && rig.linearVelocity.magnitude < 1f
                && !IsNearCharacterWithinRadius(__instance, sleepRadius))
            {
                __instance.sleeping = true;
                __instance.UpdateSleeping();
            }
        }

        return false; // skip original
    }

[HarmonyPatch(typeof(Mob), "Targeting")]
    [HarmonyPrefix]
    private static bool MobTargeting_Prefix(Mob __instance, ref float __state)
    {
        __state = float.NaN;

        if (__instance is not Scorpion)
        {
            return true;
        }

        bool held = IsHeldAsItem(__instance);

        // Only aggressive when held.
        if (Plugin.CfgOnlyAggressiveWhenHeld.Value && !held)
        {
            ClearTarget(__instance);
            return false;
        }

        // Disable target acquisition towards players unless held.
        if (!Plugin.CfgEnablePlayerTargeting.Value && !held)
        {
            ClearTarget(__instance);
            return false;
        }

        float mult = Mathf.Max(0f, Plugin.CfgAggroDistanceMultiplier.Value);
        bool needsAggroOverride = !Mathf.Approximately(mult, 1f);
        bool needsLosOverride = !Plugin.CfgRequireLineOfSight.Value;
        bool needsPoisonedOverride = Plugin.CfgTargetPoisonedPlayers.Value;

        // If all targeting-related options are vanilla, run the game's original method for maximum fidelity.
        if (!needsAggroOverride && !needsLosOverride && !needsPoisonedOverride)
        {
            return true;
        }

        // If we're ONLY changing aggro range, run vanilla Targeting() with a temporarily scaled aggroDistance.
        if (needsAggroOverride && !needsLosOverride && !needsPoisonedOverride)
        {
            __state = __instance.aggroDistance;
            __instance.aggroDistance = __state * mult;
            return true;
        }

        // Otherwise, we must re-implement the targeting loop to change LoS / poisoned targeting.
        if (InTargetingCooldown(__instance) || (AttackingRef(__instance) && TargetCharRef(__instance) != null))
        {
            return false;
        }

        TimeLastCheckedForTargetRef(__instance) = Time.time;

        Character? bestChar = null;
        float bestDist = __instance.aggroDistance * mult;

        bool requireLos = Plugin.CfgRequireLineOfSight.Value;
        bool targetPoisoned = Plugin.CfgTargetPoisonedPlayers.Value;

        Vector3 myCenter = Center(__instance);
        Character? forced = __instance.forcedCharacterTarget;

        foreach (Character c in Character.AllCharacters)
        {
            if (!c.data.fullyConscious)
            {
                continue;
            }

            if (forced != null && c != forced)
            {
                continue;
            }

            float d = Vector3.Distance(__instance.transform.position, c.Center);
            if (d >= bestDist)
            {
                continue;
            }

            if (!targetPoisoned && c.refs.afflictions.HasAfflictionType(Affliction.AfflictionType.PoisonOverTime, out _))
            {
                continue;
            }

            if (requireLos && HelperFunctions.LineCheck(myCenter, c.Center, HelperFunctions.LayerType.TerrainMap).transform)
            {
                continue;
            }

            bestDist = d;
            bestChar = c;
        }

        try
        {
            SetTargetChar?.Invoke(__instance, new object?[] { bestChar });
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"Failed to set scorpion target (safe to ignore): {ex.Message}");
        }

        return false; // skip original
    }

    
    [HarmonyPatch(typeof(Mob), "Targeting")]
    [HarmonyPostfix]
    private static void MobTargeting_Postfix(Mob __instance, float __state)
    {
        // Restore aggroDistance if we temporarily scaled it.
        if (__instance is not Scorpion)
        {
            return;
        }

        if (float.IsNaN(__state))
        {
            return;
        }

        __instance.aggroDistance = __state;
    }

    [HarmonyPatch(typeof(Scorpion), "InflictAttack")]

    [HarmonyPrefix]
    private static bool ScorpionInflictAttack_Prefix(Scorpion __instance, Character character)
    {
        // Vanilla behavior is fully preserved unless the user changes config values.
        bool vanillaDamageType = Plugin.CfgDamageType.Value == Plugin.DamageTypeOverride.Vanilla ||
                                Plugin.CfgDamageType.Value == Plugin.DamageTypeOverride.Poison;
        bool vanillaGlobalMultiplier = Mathf.Approximately(Plugin.CfgDamageMultiplier.Value, 1f);
        bool vanillaInitialEnabled = Plugin.CfgEnableInitialDamageTick.Value;
        bool vanillaOvertimeEnabled = Plugin.CfgEnableOvertimeDamageTick.Value;
        bool vanillaInitialMultiplier = Mathf.Approximately(Plugin.CfgInitialDamageMultiplier.Value, 1f);
        bool vanillaOvertimeMultiplier = Mathf.Approximately(Plugin.CfgOvertimeDamageMultiplier.Value, 1f);

        bool isVanilla = vanillaDamageType
                         && vanillaGlobalMultiplier
                         && vanillaInitialEnabled
                         && vanillaOvertimeEnabled
                         && vanillaInitialMultiplier
                         && vanillaOvertimeMultiplier;

        if (isVanilla)
        {
            return true; // run the game's original method
        }

        if (!character.IsLocal)
        {
            return false; // match vanilla: only the local player applies the status effect
        }

        float globalMult = Mathf.Max(0f, Plugin.CfgDamageMultiplier.Value);
        float initialMult = Mathf.Max(0f, Plugin.CfgInitialDamageMultiplier.Value);
        float overtimeMult = Mathf.Max(0f, Plugin.CfgOvertimeDamageMultiplier.Value);

        bool enableInitial = Plugin.CfgEnableInitialDamageTick.Value;
        bool enableOvertime = Plugin.CfgEnableOvertimeDamageTick.Value;

        float initialScale = globalMult * initialMult;
        float overtimeScale = globalMult * overtimeMult;

        CharacterAfflictions.STATUSTYPE statusType = Plugin.CfgDamageType.Value == Plugin.DamageTypeOverride.Vanilla
            ? CharacterAfflictions.STATUSTYPE.Poison
            : (CharacterAfflictions.STATUSTYPE)(int)Plugin.CfgDamageType.Value;

        // Match the game's scaling logic.
        float num = 1f - character.refs.afflictions.statusSum;
        float num2 = Mathf.Max(0.5f, num + 0.05f);

        float duration = __instance.totalPoisonTime;

        float initialStatus = enableInitial ? (0.025f * initialScale) : 0f;
        float statusPerSecond = enableOvertime
            ? ((num2 / Mathf.Max(0.01f, duration)) * overtimeScale)
            : 0f;

        // Apply the initial sting.
        if (initialStatus > 0f)
        {
            character.refs.afflictions.AddStatus(statusType, initialStatus);
        }

        // Apply over-time portion.
        if (duration > 0f && statusPerSecond > 0f)
        {
            if (statusType == CharacterAfflictions.STATUSTYPE.Poison)
            {
                // Keep vanilla affliction type for Poison so game logic (and networking) stays consistent.
                character.refs.afflictions.AddAffliction(new Affliction_PoisonOverTime(duration, 0f, statusPerSecond));
            }
            else
            {
                // For non-poison types we can't use vanilla afflictions without changing global behavior.
                // Instead, apply the effect locally; status increments will still network-sync normally.
                var runner = character.gameObject.GetComponent<ScorpionCustomDotRunner>();
                if (runner == null)
                {
                    runner = character.gameObject.AddComponent<ScorpionCustomDotRunner>();
                }
                runner.Stack(statusType, statusPerSecond, duration);
            }
        }

        // Keep vanilla knockback.
        character.AddForceAtPosition(500f * __instance.mesh.forward, __instance.transform.position, 2f);

        return false; // skip original
    }
}

/// <summary>
/// Local-only DoT runner used when the configured damage type is not Poison.
/// This intentionally does not participate in the game's Affliction serialization.
/// </summary>
internal sealed class ScorpionCustomDotRunner : MonoBehaviour
{
    private Character? _character;
    private CharacterAfflictions.STATUSTYPE _statusType;
    private float _statusPerSecond;
    private float _timeRemaining;

    private void Awake()
    {
        _character = GetComponent<Character>();
        enabled = false;
    }

    public void Stack(CharacterAfflictions.STATUSTYPE statusType, float statusPerSecond, float duration)
    {
        _statusType = statusType;
        _statusPerSecond = Mathf.Max(_statusPerSecond, statusPerSecond);
        _timeRemaining += duration;
        enabled = true;
    }

    private void Update()
    {
        if (_character == null || !_character.IsLocal)
        {
            // Only the local player should be applying their own status ticks.
            enabled = false;
            return;
        }

        if (_timeRemaining <= 0f || _statusPerSecond <= 0f)
        {
            Destroy(this);
            return;
        }

        float dt = Time.deltaTime;
        _timeRemaining -= dt;
        _character.refs.afflictions.AddStatus(_statusType, _statusPerSecond * dt);
    }
}