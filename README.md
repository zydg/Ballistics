**English** · [简体中文](README.zh-CN.md) · [Русский](README.ru.md)

# 弹道学 · Ballistics

A client-side BepInEx 5 plugin for SPT that visualises what the game itself computes:
bullet trajectories with per-hit penetration/damage colouring, explosion damage bands,
stun rings, rigid-body (grenade / flare) flight paths, and an in-hand grenade throw
prediction that follows the vanilla throw maths. Client only — no server component, no
game data is modified.

- Compatibility: SPT 4.1.6 (EFT 0.16.9.40743), BepInEx 5.4.23.5, HarmonyX
- Optional: BepInEx.ConfigurationManager (colour swatches in the config UI)
- Plugin name: `zydg666-Ballistics` · GUID `com.zydg666.ballistics`
- Languages: **Auto by default** (system UI culture, falls back to English), plus 简体中文 and Русский (`Lang/*.json`), selected with `General → Language`

## Fair-play notice

> **The prediction features can affect game balance.** `Prediction → GrenadeArc`, `TrajectoryPreview`,
> `BlastPreview`, `PreviewInner` and `PreviewStun` show information the game does not put on screen by
> default (where a grenade will land, what the held weapon's trajectory looks like, where a flying
> grenade will explode). **All of them are off by default**; enable them in the `Prediction` section of
> the config if you want them, and consider whether that fits the server or community you play on.

> **Compatibility.** It was tested with more than 100 community mods installed at the same time and no
> incompatibility showed up. If you do run into a conflict, please report it.

> **Made with AI assistance.** The code and the documentation were written with an AI assistant. If you
> spot something wrong, inconsistent or missing, please report it — feedback of any kind is welcome.

Everything else (recording completed shots, blast ranges after detonation, hit statistics) only replays
what the game has already computed and does not change your information advantage beyond hindsight.

## Install

1. Close the game.
2. Extract the archive into the game root (next to `EscapeFromTarkov.exe`) so that you get:

   ```
   BepInEx/plugins/Ballistics/Ballistics.dll
   BepInEx/plugins/Ballistics/Lang/zh.json
   BepInEx/plugins/Ballistics/Lang/ru.json
   ```

3. Start the game. Config appears at `BepInEx/config/com.zydg666.ballistics.cfg`.

Uninstall: delete `BepInEx/plugins/Ballistics` (optionally also the cfg and `BepInEx/config/Ballistics`).

## Keys

| Key | Action |
|---|---|
| F8 | Clear every trail and range (configurable) |

## Language

`General → Language` accepts `Auto` (default), `English`, `简体中文`, `Русский`.

- `Auto` is the shipped default: it follows the system UI culture (zh → 简体中文, ru → Русский).
- Anything unrecognised — including a missing/unreadable `Lang` folder — falls back to English.
- English strings are built into the DLL; other languages are read from
  `BepInEx/plugins/Ballistics/Lang/<code>.json` (flat `{"key":"text"}` map). Missing keys fall
  back to English, then to the raw key name.
- Changing the language only affects **newly bound** text; the panel and the config labels switch
  immediately, and only a config *description* needs a game restart to update.

## Configuration

Generated at `BepInEx/config/com.zydg666.ballistics.cfg`. Sections are `General`,
`Trajectory`, `Explosives`, `Prediction`, `Debug`; keys are stable ASCII identifiers, the
description text follows the selected language.

**General**

| Key | Default | Description |
|---|---|---|
| `Language` | `Auto` | UI language: Auto / English / 简体中文 / Русский. The panel switches immediately; restart the game to refresh config descriptions. |
| `Enabled` | `true` | Master switch: turning it off draws nothing. |
| `ClearKey` | `KeyCode.F8` | Remove every trail and range from the screen. |
| `OnlyLocalShots` | `true` | Draw only shots fired by the local player. |

**Trajectory**

