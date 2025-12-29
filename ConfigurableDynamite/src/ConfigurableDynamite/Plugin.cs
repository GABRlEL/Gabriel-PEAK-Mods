using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace ConfigurableDynamite;

// This BepInAutoPlugin attribute comes from the Hamunii.BepInEx.AutoPlugin
// NuGet package, and it will generate the BepInPlugin attribute for you!
// For more info, see https://github.com/Hamunii/BepInEx.AutoPlugin
[BepInAutoPlugin]
public partial class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; } = null!;

    // Config
    internal static ConfigEntry<bool> AutoFuseEnabled { get; private set; } = null!;
    internal static ConfigEntry<float> AutoFuseDistance { get; private set; } = null!;
    internal static ConfigEntry<float> FuseLengthSeconds { get; private set; } = null!;
    internal static ConfigEntry<bool> FuseOnUse { get; private set; } = null!;
    internal static ConfigEntry<bool> FuseOnThrow { get; private set; } = null!;
    internal static ConfigEntry<float> DamageMultiplier { get; private set; } = null!;
    internal static ConfigEntry<float> RangeMultiplier { get; private set; } = null!;

    // Config helpers (tri-state overrides for "keep vanilla" defaults)
    internal enum OverrideToggle
    {
        Vanilla = 0,
        Enabled = 1,
        Disabled = 2,
    }

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

    internal static ConfigEntry<DamageTypeOverride> DynamiteDamageType { get; private set; } = null!;

    internal static ConfigEntry<bool> UnlitDynamiteCanBePocketed { get; private set; } = null!;
    internal static ConfigEntry<OverrideToggle> ItemCooking { get; private set; } = null!;
    internal static ConfigEntry<OverrideToggle> ItemLaunching { get; private set; } = null!;
    internal static ConfigEntry<float> ItemLaunchDistanceMultiplier { get; private set; } = null!;

    private Harmony? _harmony;

    // Used to mark AOE components spawned by Dynamite explosions.
    private static bool _spawningDynamiteExplosion;

    private void Awake()
    {
        Log = Logger;

        // -------- Config (defaults match vanilla) --------
        AutoFuseEnabled = Config.Bind(
            "Fuse",
            "AutoFuseEnabled",
            true,
            "Vanilla behaviour is TRUE: the dynamite fuse lights automatically when a player gets close.\n" +
            "Set to FALSE to disable proximity ignition."
        );

        AutoFuseDistance = Config.Bind(
            "Fuse",
            "AutoFuseDistance",
            -1f,
            "Distance (in Unity units) from a player to a dynamite for the fuse to light when AutoFuseEnabled is TRUE.\n" +
            "Set to -1 to keep vanilla distance."
        );

        FuseLengthSeconds = Config.Bind(
            "Fuse",
            "FuseLengthSeconds",
            -1f,
            "Fuse length in seconds.\n" +
            "Set to -1 to keep vanilla fuse length."
        );

        FuseOnUse = Config.Bind(
            "Fuse",
            "StartFuseOnUse",
            false,
            "When AutoFuseEnabled is FALSE, start the fuse when the item is used (primary use)."
        );

        FuseOnThrow = Config.Bind(
            "Fuse",
            "StartFuseOnThrow",
            false,
            "When AutoFuseEnabled is FALSE, start the fuse when the item is thrown."
        );

        DamageMultiplier = Config.Bind(
            "Explosion",
            "DamageMultiplier",
            1f,
            "Multiplier applied to dynamite explosion damage (AOE.statusAmount).\n" +
            "1 = vanilla."
        );

        RangeMultiplier = Config.Bind(
            "Explosion",
            "RangeMultiplier",
            1f,
            "Multiplier applied to dynamite explosion range (AOE.range).\n" +
            "1 = vanilla."
        );

                DynamiteDamageType = Config.Bind(
            "Explosion",
            "DamageType",
            DamageTypeOverride.Vanilla,
            "Controls the status/damage type dynamite applies (AOE.statusType). Vanilla keeps the game's original setting.\n" +
            "Cold = freeze, Hot = burn, Spores = shroom."
        );

        UnlitDynamiteCanBePocketed = Config.Bind(
            "Item",
            "UnlitDynamiteCanBePocketed",
            false,
            "If TRUE, unlit dynamite can be pocketed. Lit dynamite remains unpocketable. Default FALSE keeps vanilla behaviour."
        );

        ItemCooking = Config.Bind(
            "Explosion",
            "ItemCooking",
            OverrideToggle.Vanilla,
            "Controls whether dynamite explosions cook items via AOE.cooksItems. Vanilla keeps the game's original setting."
        );

        ItemLaunching = Config.Bind(
            "Explosion",
            "ItemLaunching",
            OverrideToggle.Vanilla,
            "Controls whether dynamite explosions can launch items via AOE.canLaunchItems. Vanilla keeps the game's original setting."
        );

        ItemLaunchDistanceMultiplier = Config.Bind(
            "Explosion",
            "ItemLaunchDistanceMultiplier",
            1f,
            "Multiplier applied to dynamite item-launch strength (AOE.itemKnockbackMultiplier). 1 = vanilla."
        );

        // Hooks
        _harmony = new Harmony(Info.Metadata.GUID);
        _harmony.PatchAll();

        Log.LogInfo($"Plugin {Name} is loaded!");
    }

    private void OnDestroy()
    {
        try
        {
            _harmony?.UnpatchSelf();
        }
        catch
        {
            // ignored
        }
    }

    private static void TryLightFuse(Dynamite dynamite)
    {
        // Guard against re-lighting.
        if (dynamite.GetData<BoolItemData>(DataEntryKey.FlareActive).Value)
        {
            return;
        }

        dynamite.LightFlare();
    }

    private static bool HasFuseOverride(out float fuseSeconds)
    {
        fuseSeconds = FuseLengthSeconds.Value;
        return fuseSeconds > 0f;
    }

    private static bool HasExplosionOverrides()
    {
        return !Mathf.Approximately(DamageMultiplier.Value, 1f) ||
               !Mathf.Approximately(RangeMultiplier.Value, 1f) ||
               ItemCooking.Value != OverrideToggle.Vanilla ||
               ItemLaunching.Value != OverrideToggle.Vanilla ||
               DynamiteDamageType.Value != DamageTypeOverride.Vanilla ||
               !Mathf.Approximately(ItemLaunchDistanceMultiplier.Value, 1f);
    }

    private static void ApplyFuseConfig(Dynamite dynamite)
    {
        // Proximity ignition toggle + distance override
        if (!AutoFuseEnabled.Value)
        {
            // Easiest way to disable vanilla proximity ignition without a private-method patch.
            dynamite.lightFuseRadius = 0f;
        }
        else if (AutoFuseDistance.Value > 0f)
        {
            dynamite.lightFuseRadius = AutoFuseDistance.Value;
        }

        // Fuse length override
        if (HasFuseOverride(out float fuseSeconds))
        {
            dynamite.startingFuseTime = fuseSeconds;
        }
    }
    private static void ApplyPocketConfig(Dynamite dynamite)
    {
        if (!UnlitDynamiteCanBePocketed.Value)
        {
            return;
        }

        if (dynamite?.item?.UIData == null)
        {
            return;
        }

        bool lit = dynamite.GetData<BoolItemData>(DataEntryKey.FlareActive).Value;
        dynamite.item.UIData.canPocket = !lit;
    }


    private static void EnsureFullFuseIfNotLit(Dynamite dynamite)
    {
        if (!HasFuseOverride(out float fuseSeconds))
        {
            return;
        }

        // Only reset when not currently burning.
        if (dynamite.GetData<BoolItemData>(DataEntryKey.FlareActive).Value)
        {
            return;
        }

        FloatItemData fuel = dynamite.GetData(DataEntryKey.Fuel, () => new FloatItemData { Value = fuseSeconds });
        fuel.Value = fuseSeconds;
    }

    private static void ApplyExplosionConfig(AOE aoe)
    {
        if (!HasExplosionOverrides())
        {
            return;
        }

        // Apply once per AOE instance.
        DynamiteExplosionAoeMarker marker = aoe.gameObject.GetComponent<DynamiteExplosionAoeMarker>() ??
                                            aoe.gameObject.AddComponent<DynamiteExplosionAoeMarker>();

        if (marker.Applied)
        {
            return;
        }

        // Mark as dynamite explosion AOE (in case another patch wants to check).
        marker.IsDynamiteExplosion = true;
        marker.Applied = true;

        if (!Mathf.Approximately(RangeMultiplier.Value, 1f))
        {
            aoe.range *= RangeMultiplier.Value;
        }

        if (!Mathf.Approximately(DamageMultiplier.Value, 1f))
        {
            aoe.statusAmount *= DamageMultiplier.Value;
        }

        if (DynamiteDamageType.Value != DamageTypeOverride.Vanilla)
        {
            // Force use of STATUSTYPE rather than any prefab-provided illegalStatus.
            aoe.illegalStatus = "";
            aoe.statusType = (CharacterAfflictions.STATUSTYPE)(int)DynamiteDamageType.Value;
        }

        if (ItemCooking.Value != OverrideToggle.Vanilla)
        {
            aoe.cooksItems = ItemCooking.Value == OverrideToggle.Enabled;
        }

        if (ItemLaunching.Value != OverrideToggle.Vanilla)
        {
            aoe.canLaunchItems = ItemLaunching.Value == OverrideToggle.Enabled;
        }

        if (!Mathf.Approximately(ItemLaunchDistanceMultiplier.Value, 1f))
        {
            aoe.itemKnockbackMultiplier *= ItemLaunchDistanceMultiplier.Value;
        }
    }

    private sealed class DynamiteExplosionAoeMarker : MonoBehaviour
    {
        public bool IsDynamiteExplosion;
        public bool Applied;
    }

    

    private sealed class PendingThrowFuseMarker : MonoBehaviour
    {
        // Marker component used to indicate "this item was just thrown" so we can ignite
        // AFTER instance data has been applied (SetItemInstanceDataRPC), otherwise the sync can overwrite FlareActive.
    }

    private sealed class PendingUseFuseMarker : MonoBehaviour
    {
        // Marker component used to indicate "this item started a primary use" so we can ignite
        // AFTER the use timer/cast completes (FinishCastPrimary), not immediately on button press.
    }
