# BetterPreplacedPitons

A PEAK mod that changes how **preplaced** pitons and mesa pickaxes “wear out”.

By default, PEAK has:
- **Preplaced rope pitons / rope anchors** that slowly time out and **detach** after enough use.
- **Mesa pickaxes** (preplaced climb handles) that **crack** and eventually **break** after hanging on them.

This mod lets you:
- Make those objects **unbreakable** *(default)*  
- Or **scale** how fast they break *(slower/faster than vanilla)*
- Optionally *(OFF by default)* ignore the host’s break/detach events locally (**client-side persistence / deliberate desync**)

---

## What this mod changes

### 1) Preplaced rope pitons (rope anchors)
In-game these are handled by `BreakableRopeAnchor`.

- The game maintains a timer (internally `willBreakInTime`) that counts down while players are rope-climbing.
- When it reaches zero, the rope **detaches** via a networked RPC.

**What the mod does:**
- If configured as **unbreakable**, it prevents the **authoritative owner** (Photon `IsMine`) from ever reaching zero.
- If configured with a **multiplier**, it scales how quickly that timer ticks down **while someone is climbing**.

### 2) Mesa pickaxes (preplaced climb handles)
In-game these are handled by `ShittyPiton` (yep, that’s the actual name).

- The game counts “hang time”, then enters a cracking phase, then breaks via networked RPCs.

**What the mod does:**
- If configured as **unbreakable**, it prevents the **authoritative owner** from progressing to the breaking state.
- If configured with a **multiplier**, it scales both:
  - the hang-time countdown, and
  - the crack/break progression.

---

## Multiplayer behavior

### Multiplayer-safe by default ✅
By default, this mod is **authority-respecting**:
- It does **not** ignore networked break/detach events.
- Everyone stays consistent with what the **object owner** (usually the host/master client) decides.

Practical effect:
- **Host/owner has mod** → everyone benefits.
- **Only client has mod** and host/owner is vanilla → host/owner can still break/detach objects for everyone.

### Optional client-side persistence (desync mode) ⚠️
There is a config option to intentionally “detach” from host authority locally:

- Your client ignores certain break/detach RPCs and keeps those objects alive **on your side only**.
- This is **intentional desync**. Others may see it broken while you don’t.
- Expect weirdness: visual inconsistency, interaction mismatch, etc.

---

## Installation

1. Install **BepInEx** for PEAK.
2. Put `BetterPreplacedPitons.dll` into:
   - `BepInEx/plugins/`
3. Launch the game once to generate config.
4. Edit the config in:
   - `BepInEx/config/`

---

## Configuration

### Meaning of the speed multipliers
Both pitons and pickaxes use the same multiplier semantics:

- `<= 0` → **unbreakable** *(default is `-1`)*
- `1.0` → **vanilla** behavior
- `0.5` → **50% slower** breaking *(lasts 2× longer)*
- `2.0` → **2× faster** breaking

---

## Config entries

### `[General]`

#### `EnablePreplacedPitons` *(bool)*
- **Default:** `true`
- If `false`, the mod will not modify preplaced rope pitons at all (fully vanilla for pitons).

#### `PreplacedPitonBreakSpeedMultiplier` *(float)*
- **Default:** `-1`
- Controls how fast preplaced rope pitons “wear out”.
- See **Meaning of the speed multipliers** above.

#### `EnableMesaPickaxes` *(bool)*
- **Default:** `true`
- If `false`, the mod will not modify mesa pickaxes at all (fully vanilla for pickaxes).

#### `MesaPickaxeBreakSpeedMultiplier` *(float)*
- **Default:** `-1`
- Controls how fast mesa pickaxes crack/break.
- See **Meaning of the speed multipliers** above.

---

### `[Multiplayer]`

#### `ClientSidePersistence` *(bool)*
- **Default:** `false`
- **When `false` (default):** Multiplayer-safe.
  - Your client respects host/owner break/detach RPCs.
  - No intentional desync.

- **When `true`:** Client-side persistence / deliberate desync.
  - Your client ignores certain break/detach RPCs and keeps the objects alive locally.
  - Other players may still see them broken/detached.
  - Use only if you *explicitly* want that behavior.

---

## Quick examples

### 1) Never break anything (default)
```ini
EnablePreplacedPitons = true
PreplacedPitonBreakSpeedMultiplier = -1
EnableMesaPickaxes = true
MesaPickaxeBreakSpeedMultiplier = -1
ClientSidePersistence = false
```

### 2) Vanilla pitons, unbreakable pickaxes
```ini
PreplacedPitonBreakSpeedMultiplier = 1
MesaPickaxeBreakSpeedMultiplier = -1
```

### 3) Everything lasts 2× longer
```ini
PreplacedPitonBreakSpeedMultiplier = 0.5
MesaPickaxeBreakSpeedMultiplier = 0.5
```

### 4) Client-side persistence (desync mode)
```ini
ClientSidePersistence = true
```

---

## Notes / troubleshooting

- After changing config values, a full game restart is safest.
- If PEAK updates and changes internal names/fields, the mod might stop affecting objects or only partially work.
- In multiplayer: for *everyone* to benefit consistently, the authoritative owner (usually the host/master client) should run the mod.

---

Happy climbing, nya~ 🐾
