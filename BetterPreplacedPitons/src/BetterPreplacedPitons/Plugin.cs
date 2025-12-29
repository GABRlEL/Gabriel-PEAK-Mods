﻿using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Photon.Pun;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace BetterPreplacedPitons;

[BepInAutoPlugin]
public partial class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; } = null!;

    // Enable/disable applying any modifications at all
    internal static ConfigEntry<bool> EnablePreplacedPitons { get; private set; } = null!;
    internal static ConfigEntry<bool> EnableMesaPickaxes { get; private set; } = null!;

    // Speed multipliers: <= 0 => unbreakable, 1 => vanilla, 0.5 => 50% slower, 2 => 2x faster
    internal static ConfigEntry<float> PreplacedPitonBreakSpeedMultiplier { get; private set; } = null!;
    internal static ConfigEntry<float> MesaPickaxeBreakSpeedMultiplier { get; private set; } = null!;

    // Multiplayer safety switch:
    // false (default): respect networked break/detach events (safe, consistent)
    // true: ignore those RPCs client-side (desync-by-design)
    internal static ConfigEntry<bool> ClientSidePersistence { get; private set; } = null!;

    private Harmony? _harmony;

    private void Awake()
    {
        Log = Logger;

        EnablePreplacedPitons = Config.Bind(
            "General",
            "EnablePreplacedPitons",
            true,
            "If true, applies the configured break-speed behavior to preplaced rope pitons (BreakableRopeAnchor)."
        );

        PreplacedPitonBreakSpeedMultiplier = Config.Bind(
            "General",
            "PreplacedPitonBreakSpeedMultiplier",
            -1f,
            "Break-speed multiplier for preplaced rope pitons.\n" +
            "  <= 0 : unbreakable (default)\n" +
            "   1.0 : vanilla\n" +
            "   0.5 : 50% slower (lasts 2x longer)\n" +
            "   2.0 : 2x faster"
        );

        EnableMesaPickaxes = Config.Bind(
            "General",
            "EnableMesaPickaxes",
            true,
            "If true, applies the configured break-speed behavior to mesa pickaxes (ShittyPiton)."
        );

        MesaPickaxeBreakSpeedMultiplier = Config.Bind(
            "General",
            "MesaPickaxeBreakSpeedMultiplier",
            -1f,
            "Break-speed multiplier for mesa pickaxes (ShittyPiton). Same meaning as the piton multiplier."
        );

        ClientSidePersistence = Config.Bind(
            "Multiplayer",
            "ClientSidePersistence",
            false,
            "OFF (default) = multiplayer-safe: this mod respects break/detach RPCs from the host/owner.\n" +
            "ON = client-side persistence: ignore host break/detach RPCs locally and keep those objects alive on your client.\n" +
            "⚠ This will desync you from other players on purpose."
        );

        _harmony = new Harmony(Info.Metadata.GUID);
        _harmony.PatchAll();

        // Apply to already-existing instances (cheap + helps if plugin reloads mid-session)
        BreakableRopeAnchorPatches.ApplyToAllExisting();
        ShittyPitonPatches.ApplyToAllExisting();

        Log.LogInfo($"Loaded {Name}! " +
                    $"Pitons: enabled={EnablePreplacedPitons.Value}, mult={PreplacedPitonBreakSpeedMultiplier.Value}. " +
                    $"Pickaxes: enabled={EnableMesaPickaxes.Value}, mult={MesaPickaxeBreakSpeedMultiplier.Value}. " +
                    $"ClientSidePersistence={ClientSidePersistence.Value}");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
        _harmony = null;
    }

    internal static float GetMultiplier(bool enabled, float mult)
    {
        if (!enabled) return float.NaN; // sentinel: "do not touch"
        if (float.IsNaN(mult) || float.IsInfinity(mult)) return -1f;
        if (mult <= 0f) return -1f;     // unbreakable
        return mult;
    }
}

#region Preplaced rope pitons (BreakableRopeAnchor)

[HarmonyPatch(typeof(BreakableRopeAnchor))]
internal static class BreakableRopeAnchorPatches
{
    private static FieldInfo? _willBreakInTime;
    private static FieldInfo? _photonView;
    private static FieldInfo? _anchor;
    private static FieldInfo? _isBreaking;

