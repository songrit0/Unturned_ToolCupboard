# 🏠 ToolCupboard — Unturned Base Decay (Chat-based)

> Rust-style **base decay** for Unturned (RocketMod). **No UI — everything is delivered through chat.**
> ระบบ **ผุฐาน** สไตล์ Rust สำหรับ Unturned ไม่มี UI แจ้งเตือนผ่านแชททั้งหมด

![Game](https://img.shields.io/badge/game-Unturned-2f9e44)
![Framework](https://img.shields.io/badge/framework-RocketMod-blue)
![.NET](https://img.shields.io/badge/.NET-Framework%204.8-512bd4)
![License](https://img.shields.io/badge/license-MIT-lightgrey)

---

## ✨ Overview

สิ่งก่อสร้าง (barricade / structure) ที่ **ไม่มีตัวป้องกัน** จะค่อย ๆ เสีย HP จนพัง
ส่วนที่อยู่ในรัศมีของ **ตัวป้องกัน** จะถูกซ่อม (heal) คืน — เพื่อบีบให้ผู้เล่นต้องดูแลฐานของตัวเอง

Buildings left without a protection device slowly **decay** and eventually break.
Buildings inside an active protection bubble are **healed** instead. All feedback is sent via chat.

## 🔑 Features

- 🧱 Decays **barricades & structures** server-wide, sliced across frames (no lag spikes)
- 🛡️ **4 protection methods**: Claim Flag · powered Generator · claimed Bed · Custom Item
- 👤 **Owner/group aware** (`RequireSameOwner`) — a stranger's device won't shield your base, and yours won't shield an enemy's
- 💬 **Chat-only**, fully bilingual **EN/TH** messages
- 🟢 **Protection-radius rings** — `/decay` draws an effect ring around your own/group devices showing their radius (sent only to you)
- 📍 **Real-time warning** the moment a player steps onto their own *unprotected* base (edge-triggered, no spam)
- 🩹 `{how}` placeholder auto-explains how to protect, based on live config
- ⚙️ Percentage or flat HP decay/heal, fully configurable
- 🔁 Hot-reloadable config (`/tcreload`)

## 🧠 How it works

1. Every `DamageInterval` / `HealingInterval` the engine starts a **sweep** of all buildings.
2. The sweep is **chunked across ticks** (`MaxBuildablesPerTick`) so a huge base count never stalls the server.
3. Before each sweep it scans for active **protection devices** → builds a list of protection bubbles.
4. For each building: inside a bubble (and owner/group matches, if `RequireSameOwner`) → **heal**; otherwise → **decay**.
5. When a building drops below `WarnHealthThreshold` or is destroyed, the online owner is notified (with cooldown).
6. Separately, a **presence check** warns a player the instant they enter their own unprotected base.

Everything runs on the single Unity main thread (the SDG building APIs are not thread-safe).

---

## 📦 Requirements

- Unturned **Dedicated Server**
- **RocketMod** (LDM / RocketModFix)
- Built against: Unturned `3.26.3.2`, RocketMod `4.9.3.18` (other recent versions should work)

## 🚀 Installation

1. Download `ToolCupboard.dll` (from `bin/` or a release).
2. Place it in your server's `Rocket/Plugins/ToolCupboard/` folder.
3. Start the server once — RocketMod auto-generates `ToolCupboard.configuration.xml`.
4. (Optional) edit the config, then `/tcreload`.

## 🛠️ Build from source

```bash
git clone https://github.com/songrit0/Unturned_ToolCupboard.git
cd Unturned_ToolCupboard
```

Edit the Unturned/RocketMod paths in `ToolCupboard.csproj` (and `build.ps1`) if your install differs, then:

```bash
# With the .NET SDK:
dotnet build ToolCupboard.csproj -c Release

# Without the .NET SDK (uses Roslyn csc from Visual Studio + game DLLs directly):
powershell -ExecutionPolicy Bypass -File build.ps1
```

Output: `bin/ToolCupboard.dll`

---

## ⌨️ Commands

| Command | Aliases | Permission | Description |
|---------|---------|------------|-------------|
| `/decay` | — | `toolcupboard.decay` | Check whether your current spot is protected; also draws protection-radius rings around your own nearby devices |
| `/toolcupboardreload` | `/tcreload` | `toolcupboard.reload` | Reload config from disk + rebuild engine |

Add `toolcupboard.decay` to your players' group in `Rocket/Permissions.config.xml` (admins with `*` already have everything):

```xml
<Group Id="default">
  <Permissions>
    <Permission>toolcupboard.decay</Permission>
  </Permissions>
</Group>
```

---

## ⚙️ Configuration (`ToolCupboard.configuration.xml`)

### `<Decay>` — unprotected buildings lose HP
| Field | Default | Meaning |
|-------|---------|---------|
| `DamageInterval` | `3600` | Seconds between decay passes (each building hit once per pass) |
| `UsePercentage` | `true` | `true` = % of max HP, `false` = flat HP |
| `DamagePerInterval` | `5` | % (or HP) removed per pass — effective minimum 1 HP |
| `MaxBuildablesPerTick` | `200` | Buildings processed per FixedUpdate (anti-lag throttle) |

### `<Healing>` — protected buildings regenerate
| Field | Default | Meaning |
|-------|---------|---------|
| `HealingInterval` | `1800` | Seconds between healing passes |
| `UsePercentage` | `true` | Same as Decay |
| `HealingPerInterval` | `10` | % (or HP) restored per pass (never above max) |

### `<Protection>`
| Field | Default | Meaning |
|-------|---------|---------|
| `RequireSameOwner` | `true` | Only protect buildings of the same owner/group as the device |
| `UseClaimFlags` / `ClaimFlagRadius` | `false` / `32` | Claim Flag protection |
| `UseGenerators` / `GeneratorRadius` / `RequireFuel` | `true` / `16` / `false` | Powered generator (optionally needs fuel) |
| `UseBeds` / `BedRadius` / `RequireClaimed` | `false` / `16` / `true` | Bed (optionally must be claimed as spawn) |
| `CustomItems` | empty | `<CustomItem Id="1234" Radius="20" />` entries |

> **Default ships with Generator-only protection.** Flip `UseClaimFlags` / `UseBeds` to `true` to enable the rest.

### `<Visual>` — protection-radius ring on `/decay`
When a player runs `/decay`, a horizontal ring of effect points is drawn around each of **their own
(or their group's)** protection devices nearby, showing the exact protection radius. The ring is sent
**only to the caller** (via `SetRelevantPlayer`), so other players never see it and it never reveals enemy bubbles.

| Field | Default | Meaning |
|-------|---------|---------|
| `ShowProtectionRings` | `true` | Master toggle for the `/decay` ring |
| `RingEffectId` | `130` | EffectAsset id used for each ring point — **verify it exists on your server** |
| `RingDisplayRange` | `48` | Only draw rings for your devices within this many metres of you |
| `RingDurationSeconds` | `5` | How long the ring keeps re-drawing after `/decay` |
| `RingInterval` | `0.5` | Seconds between ring re-draws (smaller = smoother, more packets) |
| `RingYOffset` | `0.5` | Vertical offset of the ring relative to the device |
| `RingPointSpacing` | `2.5` | Target metres between points; point count scales with radius |
| `RingMaxPoints` | `64` | Hard cap on points per ring (network safety) |

> An unknown `RingEffectId` logs a warning once and the ring is skipped — pick a valid EffectAsset id.

### Other
| Field | Default | Meaning |
|-------|---------|---------|
| `IncludeVehicleBarricades` | `false` | Also affect barricades placed on vehicles |
| `BypassItemIds` | empty | `<Id>1158</Id>` — item ids that never decay |
| `WarnHealthThreshold` | `50` | Warn the owner once a part drops below this % HP |
| `WarnCooldown` | `300` | Min seconds between HP-drop warnings per owner |
| `WarnOnBaseEnter` | `true` | Warn the moment a player enters their own **unprotected** base |
| `PresenceCheckInterval` | `3` | Seconds between presence checks |
| `BaseNearRadius` | `8` | How close (m) to your own building counts as "on base" |

### Messages
Each `Msg…` has `Text` + `Color` (name or `#RRGGBB`) and supports inline rich text (`<color>`, `<b>`).

| Message | Placeholders | When |
|---------|--------------|------|
| `MsgDecaying` | `{count}` `{how}` | HP-drop warning to owner |
| `MsgDestroyed` | `{count}` `{how}` | Parts destroyed by decay |
| `MsgStatusProtected` | `{type}` | `/decay` while protected |
| `MsgStatusUnprotected` | `{how}` | `/decay` **and** the on-base-enter warning |
| `MsgReloaded` | — | `/tcreload` confirmation |

**Placeholders:** `{count}` = number of parts · `{type}` = protecting device · `{how}` = auto-generated "how to protect" hint built from the enabled methods + radii.

---

## 💬 Two kinds of warnings

1. **Entered unprotected base** (`WarnOnBaseEnter`) — instant, edge-triggered the moment you step near your own building in an unprotected spot. Silent while protected; won't repeat until you leave and return.
2. **HP dropping** (`MsgDecaying` / `WarnHealthThreshold`) — fired during a decay pass when parts get low, with a cooldown.

Example (Generator-only config):
```
⚠ NOT protected - buildings here will decay! | จุดนี้ไม่มีการป้องกัน สิ่งก่อสร้างจะผุ!  How to protect: turn on a Generator within 16m / เปิดเครื่องปั่นไฟในระยะ 16m
```

---

## 🧪 Testing tips

- Temporarily set `DamageInterval` low (e.g. `20`) and `WarnHealthThreshold` high (e.g. `95`) to see decay/warnings fast — then revert.
- Test a building with **no protection device nearby**, or it will simply heal and stay silent.
- During live testing, add valuable items to `BypassItemIds` (or lower `DamagePerInterval`) so real bases aren't wrecked.

> ⚠️ Adding new config fields? Delete the old `ToolCupboard.configuration.xml` (or add the new fields manually) so RocketMod regenerates defaults — missing fields load as `false`/`0`, not their intended defaults.

---

## 📁 Project structure

```
ToolCupboardPlugin.cs          Main plugin (RocketPlugin<TConfig>, FixedUpdate driver)
ToolCupboardConfiguration.cs   Config schema + defaults
Models/  Message.cs, ProtectionSource.cs
Services/  DecayEngine.cs (core), ChatNotifier.cs, PresenceNotifier.cs, RingDisplayService.cs
Commands/  CommandDecay.cs, CommandToolCupboardReload.cs
build.ps1 · ToolCupboard.csproj · plugin.xml
```

## 📜 License

MIT — feel free to use and modify.

## 🙌 Credits

Inspired by [RestoreMonarchy's Tool Cupboard](https://restoremonarchy.com/plugins/toolcupboard).
Built by **imaximum.tech**.
