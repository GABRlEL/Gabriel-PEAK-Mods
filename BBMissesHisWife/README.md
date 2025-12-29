# BB Misses His Wife (PEAK mod)

This mod changes **Bing Bong** (PEAK’s mascot / “Magic Conch”-style item) so that when **you** use Bing Bong, it always plays the **“I miss my wife”** response instead of randomly picking a line.

It also includes optional settings to:
- **Client-side override** what *you* hear when **other players** use Bing Bong (off by default).
- **Host/MasterClient “best-effort” enforcement** to try forcing the wife line on other clients (even if they don’t run the mod).

---

## What it does in-game

### Default behavior (recommended)
- ✅ **When YOU use Bing Bong:** everyone sees/hears **“I miss my wife”**.
- ✅ **When someone else uses Bing Bong:** you see/hear the **real rolled line** (vanilla behavior).

### Optional behaviors
- **Client-side override:** make your own client always display/play the wife line even when others use Bing Bong.
- **Host/MasterClient enforcement:** if you are the Photon **MasterClient**, try to force other clients to get the wife line too (best-effort, see limitations).

---

## Installation

1. Install **BepInEx** for PEAK.
2. Put the built DLL into:
   - `PEAK/BepInEx/plugins/`
3. Launch the game once to generate the config file.

> Tip: If you can’t find the config file, search in `PEAK/BepInEx/config/` for the text `MasterClientForceWifeForEveryone`.

---

## Configuration

Config lives in `PEAK/BepInEx/config/`.

Below are **all config entries**, grouped by section.

### [General]

#### `ForceResponseIndex` (default: `-1`)
Force a specific Bing Bong response by index.
- `-1` = auto-detect the “wife” response by subtitle / localization / clip name.
- `0+` = force that exact response index.

Use this if auto-detection fails after a game update.

#### `OverrideIncomingRpc` (default: `false`)
**Client-side only.**
If enabled, your client will rewrite incoming Bing Bong results so **you always see/hear “I miss my wife”** even when *other players* use Bing Bong.

- `false` (default) = preserve vanilla for others’ uses (recommended)
- `true` = always show wife line on your screen, regardless of who used it

#### `IgnoreSpamCooldown` (default: `false`)
If enabled, ignores Bing Bong’s “spamming” flag (cooldown logic) on your client.
This is mainly useful together with `OverrideIncomingRpc`.

---

### [AutoDetect]

These are used only when `ForceResponseIndex = -1`.

#### `SubtitleKeywords` (default: `wife,misses his wife`)
Comma-separated keywords matched against:
- the subtitle ID, and
- localized subtitle text

If a match is found, that response becomes the target.

#### `ClipNameKeywords` (default: `wife,misswife,misseswife`)
Comma-separated keywords matched against the AudioClip name.

---

### [Host]

These options try to influence what **other clients** receive.

#### `MasterClientForceWifeForEveryone` (default: `false`)
If enabled and you are the Photon **MasterClient**, the mod will attempt to force
other players’ Bing Bong results to “wife” by broadcasting an additional RPC.

⚠️ **Important limitation:** PEAK uses Photon PUN RPCs and there is no authoritative server rewrite.
This is best-effort and can be affected by latency / ordering. In practice, this causes clients to run another try which results in “wife”.

#### `MasterClientForceAlsoAffectsHostView` (default: `true`)
If host forcing is enabled, this controls whether the MasterClient (you) also rewrites what you see/hear for other players’ uses while enforcing.

- `true` = host also sees “wife” when enforcing
- `false` = host can enforce for others while still seeing the real rolled line (when possible)

#### `MasterClientIgnoreSpamCooldown` (default: `true`)
If enabled, host enforcement sends `spamming=false` (more aggressive).
If disabled, it preserves the original spam flag.

#### `InterruptVanillaAudio` (default: `true`)
When host enforcement is enabled, tries to **reduce “two clips at once”**
by timing the forced RPC so it interrupts the original voice line on unmodded clients.

This is a mitigation, not a perfect guarantee (depends on timing/network).

#### `BroadcastDelaySeconds` (default: `0.60`)
Delay before the host sends the forced RPC to other clients.

This delay is important:
- Too low → original line often still plays fully and overlaps
- Too high → wife line feels delayed

Recommended range: **2.4–2.8** depending on lobby latency.

---

### [Fixes]

These are local quality-of-life fixes to reduce overlap and weirdness when multiple `Ask()` calls happen close together.

#### `StopPreviousAskRoutines` (default: `true`)
Stops previously running Bing Bong “Ask” coroutine(s) **on this client** before starting a new one.
Helps prevent local double-audio when multiple RPCs arrive.

#### `StopAudioImmediately` (default: `true`)
Stops Bing Bong’s AudioSource immediately when a new Ask begins **on this client**.
Helps reduce overlap on modded clients.

---

### [Debug]

#### `VerboseLogging` (default: `false`)
Logs Bing Bong response candidates (subtitle IDs, localized text, clip names) once per session.
Useful to determine the correct `ForceResponseIndex` after a game update.

---

## Recommended presets

### Normal (clean + predictable)
- `OverrideIncomingRpc = false`
- `MasterClientForceWifeForEveryone = false`

### Client-side “I always want wife”
- `OverrideIncomingRpc = true`

### Host “try to force it for everyone”
- `MasterClientForceWifeForEveryone = true`
- `InterruptVanillaAudio = true`
- `BroadcastDelaySeconds = 2.60` (tune 2.4–2.8)

---

## Troubleshooting

### “It stopped forcing the wife line”
Turn on `VerboseLogging`, use Bing Bong once, and read `BepInEx/LogOutput.log`.
Then set `ForceResponseIndex` to the printed index you want.

### “Host enforcement plays 2 clips at once”
Increase `BroadcastDelaySeconds` a bit (e.g. `2.8`).
Also ensure `InterruptVanillaAudio = true` and `Fixes` options are enabled.

---

## Credits / Tech
- Built with **BepInEx** + **Harmony**
- Patches PEAK’s `Action_AskBingBong` to control which response index gets used
