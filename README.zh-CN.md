[English](README.md) · **简体中文** · [Русский](README.ru.md)

# 弹道学 · Ballistics

SPT 客户端 BepInEx 5 插件：把游戏自己算出来的弹道、爆炸范围、眩晕范围、刚体飞行路径画出来，
并提供与原版投掷逻辑一致的手雷抛物线预测。仅客户端，不改任何游戏数据。

- 兼容：SPT 4.1.6（EFT 0.16.9.40743）、BepInEx 5.4.23.5、HarmonyX
- 插件名：`zydg666-Ballistics` · GUID `com.zydg666.ballistics`
- 可选：BepInEx.ConfigurationManager（配置界面显示选色块）
- 语言：**默认 Auto**（跟随系统语言，取不到用英文），另有简体中文、Русский（`Lang/*.json`），在 `General → Language` 选择

## 注意事项

> **预测功能可能影响游戏平衡性。** `Prediction → GrenadeArc`、`TrajectoryPreview`、`BlastPreview`、
> `PreviewInner`、`PreviewStun` 会显示游戏本来不直接给玩家的信息（手雷落点、手持武器的弹道走向、
> 飞行中手雷的爆炸范围）。**这些功能默认全部关闭**，需要时在配置的 `Prediction` 分组里自行开启，
> 并自行判断是否符合你所在服务器或社区的要求。

> **兼容性。** 测试时同时装了 100 多个社区常用 mod，暂未发现兼容性问题；如果你遇到冲突，欢迎反馈。

> **AI 参与开发。** 本 mod 的代码与文档由 AI 辅助完成，若发现疏漏、错误或前后不一致的地方，欢迎反馈。

其余功能（已射击的弹道回放、爆炸后的真实范围、命中统计）只是把游戏已经算好的结果画出来，
不改变你在开火前的信息优势。

## 安装

1. 关闭游戏；
2. 解压到游戏根目录（与 `EscapeFromTarkov.exe` 同级），得到：

   ```
   BepInEx/plugins/Ballistics/Ballistics.dll
   BepInEx/plugins/Ballistics/Lang/zh.json
   BepInEx/plugins/Ballistics/Lang/ru.json
   ```

3. 启动游戏，配置生成在 `BepInEx/config/com.zydg666.ballistics.cfg`。

卸载：删除 `BepInEx/plugins/Ballistics`（可选一并删除 cfg 与 `BepInEx/config/Ballistics`）。

## 按键

| 按键 | 作用 |
|---|---|
| F8 | 清屏（可改） |

## 语言

`General → Language` 可选 `Auto`（默认）、`English`、`简体中文`、`Русский`。

- **默认 Auto**：跟随系统界面语言（zh → 简体中文，ru → Русский）；
- 无法识别时回退英文，包括 `Lang` 文件夹缺失或读不到的情况；
- 英文内置在 DLL；其它语言从 `BepInEx/plugins/Ballistics/Lang/<code>.json` 读取（扁平 `{"键":"文本"}`），
  缺项回退英文，再回退键名；
- 切换语言只影响**之后绑定**的文本；面板与配置标签立即切换，配置描述需重启游戏才更新。

## 配置

生成于 `BepInEx/config/com.zydg666.ballistics.cfg`，分组 `General`、`Trajectory`、
`Explosives`、`Prediction`、`Debug`；键名为稳定英文标识，说明文字随语言变化。

**General**

| Key | Default | Description |
|---|---|---|
| `Language` | `Auto` | 界面语言：Auto / English / 简体中文 / Русский。面板立即切换，配置描述需重启游戏才更新。 |
| `Enabled` | `true` | 总开关：关闭后本插件不绘制任何内容。 |
| `ClearKey` | `KeyCode.F8` | 清除屏幕上所有轨迹与爆炸范围。 |
| `OnlyLocalShots` | `true` | 只绘制本机玩家开出的子弹。 |

**Trajectory**