    private static void EnsureFields()
    {
        _willBreakInTime ??= AccessTools.Field(typeof(BreakableRopeAnchor), "willBreakInTime");
        _photonView ??= AccessTools.Field(typeof(BreakableRopeAnchor), "photonView");
        _anchor ??= AccessTools.Field(typeof(BreakableRopeAnchor), "anchor");
        _isBreaking ??= AccessTools.Field(typeof(BreakableRopeAnchor), "isBreaking");
    }

    internal static void ApplyToAllExisting()
    {
        foreach (var a in Resources.FindObjectsOfTypeAll<BreakableRopeAnchor>())
            EnforceUnbreakableIfConfigured(a);
    }

    [HarmonyPostfix]
    [HarmonyPatch("Start")]
    private static void StartPostfix(BreakableRopeAnchor __instance)
    {
        EnforceUnbreakableIfConfigured(__instance);
    }

    [HarmonyPrefix]
    [HarmonyPatch("Update")]
    private static void UpdatePrefix(BreakableRopeAnchor __instance)
    {
        EnsureFields();
        if (_willBreakInTime == null || _photonView == null || _anchor == null)
            return;

        float m = Plugin.GetMultiplier(Plugin.EnablePreplacedPitons.Value, Plugin.PreplacedPitonBreakSpeedMultiplier.Value);
        if (float.IsNaN(m))
            return;

        var pv = _photonView.GetValue(__instance) as PhotonView;
        bool isMine = pv != null && pv.IsMine;

        // Unbreakable mode:
        // - for the owner: prevent timer from ever reaching 0 and stop any already-started coroutine
        // - for non-owners: no-op (network will determine the outcome)
        if (m < 0f)
        {
            if (isMine)
            {
                _willBreakInTime.SetValue(__instance, float.PositiveInfinity);

                // If it already started breaking (e.g. hot-reload), stop the coroutine before it RPCs Detach.
                if (_isBreaking != null && (bool)_isBreaking.GetValue(__instance))
                {
                    __instance.StopAllCoroutines();
                    _isBreaking.SetValue(__instance, false);
                }
            }
            return;
        }

        // Speed scaling: only matters on the owner, only when at least one climber is on this rope.
        if (!isMine || Mathf.Approximately(m, 1f))
            return;

        var anchor = _anchor.GetValue(__instance) as RopeAnchorWithRope;
        if (anchor?.rope == null)
            return;

        // Match vanilla logic (count rope climbers).
        int climbers = 0;
        List<Character> chars = PlayerHandler.GetAllPlayerCharacters();
        for (int i = 0; i < chars.Count; i++)
        {
            var ch = chars[i];
            if (ch != null && ch.data != null && ch.data.isRopeClimbing && ch.data.heldRope == anchor.rope)
                climbers++;
        }

        if (climbers <= 0)
            return;

        // Vanilla will do: willBreakInTime -= dt;
        // We want:         willBreakInTime -= dt * m;
        // Pre-adjust so vanilla produces the scaled result.
        float t = (float)_willBreakInTime.GetValue(__instance);
        t += Time.deltaTime * (1f - m);
        _willBreakInTime.SetValue(__instance, t);
    }

    private static void EnforceUnbreakableIfConfigured(BreakableRopeAnchor inst)
    {
        EnsureFields();
        if (_willBreakInTime == null || _photonView == null)
            return;

        float m = Plugin.GetMultiplier(Plugin.EnablePreplacedPitons.Value, Plugin.PreplacedPitonBreakSpeedMultiplier.Value);
        if (float.IsNaN(m))
            return;

        if (m < 0f)
        {
            var pv = _photonView.GetValue(inst) as PhotonView;
            if (pv != null && pv.IsMine)
                _willBreakInTime.SetValue(inst, float.PositiveInfinity);
        }
    }
}

#endregion

#region Mesa pickaxes (ShittyPiton)

[HarmonyPatch(typeof(ShittyPiton))]
internal static class ShittyPitonPatches
{
    private static FieldInfo? _totalSecondsOfHang;
    private static FieldInfo? _sinceCrack;
    private static FieldInfo? _isHung;
    private static FieldInfo? _isBreaking;
    private static FieldInfo? _disabled;
    private static FieldInfo? _crackScale;
    private static FieldInfo? _cracksToBreak;
    private static FieldInfo? _view;
    private static FieldInfo? _handle;