// ---------------- Harmony patches ----------------

    [HarmonyPatch(typeof(Dynamite), nameof(Dynamite.Awake))]
    private static class Dynamite_Awake_Patch
    {
        private static void Postfix(Dynamite __instance)
        {
            try
            {
                ApplyFuseConfig(__instance);
            
                ApplyPocketConfig(__instance);
            }
            catch (Exception e)
            {
                Log.LogError(e);
            }
        }
    }
    [HarmonyPatch(typeof(Dynamite), nameof(Dynamite.SetFlareLitRPC))]
    private static class Dynamite_SetFlareLitRPC_Patch
    {
        private static void Postfix(Dynamite __instance)
        {
            try
            {
                ApplyPocketConfig(__instance);
            }
            catch (Exception e)
            {
                Log.LogError(e);
            }
        }
    }


    [HarmonyPatch(typeof(Dynamite), nameof(Dynamite.OnInstanceDataSet))]
    private static class Dynamite_OnInstanceDataSet_Patch
    {
        private static void Postfix(Dynamite __instance)
        {
            try
            {
                // If we're overriding fuse length, ensure the stored fuel matches (for newly spawned or synced items).
                EnsureFullFuseIfNotLit(__instance);
            }
            catch (Exception e)
            {
                Log.LogError(e);
            }
        }
    }

    // When throwing an item, the game calls RPC_SetThrownData first, then applies item instance data (SetItemInstanceDataRPC).
    // If we ignite the fuse during RPC_SetThrownData, the subsequent instance-data sync can overwrite FlareActive back to false.
    // We therefore mark the item here and ignite after SetItemInstanceDataRPC runs.
    [HarmonyPatch(typeof(Item), nameof(Item.RPC_SetThrownData))]
    private static class Item_RPC_SetThrownData_Patch
    {
        private static void Postfix(Item __instance)
        {
            try
            {
                if (AutoFuseEnabled.Value)
                {
                    // These options are meant for the "manual ignition" mode.
                    return;
                }

                if (!FuseOnThrow.Value)
                {
                    return;
                }

                if (__instance == null)
                {
                    return;
                }

                Dynamite? dynamite = __instance.GetComponent<Dynamite>();
                if (dynamite == null)
                {
                    return;
                }

                if (__instance.gameObject.GetComponent<PendingThrowFuseMarker>() == null)
                {
                    __instance.gameObject.AddComponent<PendingThrowFuseMarker>();
                }
            }
            catch (Exception e)
            {
                Log.LogError(e);
            }
        }
    }

    [HarmonyPatch(typeof(Item), nameof(Item.SetItemInstanceDataRPC))]
    private static class Item_SetItemInstanceDataRPC_Patch
    {
        private static void Postfix(Item __instance)
        {
            try
            {
                PendingThrowFuseMarker? marker = __instance?.gameObject?.GetComponent<PendingThrowFuseMarker>();
                if (marker == null)
                {
                    return;
                }

                // Remove marker first so we don't retry on subsequent syncs.
                UnityEngine.Object.Destroy(marker);

                if (AutoFuseEnabled.Value || !FuseOnThrow.Value)
                {
                    return;
                }

                Dynamite? dynamite = __instance.GetComponent<Dynamite>();
                if (dynamite == null)
                {
                    return;
                }

                TryLightFuse(dynamite);
            }
            catch (Exception e)
            {
                Log.LogError(e);
            }
        }
    }


    [HarmonyPatch(typeof(Item), nameof(Item.StartUsePrimary))]
    private static class Item_StartUsePrimary_Patch
    {
        private static void Postfix(Item __instance)
        {
            try
            {
                if (AutoFuseEnabled.Value)
                {
                    // Only intended to be used when AutoFuse is disabled.
                    return;
                }

                if (!FuseOnUse.Value)
                {
                    return;
                }

                if (__instance == null)
                {
                    return;
                }

                Dynamite? dynamite = __instance.GetComponent<Dynamite>();
                if (dynamite == null)
                {
                    return;
                }

                // Don't light immediately: wait for the use cast to finish (FinishCastPrimary).
                if (__instance.gameObject.GetComponent<PendingUseFuseMarker>() == null)
                {
                    __instance.gameObject.AddComponent<PendingUseFuseMarker>();
                }
            }
            catch (Exception e)
            {
                Log.LogError(e);
            }
        }
    }

    // FinishCastPrimary fires when the primary-use timer reaches 100%.
    // We ignite here so "StartFuseOnUse" respects the use progress bar / cast time.
    [HarmonyPatch(typeof(Item), "FinishCastPrimary")]
    private static class Item_FinishCastPrimary_Patch
    {
        private static void Postfix(Item __instance)
        {
            try
            {
                PendingUseFuseMarker? marker = __instance?.gameObject?.GetComponent<PendingUseFuseMarker>();
                if (marker == null)
                {
                    return;
                }

                // Remove marker first so we don't retry.
                UnityEngine.Object.Destroy(marker);

                if (AutoFuseEnabled.Value || !FuseOnUse.Value)
                {
                    return;
                }

                Dynamite? dynamite = __instance.GetComponent<Dynamite>();
                if (dynamite == null)
                {
                    return;
                }

                TryLightFuse(dynamite);
            }
            catch (Exception e)
            {
                Log.LogError(e);
            }
        }
    }

    // If the player cancels use before the cast finishes, clear the pending marker.
    [HarmonyPatch(typeof(Item), nameof(Item.CancelUsePrimary))]
    private static class Item_CancelUsePrimary_Patch
    {
        private static void Postfix(Item __instance)
        {
            try
            {
                PendingUseFuseMarker? marker = __instance?.gameObject?.GetComponent<PendingUseFuseMarker>();
                if (marker != null)
                {
                    UnityEngine.Object.Destroy(marker);
                }
            }
            catch (Exception e)
            {
                Log.LogError(e);
            }
        }
    }

    // Toggle a "we are spawning a dynamite explosion" flag while RPC_Explode instantiates the explosion prefab.
    [HarmonyPatch(typeof(Dynamite), "RPC_Explode")]
    private static class Dynamite_RPC_Explode_Patch
    {
        private static void Prefix()
        {
            if (HasExplosionOverrides())
            {
                _spawningDynamiteExplosion = true;
            }
        }

        private static void Finalizer(Exception? __exception)
        {
            _spawningDynamiteExplosion = false;

            if (__exception != null)
            {
                Log.LogError(__exception);
            }
        }
    }

    [HarmonyPatch(typeof(AOE), "OnEnable")]
    private static class AOE_OnEnable_Patch
    {
        private static void Prefix(AOE __instance)
        {
            try
            {
                if (!_spawningDynamiteExplosion)
                {
                    return;
                }

                ApplyExplosionConfig(__instance);
            }
            catch (Exception e)
            {
                Log.LogError(e);
            }
        }
    }

    [HarmonyPatch(typeof(AOE), nameof(AOE.Explode))]
    private static class AOE_Explode_Patch
    {
        private static void Prefix(AOE __instance)
        {
            try
            {
                // If this AOE was spawned during a dynamite explosion, it will already have a marker from OnEnable.
                DynamiteExplosionAoeMarker? marker = __instance.gameObject.GetComponent<DynamiteExplosionAoeMarker>();
                if (marker == null || !marker.IsDynamiteExplosion)
                {
                    return;
                }

                ApplyExplosionConfig(__instance);
            }
            catch (Exception e)
            {
                Log.LogError(e);
            }
        }
    }
}