| Key | Default | Description |
|---|---|---|
| `BulletTrails` | `true` | 绘制子弹实际飞行的弹道。 |
| `TrailAnimation` | `true` | 开 = 沿游戏弹道点逐帧揭开（能看到子弹飞过）；关 = 射击瞬间整条弹道一次画出。 |
| `AnimationSpeed` | `1.0` | 播放速度：1.0 = 真实速度，0.5 = 半速（便于看清），2.0 = 双倍。 |
| `TrailLifetime` | `2.0` | 轨迹/范围停留秒数：0 = 永久，2 = 两秒；设为 0.01 时可以实现类似曳光弹的效果。 |
| `MaxTrails` | `0` | 同屏轨迹上限：0 = 不限（内部 256），N = 只留最新 N 条；超出时从最早生成的轨迹开始清除。 |
| `ColorBy` | `Penetration` | 着色依据：穿透力 / 伤害 / 关闭（用弹道颜色）。 |
| `LineWidth` | `0.01f` | 线条粗细（米）。 |
| `TrailAlpha` | `1f` | 弹道透明度，1 = 不透明，0.3 = 半透明。 |
| `ColorSelf` | `#1E3A8A` | 自己开枪时的弹道颜色。 |
| `ColorScav` | `#FF8C1A` | Scav / Savage 的弹道颜色。 |
| `ColorPmc` | `#FF2D2D` | PMC 的弹道颜色。 |
| `ColorBoss` | `#FF69B4` | Boss（含随从）的弹道颜色。 |
| `StepPalette` | `#3FA9F5,#2FC48C,#7CD44B,#D8D23A,#F0A92E,#F07A2E,#E8452E,#D22E52,#C64BB0,#9A5BD6,#FFFFFF` | 每档一个十六进制颜色，逗号分隔；档数越多分层越细。 |
| `PenetrationScale` | `100f` | 绝对值上色量程：穿透力达到该值时显示为末端颜色。 |
| `DamageScale` | `500f` | 绝对值上色量程：伤害达到该值时显示为末端颜色。 |
| `BaseColor` | `#FF0000` | 回退颜色，着色依据 = 关闭 时使用。 |
| `RigidbodyTrails` | `true` | 记录手雷、信号弹等物理刚体的飞行路径。 |
| `RigidbodyColor` | `#FFB733` | 物理刚体轨迹颜色（抛物线预测线也用它）。 |

**Explosives**

| Key | Default | Description |
|---|---|---|
| `DamageBands` | `true` | 三层伤害壳（满伤/直面冲击/有伤害），替代单层体。 |
| `Ranges` | `true` | 绘制爆炸物（手雷、爆炸子弹）的爆炸范围。 |
| `RangeAlpha` | `0.18f` | 爆炸范围体积的不透明度。 |
| `RangeColor` | `#FF4D1A` | 外层范围颜色。 |
| `InnerRange` | `true` | 绘制 MinExplosionDistance 内的满伤害范围。 |
| `InnerAlpha` | `0.45f` | 内径范围的不透明度。 |
| `InnerColor` | `#FFC73F` | 内径范围颜色。 |
| `StunRing` | `false` | 绘制眩晕半径环。 |
| `StunColor` | `#FFE659` | 眩晕圈颜色。 |
| `GeometryClipping` | `true` | 开 = 按墙体裁剪；关 = 完整球体。 |
| `GeometryDensity` | `20` | 网格分段 8–40；越大越贴合墙体、射线越多，数值设置过高可能会影响性能。 |

> Prediction 分组默认全部关闭 —— 见开头的「注意事项」：这些功能可能影响游戏平衡性。


**Prediction**

| Key | Default | Description |
|---|---|---|
| `GrenadeArc` | `false` | 拔销时绘制投掷弧线。 |
| `ArcSteps` | `60` | 弧线步数 10–126；越大越平滑、落点越准。 |
| `ArcColor` | `#FFB733` | 预测线颜色。 |
| `ArcWidth` | `0.012f` | 抛物线线宽（米）。 |
| `TrajectoryPreview` | `false` | 实时预测手持武器弹道；默认关闭。 |
| `BlastPreview` | `false` | 显示飞行中手雷的爆炸范围；默认关闭。 |
| `PreviewInner` | `true` | 预测时同时显示内径（满伤害）范围。 |
| `PreviewStun` | `false` | 预测时同时显示眩晕范围。 |

**调试**