| Key | Default | Description |
|---|---|---|
| `BulletTrails` | `true` | Draw the trajectory the bullet actually flies. |
| `TrailAnimation` | `true` | On = revealed point by point along the game's ballistic points (you see the bullet fly); off = the whole trajectory is drawn instantly when the shot is fired. |
| `AnimationSpeed` | `1.0` | Playback speed: 1.0 = real time, 0.5 = half speed (easier to follow), 2.0 = double. |
| `TrailLifetime` | `2.0` | Seconds a trail stays on screen: 0 = forever, 2 = two seconds; setting it to 0.01 gives a tracer-like effect. |
| `MaxTrails` | `0` | Concurrent trails: 0 = no limit (internal cap 256), N = keep the newest N. Over the cap the oldest trail is released first. |
| `ColorBy` | `Penetration` | What the colour represents: Penetration / Damage / Off (use Base color). |
| `LineWidth` | `0.01f` | Line thickness in metres. |
| `TrailAlpha` | `1f` | Trail alpha, 1 = opaque, 0.3 = translucent. |
| `ColorSelf` | `#1E3A8A` | Trail colour for your own shots. |
| `ColorScav` | `#FF8C1A` | Trail colour for Scav / Savage shots. |
| `ColorPmc` | `#FF2D2D` | Trail colour for PMC shots. |
| `ColorBoss` | `#FF69B4` | Trail colour for Boss (and followers) shots. |
| `StepPalette` | `#3FA9F5,#2FC48C,#7CD44B,#D8D23A,#F0A92E,#F07A2E,#E8452E,#D22E52,#C64BB0,#9A5BD6,#FFFFFF` | One hex colour per step, comma separated; more steps = finer bands. |
| `PenetrationScale` | `100f` | Absolute-value colouring scale: penetration at this value shows the last colour. |
| `DamageScale` | `500f` | Absolute-value colouring scale: damage at this value shows the last colour. |
| `BaseColor` | `#FF0000` | Fallback colour, used when Color by = Off. |
| `RigidbodyTrails` | `true` | Record flight paths of grenades, flares and other rigid bodies. |
| `RigidbodyColor` | `#FFB733` | Colour of rigid-body trails (also used by the grenade arc preview). |

**Explosives**

| Key | Default | Description |
|---|---|---|
| `DamageBands` | `true` | Three damage shells (full / direct / any) instead of one volume. |
| `Ranges` | `true` | Draw explosion ranges of grenades and explosive rounds. |
| `RangeAlpha` | `0.18f` | Opacity of the explosion volume. |
| `RangeColor` | `#FF4D1A` | Colour of the outer range. |
| `InnerRange` | `true` | Draw the full-damage range inside MinExplosionDistance. |
| `InnerAlpha` | `0.45f` | Opacity of the inner range. |
| `InnerColor` | `#FFC73F` | Colour of the inner range. |
| `StunRing` | `false` | Draw the stun radius ring. |
| `StunColor` | `#FFE659` | Colour of the stun ring. |
| `GeometryClipping` | `true` | On = clipped by walls; off = plain sphere. |
| `GeometryDensity` | `20` | Mesh segments 8–40; higher = tighter to walls and more rays, and setting it too high can hurt performance. |

> Prediction entries are **off by default** — see the fair-play notice at the top: they can affect game balance.


**Prediction**

| Key | Default | Description |
|---|---|---|
| `GrenadeArc` | `false` | Draw the throw arc while a grenade is primed. |
| `ArcSteps` | `60` | Arc steps 10–126; higher = smoother, more accurate landing. |
| `ArcColor` | `#FFB733` | Colour of the predicted arc. |
| `ArcWidth` | `0.012f` | Arc line width in metres. |
| `TrajectoryPreview` | `false` | Predict the held weapon's trajectory live; off by default. |
| `BlastPreview` | `false` | Show a flying grenade's blast range; off by default. |
| `PreviewInner` | `true` | Also preview the full-damage inner range. |
| `PreviewStun` | `false` | Also preview the stun range. |

**Debug**

