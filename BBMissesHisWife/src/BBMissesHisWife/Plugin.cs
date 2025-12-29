using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace BBMissesHisWife;

// This BepInAutoPlugin attribute comes from the Hamunii.BepInEx.AutoPlugin
// NuGet package, and it will generate the BepInPlugin attribute for you.
[BepInAutoPlugin]
public partial class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; } = null!;

    private static Harmony _harmony = null!;

    // Config
    private static ConfigEntry<int> _forceIndex = null!;
    private static ConfigEntry<string> _subtitleKeywords = null!;
    private static ConfigEntry<string> _clipKeywords = null!;
    private static ConfigEntry<bool> _overrideIncomingRpc = null!;
    private static ConfigEntry<bool> _ignoreSpamCooldown = null!;
    private static ConfigEntry<bool> _verboseLogging = null!;

    // Host / lobby behavior
    private static ConfigEntry<bool> _masterClientForceWifeForEveryone = null!;
    private static ConfigEntry<bool> _masterClientForceAlsoAffectsHostView = null!;
    private static ConfigEntry<bool> _masterClientIgnoreSpamCooldown = null!;
    private static ConfigEntry<float> _masterClientBroadcastDelaySeconds = null!;
    private static ConfigEntry<bool> _masterClientInterruptVanillaAudio = null!;

    // Fixes
    private static ConfigEntry<bool> _stopPreviousAskRoutines = null!;
    private static ConfigEntry<bool> _stopAudioImmediately = null!;

    // Cache (keyed by Unity instance id of Action_AskBingBong)
    private static readonly Dictionary<int, int> CachedTargetIndexByInstanceId = new();
    private static bool _printedCandidatesThisSession;

    private void Awake()
    {
        Log = Logger;

        _forceIndex = Config.Bind(
            "General",
            "ForceResponseIndex",
            -1,
            "If >= 0, forcibly uses this response index. Leave at -1 to auto-detect.");

        _overrideIncomingRpc = Config.Bind(
            "General",
            "OverrideIncomingRpc",
            false,
            "If true, overrides incoming Bing Bong RPCs too (client-side). Keep this OFF (default) if you want other players' Bing Bong lines to stay random/vanilla on your screen.");

        _ignoreSpamCooldown = Config.Bind(
            "General",
            "IgnoreSpamCooldown",
            false,
            "If true, plays the line even when the game thinks you're spamming Bing Bong (within 1s).");

        _subtitleKeywords = Config.Bind(
            "AutoDetect",
            "SubtitleKeywords",
            "wife,misses his wife",
            "Comma-separated keywords; if any match the Bing Bong subtitle text OR subtitle ID, that response is selected.");

        _clipKeywords = Config.Bind(
            "AutoDetect",
            "ClipNameKeywords",
            "wife,misswife,misseswife",
            "Comma-separated keywords; if any match the AudioClip name, that response is selected.");

        _verboseLogging = Config.Bind(
            "Debug",
            "VerboseLogging",
            false,
            "If true, logs all Bing Bong responses (subtitle text + clip name) once per session to help you pick a ForceResponseIndex.");

        // Host options
        _masterClientForceWifeForEveryone = Config.Bind(
            "Host",
            "MasterClientForceWifeForEveryone",
            false,
            "If true AND you are the Photon Master Client, you will attempt to force OTHER players' Bing Bong results to 'wife' for everyone in the lobby (even unmodded clients).");

        _masterClientForceAlsoAffectsHostView = Config.Bind(
            "Host",
            "MasterClientForceAlsoAffectsHostView",
            true,
            "If true, when MasterClientForceWifeForEveryone is enabled and you are Master Client, your own screen will also show the forced 'wife' line when other players use Bing Bong.");

        _masterClientIgnoreSpamCooldown = Config.Bind(
            "Host",
            "MasterClientIgnoreSpamCooldown",
            true,
            "If true (recommended), the host-forced line will be sent with spamming=false so it actually plays (vanilla cancels audio/subtitles when spamming=true).");

        _masterClientInterruptVanillaAudio = Config.Bind(
            "Host",
            "InterruptVanillaAudio",
            true,
            "If true (recommended), the host-forced RPC is delayed so it can STOP the original random voice on unmodded clients before playing the forced 'wife' clip.");

        _masterClientBroadcastDelaySeconds = Config.Bind(
            "Host",
            "BroadcastDelaySeconds",
            2.60f,
            "Delay before host sends the forced RPC to other clients. Needs to be >2.6s to reliably interrupt the original on unmodded clients. Try 2.4–2.8 depending on latency.");

        // Fixes
        _stopPreviousAskRoutines = Config.Bind(
            "Fixes",
            "StopPreviousAskRoutines",
            true,
            "If true, this plugin will stop the previous Bing Bong Ask coroutine before starting a new one (prevents local double-audio when multiple Ask RPCs arrive).");

        _stopAudioImmediately = Config.Bind(
            "Fixes",
            "StopAudioImmediately",
            true,
            "If true, immediately stops Bing Bong's AudioSource before starting the (new) AskRoutine on THIS client. Helps prevent local overlap.");

        _harmony = new Harmony(Info.Metadata.GUID);
        _harmony.PatchAll(typeof(Plugin).Assembly);

        Log.LogInfo($"Plugin {Name} loaded. Nya~ 🐾");
    }

    internal static int? GetTargetResponseIndex(Action_AskBingBong action)
    {
        if (action == null || action.responses == null || action.responses.Length == 0)
            return null;

        // Hard override first.
        int forced = _forceIndex.Value;
        if (forced >= 0 && forced < action.responses.Length)
            return forced;

        // Cached?
        int key = action.GetInstanceID();
        if (CachedTargetIndexByInstanceId.TryGetValue(key, out int cached) && cached >= 0 && cached < action.responses.Length)
            return cached;

        string[] subTokens = SplitKeywords(_subtitleKeywords.Value);
        string[] clipTokens = SplitKeywords(_clipKeywords.Value);

        int bestIndex = -1;
        int bestScore = 0;

        for (int i = 0; i < action.responses.Length; i++)
        {
            var resp = action.responses[i];
            if (resp == null)
                continue;

            int score = 0;

            string subtitleId = resp.subtitleID ?? string.Empty;
            if (subTokens.Length > 0 && ContainsAny(subtitleId, subTokens))
                score += 30;

            string localized = TryGetLocalizedText(subtitleId);
            if (!string.IsNullOrEmpty(localized) && !localized.StartsWith("LOC:", StringComparison.OrdinalIgnoreCase))
            {
                if (subTokens.Length > 0 && ContainsAny(localized, subTokens))
                    score += 120;

                // Small bias toward the default keyword
                if (localized.IndexOf("wife", StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 10;
            }

            string clipName = FirstClipName(resp);
            if (!string.IsNullOrEmpty(clipName))
            {
                if (clipTokens.Length > 0 && ContainsAny(clipName, clipTokens))
                    score += 100;

                if (clipName.IndexOf("wife", StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 10;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = i;
            }
        }

        if (_verboseLogging.Value && !_printedCandidatesThisSession)
        {
            _printedCandidatesThisSession = true;
            LogCandidates(action);
        }

        // Require a minimum score; otherwise we might select an unrelated line.
        if (bestIndex >= 0 && bestScore >= 80)
        {
            CachedTargetIndexByInstanceId[key] = bestIndex;
            return bestIndex;
        }

        if (_verboseLogging.Value)
        {
            Log.LogWarning(
                "BBMissesHisWife couldn't confidently auto-detect the 'wife' response. " +
                "Enable VerboseLogging and/or set ForceResponseIndex in the config (BepInEx/config/*.cfg).");
        }

        return null;
    }

    private static void LogCandidates(Action_AskBingBong action)
    {
        try
        {
            Log.LogInfo("Bing Bong responses detected (index: subtitleID => localizedText | clipName):");
            for (int i = 0; i < action.responses.Length; i++)
            {
                var r = action.responses[i];
                if (r == null)
                {
                    Log.LogInfo($"  [{i}] <null>");
                    continue;
                }

                string id = r.subtitleID ?? "";
                string text = TryGetLocalizedText(id);
                if (text.Length > 140)
                    text = text.Substring(0, 140) + "…";

                string clip = FirstClipName(r);

                Log.LogInfo($"  [{i}] {id} => {text} | {clip}");
            }
        }
        catch (Exception e)
        {
            Log.LogDebug($"Failed to log candidates: {e}");
        }
    }

    private static string TryGetLocalizedText(string subtitleId)
    {
        if (string.IsNullOrWhiteSpace(subtitleId))
            return string.Empty;

        try
        {
            // printDebug=true returns "LOC: <ID>" instead of spamming errors if not found.
            return LocalizedText.GetText(subtitleId, printDebug: true) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string FirstClipName(Action_AskBingBong.BingBongResponse resp)
    {
        try
        {
            var clip = resp.sfx?.clips?.FirstOrDefault(c => c != null);
            return clip != null ? clip.name : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string[] SplitKeywords(string raw)
        => (raw ?? string.Empty)
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToArray();

    private static bool ContainsAny(string haystack, string[] needles)
    {
        if (string.IsNullOrEmpty(haystack) || needles.Length == 0)
            return false;

        foreach (var n in needles)
        {
            if (n.Length == 0)
                continue;

            if (haystack.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    // -------- Harmony patches --------

    // Forces OUR outgoing ask to always pick the wife response index.
    [HarmonyPatch(typeof(Action_AskBingBong), nameof(Action_AskBingBong.RunAction))]
    private static class Patch_ActionAskBingBong_RunAction
    {
        private static bool Prefix(Action_AskBingBong __instance)
        {
            int? idx = GetTargetResponseIndex(__instance);
            if (!idx.HasValue)
                return true; // fall back to vanilla RNG

            try
            {
                var item = ItemField(__instance);
                if (item == null || item.photonView == null)
                    return true;

                float lastAsked = LastAskedField(__instance);
                bool spamming = Time.time < lastAsked + 1f;

                // Our own usage: ALWAYS send the forced index.
                item.photonView.RPC("Ask", RpcTarget.All, idx.Value, spamming);

                if (Time.time > lastAsked + 1f)
                    LastAskedField(__instance) = Time.time;

                return false; // skip original random selection
            }
            catch (Exception e)
            {
                Log.LogError($"BBMissesHisWife RunAction patch failed, falling back to vanilla behavior: {e}");
                return true;
            }
        }
    }

    // Re-implements Ask so we can:
    //  - optionally override (client-side)
    //  - optionally re-broadcast as MasterClient (host "force for everyone")
    //  - stop previous ask coroutines on THIS client (no local double audio)
    [HarmonyPatch(typeof(Action_AskBingBong), nameof(Action_AskBingBong.Ask))]
    private static class Patch_ActionAskBingBong_Ask
    {
        private static bool Prefix(Action_AskBingBong __instance, int index, bool spamming)
        {
            try
            {
                var item = ItemField(__instance);
                if (item == null || item.holderCharacter == null)
                    return false; // vanilla also does nothing

                bool isMaster = PhotonNetwork.IsMasterClient;
                bool isMine = item.photonView != null && item.photonView.IsMine;

                int originalIndex = index;

                // Determine our target index (if any)
                int? forcedIdx = GetTargetResponseIndex(__instance);

                // Host: rebroadcast to others so they (try to) get wife too
                if (_masterClientForceWifeForEveryone.Value && isMaster && !isMine && forcedIdx.HasValue && originalIndex != forcedIdx.Value)
                {
                    bool forcedSpamming = _masterClientIgnoreSpamCooldown.Value ? false : spamming;

                    // If we want the host's own view to also be forced, override locally too.
                    if (_masterClientForceAlsoAffectsHostView.Value)
                        index = forcedIdx.Value;

                    // Delay to interrupt vanilla audio on unmodded clients:
                    // Vanilla plays the response ~1.0s after Ask; it calls source.Stop() at +0.5s.
                    // So if we send the forced Ask at >0.5s after the original, our forced coroutine's Stop()
                    // happens after vanilla started, cutting it off.
                    float delay = Mathf.Max(0f, _masterClientBroadcastDelaySeconds.Value);
                    if (_masterClientInterruptVanillaAudio.Value)
                        delay = Mathf.Max(0.55f, delay); // needs to be > 0.50s

                    __instance.StartCoroutine(SendForcedAskAfterDelay(item.photonView, forcedIdx.Value, forcedSpamming, delay));
                }

                // Client-side override (default OFF)
                bool localOverride = _overrideIncomingRpc.Value
                                     || (_masterClientForceWifeForEveryone.Value && isMaster && _masterClientForceAlsoAffectsHostView.Value);

                if (localOverride && forcedIdx.HasValue)
                    index = forcedIdx.Value;

                if (_ignoreSpamCooldown.Value)
                    spamming = false;

                // If host forcing is enabled on this host, we generally want the forced line to actually play.
                if (_masterClientForceWifeForEveryone.Value && isMaster && forcedIdx.HasValue && _masterClientIgnoreSpamCooldown.Value)
                    spamming = false;

                // ---- Run the vanilla Ask effects, but with better coroutine control on THIS client ----
                if (__instance.squishAnim != null)
                    __instance.squishAnim.SetTrigger("Squish");

                if (SFX_Player.instance != null && __instance.squeak != null)
                    SFX_Player.instance.PlaySFX(__instance.squeak, __instance.transform.position, __instance.transform);

                if (__instance.subtitles != null)
                    __instance.subtitles.gameObject.SetActive(false);

                if (_stopPreviousAskRoutines.Value)
                {
                    StopTrackedCoroutine(__instance);
                    StopTrackedSubtitleCoroutine(__instance);
                }

                if (_stopAudioImmediately.Value && __instance.source != null)
                    __instance.source.Stop();

                StartTrackedAskRoutine(__instance, index, spamming);

                return false; // skip original Ask
            }
            catch (Exception e)
            {
                Log.LogError($"BBMissesHisWife Ask patch failed, falling back to vanilla behavior: {e}");
                return true; // let the game handle it
            }
        }
    }

    private static IEnumerator SendForcedAskAfterDelay(PhotonView pv, int forcedIndex, bool spamming, float delay)
    {
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        if (pv != null)
        {
            // Send only to others to avoid the host seeing duplicate asks.
            pv.RPC("Ask", RpcTarget.Others, forcedIndex, spamming);
        }
    }

    // ---- Coroutine tracking (local only) ----

    private static readonly MethodInfo? AskRoutineMethod = AccessTools.Method(typeof(Action_AskBingBong), "AskRoutine");

    private static readonly AccessTools.FieldRef<Action_AskBingBong, Coroutine> AskRoutineField =
        AccessTools.FieldRefAccess<Action_AskBingBong, Coroutine>("askRoutine");

    private static readonly AccessTools.FieldRef<Action_AskBingBong, Coroutine> SubtitleRoutineField =
        AccessTools.FieldRefAccess<Action_AskBingBong, Coroutine>("subtitleRoutine");

    private static void StartTrackedAskRoutine(Action_AskBingBong action, int index, bool spamming)
    {
        if (AskRoutineMethod == null)
        {
            // Super unlikely, but if reflection fails, do nothing rather than crashing.
            Log.LogWarning("AskRoutine method not found via reflection; Bing Bong will not play audio/subtitles.");
            return;
        }

        var enumerator = AskRoutineMethod.Invoke(action, new object[] { index, spamming }) as IEnumerator;
        if (enumerator == null)
            return;

        Coroutine c = action.StartCoroutine(enumerator);
        AskRoutineField(action) = c;
    }

    private static void StopTrackedCoroutine(Action_AskBingBong action)
    {
        try
        {
            Coroutine c = AskRoutineField(action);
            if (c != null)
                action.StopCoroutine(c);

            AskRoutineField(action) = null!;
        }
        catch
        {
            // ignore
        }
    }

    private static void StopTrackedSubtitleCoroutine(Action_AskBingBong action)
    {
        try
        {
            Coroutine c = SubtitleRoutineField(action);
            if (c != null)
                action.StopCoroutine(c);

            SubtitleRoutineField(action) = null!;
        }
        catch
        {
            // ignore
        }
    }

    // Fast field accessors (avoid reflection allocations each call).
    private static readonly AccessTools.FieldRef<Action_AskBingBong, float> LastAskedField =
        AccessTools.FieldRefAccess<Action_AskBingBong, float>("lastAsked");

    private static readonly AccessTools.FieldRef<ItemActionBase, Item> ItemFieldBase =
        AccessTools.FieldRefAccess<ItemActionBase, Item>("item");

    private static Item? ItemField(Action_AskBingBong action)
    {
        try
        {
            return ItemFieldBase(action);
        }
        catch
        {
            return null;
        }
    }
}