| 键 | 默认 | 说明 |
|---|---|---|
| `ShowOverlayPanel` | `false` | 是否在屏幕上显示信息面板（本组第一项）。 |
| `PanelFontSize` | `16` | 面板字号（像素）。 |
| `PanelX` | `20` | 面板左边距（像素）。 |
| `PanelY` | `10` | 面板上边距（像素）。 |
| `PanelBackground` | `true` | 是否绘制深色底板。 |
| `PanelTextColor` | `#FFFFFF` | 文字颜色（十六进制）。 |
| `VerboseLog` | `false` | 记录每次命中与判定；默认关闭。 |

## 绘制口径（与原版对齐）

| 环节 | 依据 |
|---|---|
| 采样 | `Shot.UpdateBulletPosition` 后取 `PositionHistory` 最后一条 |
| 终止 | `HittedBallisticCollider != null` 或超过 `AmmoLifeTime` |
| 命中点 | `Shot.HitPoint`，线尾对齐弹孔 |
| 穿透判定 | 游戏自己的停止信号（`BlockedBy` / `DeflectedBy` / `IsShotFinished`）；同一弹链后续再命中即视为上一点已穿透 |
| 着色 | 命中时 `DamageInfo.PenetrationPower` / `Damage` ÷ 弹药初始值 → 阶梯色带 |
| 子弹出 | `Shot.Create` 补丁注册，数值取子弹出自身字段 |

爆炸范围仅在原版条件成立时绘制：`MaxExplosionDistance > 0.1`、`FragmentIndex == 0`、
`IsFirstHit`、`TimeSinceShot >= FuzeArmTimeSec`。

手雷抛物线：出手点、方向、初速均为原版投掷路径实测（冲量语义 Δv = force / mass），
叠加玩家移动速度，起点按实测 0.475 s 出手延迟前移。

## 已知限制

- 原版投掷方向带 `Random.onUnitSphere × (0.5 − 耐力)` 随机项（最大 ±5.7°），落点无法精确预测；
- 穿透玻璃等薄材料损耗 <1%，阶梯色带不变化是正确表现；
- 实时爆炸预览与记录范围最多相差 0.25 m（刷新节流）；
- 信号弹只记录飞行路径。

## 开发结构

同一份源码出两种产物，用 `build.ps1 -Config` 切换：

- **Release（固定壳，发布用）**：`Loader.cs + Impl.cs + Lang.cs + LangEn.g.cs + Version.cs` 加
  `/define:STATIC_BUILD` 编成**单个 `Ballistics.dll`**，逻辑全在里面，运行时不加载任何外部程序集。
- **Debug（壳 + 热重载，开发用）**：分成两份 —— `Ballistics.dll` 只做壳（入口、配置、面板），
  `Impl.cs` 单独编成 `BepInEx/plugins/Ballistics/Impl/BallisticsImpl.bytes`；壳在启动时和按
  `Debug → ReloadKey` 时用 `Assembly.Load(bytes)` 载入实现，载入前先 `Config.Reload()` 并
  `UnpatchSelf()` 撤掉旧补丁，所以改完代码不用退游戏。

`LangEn.g.cs` 由 `lang/en.json` 在构建时生成（英文因此编进 DLL），`Lang/zh.json`、`Lang/ru.json`
运行时读取，改完只需重载。

源文件分工：

- `Loader.cs` —— 壳：`BaseUnityPlugin` 入口、`General.Language` 与 `Debug` 面板配置的绑定、
  静态/热重载两条加载路径、`ShellHost`（`Update` 驱动实现每帧 Tick，`OnGUI` 画面板）。
- `Impl.cs` —— 实现：全部 Harmony 补丁（`Shot.Create`、`UpdateBulletPosition`、`HandleCollision`、
  穿透衰减、子弹出、手雷与爆炸、刚体追踪、本地玩家识别）、轨迹记录与着色、面板文本、其余配置项。
- `Lang.cs` —— 语言解析（Auto → 系统文化 → 回退英文）与扁平 JSON 读取。
- `lang/*.json` —— 翻译文件；`en.json` 是英文唯一来源，构建时生成 `LangEn.g.cs` 编进 DLL。
- `build.ps1` —— 生成 `Version.cs`/`LangEn.g.cs`，按 `-Config` 编译、安装、打包。