| Key | Default | Description |
|---|---|---|
| `ShowOverlayPanel` | `false` | Show the information panel on screen (first entry in this section). |
| `PanelFontSize` | `16` | Text size in pixels. |
| `PanelX` | `20` | Panel left edge in pixels from the screen left. |
| `PanelY` | `10` | Panel top edge in pixels from the screen top. |
| `PanelBackground` | `true` | Draw a dark box behind the text. |
| `PanelTextColor` | `#FFFFFF` | Hex colour, e.g. #FFFFFF. |
| `VerboseLog` | `false` | Log every hit and decision; off by default. |

## Drawing model (matches the vanilla game)

| Step | Source |
|---|---|
| Sampling | Last entry of `Shot.PositionHistory` after `Shot.UpdateBulletPosition` |
| Termination | `HittedBallisticCollider != null` or `AmmoLifeTime` exceeded |
| Hit point | `Shot.HitPoint` (`_hit.point`), trail end snapped to the bullet hole |
| Penetration test | The game's own stop signal (`BlockedBy` / `DeflectedBy` / `IsShotFinished`); when the same chain hits again the previous point counts as passed through |
| Colouring | `DamageInfo.PenetrationPower` / `Damage` at each hit ÷ the ammo's initial value → stepped band |
| Child shots | Registered in a `Shot.Create` patch, values read from the child shot itself |

Explosion ranges are drawn only when the vanilla conditions hold: `MaxExplosionDistance > 0.1`,
`FragmentIndex == 0`, `IsFirstHit`, `TimeSinceShot >= FuzeArmTimeSec`.

Grenade arc: release point, direction and launch speed are all measured from the vanilla throw path
(impulse semantics Δv = force / mass); the player's velocity is added and the origin is advanced by the
measured 0.475 s release delay.

## Known limitations

- Vanilla adds `Random.onUnitSphere × (0.5 − stamina)` to the throw direction (up to ±5.7°), so a single landing point cannot be predicted exactly.
- Penetrating thin materials (glass, thin sheet metal) costs well under 1% of the remaining value; the stepped band correctly shows no colour change.
- The live blast preview can lag the recorded range by up to 0.25 m (refresh throttling).
- Flares only record their flight path, not an explosion.

## Development structure

The same sources produce two builds, switched with `build.ps1 -Config`:

- **Release (fixed shell, for distribution)**: `Loader.cs + Impl.cs + Lang.cs + LangEn.g.cs + Version.cs`
  compiled with `/define:STATIC_BUILD` into a **single `Ballistics.dll`**; all logic is inside and no
  assembly is loaded at runtime.
- **Debug (shell + hot reload, for development)**: two pieces — `Ballistics.dll` is only the shell
  (entry point, config, panel) while `Impl.cs` is compiled separately into
  `BepInEx/plugins/Ballistics/Impl/BallisticsImpl.bytes`; the shell loads it with `Assembly.Load(bytes)`
  on start-up and on `Debug → ReloadKey`, calling `Config.Reload()` and `UnpatchSelf()` first, so code
  changes do not need a game restart.

`LangEn.g.cs` is generated from `lang/en.json` at build time (that is why English is compiled in);
`Lang/zh.json` and `Lang/ru.json` are read at runtime, so they only need a reload.

Source files:

- `Loader.cs` — the shell: `BaseUnityPlugin` entry point, binds `General.Language` and the `Debug`
  panel settings, implements both the static and the hot-reload load path, and hosts `ShellHost`
  (`Update` drives the implementation tick, `OnGUI` draws the panel).
- `Impl.cs` — the implementation: every Harmony patch (`Shot.Create`, `UpdateBulletPosition`,
  `HandleCollision`, penetration degradation, child shots, grenade and explosion, rigid-body tracking,
  local-player detection), trail recording and colouring, panel text and the remaining config entries.
- `Lang.cs` — language resolution (Auto → system culture → English fallback) and the flat JSON reader.
- `lang/*.json` — translations; `en.json` is the single English source and is compiled into the DLL as
  generated `LangEn.g.cs`.
- `build.ps1` — generates `Version.cs` / `LangEn.g.cs`, then compiles, installs and packages according
  to `-Config`.