    private static void EnsureFields()
    {
        _totalSecondsOfHang ??= AccessTools.Field(typeof(ShittyPiton), "totalSecondsOfHang");
        _sinceCrack ??= AccessTools.Field(typeof(ShittyPiton), "sinceCrack");
        _isHung ??= AccessTools.Field(typeof(ShittyPiton), "isHung");
        _isBreaking ??= AccessTools.Field(typeof(ShittyPiton), "isBreaking");
        _disabled ??= AccessTools.Field(typeof(ShittyPiton), "disabled");
        _crackScale ??= AccessTools.Field(typeof(ShittyPiton), "crackScale");
        _cracksToBreak ??= AccessTools.Field(typeof(ShittyPiton), "cracksToBreak");
        _view ??= AccessTools.Field(typeof(ShittyPiton), "view");
        _handle ??= AccessTools.Field(typeof(ShittyPiton), "handle");
    }

    internal static void ApplyToAllExisting()
    {
        foreach (var p in Resources.FindObjectsOfTypeAll<ShittyPiton>())
            EnforceUnbreakableIfConfigured(p);
    }

    [HarmonyPostfix]
    [HarmonyPatch("Start")]
    private static void StartPostfix(ShittyPiton __instance)
    {
        EnforceUnbreakableIfConfigured(__instance);
    }

    [HarmonyPrefix]
    [HarmonyPatch("Update")]
    private static void UpdatePrefix(ShittyPiton __instance)
    {
        EnsureFields();
        if (_totalSecondsOfHang == null || _sinceCrack == null || _isHung == null || _isBreaking == null || _view == null)
            return;

        float m = Plugin.GetMultiplier(Plugin.EnableMesaPickaxes.Value, Plugin.MesaPickaxeBreakSpeedMultiplier.Value);
        if (float.IsNaN(m))
            return;

        var pv = _view.GetValue(__instance) as PhotonView;
        bool isMine = pv != null && pv.IsMine;

        // Unbreakable: keep owner from ever reaching "start breaking" and stop any ongoing breaking logic locally.
        if (m < 0f)
        {
            if (isMine)
            {
                _totalSecondsOfHang.SetValue(__instance, float.PositiveInfinity);
            }

            // If already in breaking mode (e.g. hot-reload), disable local breaking visuals/logic.
            _isBreaking.SetValue(__instance, false);
            if (_disabled != null) _disabled.SetValue(__instance, false);
            if (_crackScale != null) _crackScale.SetValue(__instance, 0f);
            if (_sinceCrack != null) _sinceCrack.SetValue(__instance, 0f);
            if (_cracksToBreak != null) _cracksToBreak.SetValue(__instance, 4);

            try
            {
                if (__instance.crack != null && __instance.crack.activeSelf)
                    __instance.crack.SetActive(false);
            }
            catch { /* ignore */ }

            return;
        }

        // Speed scaling (applies both to hang timer and crack cadence).
        if (Mathf.Approximately(m, 1f))
            return;

        bool hung = (bool)_isHung.GetValue(__instance);
        bool breaking = (bool)_isBreaking.GetValue(__instance);
        float dt = Time.deltaTime;

        if (breaking)
        {
            // Vanilla: if (isHung) sinceCrack += dt;
            // Want:              sinceCrack += dt * m;
            if (hung)
            {
                float sc = (float)_sinceCrack.GetValue(__instance);
                sc += dt * (m - 1f); // pre-adjust so vanilla adds dt -> net dt*m
                _sinceCrack.SetValue(__instance, sc);
            }
        }
        else
        {
            // Vanilla: else if (view.IsMine && isHung) totalSecondsOfHang -= dt;
            // Want:                          ...      totalSecondsOfHang -= dt * m;
            if (isMine && hung)
            {
                float t = (float)_totalSecondsOfHang.GetValue(__instance);
                t += dt * (1f - m); // pre-adjust so vanilla subtracts dt -> net subtract dt*m
                _totalSecondsOfHang.SetValue(__instance, t);
            }
        }
    }

    // Hard safety: if unbreakable, prevent the OWNER from progressing the crack counter (which is what eventually sends RPCA_Break).
    [HarmonyPrefix]
    [HarmonyPatch("Crack")]
    private static bool CrackPrefix(ShittyPiton __instance)
    {
        float m = Plugin.GetMultiplier(Plugin.EnableMesaPickaxes.Value, Plugin.MesaPickaxeBreakSpeedMultiplier.Value);
        if (float.IsNaN(m) || m >= 0f)
            return true;

        // Only the owner can actually send the break RPC; block it there.
        // Non-owners can still do visuals if they want; blocking on owner is what prevents the break network-wide.
        try
        {
            if (__instance.photonView != null && __instance.photonView.IsMine)
                return false;
        }
        catch { /* ignore */ }

        return true;
    }

    private static void EnforceUnbreakableIfConfigured(ShittyPiton inst)
    {
        EnsureFields();
        if (_totalSecondsOfHang == null || _view == null)
            return;

        float m = Plugin.GetMultiplier(Plugin.EnableMesaPickaxes.Value, Plugin.MesaPickaxeBreakSpeedMultiplier.Value);
        if (float.IsNaN(m))
            return;

        if (m < 0f)
        {
            var pv = _view.GetValue(inst) as PhotonView;
            if (pv != null && pv.IsMine)
                _totalSecondsOfHang.SetValue(inst, float.PositiveInfinity);
        }
    }
}

#endregion

#region Client-side persistence (optional desync mode)

/// <summary>
/// Optional client-side behavior to keep these objects alive locally even if the host/owner breaks them.
/// OFF by default for multiplayer safety.
/// </summary>
[HarmonyPatch]
internal static class ClientSidePersistencePatches
{
    private static FieldInfo? _ropeAttachedToAnchor;

    // Rope.Detach_Rpc is used for many rope events; we only ignore it for ropes anchored to a BreakableRopeAnchor anchor.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Rope), nameof(Rope.Detach_Rpc))]
    private static bool Rope_Detach_Rpc_Prefix(Rope __instance, float segmentLength)
    {
        if (!Plugin.ClientSidePersistence.Value)
            return true;

        if (!Plugin.EnablePreplacedPitons.Value)
            return true;

        // Only "detach from host" on clients who are NOT the rope owner.
        if (__instance.photonView != null && __instance.photonView.IsMine)
            return true;

        // Only ignore detach when rope is currently anchored to a BreakableRopeAnchor anchor (preplaced breakable rope anchors).
        if (__instance.attachmenState != Rope.ATTACHMENT.anchored)
            return true;

        _ropeAttachedToAnchor ??= AccessTools.Field(typeof(Rope), "attachedToAnchor");
        var anchor = _ropeAttachedToAnchor?.GetValue(__instance) as RopeAnchor;
        if (anchor == null)
            return true;

        // BreakableRopeAnchor lives on the same object as RopeAnchorWithRope / RopeAnchor for preplaced anchors.
        if (anchor.GetComponent<BreakableRopeAnchor>() != null)
        {
            // Skip processing detach locally -> rope stays attached on this client.
            return false;
        }

        return true;
    }

    // ShittyPiton break/startbreak RPCs: safe to ignore in client-side persistence mode.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(ShittyPiton), "RPCA_StartBreaking")]
    private static bool ShittyPiton_RPCA_StartBreaking_Prefix(ShittyPiton __instance)
    {
        if (!Plugin.ClientSidePersistence.Value)
            return true;

        if (!Plugin.EnableMesaPickaxes.Value)
            return true;

        // Only ignore remote authority actions (non-owner).
        if (__instance.photonView != null && __instance.photonView.IsMine)
            return true;

        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(ShittyPiton), "RPCA_Break")]
    private static bool ShittyPiton_RPCA_Break_Prefix(ShittyPiton __instance)
    {
        if (!Plugin.ClientSidePersistence.Value)
            return true;

        if (!Plugin.EnableMesaPickaxes.Value)
            return true;

        if (__instance.photonView != null && __instance.photonView.IsMine)
            return true;

        return false;
    }
}

#endregion
