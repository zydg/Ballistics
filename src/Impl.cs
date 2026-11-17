using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using Dsh.BallisticsDebug.Shared;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Dsh.BallisticsDebug.Impl
{
    public static class ImplPlugin
    {
        public static object Install(BaseUnityPlugin host, ManualLogSource log, Harmony harmony, int reloadSeq)
        {
            try { return new ImplContext(host, log, harmony, reloadSeq); }
            finally { Patches._installPhase = false; }
        }
    }

    public class ImplContext
    {
        private readonly ManualLogSource _log;
        private readonly Harmony _harmony;
        internal static float InstalledAt;
        internal static string Version = "?";
        internal static int ReloadSeq;
        private static EventHandler _blastCfgHandler;

        internal static ConfigEntry<bool> ModOn;
        internal static ConfigEntry<bool> BulletTrail;
        internal static ConfigEntry<bool> TrailAnim;
        internal static ConfigEntry<float> TrailAnimSpeed;
        internal static ConfigEntry<float> TrailLife;
        internal static ConfigEntry<int> TrailMax;
        internal static ConfigEntry<string> TrailColorBy;
        internal static ConfigEntry<string> StepColors;
        internal static ConfigEntry<string> Language;
        internal static ConfigEntry<float> TrailWidth;
        internal static ConfigEntry<string> TrailColor;
        internal static ConfigEntry<float> TrailAlpha;
        internal static ConfigEntry<bool> ThrowPredict;
        internal static ConfigEntry<int> ThrowSteps;
        // ------------------------- 颜色配置：选色块 -------------------------
        private static readonly string[] PaletteColors = new string[]
        {
            "#FF4D1A", "#FFB733", "#FFE659", "#46E05A", "#2FC48C", "#3FA9F5",
            "#1E3A8A", "#9A5BD6", "#C64BB0", "#FF2D2D", "#FF69B4", "#FF8C1A",
            "#FFFFFF", "#C8C8C8", "#808080", "#202020", "#000000", "#7CD44B",
            "#D8D23A", "#F07A2E", "#D22E52", "#00E5FF", "#00FF88", "#B0FF3A"
        };

        // 给 ConfigurationManager 用的选色块抽屉：点色块即写入十六进制字符串
        internal static void ColorDrawer(ConfigEntryBase entry)
        {
            try
            {
                string cur = entry.BoxedValue as string ?? "#FFFFFF";
                GUILayout.BeginVertical();
                GUILayout.Label(entry.Definition.Key + "  " + cur);
                for (int i = 0; i < PaletteColors.Length; i += 12)
                {
                    GUILayout.BeginHorizontal();
                    for (int k = i; k < i + 12 && k < PaletteColors.Length; k++)
                    {
                        string hex = PaletteColors[k];
                        Color c; if (!ColorUtility.TryParseHtmlString(hex, out c)) c = Color.white;
                        Color old = GUI.backgroundColor;
                        GUI.backgroundColor = c;
                        if (GUILayout.Button(cur == hex ? "o" : " ", GUILayout.Width(20f), GUILayout.Height(20f)))
                            entry.BoxedValue = hex;
                        GUI.backgroundColor = old;
                    }
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndVertical();
            }
            catch (Exception) { }
        }

        // 反射挂 ConfigurationManager 的 CustomDrawer；未装该插件时返回 null，配置退化为普通文本
        private static object ColorAttr()
        {
            try
            {
                Type t = Type.GetType("ConfigurationManager.ConfigurationManagerAttributes, ConfigurationManager");
                if (t == null) return null;
                object o = Activator.CreateInstance(t);
                PropertyInfo p = t.GetProperty("CustomDrawer");
                if (p != null) p.SetValue(o, new Action<ConfigEntryBase>(ColorDrawer), null);
                return o;
            }
            catch (Exception) { return null; }
        }

        internal static ConfigDescription ColorDesc(string desc)
        {
            object attr = ColorAttr();
            return attr == null ? new ConfigDescription(desc) : new ConfigDescription(desc, null, attr);
        }

        internal static ConfigEntry<string> ThrowColor;
        internal static ConfigEntry<float> ThrowWidth;
        internal static ConfigEntry<bool> BlastBandsOn;
        internal static ConfigEntry<string> RoleSelfColor;
        internal static ConfigEntry<string> RoleScavColor;
        internal static ConfigEntry<string> RolePmcColor;
        internal static ConfigEntry<string> RoleBossColor;
        internal static ConfigEntry<float> PenMax;
        internal static ConfigEntry<float> DmgMax;
        internal static ConfigEntry<bool> PhysOn;
        internal static ConfigEntry<string> PhysColor;

        internal static ConfigEntry<bool> BlastOn;
        internal static ConfigEntry<float> BlastAlpha;
        internal static ConfigEntry<string> BlastColor;
        internal static ConfigEntry<bool> InnerOn;
        internal static ConfigEntry<float> InnerAlpha;
        internal static ConfigEntry<string> InnerColor;
        internal static ConfigEntry<bool> StunOn;
        internal static ConfigEntry<string> StunColor;
        internal static ConfigEntry<bool> GeomOn;
        internal static ConfigEntry<int> GeomSegments;

        internal static ConfigEntry<bool> OnlyMine;
        internal static ConfigEntry<KeyCode> ResetKey;
        internal static ConfigEntry<bool> LiveTrail;
        internal static ConfigEntry<bool> LiveBlast;
        internal static ConfigEntry<bool> LiveInner;
        internal static ConfigEntry<bool> LiveStun;

        internal static ConfigEntry<bool> DebugLog;

        public ImplContext(BaseUnityPlugin host, ManualLogSource log, Harmony harmony, int reloadSeq)
        {
            Patches._installPhase = true;
            _log = log; _harmony = harmony;
            InstalledAt = Time.realtimeSinceStartup;
            ReloadSeq = reloadSeq;
            try { Version = typeof(ImplContext).Assembly.GetName().Version.ToString(); } catch (Exception) { }

            // ---------- 常规 General ----------
            Language = host.Config.Bind("General", "Language", "Auto", new ConfigDescription(Lang.T("cfg.language"), new AcceptableValueList<string>("Auto", "English", "简体中文", "Русский")));
            Lang.Configure(Language.Value);
            Lang.Init(System.IO.Path.Combine(Paths.PluginPath, System.IO.Path.Combine("Ballistics", "Lang")));
            ModOn = host.Config.Bind("General", "Enabled", true, new ConfigDescription(Lang.T("cfg.general.enabled")));
            ResetKey = host.Config.Bind("General", "ClearKey", KeyCode.F8, new ConfigDescription(Lang.T("cfg.general.clearKey")));
            OnlyMine = host.Config.Bind("General", "OnlyLocalShots", true, new ConfigDescription(Lang.T("cfg.general.onlyLocalShots")));

            // ---------- 弹道 Trajectory：先开关，再外观 ----------
            BulletTrail = host.Config.Bind("Trajectory", "BulletTrails", true, new ConfigDescription(Lang.T("cfg.trajectory.bulletTrails")));
            TrailAnim = host.Config.Bind("Trajectory", "TrailAnimation", true, new ConfigDescription(Lang.T("cfg.trajectory.trailAnimation")));
            PhysOn = host.Config.Bind("Trajectory", "RigidbodyTrails", true, new ConfigDescription(Lang.T("cfg.trajectory.rigidbodyTrails")));
            TrailColorBy = host.Config.Bind("Trajectory", "ColorBy", "Penetration", new ConfigDescription(Lang.T("cfg.trajectory.colorBy"), new AcceptableValueList<string>("Penetration", "Damage", "Off")));
            StepColors = host.Config.Bind("Trajectory", "StepPalette", "#3FA9F5,#2FC48C,#7CD44B,#D8D23A,#F0A92E,#F07A2E,#E8452E,#D22E52,#C64BB0,#9A5BD6,#FFFFFF", Lang.T("cfg.trajectory.stepPalette"));
            TrailColor = host.Config.Bind("Trajectory", "BaseColor", "#FF0000", ColorDesc(Lang.T("cfg.trajectory.baseColor")));
            PenMax = host.Config.Bind("Trajectory", "PenetrationScale", 100f, new ConfigDescription(Lang.T("cfg.trajectory.penetrationScale"), new AcceptableValueRange<float>(1f, 200f)));
            DmgMax = host.Config.Bind("Trajectory", "DamageScale", 500f, new ConfigDescription(Lang.T("cfg.trajectory.damageScale"), new AcceptableValueRange<float>(1f, 400f)));
            TrailAnimSpeed = host.Config.Bind("Trajectory", "AnimationSpeed", 1f, new ConfigDescription(Lang.T("cfg.trajectory.animationSpeed"), new AcceptableValueRange<float>(0.01f, 2f)));
            TrailLife = host.Config.Bind("Trajectory", "TrailLifetime", 2f, new ConfigDescription(Lang.T("cfg.trajectory.trailLifetime"), new AcceptableValueRange<float>(0f, 600f)));
            TrailMax = host.Config.Bind("Trajectory", "MaxTrails", 0, new ConfigDescription(Lang.T("cfg.trajectory.maxTrails"), new AcceptableValueRange<int>(0, 4096)));
            TrailWidth = host.Config.Bind("Trajectory", "LineWidth", 0.01f, new ConfigDescription(Lang.T("cfg.trajectory.lineWidth"), new AcceptableValueRange<float>(0.001f, 0.2f)));
            TrailAlpha = host.Config.Bind("Trajectory", "TrailAlpha", 1f, new ConfigDescription(Lang.T("cfg.trajectory.trailAlpha"), new AcceptableValueRange<float>(0.05f, 1f)));
            RoleSelfColor = host.Config.Bind("Trajectory", "ColorSelf", "#1E3A8A", ColorDesc(Lang.T("cfg.trajectory.colorSelf")));
            RoleScavColor = host.Config.Bind("Trajectory", "ColorScav", "#FF8C1A", ColorDesc(Lang.T("cfg.trajectory.colorScav")));
            RolePmcColor = host.Config.Bind("Trajectory", "ColorPmc", "#FF2D2D", ColorDesc(Lang.T("cfg.trajectory.colorPmc")));
            RoleBossColor = host.Config.Bind("Trajectory", "ColorBoss", "#FF69B4", ColorDesc(Lang.T("cfg.trajectory.colorBoss")));
            PhysColor = host.Config.Bind("Trajectory", "RigidbodyColor", "#FFB733", ColorDesc(Lang.T("cfg.trajectory.rigidbodyColor")));

            // ---------- 爆炸物 Explosives：先开关，再外观 ----------
            BlastOn = host.Config.Bind("Explosives", "Ranges", true, new ConfigDescription(Lang.T("cfg.explosives.ranges")));
            InnerOn = host.Config.Bind("Explosives", "InnerRange", true, new ConfigDescription(Lang.T("cfg.explosives.innerRange")));
            StunOn = host.Config.Bind("Explosives", "StunRing", false, new ConfigDescription(Lang.T("cfg.explosives.stunRing")));
            BlastBandsOn = host.Config.Bind("Explosives", "DamageBands", true, Lang.T("cfg.explosives.damageBands"));
            BlastAlpha = host.Config.Bind("Explosives", "RangeAlpha", 0.18f, new ConfigDescription(Lang.T("cfg.explosives.rangeAlpha"), new AcceptableValueRange<float>(0.02f, 0.8f)));
            BlastColor = host.Config.Bind("Explosives", "RangeColor", "#FF4D1A", ColorDesc(Lang.T("cfg.explosives.rangeColor")));
            InnerAlpha = host.Config.Bind("Explosives", "InnerAlpha", 0.45f, new ConfigDescription(Lang.T("cfg.explosives.innerAlpha"), new AcceptableValueRange<float>(0.02f, 1f)));
            InnerColor = host.Config.Bind("Explosives", "InnerColor", "#FFC73F", ColorDesc(Lang.T("cfg.explosives.innerColor")));
            StunColor = host.Config.Bind("Explosives", "StunColor", "#FFE659", ColorDesc(Lang.T("cfg.explosives.stunColor")));
            GeomOn = host.Config.Bind("Explosives", "GeometryClipping", true, new ConfigDescription(Lang.T("cfg.explosives.geometryClipping")));
            GeomSegments = host.Config.Bind("Explosives", "GeometryDensity", 20, new ConfigDescription(Lang.T("cfg.explosives.geometryDensity"), new AcceptableValueRange<int>(8, 40)));

            // ---------- 预测 Prediction：先开关，再外观 ----------
            ThrowPredict = host.Config.Bind("Prediction", "GrenadeArc", false, Lang.T("cfg.prediction.grenadeArc"));
            LiveTrail = host.Config.Bind("Prediction", "TrajectoryPreview", false, new ConfigDescription(Lang.T("cfg.prediction.trajectoryPreview")));
            LiveBlast = host.Config.Bind("Prediction", "BlastPreview", false, new ConfigDescription(Lang.T("cfg.prediction.blastPreview")));
            LiveInner = host.Config.Bind("Prediction", "PreviewInner", true, new ConfigDescription(Lang.T("cfg.prediction.previewInner")));
            LiveStun = host.Config.Bind("Prediction", "PreviewStun", false, new ConfigDescription(Lang.T("cfg.prediction.previewStun")));
            ThrowSteps = host.Config.Bind("Prediction", "ArcSteps", 60, new ConfigDescription(Lang.T("cfg.prediction.arcSteps"), new AcceptableValueRange<int>(10, 126)));
            ThrowWidth = host.Config.Bind("Prediction", "ArcWidth", 0.012f, new ConfigDescription(Lang.T("cfg.prediction.arcWidth"), new AcceptableValueRange<float>(0.001f, 0.2f)));
            ThrowColor = host.Config.Bind("Prediction", "ArcColor", "#FFB733", ColorDesc(Lang.T("cfg.prediction.arcColor")));

            // ---------- 调试 Debug：面板开关放在本组第一 ----------
            AssignLoader("ShowOverlay", host.Config.Bind("Debug", "ShowOverlayPanel", false, new ConfigDescription(Lang.T("cfg.debug.showOverlayPanel"))));
            AssignLoader("FontSize", host.Config.Bind("Debug", "PanelFontSize", 16, new ConfigDescription(Lang.T("cfg.debug.panelFontSize"), new AcceptableValueRange<int>(8, 48))));
            AssignLoader("PosX", host.Config.Bind("Debug", "PanelX", 20, new ConfigDescription(Lang.T("cfg.debug.panelX"), new AcceptableValueRange<int>(0, 3840))));
            AssignLoader("PosY", host.Config.Bind("Debug", "PanelY", 10, new ConfigDescription(Lang.T("cfg.debug.panelY"), new AcceptableValueRange<int>(0, 2160))));
            AssignLoader("Background", host.Config.Bind("Debug", "PanelBackground", true, Lang.T("cfg.debug.panelBackground")));
            AssignLoader("TextColor", host.Config.Bind("Debug", "PanelTextColor", "#FFFFFF", Lang.T("cfg.debug.panelTextColor")));
            DebugLog = host.Config.Bind("Debug", "VerboseLog", false, new ConfigDescription(Lang.T("cfg.debug.verboseLog")));

            // 爆炸可视化开关一变就清场，避免“关了配置但旧的范围/眩晕环还留在原地”
            if (_blastCfgHandler != null)
            {
                BlastOn.SettingChanged -= _blastCfgHandler;
                InnerOn.SettingChanged -= _blastCfgHandler;
                StunOn.SettingChanged -= _blastCfgHandler;
                BlastBandsOn.SettingChanged -= _blastCfgHandler;
            }
            _blastCfgHandler = (s2, e2) => Patches.ClearBlastVisuals();
            BlastOn.SettingChanged += _blastCfgHandler;
            InnerOn.SettingChanged += _blastCfgHandler;
            StunOn.SettingChanged += _blastCfgHandler;
            BlastBandsOn.SettingChanged += _blastCfgHandler;
#if !STATIC_BUILD
            AssignLoader("_reloadKey", host.Config.Bind("Debug", "ReloadKey", KeyCode.F10, new ConfigDescription(Lang.T("cfg.debug.reloadKey"))));
#endif

            Patches.Cfg = host.Config;
            if (Patches.LangHandler != null) Language.SettingChanged -= Patches.LangHandler;
            Patches.LangHandler = (s3, e3) => Patches.RelabelConfig();
            Language.SettingChanged += Patches.LangHandler;
            Patches.RelabelConfig();
            Patches.Log = log;
            RegisterProvider();
            Warmup();
            Patch();
            Patches._installPhase = false;
            _log.LogInfo("[弹道学] install done v" + Version + " #" + reloadSeq + " tag=" + BuildInfo.Tag);
        }

        // 把 cfg 条目交给壳（Loader）用：两者可能是不同程序集，只能反射赋值
        private static void AssignLoader(string field, object value)
        {
            try
            {
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type t = a.GetType("Dsh.BallisticsDebug.Loader.Loader");
                    if (t == null) continue;
                    FieldInfo fi = t.GetField(field, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (fi != null) fi.SetValue(null, value);
                    return;
                }
            }
            catch (Exception) { }
        }

        private void RegisterProvider()
        {
            try
            {
                Type shell = FindShell();
                if (shell == null) { _log.LogWarning("[弹道学] Shell type not found"); return; }
                FieldInfo tf = shell.GetField("Text", BindingFlags.Public | BindingFlags.Static);
                if (tf != null) tf.SetValue(null, new Func<string>(Patches.OverlayText));
                FieldInfo tk = shell.GetField("Tick", BindingFlags.Public | BindingFlags.Static);
                if (tk != null) tk.SetValue(null, new Action(Patches.Tick));
                _log.LogInfo("[弹道学] provider registered tag=" + BuildInfo.Tag);
            }
            catch (Exception e) { _log.LogWarning("[弹道学] provider failed: " + e.Message); }
        }

        private static Type FindShell()
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type c = a.GetType("Dsh.BallisticsDebug.Loader.Shell");
                if (c != null) return c;
            }
            return null;
        }

        private void Warmup()
        {
            try
            {
                LineRenderer lr = Patches.SpawnLine(2, Color.white, 0.01f);
                if (lr != null) { lr.SetPosition(0, Vector3.zero); lr.SetPosition(1, Vector3.forward); }
                Patches.KillLast();
            }
            catch (Exception e) { _log.LogWarning("[弹道学] warmup failed: " + e.Message); }
        }

        private void Patch()
        {
            Type calc = FindType("BallisticsCalculator");
            Type shot = FindType("Shot");
            Type collider = FindType("BallisticCollider");

            if (calc != null)
            {
                SafePatch(FindMethod(calc, "CreateShot", 8), "CreateShotPostfix", false);
                SafePatch(FindMethod(calc, "GetAmmoPenetrationPower", 3), "PenetrationPostfix", false);
                SafePatch(FindMethod(calc, "UpdateVisualBullets", 0), "TickPostfix", false);
            }
            else _log.LogWarning("[弹道学] BallisticsCalculator not found");

            if (shot != null)
            {
                SafePatch(FindMethod(shot, "UpdateBulletPosition", 1), "RecordStepPostfix", false);
                SafePatch(FindMethod(shot, "HandleCollision", 3), "HandleCollisionPostfix", false);
                MethodInfo degrade = FindMethod(shot, "ApplyPenetratedDegradation", 3);
                if (degrade != null)
                {
                    SafePatch(degrade, "PenDegradePre", true);
                    SafePatch(degrade, "PenDegradePost", false);
                }
                else _log.LogWarning("[弹道学] ApplyPenetratedDegradation not found");
                MethodInfo create = null;
                foreach (MethodInfo m in shot.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                    if (m.Name == "Create" && m.GetParameters().Length == 25) { create = m; break; }
                SafePatch(create, "ChildShotPostfix", false);
            }
            else _log.LogWarning("[弹道学] Shot not found");

            try
            {
                foreach (GameObject go0 in Resources.FindObjectsOfTypeAll<GameObject>())
                    if (go0 != null && go0.name == "dsh_throw") UnityEngine.Object.Destroy(go0);
            }
            catch (Exception) { }
            try { Patches.LoadThr(); } catch (Exception) { }

            Type grenade = FindType("Grenade");
            if (grenade != null)
            {
                string[] names = new string[] { "Explosion", "OnExplosion", "InvokeBlowUpEvent", "ExternalInvokeBlowUpEvent" };
                int patched = 0;
                foreach (MethodInfo m in grenade.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    bool want = false;
                    for (int i = 0; i < names.Length; i++) if (m.Name == names[i]) { want = true; break; }
                    if (!want) continue;
                    SafePatch(m, "GrenadeBlowupPostfix", false);
                    patched++;
                }
                foreach (MethodInfo m in grenade.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    if (m.Name == "GetDamageInfo") { SafePatch(m, "DamageInfoPostfix", false); patched++; }
                _log.LogInfo("[弹道学] grenade hooks: " + patched);

                SafePatch(FindMethod(grenade, "SetThrowForce", 1), "CatchThrowPrefix", true);
                Type bghc = FindType("BaseGrenadeHandsController");
                if (bghc != null)
                {
                    int nG = 0;
                    foreach (MethodInfo mm in bghc.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                        if (mm.Name == "ThrowGrenade") { SafePatch(mm, "CatchThrowGrenadePrefix", true); nG++; }
                    _log.LogInfo("[弹道学] ThrowGrenade hooks: " + nG);
                }
                else _log.LogWarning("[弹道学] BaseGrenadeHandsController not found");
                int trackOk = 0;
                trackOk += TrackHook(grenade, "Grenade");
                Type flare = FindType("FlareCartridge");
                if (flare != null) trackOk += TrackHook(flare, "FlareCartridge");
                _log.LogInfo("[弹道学] physics track hooks: " + trackOk + "/4");
            }
            else _log.LogWarning("[弹道学] Grenade type not found");

            Type exp = FindType("ExplosionSharedMethods");
            if (exp != null)
            {
                SafePatch(FindMethod(exp, "AffectPlayersShared", 0), "BlastVolumePrefix", true);
                SafePatch(FindMethod(exp, "Explosion", 0), "BlastVolumePrefix", true);
            }
            else _log.LogWarning("[弹道学] ExplosionSharedMethods not found");
            if (exp != null)
            {
                if (ImplContext.DebugLog != null && ImplContext.DebugLog.Value)
                {
                    DumpIL(exp, "AffectPlayersShared");
                    DumpIL(exp, "Explosion");
                }
            }

            if (collider != null) SafePatch(FindMethod(collider, "ApplyHit", 2), "ApplyHitPrefix", true);
            else _log.LogWarning("[弹道学] BallisticCollider not found");

            // 本地玩家：挂 EFT.LocalPlayer 的生命周期方法把玩家对象抓进缓存（零场景扫描）
            Type lpType = FindType("LocalPlayer");
            if (lpType != null)
            {
                int seeded = 0;
                foreach (MethodInfo m in lpType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (m.Name == "ManualUpdate") { SafePatch(m, "LocalPlayerSeedPostfix", false); seeded++; continue; }
                    if (m.GetParameters().Length != 0) continue;
                    if (m.Name != "CG_Create" && m.Name != "Awake" && m.Name != "Start") continue;
                    SafePatch(m, "LocalPlayerSeedPostfix", false);
                    seeded++;
                }
                _log.LogInfo("[弹道学] local player seed hooks: " + seeded);
                if (seeded == 0)
                {
                    string ms = "";
                    foreach (MethodInfo m in lpType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                        ms += m.Name + "(" + m.GetParameters().Length + "),";
                    _log.LogInfo("[弹道学] LocalPlayer methods: " + ms);
                }
            }
            else _log.LogWarning("[弹道学] LocalPlayer type not found");
        }

        internal void DumpIL(Type t, string methodName)
        {
            if (t == null) return;
            int n = 0;
            foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (m.Name != methodName) continue;
                MethodBody body;
                try { body = m.GetMethodBody(); } catch (Exception) { continue; }
                if (body == null) continue;
                byte[] il = body.GetILAsByteArray();
                n++;
                _log.LogInfo("[弹道学][IL] " + t.Name + "." + m.Name + " len=" + (il == null ? 0 : il.Length));
                if (il == null) continue;
                Module mod = m.Module;
                for (int i = 0; i < il.Length - 4; i++)
                {
                    byte op = il[i];
                    int tok = BitConverter.ToInt32(il, i + 1);
                    if (op == 0x28 || op == 0x6F || op == 0x73)
                    {
                        try { MethodBase mb = mod.ResolveMethod(tok); _log.LogInfo("[弹道学][IL]   call " + mb.DeclaringType.Name + "." + mb.Name); i += 4; }
                        catch (Exception) { }
                    }
                    else if (op == 0x7B || op == 0x7E || op == 0x7D || op == 0x80)
                    {
                        try { FieldInfo fi = mod.ResolveField(tok); _log.LogInfo("[弹道学][IL]   field " + fi.DeclaringType.Name + "." + fi.Name); i += 4; }
                        catch (Exception) { }
                    }
                }
            }
            if (n == 0) _log.LogInfo("[弹道学][IL] " + t.Name + "." + methodName + " not found");
        }
        private int TrackHook(Type t, string label)
        {
            int n = 0;
            string[] names = new string[] { "Update", "LateUpdate" };
            for (int i = 0; i < names.Length; i++)
            {
                MethodInfo m = FindMethod(t, names[i], 0);
                if (m == null) { _log.LogWarning("[弹道学] track target missing: " + label + "." + names[i]); continue; }
                SafePatch(m, "PhysicsTrackPostfix", false);
                n++;
            }
            return n;
        }
        private void SafePatch(MethodInfo target, string patchName, bool prefix)
        {
            if (target == null) { _log.LogWarning("[弹道学] target missing: " + patchName); return; }
            try
            {
                MethodInfo pm = typeof(Patches).GetMethod(patchName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (pm == null) { _log.LogError("[弹道学] patch method missing: " + patchName); return; }
                if (prefix) _harmony.Patch(target, new HarmonyMethod(pm));
                else _harmony.Patch(target, null, new HarmonyMethod(pm));
                _log.LogInfo("[弹道学] patched " + target.DeclaringType.Name + "." + target.Name);
            }
            catch (Exception e) { _log.LogError("[弹道学] patch failed " + target.Name + ": " + e.Message); }
        }

        private static readonly Dictionary<string, Type> _typeCache = new Dictionary<string, Type>(StringComparer.Ordinal);
        private static readonly HashSet<string> _typeMiss = new HashSet<string>(StringComparer.Ordinal);

        internal static Type FindType(string shortName)
        {
            if (shortName == null) return null;
            Type hit;
            if (_typeCache.TryGetValue(shortName, out hit)) return hit;
            if (_typeMiss.Contains(shortName)) return null;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }
                catch (Exception) { continue; }
                if (types == null) continue;
                foreach (Type t in types) { if (t != null && t.Name == shortName) { _typeCache[shortName] = t; return t; } }
            }
            if (!_typeCache.ContainsKey(shortName)) _typeMiss.Add(shortName);
            return null;
        }

        private static MethodInfo FindMethod(Type t, string name, int paramCount)
        {
            if (t == null) return null;
            foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (m.Name != name) continue;
                if (paramCount > 0 && m.GetParameters().Length != paramCount) continue;
                return m;
            }
            foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.Static))
            {
                if (m.Name != name) continue;
                if (paramCount > 0 && m.GetParameters().Length != paramCount) continue;
                return m;
            }
            return null;
        }

        public void Uninstall()
        {
            try { _harmony.UnpatchSelf(); } catch (Exception) { }
            Patches.Reset();
            try
            {
                Type shell = FindShell();
                if (shell != null)
                {
                    FieldInfo tf = shell.GetField("Text", BindingFlags.Public | BindingFlags.Static);
                    if (tf != null) tf.SetValue(null, null);
                    FieldInfo tk = shell.GetField("Tick", BindingFlags.Public | BindingFlags.Static);
                    if (tk != null) tk.SetValue(null, null);
                }
            }
            catch (Exception) { }
        }
    }

    internal static class Patches
    {
        internal static ManualLogSource Log;
        internal static BepInEx.Configuration.ConfigFile Cfg;
        internal static EventHandler LangHandler;

        // ConfigurationManager 的条目标签默认取配置键（英文），这里用 name.* 覆盖成当前语言
        internal static void RelabelConfig()
        {
            try
            {
                if (Cfg == null) return;
                FieldInfo descField = typeof(ConfigEntryBase).GetField("<Description>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
                if (descField == null) return;
                foreach (ConfigDefinition def in Cfg.Keys)
                {
                    ConfigEntryBase entry = Cfg[def];
                    if (entry == null) continue;
                    string id = def.Section == "General" && def.Key == "Language"
                        ? "name.language"
                        : "name." + def.Section.ToLowerInvariant() + "." + char.ToLowerInvariant(def.Key[0]) + def.Key.Substring(1);
                    string label = Lang.T(id);
                    if (string.Equals(label, id, StringComparison.Ordinal)) continue;
                    ConfigDescription old = descField.GetValue(entry) as ConfigDescription;
                    object attr = MakeCmAttr(label, old);
                    object[] tags = attr == null ? (old == null ? null : old.Tags)
                        : (old == null || old.Tags == null ? new object[] { attr } : AppendTag(old.Tags, attr));
                    descField.SetValue(entry, new ConfigDescription(old == null ? label : old.Description, old == null ? null : old.AcceptableValues, tags));
                }
            }
            catch (Exception) { }
        }

        private static object[] AppendTag(object[] tags, object attr)
        {
            List<object> list = new List<object>(tags.Length + 1);
            for (int i2 = 0; i2 < tags.Length; i2++) { object t2 = tags[i2]; if (t2 != null && t2.GetType().Name == "ConfigurationManagerAttributes") continue; list.Add(t2); }
            list.Add(attr);
            return list.ToArray();
        }

        // 造一个 ConfigurationManagerAttributes：DispName=本地化标签，并保留原来的 CustomDrawer
        private static object MakeCmAttr(string label, ConfigDescription old)
        {
            try
            {
                Type t = Type.GetType("ConfigurationManager.ConfigurationManagerAttributes, ConfigurationManager");
                if (t == null) return null;
                object o = Activator.CreateInstance(t);
                PropertyInfo pn = t.GetProperty("DispName");
                if (pn != null) pn.SetValue(o, label, null);
                if (old != null && old.Tags != null)
                {
                    for (int i2 = 0; i2 < old.Tags.Length; i2++)
                    {
                        object src = old.Tags[i2];
                        if (src == null || src.GetType() != t) continue;
                        PropertyInfo cd = t.GetProperty("CustomDrawer");
                        if (cd != null) cd.SetValue(o, cd.GetValue(src, null), null);
                    }
                }
                return o;
            }
            catch (Exception) { return null; }
        }
        // 安装期日志总是输出；运行期日志只在 [调试] 详细日志输出 打开时输出
        // 一条命中记录：材质/阈值/概率 + 穿透前后的穿透力与伤害 + 距离衰减
        internal sealed class HitRec
        {
            public string Mat = "-";
            public string Level = "?";
            public string Chance = "";
            public float PenBefore, PenAfter, DmgBefore, DmgAfter, FalloffPct;
            public bool Degraded;
            public int ShotId = -1;

            private static string Pct(float before, float after)
            {
                if (before <= 0f) return "";
                float d = (after - before) / before * 100f;
                return " (" + (d >= 0f ? "+" : "") + d.ToString("F1") + "%)";
            }

            // 单颗弹丸的紧凑写法：材质 + 等级 + 穿透力变化
            public string Short()
            {
                string s = MatName(Mat) + " " + Lang.T("ui.level") + " " + Level;
                if (Degraded) s += " " + PenBefore.ToString("F1") + "→" + PenAfter.ToString("F1");
                else s += " " + PenBefore.ToString("F1") + "(" + Lang.T("ui.notPenetrated") + ")";
                return s;
            }

            public string Render(int index)
            {
                string s = index + ". " + MatName(Mat) + "  " + Lang.T("ui.level") + " " + Level
                    + (string.IsNullOrEmpty(Chance) ? "" : "  (" + Lang.T("ui.chance") + " " + Chance + ")");
                if (Degraded)
                    s += "  " + Lang.T("ui.pen") + " " + PenBefore.ToString("F1") + "→" + PenAfter.ToString("F1") + Pct(PenBefore, PenAfter)
                        + "  " + Lang.T("ui.dmg") + " " + DmgBefore.ToString("F1") + "→" + DmgAfter.ToString("F1") + Pct(DmgBefore, DmgAfter);
                else
                    s += "  " + Lang.T("ui.pen") + " " + PenBefore.ToString("F1")
                        + "  " + Lang.T("ui.dmg") + " " + DmgBefore.ToString("F1")
                        + "  (" + Lang.T("ui.notPenetrated") + ")";
                s += "  " + Lang.T("ui.falloff") + " " + FalloffPct.ToString("F1") + "%";
                return s;
            }
        }

        // 详细日志(`Debug → VerboseLog`)打开时不再按条数截断；这里只留一个防刷爆上限
        private const int LogCap = 20000;
        internal static bool _installPhase;
        internal static void LogInfo(string s) { if (Log == null) return; if (!_installPhase && !(ImplContext.DebugLog != null && ImplContext.DebugLog.Value)) return; Log.LogInfo(s); }
        internal static void LogAlways(string s) { if (Log != null) Log.LogInfo(s); }
        internal static bool VerboseOn { get { return _installPhase || (ImplContext.DebugLog != null && ImplContext.DebugLog.Value); } }

        internal static int Shots, Traced, Hits, TickCount;
        internal static int SubShots;   // 由根弹出派生的子弹出（穿透/跳弹/碎片）
        private static readonly Dictionary<int, int> StepCount = new Dictionary<int, int>();   // 每发已处理步数
        internal static string LastAmmo = "-";
        internal static int LastPen = -1;
        internal static string LastMaterial = "-";
        internal static string LastThresholdLevel = "?";
        internal static string LastThresholdChance = "";
        internal static string LastThresholdHitType = "";
        internal static string LastAmmoRaw = "-";

        private static readonly List<LineRenderer> Spawned = new List<LineRenderer>();
        private static readonly Dictionary<LineRenderer, int> PtsByLine = new Dictionary<LineRenderer, int>();   // 每条线当前点数
        private static readonly List<LineRenderer> Free = new List<LineRenderer>();
        private static readonly List<float> Expiry = new List<float>();
        private static readonly List<int> PointsPer = new List<int>();
        private static readonly List<GameObject> Blasts = new List<GameObject>();
        private static readonly List<float> BlastExpiry = new List<float>();
        private static readonly Dictionary<int, LineRenderer> Trails = new Dictionary<int, LineRenderer>();
        private static readonly Dictionary<int, int> TrailPts = new Dictionary<int, int>();
        private static readonly Dictionary<int, float> TrailSeen = new Dictionary<int, float>();
        private static readonly Dictionary<int, object> TrailShot = new Dictionary<int, object>();
        private static readonly Dictionary<object, int> ShotId = new Dictionary<object, int>();
        private static readonly Dictionary<int, Vector3> StartPos = new Dictionary<int, Vector3>();
        private static readonly Dictionary<int, bool> ClosedByHit = new Dictionary<int, bool>();
        private static readonly Dictionary<int, int> VolleyOfShot = new Dictionary<int, int>();      // 弹丸 -> 齐射编号
        private static readonly Dictionary<int, List<int>> VolleyShots = new Dictionary<int, List<int>>(); // 齐射 -> 弹丸 id
        private static int _nextVolley;
        private static int _volleyFrame = -1;
        private static string _volleyAmmo = "";
        internal static int LastVolley = -1;
        private static readonly Queue<int> _volleyOrder = new Queue<int>();
        private static readonly Dictionary<int, int> ParentOf = new Dictionary<int, int>();          // 子弹出 -> 父弹出
        private static readonly Dictionary<int, List<HitRec>> ChainHits = new Dictionary<int, List<HitRec>>();  // 弹链根 -> 命中记录
        private static readonly Dictionary<object, HitRec> LastRecByShot = new Dictionary<object, HitRec>();   // Shot -> 最近一条命中记录
        private static readonly Dictionary<object, float> _degPenBefore = new Dictionary<object, float>();
        private static readonly Dictionary<object, float> _degDmgBefore = new Dictionary<object, float>();
        internal static int LastChainRoot = -1;
        internal static int LastChainTotal;
        private static readonly Dictionary<int, float> PendingHitAt = new Dictionary<int, float>();
        private static readonly Dictionary<int, bool> Grenade = new Dictionary<int, bool>();
        private static readonly Dictionary<int, int> Pen = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> Dmg = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> PenBase = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> DmgBase = new Dictionary<int, int>();
        private static readonly Dictionary<int, List<float>> PenHist = new Dictionary<int, List<float>>();
        private static readonly Dictionary<int, List<float>> DmgHist = new Dictionary<int, List<float>>();
        private static readonly Dictionary<int, List<float>> RealPen = new Dictionary<int, List<float>>();   // 命中时游戏算出的穿透力
        private static readonly Dictionary<int, List<float>> RealDmg = new Dictionary<int, List<float>>();   // 命中时游戏算出的伤害
        private static readonly Dictionary<int, List<int>> RealAt = new Dictionary<int, List<int>>();       // 对应轨迹点索引
        private static readonly Dictionary<int, float[]> RatioCurve = new Dictionary<int, float[]>();
        private static readonly Dictionary<int, float> PenScale = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> DmgScale = new Dictionary<int, float>();
        private static readonly Dictionary<int, Vector3[]> PlayPos = new Dictionary<int, Vector3[]>();
        private static readonly Dictionary<int, float> PlayIdx = new Dictionary<int, float>();
        private static readonly Dictionary<int, bool> FullMode = new Dictionary<int, bool>();   // 关闭动画：整条弹道一次铺满
        private static readonly HashSet<LineRenderer> _active = new HashSet<LineRenderer>();
        private static readonly Dictionary<object, string> _degBefore = new Dictionary<object, string>();
        internal static string LastDegrade = "-";
        internal static string LastFalloffId = "";
        internal static float LastFalloffSpeed = -1f;
        internal static float LastFalloffFactor = 1f;
        internal static float LastHitPen = -1f;
        internal static string LastHitMat = "-";
        internal static string LastHitLevel = "-";
        internal static float LastHitLevelNum = -1f;
        private static int _coordLogs;
        private static int _ownerLogs;
        private static readonly Dictionary<int, Vector3> PendingHit = new Dictionary<int, Vector3>();
        private static int _castLogs;
        private static int _childLogs;
        private static int _speedLogs;
        private static int _hcLogs;
        private static int _oddLogs;
        private static int _lifeLogs;
        internal static float LastHitDmg = -1f;
        internal static int DegradeCount;
        private static int TotalPoints;
        private static int _nextId;
        private static Shader _shader;
        private static Material _mat;
        private static int _errors;

        private static void SafeLog(string s) { _errors++; if (_errors < 30) LogAlways("[弹道学] " + s); }

        // 反射结果按 (类型, 成员名) 缓存：实时绘制每帧要读十几个成员，未缓存时每次都是
        // GetProperty + GetField + GetMethod 三次查找
        private sealed class MemberSlot
        {
            public PropertyInfo P;
            public FieldInfo F;
            public MethodInfo M;
        }

        private static readonly Dictionary<Type, Dictionary<string, MemberSlot>> _memberCache
            = new Dictionary<Type, Dictionary<string, MemberSlot>>();

        internal static object GetMember(object obj, string name)
        {
            if (obj == null || name == null) return null;
            Type t = obj.GetType();
            Dictionary<string, MemberSlot> map;
            if (!_memberCache.TryGetValue(t, out map))
            {
                map = new Dictionary<string, MemberSlot>(StringComparer.Ordinal);
                _memberCache[t] = map;
            }
            MemberSlot slot;
            if (!map.TryGetValue(name, out slot))
            {
                slot = new MemberSlot();
                try
                {
                    PropertyInfo p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (p != null && p.GetIndexParameters().Length == 0) slot.P = p;
                    if (slot.P == null)
                    {
                        FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (f != null) slot.F = f;
                    }
                    if (slot.P == null && slot.F == null)
                    {
                        MethodInfo mi = t.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                        if (mi != null && mi.ReturnType != typeof(void)) slot.M = mi;
                    }
                }
                catch (Exception) { }
                map[name] = slot;
            }
            try
            {
                if (slot.P != null) return slot.P.GetValue(obj, null);
                if (slot.F != null) return slot.F.GetValue(obj);
                if (slot.M != null) return slot.M.Invoke(obj, null);
            }
            catch (Exception) { }
            return null;
        }

        internal static object GetMember(object obj, params string[] names)
        {
            if (obj == null || names == null) return null;
            for (int i = 0; i < names.Length; i++)
            {
                object v = GetMember(obj, names[i]);
                if (v != null) return v;
            }
            return null;
        }

        private static bool TryVector(object obj, out Vector3 v, params string[] names)
        {
            object o = GetMember(obj, names);
            if (o is Vector3) { v = (Vector3)o; return true; }
            v = Vector3.zero; return false;
        }

        // EFT 的 Name/ShortName 是本地化 key（形如 "601949593ae8f707c4608daa ShortName"），
        // 真名要查本地化表。这里只在不是 key 时采用，取不到就退回模板短 id。
        private static bool LooksLikeKey(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length < 16) return false;
            int hex = 0;
            for (int i = 0; i < s.Length && i < 24; i++)
            {
                char ch = s[i];
                bool h = (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F');
                if (!h) break;
                hex++;
            }
            return hex >= 16;
        }

        private static readonly HashSet<string> _ammoDumped = new HashSet<string>();

        private static string AmmoLabel(object ammo)
        {
            if (ammo == null) return "?";
            object tpl = GetMember(ammo, "Template", "AmmoTemplate");
            string[] names = new string[] { "ShortName", "shortName", "Name", "name", "_name" };
            for (int pass = 0; pass < 2; pass++)
            {
                object src = pass == 0 && tpl != null ? tpl : ammo;
                if (src == null) continue;
                for (int i = 0; i < names.Length; i++)
                {
                    string v = GetMember(src, names[i]) as string;
                    if (string.IsNullOrEmpty(v) || LooksLikeKey(v)) continue;
                    return v;
                }
            }
            object id = GetMember(tpl ?? ammo, "TemplateId", "StringId", "_id");
            string sid = id == null ? "?" : id.ToString();
            return sid.Length > 8 ? sid.Substring(0, 8) : sid;
        }

        // 打开详细日志时，把弹药的各个名称字段原样打出来（便于确认哪个才是真名）
        internal static void AmmoDump(object ammo)
        {
            if (ammo == null || ImplContext.DebugLog == null || !ImplContext.DebugLog.Value) return;
            object tpl = GetMember(ammo, "Template", "AmmoTemplate");
            string key = (GetMember(tpl ?? ammo, "TemplateId", "StringId", "_id") ?? "?").ToString();
            if (_ammoDumped.Contains(key) || _ammoDumped.Count > 40) return;
            _ammoDumped.Add(key);
            LogInfo("[弹道学][AMMO] " + key
                + " tplName=" + (GetMember(tpl, "Name") as string ?? "-")
                + " tplShort=" + (GetMember(tpl, "ShortName") as string ?? "-")
                + " tpl_name=" + (GetMember(tpl, "_name") as string ?? "-")
                + " itemName=" + (GetMember(ammo, "Name") as string ?? "-")
                + " itemShort=" + (GetMember(ammo, "ShortName") as string ?? "-"));
        }

        internal static bool ByDamage()
        {
            if (ImplContext.TrailColorBy == null) return false;
            string v = ImplContext.TrailColorBy.Value ?? "";
            return v == "Damage" || v == "伤害";
        }

        internal static int ValueOfPen(int id)
        {
            float f; if (PenScale.TryGetValue(id, out f) && f > 0f) return (int)Math.Round(f);
            int v; return Pen.TryGetValue(id, out v) ? v : 0;
        }

        internal static int ValueOfDmg(int id)
        {
            float f; if (DmgScale.TryGetValue(id, out f) && f > 0f) return (int)Math.Round(f);
            int v; return Dmg.TryGetValue(id, out v) ? v : 0;
        }


        internal static readonly Dictionary<int, int> ShotRole = new Dictionary<int, int>();
        private static int _roleLogs;

        internal static bool IsLocalShot(object shot)
        {
            string lpid = LocalProfileId();
            object pidObj = GetMember(shot, "PlayerProfileID", "ProfileId");
            string spid = pidObj as string;
            return lpid != null && spid != null && lpid == spid;
        }

        internal static int RoleOfPlayer(object pl)
        {
            if (pl == null) return 2;
            try
            {
                object prof = GetMember(pl, "Profile");
                object info = prof == null ? null : GetMember(prof, "Info");
                if (info == null) return 2;
                object st = GetMember(info, "Settings");
                if (st != null)
                {
                    object isBoss = GetMember(st, "IsBoss");
                    if (isBoss is bool && (bool)isBoss) return 3;
                    object ro = GetMember(st, "Role");
                    if (ro != null)
                    {
                        string rs = ro.ToString().ToLowerInvariant();
                        if (rs.Contains("boss") || rs.Contains("follower")) return 3;
                    }
                }
                object side = GetMember(info, "Side");
                string sd = side == null ? "" : side.ToString().ToLowerInvariant();
                if (sd.Contains("pmc")) return 2;
                return 1;
            }
            catch (Exception) { return 2; }
        }

        internal static Color RoleColor(int role)
        {
            ConfigEntry<string> e = role == 0 ? ImplContext.RoleSelfColor
                : role == 1 ? ImplContext.RoleScavColor
                : role == 3 ? ImplContext.RoleBossColor
                : ImplContext.RolePmcColor;
            Color dflt = role == 0 ? new Color(0.12f, 0.23f, 0.54f)
                : role == 1 ? new Color(1f, 0.55f, 0.1f)
                : role == 3 ? new Color(1f, 0.41f, 0.71f)
                : new Color(1f, 0.18f, 0.18f);
            string hex = e == null ? null : e.Value;
            return ParseHex(hex, dflt, 1f);
        }

        // 预测线：按"自己角色色 + 当前弹药数值"取色
        // 预测线：与记录线走同一套着色（同一数值必得同一颜色）
        internal static Color LivePredictColor(object tpl)
        {
            int by = ColorByKind();
            float v = 0f;
            object o = GetMember(tpl, by == 1 ? "Damage" : "PenetrationPower");
            if (o != null) { try { v = Convert.ToSingle(o); } catch (Exception) { } }
            return ColorForValue(0, v, false);
        }
        internal static Color BaseColorOfKey(int key)
        {
            // 着色依据 = 关闭：按角色取色；预测线（key<=0）当作“自己”
            int role = 0;
            if (key > 0)
            {
                if (!ShotRole.TryGetValue(key, out role)) return TrailBaseC();   // 角色未知时用兜底单色
            }
            return RoleColor(role);
        }
        // 按"剩余百分比 0~100"取色：10% 一档，与弹药绝对数值无关
        internal static Color ColorForPct(int key, float pct)
        {
            int by = ColorByKind();
            if (by == 2) return A(BaseColorOfKey(key));
            float n = Mathf.Max(1f, StepPaletteCount() - 1);
            float step = 100f / n;
            float pc = Mathf.Clamp(pct, 0f, 100f);
            float b = Mathf.Floor(pc / step);
            float frac = Mathf.Clamp01((pc - b * step) / step);
            return A(Color.Lerp(StepPalette((int)b), StepPalette((int)b + 1), frac));
        }
        internal static Color ColorForValue(int key, float v, bool gren)
        {
            if (gren) return A(new Color(1f, 0.92f, 0.2f));
            int by = ColorByKind();
            if (by == 2 || v <= 0f) return A(BaseColorOfKey(key));
            // 阶梯着色：档距 = 量程 / (色带档数-1)，整条色带覆盖 0..量程 且不重复
            float scale2 = by == 1
                ? (ImplContext.DmgMax == null ? 500f : Mathf.Max(1f, ImplContext.DmgMax.Value))
                : (ImplContext.PenMax == null ? 100f : Mathf.Max(1f, ImplContext.PenMax.Value));
            float step = scale2 / Mathf.Max(1f, StepPaletteCount() - 1);
            float b = Mathf.Floor(v / step);
            float frac = Mathf.Clamp01((v - b * step) / step);
            Color c1 = StepPalette((int)b);
            Color c2 = StepPalette((int)b + 1);
            return A(Color.Lerp(c1, c2, frac));
        }

        private static string _stepCacheRaw;
        private static Color[] _stepCache;

        // 每 10 点一档的色带（配置：逗号分隔的十六进制）
        internal static int StepPaletteCount()
        {
            StepPalette(0);
            return _stepCache == null ? 1 : _stepCache.Length;
        }
        internal static Color StepPalette(int idx)
        {
            string raw = ImplContext.StepColors == null ? "" : (ImplContext.StepColors.Value ?? "");
            if (_stepCache == null || (!ReferenceEquals(raw, _stepCacheRaw) && raw != _stepCacheRaw))
            {
                _stepCacheRaw = raw;
                string[] parts = raw.Split(',');
                List<Color> list = new List<Color>();
                for (int i = 0; i < parts.Length; i++)
                {
                    string h = parts[i].Trim();
                    if (h.Length < 6) continue;
                    list.Add(ParseHex(h, Color.white, 1f));
                }
                if (list.Count == 0) { list.Add(Color.cyan); list.Add(Color.white); }
                _stepCache = list.ToArray();
            }
            int n = _stepCache.Length;
            return _stepCache[((idx % n) + n) % n];
        }
        private static string _byCacheStr;
        private static int _byCacheVal;

        // 着色依据 / 着色方式：配置字符串只在变化时比较一次（0=穿透力 1=伤害 2=关闭）
        internal static int ColorByKind()
        {
            string s = ImplContext.TrailColorBy == null ? "Penetration" : (ImplContext.TrailColorBy.Value ?? "Penetration");
            if (!ReferenceEquals(s, _byCacheStr) && s != _byCacheStr)
            {
                _byCacheStr = s;
                _byCacheVal = (s == "Off" || s == "关闭") ? 2 : ((s == "Damage" || s == "伤害") ? 1 : 0);
            }
            return _byCacheVal;
        }

        private static FieldInfo _partVelField;

        // 读弹道表：每一档的速度保留比 = |StartVelocity| / InitialSpeed
        internal static float[] BuildRatioCurve(object shot)
        {
            try
            {
                object calc = GetMember(shot, "TrajectoryInfo");
                if (calc == null) return null;
                IList hist = GetMember(calc, "history") as IList;
                if (hist == null || hist.Count == 0) return null;

                float init = 0f;
                object oi = GetMember(shot, "InitialSpeed");
                if (oi != null) { try { init = Convert.ToSingle(oi); } catch (Exception) { } }
                if (init <= 0f) return null;

                float[] arr = new float[hist.Count];
                for (int i = 0; i < hist.Count; i++)
                {
                    object part = hist[i];
                    if (part == null) { arr[i] = 1f; continue; }
                    if (_partVelField == null || _partVelField.DeclaringType != part.GetType())
                        _partVelField = part.GetType().GetField("StartVelocity",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (_partVelField == null) return null;
                    object v = _partVelField.GetValue(part);
                    if (!(v is Vector3)) { arr[i] = 1f; continue; }
                    float sp = ((Vector3)v).magnitude;
                    arr[i] = Mathf.Clamp01(sp / init);
                }
                if (_speedLogs < 12)
                {
                    _speedLogs++;
                    LogInfo("[弹道学][SPEED] init=" + init.ToString("F1")
                        + " v0=" + (arr[0] * init).ToString("F1")
                        + " ratio0=" + arr[0].ToString("F3")
                        + " ratioN=" + arr[arr.Length - 1].ToString("F3")
                        + " steps=" + arr.Length);
                }
                return arr;            }
            catch (Exception e) { SafeLog("BuildRatioCurve: " + e.Message); return null; }
        }

        private static int _curveIdx;

        internal static int CurveIndex(object shot)
        {
            float t = 0f;
            object ot = GetMember(shot, "TimeSinceShot");
            if (ot != null) { try { t = Convert.ToSingle(ot); } catch (Exception) { } }
            int idx = (int)Math.Floor(t / 0.01f);
            return idx < 0 ? 0 : idx;
        }

        // 按"命中时游戏算出的穿透力/伤害"分段上色；没有命中数据时退回初值×速度衰减
        // 分段上色：按命中真实值；签名未变则直接返回，避免每帧重建 Gradient
        private static readonly Dictionary<int, int> GradSig = new Dictionary<int, int>();
        private static readonly Dictionary<int, Gradient> GradCache = new Dictionary<int, Gradient>();
        private static readonly Dictionary<int, GradientColorKey[]> GradKeyBuf = new Dictionary<int, GradientColorKey[]>();
        private static readonly GradientAlphaKey[] GradAlphaBuf = new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) };

        internal static void ApplyTrailColors(LineRenderer plr, int key, int n, float baseV)
        {
            List<float> rv;
            bool noHit = !RealPen.TryGetValue(key, out rv) || rv.Count == 0;
            int hits = noHit ? 0 : rv.Count;
            int sig = (n >> 3) * 131 + hits;   // 每 8 个顶点才重建一次渐变，降低大量弹道时的开销
            int prevSig;
            if (GradSig.TryGetValue(key, out prevSig) && prevSig == sig) return;   // 没有新点/新命中，不用重建
            GradSig[key] = sig;

            if (noHit)
            {
                float fNow = FactorAtIndex(key, n - 1);
                plr.startColor = ColorForValue(key, baseV, false);
                plr.endColor = ColorForValue(key, baseV * fNow, false);
                return;
            }

            bool byDmg = ByDamage();
            List<float> vals = byDmg && RealDmg.ContainsKey(key) ? RealDmg[key] : rv;
            List<int> at;
            RealAt.TryGetValue(key, out at);

            int cnt = 1;
            for (int i = 0; i < vals.Count; i++) if (vals[i] > 0f) cnt++;
            if (cnt < 2)
            {
                plr.startColor = plr.endColor = ColorForPct(key, 100f);
                return;
            }

            GradientColorKey[] buf;
            if (!GradKeyBuf.TryGetValue(key, out buf) || buf.Length != cnt)
            {
                buf = new GradientColorKey[cnt];
                GradKeyBuf[key] = buf;
            }

            int w = 0;
            buf[w++] = new GradientColorKey(ColorForPct(key, 100f), 0f);
            float lastT = 0f;
            for (int i = 0; i < vals.Count && w < cnt; i++)
            {
                if (vals[i] <= 0f) continue;
                int idx = at != null && i < at.Count ? at[i] : n - 1;
                float t = Mathf.Clamp01((float)idx / Mathf.Max(1, n - 1));
                if (t <= lastT) t = Mathf.Min(1f, lastT + 0.001f);
                lastT = t;
                float pctRaw = 100f * Mathf.Clamp01(vals[i] / Mathf.Max(1f, baseV));
                buf[w++] = new GradientColorKey(ColorForPct(key, pctRaw), t);
            }

            Gradient g;
            if (!GradCache.TryGetValue(key, out g) || g == null)
            {
                g = new Gradient();
                GradCache[key] = g;
            }
            g.SetKeys(buf, GradAlphaBuf);
            plr.colorGradient = g;
        }
        internal static float FactorAtIndex(int key, int idx)
        {
            float[] cur;
            if (!RatioCurve.TryGetValue(key, out cur) || cur == null || cur.Length == 0) return 1f;
            if (idx < 0) idx = 0;
            if (idx >= cur.Length) idx = cur.Length - 1;
            float coef = 0f;
            object shot = TrailShot.ContainsKey(key) ? TrailShot[key] : null;
            if (shot != null)
            {
                object oc = GetMember(shot, "DamageDegradationCoefficient");
                if (oc != null) { try { coef = Convert.ToSingle(oc); } catch (Exception) { } }
            }
            float r = cur[idx];
            return r + (1f - r) * Mathf.Clamp01(coef);
        }

        internal static float FactorAtStep(int key, float fallback)
        {
            if (!RatioCurve.ContainsKey(key)) return fallback;
            return FactorAtIndex(key, _curveIdx);
        }

        // 用弹道表的 StartPosition 建立播放序列
        // 读出游戏自己的弹道点（TrajectoryInfo.history 每段的 StartPosition）
        internal static Vector3[] HistoryPoints(object shot)
        {
            try
            {
                object calc = GetMember(shot, "TrajectoryInfo");
                if (calc == null) return null;
                IList hist = GetMember(calc, "history") as IList;
                if (hist == null || hist.Count < 2) return null;
                FieldInfo fpos = null;
                Vector3[] pos = new Vector3[hist.Count];
                for (int i = 0; i < hist.Count; i++)
                {
                    object part = hist[i];
                    if (part == null) return null;
                    if (fpos == null || fpos.DeclaringType != part.GetType())
                        fpos = part.GetType().GetField("StartPosition",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (fpos == null) return null;
                    object v = fpos.GetValue(part);
                    if (!(v is Vector3)) return null;
                    pos[i] = (Vector3)v;
                }
                return pos;
            }
            catch (Exception) { return null; }
        }

        internal static bool StartAnimation(int key, object shot, LineRenderer lr)
        {
            if (ImplContext.TrailAnim == null || !ImplContext.TrailAnim.Value) return false;
            if (shot == null || lr == null) return false;
            try
            {
                Vector3[] pos = HistoryPoints(shot);
                if (pos == null || pos.Length < 3) return false;
                PlayPos[key] = pos;
                PlayIdx[key] = 0f;
                lr.positionCount = 2;
                lr.SetPosition(0, pos[0]);
                lr.SetPosition(1, pos[1]);
                return true;
            }
            catch (Exception e) { SafeLog("StartAnimation: " + e.Message); return false; }
        }

        // 关闭动画时：射击瞬间直接铺满整条弹道（游戏给多少点画多少点，后续点数增长时补齐）
        internal static bool FillFullTrail(int key, object shot, LineRenderer lr)
        {
            if (shot == null || lr == null) return false;
            Vector3[] pos = HistoryPoints(shot);
            if (pos == null || pos.Length < 2) return false;
            lr.positionCount = pos.Length;
            for (int i = 0; i < pos.Length; i++) lr.SetPosition(i, pos[i]);
            TrailPts[key] = pos.Length;
            if (PtsByLine.ContainsKey(lr)) TotalPoints += pos.Length - PtsByLine[lr];
            PtsByLine[lr] = pos.Length;
            FullMode[key] = true;
            bool byDmgF = ByDamage();
            float baseF = byDmgF
                ? (DmgScale.ContainsKey(key) ? DmgScale[key] : 0f)
                : (PenScale.ContainsKey(key) ? PenScale[key] : 0f);
            if (baseF > 0f) ApplyTrailColors(lr, key, pos.Length, baseF);
            return true;
        }

        // 完整模式下：游戏后续又算了新点，就把线补长（不做任何插值/推算）
        internal static void SyncFullTrail(int key, object shot)
        {
            LineRenderer lr;
            if (!Trails.TryGetValue(key, out lr) || lr == null) { FullMode.Remove(key); return; }
            Vector3[] pos = HistoryPoints(shot);
            if (pos == null) return;
            int have = TrailPts.ContainsKey(key) ? TrailPts[key] : 0;
            if (pos.Length <= have) return;
            lr.positionCount = pos.Length;
            for (int i = have; i < pos.Length; i++) lr.SetPosition(i, pos[i]);
            TrailPts[key] = pos.Length;
            if (PtsByLine.ContainsKey(lr)) TotalPoints += pos.Length - PtsByLine[lr];
            PtsByLine[lr] = pos.Length;
        }

        private static float Lifetime()
        {
            return ImplContext.TrailLife.Value;
        }

        private static Material SharedMat()
        {
            if (_mat != null) return _mat;
            if (_shader == null)
            {
                _shader = Shader.Find("Hidden/Internal-Colored");
                if (_shader == null) _shader = Shader.Find("Sprites/Default");
            }
            _mat = new Material(_shader);
            _mat.hideFlags = HideFlags.HideAndDontSave;
            _mat.SetInt("_ZWrite", 0);
            _mat.SetInt("_Cull", 0);
            _mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            _mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _mat.renderQueue = 3000;
            return _mat;
        }

        // 同屏轨迹上限：大量开枪时防止 LineRenderer 对象数量爆炸（0=不限）
        private static int LiveTrailCap()
        {
            int cfg = ImplContext.TrailMax == null ? 0 : ImplContext.TrailMax.Value;
            if (cfg > 0) return Mathf.Clamp(cfg, 8, 2048);
            return 256;
        }

        internal static LineRenderer SpawnLine(int points, Color c, float width)
        {
            width = Mathf.Clamp(width, 0.0005f, 0.2f);
            LineRenderer lr = null;
            if (Free.Count > 0)
            {
                lr = Free[Free.Count - 1];
                Free.RemoveAt(Free.Count - 1);
                if (lr != null) lr.gameObject.SetActive(true); else lr = null;
            }
            if (lr == null)
            {
                GameObject go = new GameObject("dsh_traj");
                lr = go.AddComponent<LineRenderer>();
                lr.material = SharedMat();
                lr.useWorldSpace = true;
                lr.textureMode = LineTextureMode.Stretch;
                lr.numCapVertices = 0;
                lr.numCornerVertices = 0;
            }
            lr.alignment = LineAlignment.View;
            lr.startWidth = width;
            lr.endWidth = width;
            lr.startColor = c;
            lr.endColor = c;
            lr.positionCount = points;
            Spawned.Add(lr);
            PtsByLine[lr] = points;
            PointsPer.Add(points);
            TotalPoints += points;
            float life = Lifetime();
            Expiry.Add(life > 0f ? Time.time + life : float.PositiveInfinity);
            return lr;
        }

        internal static void KillLast()
        {
            if (Spawned.Count > 0) ReleaseLine(Spawned.Count - 1, "warmup");
        }

        private static void ReleaseLine(int i, string why)
        {
            if (i < 0 || i >= Spawned.Count) return;
            LineRenderer lr = Spawned[i];
            PtsByLine.Clear();
            StepCount.Clear();
            if (lr == _liveLine) _liveLine = null;
            if (lr == _throwLine) _throwLine = null;
            if (lr != null)
            {
                lr.positionCount = 0;
                lr.gameObject.SetActive(false);
                if (Free.Count < 128) Free.Add(lr);
                else UnityEngine.Object.Destroy(lr.gameObject);
            }
            int linePts;
            if (lr != null && PtsByLine.TryGetValue(lr, out linePts)) { TotalPoints -= linePts; PtsByLine.Remove(lr); }
            else TotalPoints -= PointsPer[i];
            if (TotalPoints < 0) TotalPoints = 0;
            Spawned.RemoveAt(i); Expiry.RemoveAt(i); PointsPer.RemoveAt(i);
        }

        // 弹链根：跳弹/穿透/碎裂生成的都是子弹出，命中记录挂到同一发上
        private static int ChainRoot(int id)
        {
            int guard = 0;
            while (guard++ < 32)
            {
                int parent;
                if (!ParentOf.TryGetValue(id, out parent) || parent <= 0) return id;
                id = parent;
            }
            return id;
        }

        // 材质名本地化：没有 mat.* 词条时退回游戏枚举名（英文）
        internal static string MatName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            string id = "mat." + raw;
            string t = Lang.T(id);
            return t == id ? raw : t;
        }

        internal static void Reset()
        {
            for (int i = 0; i < Spawned.Count; i++) if (Spawned[i] != null) UnityEngine.Object.Destroy(Spawned[i].gameObject);
            for (int i = 0; i < Free.Count; i++) if (Free[i] != null) UnityEngine.Object.Destroy(Free[i].gameObject);
            for (int i = 0; i < Blasts.Count; i++) if (Blasts[i] != null) UnityEngine.Object.Destroy(Blasts[i]);
            Blasts.Clear(); BlastExpiry.Clear();
            Spawned.Clear(); Free.Clear(); Expiry.Clear(); PointsPer.Clear();
            Trails.Clear(); TrailPts.Clear(); TrailSeen.Clear(); TrailShot.Clear();
            ShotId.Clear(); StartPos.Clear(); ClosedByHit.Clear(); Grenade.Clear(); Pen.Clear(); Dmg.Clear();
            PendingHit.Clear(); PendingHitAt.Clear();
            ParentOf.Clear(); ChainHits.Clear(); LastChainRoot = -1; LastChainTotal = 0; LastRecByShot.Clear();
            VolleyOfShot.Clear(); VolleyShots.Clear(); LastVolley = -1; _volleyOrder.Clear(); _degBefore.Clear(); _degPenBefore.Clear(); _degDmgBefore.Clear();
            RealPen.Clear(); RealDmg.Clear(); RealAt.Clear(); GradSig.Clear(); GradCache.Clear(); GradKeyBuf.Clear();
            PhLine.Clear(); PhLast.Clear(); PhSeen.Clear();
            PhBlastGo.Clear(); PhBlastAt.Clear(); PhBlastT.Clear();
            PhInnerGo.Clear(); PhRingLr.Clear(); PhRingR.Clear(); PhDead.Clear();
            PhMovePos.Clear(); PhMoveT.Clear(); PhStillSince.Clear(); PhMoveStop.Clear();
            _liveLine = null;
            _throwLine = null;
            TotalPoints = 0;
            Shots = 0; SubShots = 0; Traced = 0; Hits = 0;
        }

        internal static void ClearAll()
        {
            int n = Spawned.Count;
            int nb = Blasts.Count;
            for (int i = Blasts.Count - 1; i >= 0; i--)
            {
                if (Blasts[i] != null) UnityEngine.Object.Destroy(Blasts[i]);
                Blasts.RemoveAt(i); BlastExpiry.RemoveAt(i);
            }
            _blastDone.Clear();
            for (int i = Spawned.Count - 1; i >= 0; i--)
            {
                if (Spawned[i] == _liveLine || Spawned[i] == _throwLine) continue;
                ReleaseLine(i, "clear");
            }
            Trails.Clear(); TrailPts.Clear(); TrailSeen.Clear(); TrailShot.Clear();
            ShotId.Clear(); StartPos.Clear(); ClosedByHit.Clear(); Grenade.Clear(); Pen.Clear(); Dmg.Clear();
            PendingHit.Clear(); PendingHitAt.Clear();
            RealPen.Clear(); RealDmg.Clear(); RealAt.Clear(); GradSig.Clear(); GradCache.Clear(); GradKeyBuf.Clear();
            Traced = 0;
            LogInfo("[弹道学] cleared " + n + " trail(s) and " + nb + " blast sphere(s) by hotkey");
        }

        private static int _lastTickFrame = -1;
        private static readonly HashSet<int> _dbgColor = new HashSet<int>();
        private static readonly HashSet<int> _dbgColor2 = new HashSet<int>();
        private static readonly System.Diagnostics.Stopwatch _perfSw = System.Diagnostics.Stopwatch.StartNew();

        internal static void Tick()
        {
            if (Time.frameCount == _lastTickFrame) return;   // OnGUI 每帧会调用多次，只处理一次
            _lastTickFrame = Time.frameCount;
            TickCount++;
            float now = Time.time;

            try
            {
                if (ImplContext.ResetKey != null && Input.GetKeyDown(ImplContext.ResetKey.Value)) ClearAll();
            }
            catch (Exception e) { SafeLog("clear key: " + e.Message); }

            long t0 = _perfSw.ElapsedTicks;
            PhysicsTick(now);
            long t1 = _perfSw.ElapsedTicks;
            LiveTrailTick();
            ThrowPredictTick();
            long t2 = _perfSw.ElapsedTicks;
            {
                double msPhys = (t1 - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                double msLive = (t2 - t1) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                if (msPhys + msLive > 15.0)
                {
                    LogInfo("[弹道学][PERF] phys=" + msPhys.ToString("F1") + " live=" + msLive.ToString("F1")
                        + " spawned=" + Spawned.Count + " trails=" + Trails.Count + " play=" + PlayPos.Count
                        + " blasts=" + Blasts.Count + " phseen=" + PhSeen.Count
                        + " heap=" + (GC.GetTotalMemory(false) / 1048576) + "MB");
                }
            }

            if (PlayPos.Count > 0)
            {
                float adv = (ImplContext.TrailAnimSpeed == null ? 0.05f : ImplContext.TrailAnimSpeed.Value) * Time.deltaTime / 0.01f;
                List<int> finished = null;
                foreach (KeyValuePair<int, Vector3[]> kv in PlayPos)
                {
                    Vector3[] pp = kv.Value;
                    float cur2;
                    PlayIdx.TryGetValue(kv.Key, out cur2);
                    cur2 += adv;
                    PlayIdx[kv.Key] = cur2;

                    int n = Mathf.Clamp((int)cur2 + 1, 2, pp.Length);
                    LineRenderer plr;
                    if (Trails.TryGetValue(kv.Key, out plr) && plr != null)
                    {
                        if (plr.positionCount != n) plr.positionCount = n;
                        for (int i = 0; i < n; i++) plr.SetPosition(i, pp[i]);
                        TrailPts[kv.Key] = n;

                        bool byDmg3 = ByDamage();
                        float baseV = byDmg3
                            ? (DmgScale.ContainsKey(kv.Key) ? DmgScale[kv.Key] : 0f)
                            : (PenScale.ContainsKey(kv.Key) ? PenScale[kv.Key] : 0f);
                        if (baseV > 0f) ApplyTrailColors(plr, kv.Key, n, baseV);
                        if (_dbgColor2.Count < 6 && _dbgColor2.Add(kv.Key))
                        {
                            LogInfo("[弹道学][COLOR2] id=" + kv.Key + " by=" + (byDmg3 ? "damage" : "penetration")
                                + " base=" + baseV.ToString("F1")
                                + " pen=" + (PenScale.ContainsKey(kv.Key) ? PenScale[kv.Key].ToString("F1") : "-")
                                + " dmg=" + (DmgScale.ContainsKey(kv.Key) ? DmgScale[kv.Key].ToString("F1") : "-")
                                + " hist=" + (PenHist.ContainsKey(kv.Key) ? PenHist[kv.Key].Count : 0)
                                + " start=" + plr.startColor + " end=" + plr.endColor
                                + " keys=" + plr.colorGradient.colorKeys.Length
                                + " k0=" + plr.colorGradient.colorKeys[0].color
                                + " kl=" + plr.colorGradient.colorKeys[plr.colorGradient.colorKeys.Length - 1].color
                                + " play=" + PlayPos.Count);
                        }
                    }
                    if (n >= pp.Length)
                    {
                        if (finished == null) finished = new List<int>();
                        finished.Add(kv.Key);
                    }
                }
                if (finished != null)
                    for (int i = 0; i < finished.Count; i++) { PlayPos.Remove(finished[i]); PlayIdx.Remove(finished[i]); }
            }

            if (Trails.Count > 0)
            {
                List<int> stale = null;
                foreach (KeyValuePair<int, LineRenderer> kv in Trails)
                {
                    float seen;
                    if (!TrailSeen.TryGetValue(kv.Key, out seen) || now - seen > 0.30f)
                    {
                        if (stale == null) stale = new List<int>();
                        stale.Add(kv.Key);
                    }
                }
                if (stale != null)
                    for (int i = 0; i < stale.Count; i++) CloseTrail(stale[i]);
            }

            for (int i = Blasts.Count - 1; i >= 0; i--)
            {
                if (Blasts[i] == null || now >= BlastExpiry[i])
                {
                    if (Blasts[i] != null) UnityEngine.Object.Destroy(Blasts[i]);
                    Blasts.RemoveAt(i); BlastExpiry.RemoveAt(i);
                }
            }

            int cap = ImplContext.TrailMax == null ? 256 : ImplContext.TrailMax.Value;
            int budget = 0;   // 顶点总量不再限制
            bool overCap = cap > 0 && Spawned.Count > cap;
            bool overBudget = budget > 0 && TotalPoints > budget;
            while (Spawned.Count > 0 && (overCap || overBudget))
            {
                ReleaseLine(0, overBudget ? "budget" : "caplines");
                overCap = cap > 0 && Spawned.Count > cap;
                overBudget = budget > 0 && TotalPoints > budget;
            }

            _active.Clear();
            foreach (KeyValuePair<int, LineRenderer> kv in Trails) if (kv.Value != null) _active.Add(kv.Value);

            for (int i = Spawned.Count - 1; i >= 0; i--)
            {
                if (Spawned[i] == null) { ReleaseLine(i, "null"); continue; }
                if (_active.Contains(Spawned[i])) continue;              // 飞行中不过期
                if (now >= Expiry[i] && Spawned[i] != _liveLine && !IsLiveRing(Spawned[i])) ReleaseLine(i, "expired");
            }
        }

        public static void TickPostfix(object[] __args) { Tick(); }

        private static string _localProfileId;
        private static bool _localTried;

        internal static string LocalProfileId()
        {
            if (_localTried) return _localProfileId;
            _localTried = true;
            try
            {
                // 1) GamePlayerOwner.MyPlayer
                Type owner = ImplContext.FindType("GamePlayerOwner");
                if (owner != null)
                {
                    UnityEngine.Object o = UnityEngine.Object.FindObjectOfType(owner);
                    if (o != null)
                    {
                        object pl = GetMember(o, "MyPlayer", "_myPlayer");
                        object pid = pl == null ? null : GetMember(pl, "ProfileId");
                        if (pid is string && ((string)pid).Length > 0) _localProfileId = (string)pid;
                    }
                }

                // 2) 扫描场景里的 Player，取 IsYourPlayer == true 的那个
                if (_localProfileId == null)
                {
                    Type playerT = ImplContext.FindType("Player");
                    if (playerT != null)
                    {
                        UnityEngine.Object[] all = UnityEngine.Object.FindObjectsOfType(playerT);
                        LogInfo("[弹道学] scan players: " + all.Length);
                        for (int i = 0; i < all.Length; i++)
                        {
                            object flag = GetMember(all[i], "IsYourPlayer");
                            if (flag is bool && (bool)flag)
                            {
                                object pid = GetMember(all[i], "ProfileId");
                                if (pid is string) { _localProfileId = (string)pid; break; }
                            }
                        }
                    }
                }

                LogInfo("[弹道学] local profile = " + (_localProfileId ?? "unknown"));
            }
            catch (Exception e) { SafeLog("local profile: " + e.Message); }
            return _localProfileId;
        }

        public static void CreateShotPostfix(object __instance, object[] __args, ref object __result)
        {
            try
            {
                if (!ImplContext.ModOn.Value || __result == null) return;
                Shots++;

                object ammo = (__args != null && __args.Length > 0) ? __args[0] : null;
                if (ammo == null) ammo = GetMember(__result, "Ammo");
                LastAmmo = AmmoLabel(ammo);
                AmmoDump(ammo);
                object pen = GetMember(ammo, "PenetrationPower");
                if (pen != null) { try { LastPen = Convert.ToInt32(pen); } catch (Exception) { } }

                bool mine = true;
                if (ImplContext.OnlyMine.Value)
                {
                    string local = LocalProfileId();
                    object pidObj = GetMember(__result, "PlayerProfileID", "ProfileId");
                    string pid = pidObj as string;
                    if (local != null && pid != null) mine = pid == local;
                    else if (__args != null && __args.Length > 4)
                    {
                        object flag = __args[4] == null ? null : GetMember(__args[4], "IsYourPlayer");
                        if (flag is bool) mine = (bool)flag;
                    }
                    if (_ownerLogs < LogCap)
                    {
                        _ownerLogs++;
                        LogInfo("[弹道学] owner: local=" + (local ?? "?") + " shot=" + (pid ?? "?") + " mine=" + mine);
                    }
                }
                if (!mine) return;

                int id = ++_nextId;
                ShotId[__result] = id;
                ParentOf[id] = -1;
                {
                    // 霰弹/多弹丸：同一帧、同一弹种创建的多个根弹出视为一次击发
                    if (Time.frameCount != _volleyFrame || LastAmmo != _volleyAmmo)
                    {
                        _nextVolley++;
                        _volleyFrame = Time.frameCount;
                        _volleyAmmo = LastAmmo;
                    }
                    VolleyOfShot[id] = _nextVolley;
                    List<int> vs;
                    if (!VolleyShots.TryGetValue(_nextVolley, out vs)) { vs = new List<int>(); VolleyShots[_nextVolley] = vs; }
                    if (vs.Count < 512) vs.Add(id);
                    _volleyOrder.Enqueue(_nextVolley);
                    while (_volleyOrder.Count > 128)
                    {
                        int oldV = _volleyOrder.Dequeue();
                        List<int> oldIds;
                        if (VolleyShots.TryGetValue(oldV, out oldIds))
                        {
                            for (int q = 0; q < oldIds.Count; q++)
                            {
                                VolleyOfShot.Remove(oldIds[q]);
                                ChainHits.Remove(ChainRoot(oldIds[q]));
                            }
                            VolleyShots.Remove(oldV);
                        }
                    }
                    LastVolley = _nextVolley;
                }
                {
                    int rle = IsLocalShot(__result) ? 0 : RoleOfPlayer(GetMember(__result, "Player"));
                    ShotRole[id] = rle;
                    if (_roleLogs < LogCap)
                    {
                        _roleLogs++;
                        LogInfo("[弹道学][ROLE] id=" + id + " role=" + rle + " player=" + (GetMember(__result, "Player") != null));
                    }
                }
                {
                    float fp = 0f, fd = 0f;
                    object spp = GetMember(__result, "PenetrationPower");
                    if (spp != null) { try { fp = Convert.ToSingle(spp); } catch (Exception) { } }
                    object sdd = GetMember(__result, "Damage");
                    if (sdd != null) { try { fd = Convert.ToSingle(sdd); } catch (Exception) { } }
                    if (fp > 0f) PenScale[id] = fp;
                    if (fd > 0f) DmgScale[id] = fd;
                }
                Vector3 origin = Vector3.zero;
                if (__args != null && __args.Length > 1 && __args[1] is Vector3) origin = (Vector3)__args[1];
                if (origin.sqrMagnitude < 0.01f)
                    TryVector(__result, out origin, "StartPosition", "MasterOrigin", "CurrentPosition");
                StartPos[id] = origin;
                if (pen != null) Pen[id] = LastPen;
                object dmgBase = GetMember(ammo, "Damage");
                if (dmgBase != null) { try { Dmg[id] = (int)Math.Round(Convert.ToSingle(dmgBase)); } catch (Exception) { } }

                string ammoType = ammo == null ? "" : ammo.GetType().Name;
                string weaponType = (__args != null && __args.Length > 5 && __args[5] != null) ? __args[5].GetType().Name : "";
                if (ammoType.IndexOf("Grenade", StringComparison.OrdinalIgnoreCase) >= 0
                    || weaponType.IndexOf("Grenade", StringComparison.OrdinalIgnoreCase) >= 0)
                    Grenade[id] = true;
            }
            catch (Exception e) { SafeLog("CreateShot: " + e.Message); }
        }

        public static void ChildShotPostfix(object[] __args, ref object __result)
        {
            try
            {
                if (!ImplContext.ModOn.Value || __result == null) return;
                object parent = (__args != null && __args.Length > 23) ? __args[23] : null;
                int parentId = -1;
                if (parent != null) ShotId.TryGetValue(parent, out parentId);

                // 子弹出起点：优先它自己的位置字段（跳弹/贯穿点），不沿用父弹的 origin
                Vector3 origin;
                if (!TryVector(__result, out origin, "StartPosition", "MasterOrigin", "CurrentPosition", "_currentPosition"))
                    return;
                if (origin.sqrMagnitude < 1f) return;

                int id = ++_nextId;
                ShotId[__result] = id;
                {
                    int rle = IsLocalShot(__result) ? 0 : RoleOfPlayer(GetMember(__result, "Player"));
                    ShotRole[id] = rle;
                    if (_roleLogs < LogCap)
                    {
                        _roleLogs++;
                        LogInfo("[弹道学][ROLE] id=" + id + " role=" + rle + " player=" + (GetMember(__result, "Player") != null));
                    }
                }                StartPos[id] = origin;
                {
                    int pr2;
                    if (parentId > 0 && ShotRole.TryGetValue(parentId, out pr2)) ShotRole[id] = pr2;
                ParentOf[id] = parentId;
                Shots++; SubShots++;
                {
                    // 子弹出并入父弹出所属的齐射，统计口径与母弹一致
                    int pv2;
                    if (parentId > 0 && VolleyOfShot.TryGetValue(parentId, out pv2)) VolleyOfShot[id] = pv2;
                }
                }
                // 子弹出优先用"它自己"的数值：游戏在 Create 时已按跳弹/碎片规则算好
                float pv = 0f, dv = 0f;
                object cp = GetMember(__result, "PenetrationPower");
                if (cp != null) { try { pv = Convert.ToSingle(cp); } catch (Exception) { } }
                object cd = GetMember(__result, "Damage");
                if (cd != null) { try { dv = Convert.ToSingle(cd); } catch (Exception) { } }
                if (pv <= 0f && PenScale.ContainsKey(parentId)) pv = PenScale[parentId];
                if (dv <= 0f && DmgScale.ContainsKey(parentId)) dv = DmgScale[parentId];
                if (pv <= 0f && parent != null)
                {
                    object pf = GetMember(parent, "PenetrationPower");
                    if (pf != null) { try { pv = Convert.ToSingle(pf); } catch (Exception) { } }
                }
                if (dv <= 0f && parent != null)
                {
                    object df = GetMember(parent, "Damage");
                    if (df != null) { try { dv = Convert.ToSingle(df); } catch (Exception) { } }
                }
                if (pv <= 0f && parentId > 0 && Pen.ContainsKey(parentId)) pv = Pen[parentId];
                if (pv <= 0f)
                {
                    object ammo = GetMember(__result, "Ammo");
                    object ap = ammo == null ? null : GetMember(ammo, "PenetrationPower");
                    if (ap != null) { try { pv = Convert.ToSingle(ap); } catch (Exception) { } }
                }
                if (pv > 0f) { Pen[id] = (int)Math.Round(pv); PenScale[id] = pv; }
                if (dv > 0f) { Dmg[id] = (int)Math.Round(dv); DmgScale[id] = dv; }
                bool gren;
                if (parentId > 0 && Grenade.TryGetValue(parentId, out gren) && gren) Grenade[id] = true;

                if (ImplContext.DebugLog.Value && _childLogs < LogCap)
                {
                    _childLogs++;
                    LogInfo("[弹道学][CHILD] id=" + id + " of=" + parentId + " origin=" + origin
                        + " pen=" + (Pen.ContainsKey(id) ? Pen[id].ToString() : "?"));
                }
            }
            catch (Exception e) { SafeLog("ChildShot: " + e.Message); }
        }

        public static void RecordStepPostfix(object __instance)
        {
            try
            {
                if (!ImplContext.ModOn.Value || __instance == null) return;
                int key;
                if (!ShotId.TryGetValue(__instance, out key)) return;
                // 先取这一帧的最新位置：用于判断命中之后子弹是否还在飞
                Vector3 newest = Vector3.zero;
                bool hasNewest = TryLatestPosition(__instance, out newest);
                if (ClosedByHit.ContainsKey(key))
                {
                    // 命中点在 X，之后位置又跑出去 > 5 cm → 说明这发穿透继续飞，重新打开轨迹
                    Vector3 pend;
                    if (hasNewest && PendingHit.TryGetValue(key, out pend) && Vector3.Distance(newest, pend) > 0.05f)
                    {
                        ClosedByHit.Remove(key);
            PendingHit.Remove(key); PendingHitAt.Remove(key);
                    }
                    else return;
                }

                TrailSeen[key] = Time.time;
                TrailShot[key] = __instance;

                // 生命周期检查：超过游戏给的 AmmoLifeTime 就不再取点
                int stepN;
                StepCount.TryGetValue(key, out stepN);
                stepN++;
                StepCount[key] = stepN;
                if ((stepN & 7) == 1)
                {
                float life = 0f;
                object lo = GetMember(__instance, "AmmoLifeTime");
                if (lo != null) { try { life = Convert.ToSingle(lo); } catch (Exception) { } }
                float elapsed2 = 0f;
                object to = GetMember(__instance, "TimeSinceShot");
                if (to != null) { try { elapsed2 = Convert.ToSingle(to); } catch (Exception) { } }
                bool outOfTime = false;
                if (life > 0f && elapsed2 > life) outOfTime = true;
                object fot = GetMember(__instance, "IsFlyingOutOfTime");
                if (fot is bool && (bool)fot) outOfTime = true;
                if (outOfTime)
                {
                    ClosedByHit[key] = true;
                    if (_lifeLogs < LogCap)
                    {
                        _lifeLogs++;
                        LogInfo("[弹道学][LIFE] id=" + key + " 超时 t=" + elapsed2.ToString("F2") + "s life=" + life.ToString("F2") + "s -> 封线");
                    }
                    return;
                }
                }


                // 每次调用只取最新一条位置，避免整表重放导致旧点/外推点被反复追加
                IList ph = GetMember(__instance, "PositionHistory") as IList;
                if (ph != null && ph.Count > 0 && ph[ph.Count - 1] is Vector3)
                {
                    int before = TrailPts.ContainsKey(key) ? TrailPts[key] : 0;
                    AppendVertex(key, (Vector3)ph[ph.Count - 1]);
                    int after = TrailPts.ContainsKey(key) ? TrailPts[key] : 0;
                    if (after > before)
                    {
                        _curveIdx = CurveIndex(__instance);
                        float gp = 0f, gd = 0f;
                        object op2 = GetMember(__instance, "PenetrationPower");
                        if (op2 != null) { try { gp = Convert.ToSingle(op2); } catch (Exception) { } }
                        object od2 = GetMember(__instance, "Damage");
                        if (od2 != null) { try { gd = Convert.ToSingle(od2); } catch (Exception) { } }
                        // 读表：value = 基准 × 衰减因子(该档速度比) × 命中累积
                        float fstep = FactorAtStep(key, 1f);
                        float ps, ds;
                        if (PenScale.TryGetValue(key, out ps)) { float v2 = ps * fstep; if (v2 > 0f) gp = v2; }
                        if (DmgScale.TryGetValue(key, out ds)) { float v2 = ds * fstep; if (v2 > 0f) gd = v2; }
                        if (!PenHist.ContainsKey(key)) { PenHist[key] = new List<float>(); DmgHist[key] = new List<float>(); }
                        PenHist[key].Add(gp);
                        DmgHist[key].Add(gd);
                    }
                }

                Vector3 hp;
                if (TryGameHitPoint(__instance, out hp))
                {
                    LineRenderer lr;
                    if (Trails.TryGetValue(key, out lr) && lr != null && lr.positionCount >= 2)
                        lr.SetPosition(lr.positionCount - 1, hp);
                }

                // 距离衰减渐变：起点=出膛值，末端=按当前速度比推算的衰减值
                LineRenderer gl;
                bool isG;
                bool grenNow = Grenade.TryGetValue(key, out isG) && isG;
                if (!grenNow && Trails.TryGetValue(key, out gl) && gl != null && gl.positionCount >= 2)
                {
                    float i0 = 0f, c0 = 0f, cf = 0f;
                    object a1 = GetMember(__instance, "InitialSpeed");
                    if (a1 != null) { try { i0 = Convert.ToSingle(a1); } catch (Exception) { } }
                    Vector3 vv;
                    if (TryVector(__instance, out vv, "_currentVelocity", "CurrentVelocity")) c0 = vv.magnitude;
                    object a3 = GetMember(__instance, "DamageDegradationCoefficient");
                    if (a3 != null) { try { cf = Convert.ToSingle(a3); } catch (Exception) { } }
                    float rr = i0 > 0f ? Mathf.Clamp01(c0 / i0) : 1f;
                    float ff = rr + (1f - rr) * Mathf.Clamp01(cf);

                    bool byDmg2 = ByDamage();
                    float baseVal = byDmg2 ? (DmgBase.ContainsKey(key) ? DmgBase[key] : ValueOfDmg(key))
                                           : (PenBase.ContainsKey(key) ? PenBase[key] : ValueOfPen(key));
                    List<float> histPen = null, histDmg = null;
                    bool haveHist = PenHist.TryGetValue(key, out histPen) && DmgHist.TryGetValue(key, out histDmg)
                        && histPen != null && histDmg != null && histPen.Count >= 2 && histDmg.Count >= 2;
                    if (baseVal > 0f) ApplyTrailColors(gl, key, gl.positionCount, baseVal);
                }

                if (ShotStopped(__instance))
                {
                    ClosedByHit[key] = true;
                    if (ImplContext.DebugLog.Value) LogInfo("[弹道学][STOP] id=" + key + " stopped");
                }
            }
            catch (Exception e) { SafeLog("RecordStep: " + e.Message); }
        }

        // 游戏自己写的终止信号：被挡下 / 被弹开 / 生命周期结束
        // 照抄 EffectsCommutator.IsHitPointAlreadyProcessed：同一帧内逐轴 2.5 cm 视为同一点
        private static readonly List<Vector3> _hcPoints = new List<Vector3>();
        private static int _hcFrame = -1;

        private static bool HitAlreadyProcessed(Vector3 hp)
        {
            if (Time.frameCount != _hcFrame) { _hcFrame = Time.frameCount; _hcPoints.Clear(); }
            for (int i = 0; i < _hcPoints.Count; i++)
            {
                Vector3 q = _hcPoints[i];
                if (Mathf.Abs(hp.x - q.x) < 0.025f && Mathf.Abs(hp.y - q.y) < 0.025f && Mathf.Abs(hp.z - q.z) < 0.025f)
                    return true;
            }
            _hcPoints.Add(hp);
            return false;
        }


        private static bool ShotStopped(object shot)
        {
            if (shot == null) return true;
            if (GetMember(shot, "BlockedBy") != null) return true;
            if (GetMember(shot, "DeflectedBy") != null) return true;
            object fin = GetMember(shot, "IsShotFinished");
            return fin is bool && (bool)fin;
        }

        private static void AppendVertex(int key, Vector3 p)
        {
            try
            {
                if (ImplContext.BulletTrail != null && !ImplContext.BulletTrail.Value) return;
                if (PlayPos.ContainsKey(key)) return;      // 动画中：不再逐帧追加采样
                if (FullMode.ContainsKey(key)) { object sFull; if (TrailShot.TryGetValue(key, out sFull)) SyncFullTrail(key, sFull); return; }

                LineRenderer lr;
                if (!Trails.TryGetValue(key, out lr) || lr == null)
                {
                    if (Trails.Count >= LiveTrailCap()) return;   // 到达上限：不再新建轨迹，保证帧率
                    Vector3 origin;
                    if (!StartPos.TryGetValue(key, out origin) || origin.sqrMagnitude < 0.01f) return;
                    if (Vector3.Distance(origin, p) < 0.02f) return;

                    bool gren;
                    Grenade.TryGetValue(key, out gren);
                    Color col = gren ? new Color(1f, 0.92f, 0.2f, 1f) : ColorForPct(key, 100f);
                    PenBase[key] = ValueOfPen(key);
                    DmgBase[key] = ValueOfDmg(key);
                    object shotObj;
                    if (TrailShot.TryGetValue(key, out shotObj) && shotObj != null)
                    {
                        float[] rc = BuildRatioCurve(shotObj);
                        if (rc != null) RatioCurve[key] = rc;
                    }
                    if (!PenScale.ContainsKey(key)) PenScale[key] = ValueOfPen(key);
                    if (!DmgScale.ContainsKey(key)) DmgScale[key] = ValueOfDmg(key);

                    lr = SpawnLine(2, col, ImplContext.TrailWidth.Value);
                    lr.SetPosition(0, origin);
                    lr.SetPosition(1, p);
                    Trails[key] = lr;
                    TrailPts[key] = 2;
                    if (shotObj != null && StartAnimation(key, shotObj, lr)) return;
                    if (shotObj != null && FillFullTrail(key, shotObj, lr)) { Traced++; return; }
                    Traced++;
                    return;
                }

                int n = TrailPts[key];
                if (n >= MaxPointsPerLineText()) return;
                if (Vector3.Distance(lr.GetPosition(n - 1), p) < 0.02f) return;
                lr.positionCount = n + 1;
                lr.SetPosition(n, p);
                TrailPts[key] = n + 1;
                int prevPts;
                if (PtsByLine.TryGetValue(lr, out prevPts)) { PtsByLine[lr] = n + 1; TotalPoints += 1; }
                else PtsByLine[lr] = n + 1;
            }
            catch (Exception e) { SafeLog("AppendVertex: " + e.Message); }
        }

        public static void HandleCollisionPostfix(object __instance)
        {
            try
            {
                if (!ImplContext.ModOn.Value || __instance == null) return;
                if (ImplContext.BulletTrail != null && !ImplContext.BulletTrail.Value) return;
                if (_hcLogs < LogCap) { _hcLogs++; LogInfo("[弹道学][HC] enter"); }

                int key;
                if (!ShotId.TryGetValue(__instance, out key))
                {
                    if (_hcLogs < LogCap) LogInfo("[弹道学][HC] shot not registered");
                    return;
                }
                if (ClosedByHit.ContainsKey(key))
                {
                    if (_hcLogs < LogCap) LogInfo("[弹道学][HC] id=" + key + " already closed");
                    return;
                }

                Vector3 hp;
                if (!TryGameHitPoint(__instance, out hp))
                {
                    if (_hcLogs < LogCap) LogInfo("[弹道学][HC] id=" + key + " no hit point");
                    return;
                }
                bool duplicate = HitAlreadyProcessed(hp);

                TrailSeen[key] = Time.time;
                TrailShot[key] = __instance;


                LineRenderer lr;
                bool snapped = false;
                if (Trails.TryGetValue(key, out lr) && lr != null && lr.positionCount >= 2)
                {
                    if (PlayPos.ContainsKey(key))
                    {
                        Vector3[] pp = PlayPos[key];
                        int shown = Mathf.Clamp((int)PlayIdx[key] + 1, 2, pp.Length);
                        Vector3[] arr = new Vector3[shown + 1];
                        for (int i = 0; i < shown; i++) arr[i] = pp[i];
                        arr[shown] = hp;
                        lr.positionCount = shown + 1;
                        for (int i = 0; i <= shown; i++) lr.SetPosition(i, arr[i]);
                        TrailPts[key] = shown + 1;
                        PlayPos.Remove(key); PlayIdx.Remove(key);
                        snapped = true;
                    }
                }
                if (!snapped && Trails.TryGetValue(key, out lr) && lr != null && lr.positionCount >= 2)
                {
                    // 删掉"越过接触点"的顶点，并把末点设为接触点（与弹孔位置一致）
                    TrimToHitPoint(lr, hp);
                    FullMode.Remove(key);
                    TrailPts[key] = lr.positionCount;
                    snapped = true;
                }
                if (!snapped) AppendVertex(key, hp);

                if (duplicate)
                {
                    if (_hcLogs < LogCap) LogInfo("[弹道学][HC] id=" + key + " same hit point this frame (末端已对齐)");
                    return;
                }

                // 距离衰减：factor = ratio + (1-ratio)*coef，命中后伤害与穿透力都已乘过它
                float initSpeed = 0f, curSpeed = 0f, coef = 0f, dmgNow = 0f, penNow = 0f;
                object o1 = GetMember(__instance, "InitialSpeed");
                if (o1 != null) { try { initSpeed = Convert.ToSingle(o1); } catch (Exception) { } }
                Vector3 velNow;
                if (TryVector(__instance, out velNow, "_currentVelocity", "CurrentVelocity")) curSpeed = velNow.magnitude;
                object o3 = GetMember(__instance, "DamageDegradationCoefficient");
                if (o3 != null) { try { coef = Convert.ToSingle(o3); } catch (Exception) { } }
                object o4 = GetMember(__instance, "Damage");
                if (o4 != null) { try { dmgNow = Convert.ToSingle(o4); } catch (Exception) { } }
                object o5 = GetMember(__instance, "PenetrationPower");
                if (o5 != null) { try { penNow = Convert.ToSingle(o5); } catch (Exception) { } }

                float ratio = initSpeed > 0f ? Mathf.Clamp01(curSpeed / initSpeed) : 1f;
                float factor = ratio + (1f - ratio) * Mathf.Clamp01(coef);
                if (dmgNow > 0f) Dmg[key] = (int)Math.Round(dmgNow);
                if (penNow > 0f) Pen[key] = (int)Math.Round(penNow);
                {
                    _curveIdx = CurveIndex(__instance);
                    float fs = FactorAtStep(key, 1f);
                    if (fs > 1e-4f)
                    {
                        if (penNow > 0f) PenScale[key] = penNow / fs;
                        if (dmgNow > 0f) DmgScale[key] = dmgNow / fs;
                    }
                }
                LastFalloffId = key.ToString();
                LastFalloffSpeed = ratio * 100f;
                LastFalloffFactor = factor;

                // 记录这一处命中点：弹链挂在根弹出上（子弹出沿 ParentOf 上溯）
                {
                    object colRec = GetMember(__instance, "HittedBallisticCollider");
                    HitRec hr = new HitRec();
                    hr.Mat = colRec == null ? "-" : (GetMember(colRec, "TypeOfMaterial") ?? "-").ToString();
                    hr.Level = colRec == null ? "?" : (GetMember(colRec, "PenetrationLevel") ?? "?").ToString();
                    hr.Chance = colRec == null ? "" : (GetMember(colRec, "PenetrationChance") ?? "").ToString();
                    hr.PenBefore = penNow; hr.DmgBefore = dmgNow;
                    hr.PenAfter = penNow; hr.DmgAfter = dmgNow;
                    hr.FalloffPct = (1f - factor) * 100f;
                    hr.ShotId = key;
                    int rootRec = ChainRoot(key);
                    List<HitRec> recsRec;
                    if (!ChainHits.TryGetValue(rootRec, out recsRec)) { recsRec = new List<HitRec>(); ChainHits[rootRec] = recsRec; }
                    // 同一弹链又出现新的命中点 => 上一条确实穿透了，用当前值作为它的后值
                    if (recsRec.Count > 0)
                    {
                        HitRec prevRec = recsRec[recsRec.Count - 1];
                        if (prevRec != null && !prevRec.Degraded)
                        {
                            prevRec.PenAfter = penNow;
                            prevRec.DmgAfter = dmgNow;
                            prevRec.Degraded = true;
                            DegradeCount++;
                            LastDegrade = prevRec.PenBefore.ToString("F1") + "  →  " + penNow.ToString("F1") + " / "
                                + prevRec.DmgBefore.ToString("F1") + "  →  " + dmgNow.ToString("F1");
                        }
                    }
                    if (recsRec.Count < 512) recsRec.Add(hr);
                    LastChainRoot = rootRec;
                    LastChainTotal = recsRec.Count;
                    LastRecByShot[__instance] = hr;
                }
                object hitAmmo = GetMember(__instance, "Ammo");
                RememberAmmoHit(hp, hitAmmo);

                // 爆炸弹直接命中任何表面（墙/天花板/地面/人）都在命中点画爆炸范围
                if (!_blastDebugOff && hitAmmo != null
                    && ImplContext.BlastOn != null && ImplContext.BlastOn.Value)
                {
                    // Shot.Ammo 是 Ammo 物品实例，爆炸参数在它的 Template 上
                    object hitTpl = GetMember(hitAmmo, "Template") ?? hitAmmo;
                    float ammoMaxR = Num(PropOf(hitTpl, "MaxExplosionDistance"), 0f);
                    float ammoMinR = Num(PropOf(hitTpl, "MinExplosionDistance"), 0f);
                    object fragObj = GetMember(__instance, "FragmentIndex");
                    int fragIdx = fragObj == null ? 0 : Convert.ToInt32(fragObj);

                    // 原版门槛：首次命中 + 引信已解除（GameWorld.ShotDelegate）
                    float fuze = Num(PropOf(hitTpl, "FuzeArmTimeSec"), 0f);
                    float tshot = 0f;
                    object ot2 = GetMember(__instance, "TimeSinceShot");
                    if (ot2 != null) { try { tshot = Convert.ToSingle(ot2); } catch (Exception) { } }
                    object fh = GetMember(__instance, "IsFirstHit");
                    bool isFirst = !(fh is bool) || (bool)fh;

                    // 与原版 GameWorld.ShotDelegate 一致：首次命中 + 引信已解除即爆炸
                    object bsObj = GetMember(__instance, "BulletState");
                    int bulletState = bsObj == null ? 4 : Convert.ToInt32(bsObj);

                    bool armed = isFirst && tshot >= fuze;
                    if (ammoMaxR > 0.1f && fragIdx == 0 && armed)
                        DrawBlastAt(hp, ammoMaxR, ammoMinR, "hit:" + AmmoLabel(hitAmmo), hitTpl);
                    else if (ImplContext.DebugLog.Value && ammoMaxR > 0.1f && !armed)
                        LogInfo("[弹道学] blast not armed: t=" + tshot.ToString("F2") + "s fuze="
                            + fuze.ToString("F2") + "s first=" + isFirst
                            + " state=" + bulletState + "(1=偏移 2=跳弹 3=碎裂 4=停止)");
                }

                // 只读游戏已经算好的结论，绝不重复掷骰：
                // BallisticCollider.IsPenetrated 的概率分支会消耗 UnityEngine.Random，
                // 既可能得到与本发实际不同的结果，也会推进游戏的随机序列。
                object fin = GetMember(__instance, "IsShotFinished");
                bool finished = fin is bool && (bool)fin;
                int st = -1;
                try { object bo = GetMember(__instance, "BulletState"); if (bo != null) st = Convert.ToInt32(bo); } catch (Exception) { }
                // 记录这次的实际击中点；是否到此为止只认游戏自己的停止信号（BlockedBy/DeflectedBy/IsShotFinished），
                // 不调用 IsPenetrated（内部掷骰会污染随机序列），也不靠状态码推断。
                PendingHit[key] = hp;
                PendingHitAt[key] = Time.time;
                bool stopped = ShotStopped(__instance);
                bool penetrated = !stopped;
                if (stopped) ClosedByHit[key] = true;
                if (_hcLogs < LogCap)
                {
                    object col = GetMember(__instance, "HittedBallisticCollider");
                    object mat = col == null ? null : GetMember(col, "TypeOfMaterial");
                    object lvl = col == null ? null : GetMember(col, "PenetrationLevel");
                    object chc = col == null ? null : GetMember(col, "PenetrationChance");
                    object pen = GetMember(__instance, "PenetrationPower");
                    object ammoDbg = GetMember(__instance, "Ammo");
                    object fragDbg = GetMember(__instance, "FragmentIndex");
                    LogInfo("[弹道学][HC] id=" + key + " ammo=" + AmmoLabel(ammoDbg)
                        + " ammoType=" + (ammoDbg == null ? "null" : ammoDbg.GetType().Name)
                        + " tplMaxR=" + Num(PropOf(GetMember(ammoDbg, "Template"), "MaxExplosionDistance"), -1f).ToString("F2")
                        + " frag=" + (fragDbg == null ? "?" : fragDbg.ToString())
                        + " maxR=" + Num(PropOf(ammoDbg, "MaxExplosionDistance"), -1f).ToString("F2")
                        + " first=" + ((GetMember(__instance, "IsFirstHit") is bool)
                            ? GetMember(__instance, "IsFirstHit").ToString() : "?")
                        + " t=" + Num(GetMember(__instance, "TimeSinceShot"), -1f).ToString("F2")
                        + " fuze=" + Num(PropOf(ammoDbg, "FuzeArmTimeSec"), -1f).ToString("F2")
                        + " explType=" + (GetMember(ammoDbg, "ExplosionType") ?? "-")
                        + " hp=" + hp
                        + " mat=" + (mat == null ? "null" : mat.ToString())
                        + " level=" + (lvl == null ? "?" : lvl.ToString())
                        + " chance=" + (chc == null ? "?" : chc.ToString())
                        + " pen=" + (pen == null ? "?" : pen.ToString())
                        + " penetrated=" + penetrated + " state=" + st
                        + " finished=" + finished
                        + " stopped=" + stopped
                        + " snapped=" + snapped);
                }
            }
            catch (Exception e) { SafeLog("HandleCollision: " + e.Message); }
        }




        // ================= 实时绘制（弹道预测 / 爆炸预警） =================

        internal static Color BlastColorC()
        {
            string hex = ImplContext.BlastColor == null ? "#FF4D1A" : ImplContext.BlastColor.Value;
            return ParseHex(hex, new Color(1f, 0.3f, 0.1f), 1f);
        }

        private static object _localPlayer;
        private static float _localPlayerAt;

        // 由 Harmony 钩子写入：EFT.LocalPlayer 创建/启用时把当前玩家塞进来
        private static int _lpLogs;

        internal static void SetLocalPlayer(object pl, string src)
        {
            if (pl == null) return;
            if (!ReferenceEquals(pl, _localPlayer) && _lpLogs < LogCap) { _lpLogs++; LogInfo("[弹道学] local player via " + src); }

            _localPlayer = pl;
            _localPlayerAt = Time.time;
        }

        public static void LocalPlayerSeedPostfix(object __instance)
        {
            try
            {
                if (__instance == null) return;
                object pl = GetMember(__instance, "Player", "_player", "player", "Owner", "_owner");
                if (pl == null) pl = __instance;
                if (pl == null) return;
                // 换了一个本地玩家对象 = 进入新战局：清掉上一局的全部记录，避免跨局累积
                object prevPlayer = _localPlayer;
                if (prevPlayer != null && !ReferenceEquals(prevPlayer, _localPlayer)) Reset();
                SetLocalPlayer(pl, "hook:" + __instance.GetType().Name);
            }
            catch (Exception) { }
        }
        private static float _localPlayerNextScan;   // 负结果退避：在此之前不再扫描场景

        private static Type _gwType;
        private static bool _gwUsable;   // GameWorld 单例 API 是否可用（可用但没玩家 = 不在战局）
        private static PropertyInfo _gwInst;
        private static FieldInfo _gwField;
        private static bool _gwTried;

        // 便宜路径：GameWorld.Instance.MainPlayer（静态单例，零场景扫描）
        private static object TryGameWorldPlayer()
        {
            try
            {
                if (!_gwTried)
                {
                    _gwTried = true;
                    _gwType = ImplContext.FindType("GameWorld");
                    if (_gwType != null)
                    {
                        _gwInst = _gwType.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        if (_gwInst == null)
                            _gwInst = _gwType.GetProperty("get_Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        if (_gwInst == null)
                        {
                            _gwField = _gwType.GetField("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                            if (_gwField == null)
                                _gwField = _gwType.GetField("_instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        }
                        if (_gwInst == null && _gwField == null)
                        {
                            string names = "";
                            FieldInfo[] fs = _gwType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                            for (int i = 0; i < fs.Length; i++) names += fs[i].Name + ",";
                            LogInfo("[弹道学][PERF] GameWorld 没有 Instance 访问器，静态字段: " + names);
                        }
                    }
                }
                if (_gwType == null || (_gwInst == null && _gwField == null)) return null;
                _gwUsable = true;
                object gw = _gwInst != null ? _gwInst.GetValue(null, null) : _gwField.GetValue(null);
                if (gw == null) return null;
                return GetMember(gw, "MainPlayer");
            }
            catch (Exception) { return null; }
        }

        internal static object LocalPlayerObject()
        {
            // 两个实时功能都关掉时不需要本地玩家，直接跳过（菜单里每帧全场景扫描会吃掉整帧）
            bool needLive = (ImplContext.LiveTrail != null && ImplContext.LiveTrail.Value)
                         || (ImplContext.LiveBlast != null && ImplContext.LiveBlast.Value)
                         || (ImplContext.ThrowPredict != null && ImplContext.ThrowPredict.Value);
            if (!needLive) return null;
            if (_localPlayer != null && Time.time - _localPlayerAt < 30f) return _localPlayer;
            // 负结果缓存：菜单/建角色界面没有玩家对象，扫描失败后 1.5 秒内不再扫全场景
            // 先走零开销的单例路径；菜单里 GameWorld.Instance 为空，直接判定"没有本地玩家"
            object quick = TryGameWorldPlayer();
            if (quick != null)
            {
                _localPlayer = quick;
                _localPlayerAt = Time.time;
                _localPlayerNextScan = 0f;
                return _localPlayer;
            }

            // 单例可用但取不到 MainPlayer ⇒ 不在战局（菜单/加载中），不再全场景扫描（那次约 90 ms）
            if (_gwUsable) return null;

            // 兜底扫描只在拿不到 GameWorld API 的版本上才走
            if (_localPlayer == null && Time.time < _localPlayerNextScan) return null;
            try
            {
                Type owner = ImplContext.FindType("GamePlayerOwner");
                if (owner != null)
                {
                    UnityEngine.Object o = UnityEngine.Object.FindObjectOfType(owner);
                    if (o != null) _localPlayer = GetMember(o, "MyPlayer", "_myPlayer");
                }
                if (_localPlayer == null)
                {
                    Type playerT = ImplContext.FindType("Player");
                    if (playerT != null)
                    {
                        UnityEngine.Object[] all = UnityEngine.Object.FindObjectsOfType(playerT);
                        for (int i = 0; i < all.Length; i++)
                        {
                            object flag = GetMember(all[i], "IsYourPlayer");
                            if (flag is bool && (bool)flag) { _localPlayer = all[i]; break; }
                        }
                    }
                }
                _localPlayerAt = Time.time;
                _localPlayerNextScan = (_localPlayer == null) ? Time.time + 10f : 0f;
            }
            catch (Exception e) { SafeLog("LocalPlayerObject: " + e.Message); }
            return _localPlayer;
        }

        // ---- 实时爆炸范围：手雷扔出后跟着它的位置画 ----
        private static readonly Dictionary<int, GameObject> PhBlastGo = new Dictionary<int, GameObject>();
        private static readonly Dictionary<int, Vector3> PhBlastAt = new Dictionary<int, Vector3>();
        private static readonly Dictionary<int, float> PhBlastT = new Dictionary<int, float>();
        private static readonly Dictionary<int, GameObject> PhInnerGo = new Dictionary<int, GameObject>();
        private static readonly Dictionary<int, LineRenderer> PhRingLr = new Dictionary<int, LineRenderer>();
        private static readonly Dictionary<int, float> PhRingR = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> PhDead = new Dictionary<int, float>();   // 已爆炸的预警 key
        private static readonly Dictionary<int, Vector3> PhMovePos = new Dictionary<int, Vector3>();
        private static readonly Dictionary<int, float> PhMoveT = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> PhStillSince = new Dictionary<int, float>();   // 0/时间戳：是否已开始预警
        private static readonly Dictionary<int, float> PhMoveStop = new Dictionary<int, float>();     // 停止移动的时刻

        internal static void LiveBlastUpdate(int key, Vector3 pos, object owner)
        {
            if (ImplContext.LiveBlast == null || !ImplContext.LiveBlast.Value) return;
            float deadAt;
            if (PhDead.TryGetValue(key, out deadAt)) return;
            try
            {
                // 移动是"打标记"的唯一入口；标记一旦打上就持续绘制，直到爆炸或被移除
                Vector3 prevPos = Vector3.zero; float prevT = 0f;
                bool hasPrev = PhMovePos.TryGetValue(key, out prevPos);
                if (hasPrev) hasPrev = PhMoveT.TryGetValue(key, out prevT);
                float speed = 0f;
                if (hasPrev)
                {
                    float dt = Time.time - prevT;
                    if (dt > 0.0001f) speed = Vector3.Distance(prevPos, pos) / dt;
                }
                PhMovePos[key] = pos;
                PhMoveT[key] = Time.time;

                const float MoveSpeed = 0.3f;   // 米/秒：超过即认为手雷已离手

                float armedAt;
                if (!PhStillSince.TryGetValue(key, out armedAt) || armedAt <= 0f)
                {
                    if (speed < MoveSpeed) return;      // 还没动过（在手里 / 掉在地上）→ 不绘制
                    armedAt = Time.time;
                    PhStillSince[key] = armedAt;        // 打上标记，之后一直有效
                }

                // 兜底：30 秒仍未爆炸则视为异常，移除标记
                if (Time.time - armedAt > 30f)
                {
                    PhDead[key] = Time.time;
                    LiveBlastClear(key);
                    PhMovePos.Remove(key); PhMoveT.Remove(key); PhStillSince.Remove(key);
                    return;
                }

                object item = GetMember(owner, "WeaponSource") ?? GetMember(owner, "Item") ?? GetMember(owner, "Template") ?? owner;
                object tpl = GetMember(item, "Template") ?? GetMember(item, "GrenadeItself") ?? item;
                float maxR = Num(PropOf(tpl, "MaxExplosionDistance"), 0f);
                if (maxR <= 0.1f) return;

                bool geom = ImplContext.GeomOn == null || ImplContext.GeomOn.Value;   // 与记录范围同一开关
                int dens = ImplContext.GeomSegments == null ? 20 : ImplContext.GeomSegments.Value;   // 与记录范围同一密度
                float minR = Num(PropOf(tpl, "MinExplosionDistance"), 0f);
                bool wantInner = ImplContext.LiveInner != null && ImplContext.LiveInner.Value && minR > 0.1f;
                bool wantStun = ImplContext.LiveStun != null && ImplContext.LiveStun.Value;
                float stunR = wantStun ? StunRadius(tpl, maxR) : 0f;
                float scx = 0f, scy = 0f, scz = 0f;
                if (wantStun) ContusionVals(tpl, out scx, out scy, out scz);

                Vector3 last; float lastT;
                // 贴几何时顶点是按当时位置裁剪出来的，位置一变就必须重算，不能只平移
                float moveStep = geom ? 0.25f : 1.0f;
                float refresh = geom ? 0.12f : 0.5f;
                bool need = !PhBlastAt.TryGetValue(key, out last)
                    || Vector3.Distance(last, pos) > moveStep
                    || !PhBlastT.TryGetValue(key, out lastT) || Time.time - lastT > refresh;
                if (!need)
                {
                    if (!geom)
                    {
                        GameObject cur;
                        if (PhBlastGo.TryGetValue(key, out cur) && cur != null) cur.transform.position = pos;
                        GameObject inner0;
                        if (PhInnerGo.TryGetValue(key, out inner0) && inner0 != null) inner0.transform.position = pos;
                    }
                    LineRenderer ring0;
                    if (PhRingLr.TryGetValue(key, out ring0) && ring0 != null && stunR > 0.1f)
                    if (stunR <= 0.1f) ReleaseRing(key);
                    if (!wantInner)
                    {
                        GameObject ii;
                        if (PhInnerGo.TryGetValue(key, out ii) && ii != null) UnityEngine.Object.Destroy(ii);
                        PhInnerGo.Remove(key);
                    }
                        DrawLiveRing(ring0, pos, stunR, geom, scx, scy, scz);
                    return;
                }

                GameObject old;
                if (PhBlastGo.TryGetValue(key, out old) && old != null) UnityEngine.Object.Destroy(old);
                GameObject oldInner;
                if (PhInnerGo.TryGetValue(key, out oldInner) && oldInner != null) UnityEngine.Object.Destroy(oldInner);

                Color c = BlastColorC();
                c = new Color(c.r, c.g, c.b, ImplContext.BlastAlpha == null ? 0.18f : ImplContext.BlastAlpha.Value);
                float bStr, bPen;
                BlastParamsOf(item, out bStr, out bPen);
                bool bands = ImplContext.BlastBandsOn == null || ImplContext.BlastBandsOn.Value;
                if (bands && bStr > 0f && bPen > 0f) SpawnBlastShells(pos, maxR, c, dens, bStr, bPen, minR);
                else if (geom) SpawnBlastVolume(pos, maxR, c, dens, bStr, bPen, minR, maxR);
                else SpawnBlastSphere(pos, maxR, c);

                GameObject made = Blasts.Count > 0 ? Blasts[Blasts.Count - 1] : null;
                PhBlastGo[key] = made;

                if (wantInner)
                {
                    Color ic = InnerColorC();
                    SpawnInner(pos, minR, bStr, bPen, maxR, dens);
                    PhInnerGo[key] = Blasts.Count > 0 ? Blasts[Blasts.Count - 1] : null;
                }
                else PhInnerGo.Remove(key);

                if (stunR > 0.1f)
                {
                    LineRenderer ring;
                    if (!PhRingLr.TryGetValue(key, out ring) || ring == null)
                    {
                        ring = SpawnLine(65, StunColorC(), Mathf.Max(0.02f, stunR * 0.012f));
                        PhRingLr[key] = ring;
                    }
                    ring.startColor = ring.endColor = StunColorC();
                    DrawLiveRing(ring, pos, stunR, geom, scx, scy, scz);
                    PhRingR[key] = stunR;
                }
                else
                {
                    ReleaseRing(key);
                }

                PhBlastAt[key] = pos;
                PhBlastT[key] = Time.time;
            }
            catch (Exception e) { SafeLog("LiveBlast: " + e.Message); }
        }

        // at：爆炸位置；null 表示位置未知（全部清掉）
        internal static void LiveBlastClearAll(Vector3? at = null)
        {
            List<int> victims = new List<int>();
            foreach (KeyValuePair<int, Vector3> kv in PhBlastAt)
            {
                if (at.HasValue && Vector3.Distance(kv.Value, at.Value) > 2.5f) continue;
                victims.Add(kv.Key);
            }
            if (at == null && PhBlastAt.Count == 0) victims.AddRange(PhRingLr.Keys);
            for (int i = 0; i < victims.Count; i++)
            {
                PhDead[victims[i]] = Time.time;      // 该手雷已爆炸，不再重新生成预警
                LiveBlastClear(victims[i]);
            }
            if (PhDead.Count > 128) PhDead.Clear();
        }

        internal static void LiveBlastClear(int key)
        {
            GameObject go;
            if (PhBlastGo.TryGetValue(key, out go) && go != null) UnityEngine.Object.Destroy(go);
            GameObject inner;
            if (PhInnerGo.TryGetValue(key, out inner) && inner != null) UnityEngine.Object.Destroy(inner);
            ReleaseRing(key);
            PhBlastGo.Remove(key); PhBlastAt.Remove(key); PhBlastT.Remove(key);
            PhInnerGo.Remove(key); PhRingR.Remove(key);
            PhMovePos.Remove(key); PhMoveT.Remove(key); PhStillSince.Remove(key); PhMoveStop.Remove(key);
        }

        private static void ReleaseRing(int key)
        {
            LineRenderer lr;
            if (PhRingLr.TryGetValue(key, out lr) && lr != null)
            {
                int i = Spawned.IndexOf(lr);
                if (i >= 0) ReleaseLine(i, "live-ring");
            }
            PhRingLr.Remove(key);
        }

        // 实时眩晕圈：贴几何时逐方向裁剪半径
        private static void DrawLiveRing(LineRenderer lr, Vector3 center, float radius, bool geom, float cx, float cy, float cz)
        {
            int seg = 64;
            int mask = geom ? BlastMask() : 0;
            if (lr.positionCount != seg + 1) lr.positionCount = seg + 1;
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float rr = geom ? StunRingRadiusAt(center, dir, radius, cx, cy, cz, mask) : radius;
                lr.SetPosition(i, center + dir * rr + new Vector3(0f, 0.06f, 0f));
            }
        }

        // ---- 实时弹道：用游戏的 TrajectoryCalculator 从枪口预测 ----
        private static LineRenderer _liveLine;
        private static float _liveDiagAt;
        private static bool _liveDrawn;
        private static Type _tcType; private static MethodInfo _tcInit;
        private static Func<float, float> _dragDel;
        private static object _constTpl;
        private static float _constGravity = -9.81f, _constSlow, _constMass2 = 1f;
        private static object _liveCalc;
        private static float _liveNext;
        private static int _liveFail;

        internal static string ListMembers(object o, string contains)
        {
            try
            {
                List<string> names = new List<string>();
                foreach (MemberInfo mi in o.GetType().GetMembers(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (mi.Name.IndexOf(contains, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    names.Add(mi.MemberType.ToString()[0] + ":" + mi.Name);
                }
                return string.Join(",", names.ToArray());
            }
            catch (Exception) { return "?"; }
        }

        internal static string Describe(object part)
        {
            if (part == null) return "null";
            object pos = GetMember(part, "position");
            object vel = GetMember(part, "velocity");
            return (pos is Vector3 ? ((Vector3)pos).ToString("F2") : "?")
                + "/" + (vel is Vector3 ? ((Vector3)vel).ToString("F1") : "?");
        }

        private static void LiveTrailLog(string msg)
        {
            if (_liveFail >= 3) return;
            _liveFail++;
            LogInfo("[弹道学] live trail: " + msg);
        }

        private static Camera _camCache;
        private static Vector3 _plPosPrev;
        private static float _plPosAt;
        private static Vector3 _plVel;
        private static float _camCacheAt;

        // Camera.main 内部是 FindGameObjectsWithTag，每帧多次调用很贵
        internal static Camera Cam()
        {
            if (_camCache == null || Time.time - _camCacheAt > 2f)
            {
                _camCache = Camera.main;
                _camCacheAt = Time.time;
            }
            return _camCache;
        }

        internal static Vector3 _lastPredO;
        internal static Vector3 _lastPredD;
        internal static float _lastPredV;
        internal static Vector3 _lastPredEnd;
        internal static bool _predValid;
        internal static float _throwInputAt;   // 最近一次"待投(拉栓完成)"的时间戳
        internal static float _thrMass = 0.6f;
        private static LineRenderer _throwLine;
        private static float _thrV0;
        private static Vector3 _thrOffsetLocal;
        private static bool _thrBiasCal;
        private static float _thrPitchBias;
        private static float _thrYawBias;
        private static float _thrDrag;
        private static float _thrGrav;
        private static int _thrLogs;
        private static int _tpdLogs;

        public static void CatchThrowGrenadePrefix(object __instance, object[] __args)
        {
            try
            {
                if (__args == null || __args.Length < 4) return;
                Vector3 pos = Vector3.zero, force = Vector3.zero;
                bool low = false;
                for (int i = 0; i < __args.Length; i++)
                {
                    if (__args[i] is Vector3 && pos == Vector3.zero) { pos = (Vector3)__args[i]; continue; }
                    if (__args[i] is Vector3 && force == Vector3.zero) { force = (Vector3)__args[i]; continue; }
                    if (__args[i] is bool) low = (bool)__args[i];
                }
                if (pos == Vector3.zero && force == Vector3.zero) return;
                object pl = LocalPlayerObject();
                if (pl == null) pl = GetMember(__instance, "Player");
                Component pc = pl as Component;
                Camera cam0 = Cam();
                int pk = PostureKey(pl, low);                Vector3 predBefore = (cam0 != null && _thrRel.ContainsKey(pk)) ? cam0.transform.TransformPoint(_thrRel[pk]) : Vector3.zero;

                if (cam0 != null) _thrRel[pk] = cam0.transform.InverseTransformPoint(pos);
                if (pc != null) { _thrSpawnLocal = pc.transform.InverseTransformPoint(pos); _thrRelP[pk] = _thrSpawnLocal; }
                _thrSpawnCal = (cam0 != null) || (pc != null);
                if (force.sqrMagnitude > 1f)
                {
                    _thrV0 = force.magnitude;
                    Vector3 vPure = force - _plVel;
                    if (vPure.sqrMagnitude > 1f) _thrSpeed[pk] = vPure.magnitude;
                    else _thrSpeed[pk] = force.magnitude;
                    Vector3 camF = cam0 != null ? cam0.transform.forward.normalized : Vector3.zero;
                    if (camF.sqrMagnitude > 0.01f)
                    {
                        Vector3 fn = vPure.sqrMagnitude > 1f ? vPure.normalized : force.normalized;
                        float pP = Mathf.Asin(Mathf.Clamp(camF.y, -1f, 1f)) * Mathf.Rad2Deg;
                        float fP = Mathf.Asin(Mathf.Clamp(fn.y, -1f, 1f)) * Mathf.Rad2Deg;
                        float pY = Mathf.Atan2(camF.x, camF.z) * Mathf.Rad2Deg;
                        float fY = Mathf.Atan2(fn.x, fn.z) * Mathf.Rad2Deg;
                        _thrPitchK[pk] = Mathf.Clamp(fP - pP, -60f, 60f);
                        _thrYawK[pk] = Mathf.Clamp(Mathf.DeltaAngle(pY, fY), -60f, 60f);
                    }
                }
                try
                {
                    Vector3 ourO = predBefore;
                    if (cam0 != null && _thrRel.ContainsKey(pk)) ourO = cam0.transform.TransformPoint(_thrRel[pk]);
                    Vector3 dlt = pos - ourO;
                    LogInfo("[弹道学][THRC] pos=" + pos.ToString("F2") + " ourOrigin=" + ourO.ToString("F2") + " delta=" + dlt.ToString("F2") + " mag=" + dlt.magnitude.ToString("F2") + " force=" + force.ToString("F2"));
                }
                catch (Exception) { }
                SaveThr();
                if (_thrLogs < LogCap)
                {
                    _thrLogs++;
                    if (_throwInputAt > 0f) LogInfo("[弹道学][THRD] 出手延迟=" + (Time.time - _throwInputAt).ToString("F3") + "s");
                LogInfo("[弹道学][THRG] pos=" + pos.ToString("F2") + " force=" + force.ToString("F2")
                        + " |v|=" + force.magnitude.ToString("F2") + " low=" + low
                        + " key=" + pk + " relP=" + _thrRelP.Count + " pose=" + (PropOf(pl, "Pose") == null ? "?" : PropOf(pl, "Pose").ToString()) + " lvl=" + Num(PropOf(pl, "PoseLevel"), -1f).ToString("F0") + " prone=" + (PropOf(pl, "IsInPronePose") is bool ? ((bool)PropOf(pl, "IsInPronePose")).ToString() : "?"));
                }
            }
            catch (Exception e) { if (_thrLogs < LogCap) { _thrLogs++; LogInfo("[弹道学][THRG] err " + e.GetType().Name); } }
        }

        public static void CatchThrowPrefix(object __instance, object[] __args)
        {
            try
            {
                if (__args == null || __args.Length < 1 || !(__args[0] is Vector3)) return;
                Vector3 v = (Vector3)__args[0];
                if (v.sqrMagnitude < 0.01f) return;
                _thrV0 = v.magnitude;
                _thrObj = __instance as Component;
                _thrOwner = GetMember(__instance, "Player") as Component;
                try
                {
                    Rigidbody rbC = GetMember(__instance, "Rigidbody") as Rigidbody;
                    if (rbC != null) { _thrDrag = rbC.drag; _thrGrav = rbC.useGravity ? Physics.gravity.y : 0f; }
                }
                catch (Exception) { }
                object pl = GetMember(__instance, "Player");
                Component pc = pl as Component;
                Component gc = __instance as Component;
                if (pc != null && gc != null)
                {
                    _thrOffsetLocal = pc.transform.InverseTransformPoint(gc.transform.position);
                }
                try
                {
                    Vector3 cam = Vector3.zero;
                    Camera c0 = Camera.main;
                    if (c0 != null) cam = c0.transform.forward;
                    Vector3 vn = v.normalized;
                    float align = cam.sqrMagnitude > 0.01f ? Vector3.Dot(cam.normalized, vn) : 0f;
                    if (align > 0.3f && _thrV0 > 4f)
                    {
                        float pPitch = Mathf.Asin(Mathf.Clamp(cam.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
                        float vPitch = Mathf.Asin(Mathf.Clamp(vn.y, -1f, 1f)) * Mathf.Rad2Deg;
                        float pYaw = Mathf.Atan2(cam.x, cam.z) * Mathf.Rad2Deg;
                        float vYaw = Mathf.Atan2(vn.x, vn.z) * Mathf.Rad2Deg;
                        _thrPitchBias = Mathf.Clamp(vPitch - pPitch, -50f, 50f);
                        _thrYawBias = Mathf.Clamp(Mathf.DeltaAngle(pYaw, vYaw), -50f, 50f);
                        _thrBiasCal = true;
                    }
                }
                catch (Exception) { }                if (_thrLogs < LogCap)
                {
                    _thrLogs++;
                    Vector3 aim = Vector3.zero;
                    object ld = GetMember(pl, "LookDirection");
                    if (ld is Vector3) aim = ((Vector3)ld).normalized;
                    float dot = aim.sqrMagnitude > 0.01f ? Vector3.Dot(aim, v.normalized) : 0f;
                    float ang = aim.sqrMagnitude > 0.01f ? Vector3.Angle(aim, v) : 0f;
                    string phys = "?";
                    try
                    {
                        object rb = GetMember(__instance, "Rigidbody");
                        Rigidbody r = rb as Rigidbody;
                        if (r != null) phys = "drag=" + r.drag.ToString("F2") + " angDrag=" + r.angularDrag.ToString("F2")
                            + " useGrav=" + r.useGravity + " mass=" + r.mass.ToString("F2")
                            + " g=" + (r.useGravity ? Physics.gravity.y.ToString("F2") : "0");
                    }
                    catch (Exception) { }
                    LogInfo("[弹道学][THROW] |v|=" + _thrV0.ToString("F2") + " v=" + v.ToString("F2")
                        + " aim=" + aim.ToString("F2") + " dot=" + dot.ToString("F3") + " ang=" + ang.ToString("F1")
                        + " bias=" + _thrPitchBias.ToString("F1") + "/" + _thrYawBias.ToString("F1") + (_thrBiasCal ? "(cal)" : "(skip)")
                        + " | " + phys + " | pos=" + (gc != null ? gc.transform.position.ToString("F2") : "?")
                        + " offLocal=" + _thrOffsetLocal.ToString("F2"));
                }
            }
            catch (Exception) { }
        }
        private static readonly Vector3[] _throwBuf = new Vector3[128];

        private static string ThrPath()
        {
            try
            {
                string baseDir = BepInEx.Paths.ConfigPath;
                string dir = System.IO.Path.Combine(baseDir, "Ballistics");
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                return System.IO.Path.Combine(dir, "throwcache.txt");
            }
            catch (Exception) { return null; }
        }

        internal static void SaveThr()
        {
            try
            {
                string p = ThrPath();
                if (p == null) return;
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                foreach (KeyValuePair<int, Vector3> kv in _thrRelP)
                {
                    float sp = _thrSpeed.ContainsKey(kv.Key) ? _thrSpeed[kv.Key] : 0f;
                    float pb = _thrPitchK.ContainsKey(kv.Key) ? _thrPitchK[kv.Key] : 0f;
                    float yb = _thrYawK.ContainsKey(kv.Key) ? _thrYawK[kv.Key] : 0f;
                    sb.Append(kv.Key).Append(';')
                      .Append(kv.Value.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(';')
                      .Append(kv.Value.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(';')
                      .Append(kv.Value.z.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(';')
                      .Append(sp.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(';')
                      .Append(pb.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(';')
                      .Append(yb.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
                }
                System.IO.File.WriteAllText(p, sb.ToString());
            }
            catch (Exception) { }
        }

        internal static void LoadThr()
        {
            try
            {
                string p = ThrPath();
                if (p == null || !System.IO.File.Exists(p)) return;
                foreach (string line in System.IO.File.ReadAllLines(p))
                {
                    string[] a = line.Split(';');
                    if (a.Length < 7) continue;
                    var ci = System.Globalization.CultureInfo.InvariantCulture;
                    int k = int.Parse(a[0], ci) % 6;
                    _thrRelP[k] = new Vector3(float.Parse(a[1], ci), float.Parse(a[2], ci), float.Parse(a[3], ci));
                    _thrSpeed[k] = float.Parse(a[4], ci);
                    _thrPitchK[k] = float.Parse(a[5], ci);
                    _thrYawK[k] = float.Parse(a[6], ci);
                }
                LogInfo("[弹道学][THRW] loaded " + _thrRelP.Count + " keys from cache");
            }
            catch (Exception) { }
        }

        private static FieldInfo _grenField;
        private static Type _grenType;

        internal static bool TryGrenadePos(object fc, out Vector3 pos)
        {
            pos = Vector3.zero;
            if (fc == null) return false;
            try
            {
                Type t = fc.GetType();
                if (_grenField == null || _grenType != t)
                {
                    _grenField = null;
                    for (Type c = t; c != null && c != typeof(object); c = c.BaseType)
                    {
                        FieldInfo f = c.GetField("_grenadeTransform", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                        if (f != null) { _grenField = f; break; }
                    }
                    _grenType = t;
                    if (_grenField == null) LogInfo("[弹道学][THRD] _grenadeTransform not found on " + t.Name);
                }
                if (_grenField == null) return false;
                Transform gt = _grenField.GetValue(fc) as Transform;
                if (gt == null) return false;
                pos = gt.position;
                return true;
            }
            catch (Exception) { }
            return false;
        }
        private static FieldInfo _dropField;
        private static Type _dropType;

        // 原版方向：normalize(-_dropTransform.up * 5 + Random.onUnitSphere * (0.5 - stamina))
        // 随机项不可预知，取 0 → 方向 = -_dropTransform.up
        internal static bool TryDropDir(object fc, out Vector3 dir)
        {
            dir = Vector3.zero;
            if (fc == null) return false;
            try
            {
                Type t = fc.GetType();
                if (_dropField == null || _dropType != t)
                {
                    _dropField = null;
                    for (Type c = t; c != null && c != typeof(object); c = c.BaseType)
                    {
                        FieldInfo f = c.GetField("_dropTransform", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                        if (f != null) { _dropField = f; break; }
                    }
                    _dropType = t;
                    if (_dropField == null) LogInfo("[弹道学][THRD] _dropTransform not found on " + t.Name);
                }
                if (_dropField == null) return false;
                Transform dt = _dropField.GetValue(fc) as Transform;
                if (dt == null) return false;
                dir = -dt.up;
                return dir.sqrMagnitude > 0.01f;
            }
            catch (Exception) { }
            return false;
        }

        private static object _hardSettings;
        private static FieldInfo _grenadeForceField;
        private static bool _grenadeForceTried;

        internal static float GrenadeForceConst()
        {
            if (!_grenadeForceTried)
            {
                _grenadeForceTried = true;
                try
                {
                    Type t = ImplContext.FindType("EFTHardSettings");
                    if (t != null)
                    {
                        PropertyInfo pi = t.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        if (pi != null) _hardSettings = pi.GetValue(null);
                        if (_hardSettings == null)
                        {
                            FieldInfo fi = t.GetField("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                            if (fi != null) _hardSettings = fi.GetValue(null);
                        }
                        _grenadeForceField = t.GetField("GrenadeForce", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        LogInfo("[弹道学][THRD] EFTHardSettings inst=" + (_hardSettings != null) + " field=" + (_grenadeForceField != null));
                    }
                }
                catch (Exception) { }
            }
            try
            {
                if (_hardSettings != null && _grenadeForceField != null)
                    return Convert.ToSingle(_grenadeForceField.GetValue(_hardSettings));
            }
            catch (Exception) { }
            return 0f;
        }

        private static Vector3 ThrowAimDir(object pl, object fc)
        {
            Vector3 dir = Vector3.zero;
            // 手雷控制器上的 WeaponDirection 是手部朝向（预备姿势朝下），必须跳过，直接取相机
            bool isGren = fc != null && fc.GetType().Name.IndexOf("Grenade", StringComparison.OrdinalIgnoreCase) >= 0;
            if (isGren)
            {
                Camera camg = Cam();
                if (camg != null) return camg.transform.forward;
            }
            object wd = PropOf(fc, "WeaponDirection");
            if (wd is Vector3) dir = ((Vector3)wd).normalized;
            if (dir.sqrMagnitude < 0.01f)
            {
                object ld = PropOf(pl, "LookDirection");
                if (ld is Vector3) dir = ((Vector3)ld).normalized;
            }
            if (dir.sqrMagnitude < 0.01f)
            {
                object lsd = PropOf(pl, "LocalShotDirection");
                if (lsd is Vector3) dir = ((Vector3)lsd).normalized;
            }
            if (dir.sqrMagnitude < 0.01f)
            {
                Camera cam = Camera.main;
                if (cam != null) dir = cam.transform.forward;
            }
            return dir;
        }
        internal static void DumpLines()
        {
            try
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                int n = 0;
                for (int i = 0; i < Spawned.Count && n < 8; i++)
                {
                    LineRenderer lr = Spawned[i];
                    if (lr == null) continue;
                    n++;
                    sb.Append('#').Append(i).Append(':').Append(lr.gameObject.name)
                      .Append(" w=").Append(lr.startWidth.ToString("F3"))
                      .Append(" p=").Append(lr.positionCount)
                      .Append(" c=").Append(lr.startColor.r.ToString("F2")).Append(',')
                      .Append(lr.startColor.g.ToString("F2")).Append(',')
                      .Append(lr.startColor.b.ToString("F2")).Append("; ");
                }
                LogInfo("[弹道学][THRL2] total=" + Spawned.Count + " | " + sb.ToString());
            }
            catch (Exception) { }
        }

        private static Vector3 _thrSpawnLocal;
        private static bool _thrSpawnCal;
        private static Component _thrObj;
        private static Component _thrOwner;
        private static readonly Dictionary<int, Vector3> _thrRel = new Dictionary<int, Vector3>();
        private static readonly Dictionary<int, Vector3> _thrRelP = new Dictionary<int, Vector3>();
        private static readonly Dictionary<int, float> _thrSpeed = new Dictionary<int, float>();

        private static readonly float[] _thrDefH = new float[] { 0.84f, 0.65f, 1.41f, 1.23f, 0.19f, 0.03f };
        private static readonly Dictionary<int, float> _thrPitchK = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> _thrYawK = new Dictionary<int, float>();

        private static int PostureKey(object pl, bool low)
        {
            int p = 0;
            try
            {
                object prone0 = PropOf(pl, "IsInPronePose");
                if (prone0 is bool && (bool)prone0) p = 2;
                else if (Num(PropOf(pl, "PoseLevel"), 0f) >= 0.5f) p = 1;
                else p = 0;
            }
            catch (Exception) { }
            return p * 2 + (low ? 1 : 0);
        }

        private static MethodInfo _ftpMethod;
        private static Type _ftpType;

        private static bool TryFindThrowPosition(object fc, out Vector3 pos)
        {
            pos = Vector3.zero;
            if (fc == null) return false;
            try
            {
                Type t = fc.GetType();
                if (_ftpMethod == null || _ftpType != t)
                {
                    _ftpMethod = null;
                    for (Type c = t; c != null && c != typeof(object); c = c.BaseType)
                    {
                        MethodInfo m = c.GetMethod("FindThrowPosition",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                        if (m != null) { _ftpMethod = m; break; }
                    }
                    _ftpType = t;
                    if (_ftpMethod == null) LogInfo("[弹道学][THRS] FindThrowPosition not found on " + t.Name);
                }
                if (_ftpMethod == null) return false;
                object r = _ftpMethod.Invoke(fc, null);
                if (r is Vector3) { pos = (Vector3)r; return pos != Vector3.zero; }
            }
            catch (Exception e) { if (_thrLogs < LogCap) { _thrLogs++; LogInfo("[弹道学][THRS] FindThrowPosition failed: " + e.GetType().Name); } }
            return false;
        }

        private static void HideThrowLine()
        {
            if (_throwLine == null) return;
            if (_throwLine.positionCount != 0) _throwLine.positionCount = 0;
        }

        private static Color ThrowColorC()
        {
            string hex = ImplContext.ThrowColor == null ? "#FFB733" : ImplContext.ThrowColor.Value;
            return ParseHex(hex, new Color(1f, 0.72f, 0.2f), 1f);
        }

        // 捏住可投掷物时画抛物线预测：纯重力弹道，初速可配置
        internal static void ThrowPredictTick()
        {
            if (ImplContext.ThrowPredict == null || !ImplContext.ThrowPredict.Value) { HideThrowLine(); return; }
            // 每帧重算（移动时线不能滞后），靠降低射线次数控制开销
            try
            {
                object pl = LocalPlayerObject();
                object fc = pl == null ? null : PropOf(pl, "HandsController");
                object item = fc == null ? null : PropOf(fc, "Item");
                if (ImplContext.DebugLog != null && ImplContext.DebugLog.Value && Time.frameCount % 300 == 0)
                {
                    _tpdLogs++;
                    LogInfo("[弹道学][THRP0] on=" + (ImplContext.ThrowPredict != null && ImplContext.ThrowPredict.Value)
                        + " pl=" + (pl == null ? "null" : pl.GetType().Name)
                        + " fc=" + (fc == null ? "null" : fc.GetType().Name)
                        + " item=" + (item == null ? "null" : item.GetType().Name)
                        + " maxR=" + Num(PropOf(item, "MaxExplosionDistance"), 0f).ToString("F2")
                        + " high=" + (Num(PropOf(fc, "WaitingForHighThrow"), 0f) > 0.5f)
                        + " low=" + (Num(PropOf(fc, "WaitingForLowThrow"), 0f) > 0.5f));
                }
                if (item == null) { HideThrowLine(); return; }
                bool wHigh = Num(PropOf(fc, "WaitingForHighThrow"), 0f) > 0.5f;
                bool wLow = Num(PropOf(fc, "WaitingForLowThrow"), 0f) > 0.5f;
                if (!wHigh && !wLow) { HideThrowLine(); return; }   // 没拉栓：高抛/低抛未定，不画
                _throwInputAt = Time.time;
                float maxR = Num(PropOf(item, "MaxExplosionDistance"), 0f);
                if (maxR <= 0.1f) { HideThrowLine(); return; }
                Component pc = pl as Component;
                Vector3 dir = ThrowAimDir(pl, fc);
                string srcDir = "rel";   // 实测原版投掷方向≈相机方向(偏差0.1°)，用相机+实测偏差
                // 方向用相机+实测偏差（原版投掷向量与相机方向实测差 0.06°~0.14°），不读手部朝向
                if (dir.sqrMagnitude < 0.01f)
                {
                    if (pc == null) { HideThrowLine(); return; }
                    dir = pc.transform.forward;
                }
                {
                    int bk = PostureKey(pl, wLow);
                    float pb = _thrPitchK.ContainsKey(bk) ? _thrPitchK[bk] : 0f;
                    float yb = _thrYawK.ContainsKey(bk) ? _thrYawK[bk] : 0f;
                    if (Mathf.Abs(pb) > 0.01f || Mathf.Abs(yb) > 0.01f)
                    {
                        float pitch2 = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg + pb;
                        float yaw2 = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg + yb;
                        float pr2 = pitch2 * Mathf.Deg2Rad, yr2 = yaw2 * Mathf.Deg2Rad;
                        dir = new Vector3(Mathf.Cos(pr2) * Mathf.Sin(yr2), Mathf.Sin(pr2), Mathf.Cos(pr2) * Mathf.Cos(yr2));
                    }
                }

                Vector3 origin = Vector3.zero;
                int ck = PostureKey(pl, wLow);
                Vector3 gpos;
                bool haveG = TryGrenadePos(fc, out gpos);
                Camera camNow = Cam();
                string src = "est";
                if (camNow != null && _thrRel.ContainsKey(ck)) { origin = camNow.transform.TransformPoint(_thrRel[ck]); src = "rel"; }
                else if (pc != null && _thrRelP.ContainsKey(ck)) { origin = pc.transform.TransformPoint(_thrRelP[ck]); src = "relP"; }
                else if (camNow != null && _thrRel.ContainsKey(ck)) { origin = camNow.transform.TransformPoint(_thrRel[ck]); src = "rel"; }
                else if (haveG) { origin = gpos; src = "gren"; }
                else if (pc != null) origin = pc.transform.position + Vector3.up * _thrDefH[Mathf.Clamp(ck % 6, 0, 5)] + dir * 0.15f;
                else if (_thrSpawnCal && pc != null) origin = pc.transform.TransformPoint(_thrSpawnLocal);
                object bones = PropOf(pl, "PlayerBones");
                Transform rp = bones == null ? null : AsTransform(GetMember(bones, "RightPalm"));
                if (origin == Vector3.zero && pc != null)
                {
                    // 未标定时的估计离手点：右肩上方（投掷出手位置），不是预备时放低的右手
                    origin = pc.transform.position + Vector3.up * 1.55f + pc.transform.right * 0.22f + dir * 0.35f;
                }
                if (origin == Vector3.zero)
                {
                    object fp2 = PropOf(fc, "FireportPosition");
                    if (fp2 is Vector3) origin = (Vector3)fp2;
                }
                if (origin == Vector3.zero)
                {
                    if (pc == null) { HideThrowLine(); return; }
                    origin = pc.transform.position + Vector3.up * 1.35f + dir * 0.35f;
                }

                if (_thrObj != null && _thrOwner != null)
                {
                    Vector3 wpos = _thrObj.transform.position;
                    Vector3 loc = _thrOwner.transform.InverseTransformPoint(wpos);
                    if (loc.sqrMagnitude > 0.04f)
                    {
                        _thrSpawnLocal = loc;
                        _thrSpawnCal = true;
                        if (_thrLogs < LogCap) { _thrLogs++; LogInfo("[弹道学][THRS] releaseLocal=" + loc.ToString("F2")); }
                        _thrObj = null;
                    }
                }
                float stam = 1f;
                try
                {
                    object ph = PropOf(pl, "Physical");
                    object hs = ph == null ? null : PropOf(ph, "HandsStamina");
                    stam = Num(PropOf(hs, "NormalValue"), 1f);
                }
                catch (Exception) { }
                float gf = GrenadeForceConst();
                float vCalc = gf > 0.1f ? gf * (wLow ? 0.66f : 1.16f) * Mathf.Lerp(0.4f, 1f, stam + 0.5f) : 0f;
                float vRaw = _thrSpeed.ContainsKey(ck) ? _thrSpeed[ck] : vCalc;   // 只用实测值或原版公式，无猜测兜底
                float v0 = vRaw / Mathf.Max(0.05f, _thrMass);   // 原版按冲量施加：Δv = force / mass
                int mask = BlastMask();
                int steps = ImplContext.ThrowSteps == null ? 60 : ImplContext.ThrowSteps.Value;
                steps = Mathf.Clamp(steps, 10, 126);
                const float dt = 0.02f;
                // 原版 ThrowGrenadeInner：withVelocity 时 v += Player.Velocity
                // 玩家速度：优先用位置差分（Player.Velocity 不一定可用），再与引擎值取较大者
                Vector3 pv = Vector3.zero;
                try
                {
                    if (pc != null)
                    {
                        Vector3 cur = pc.transform.position;
                        if (_plPosAt > 0.0001f)
                        {
                            float dtv = Time.time - _plPosAt;
                            if (dtv > 0.001f) _plVel = (cur - _plPosPrev) / dtv;
                        }
                        _plPosPrev = cur;
                        _plPosAt = Time.time;
                    }
                    pv = _plVel;
                    object vv = PropOf(pl, "Velocity");
                    if (vv is Vector3 && ((Vector3)vv).sqrMagnitude > pv.sqrMagnitude) pv = (Vector3)vv;
                }
                catch (Exception) { }

                // 出手提前：从按下到真正生成手雷有几十~上百毫秒，期间玩家仍在移动
                const float lead = 0.475f;   // 实测固定出手延迟（按下->生成手雷）
                origin += pv * lead;
                Vector3 p = origin;
                Vector3 v = dir * v0 + pv;
                int n = 1;
                _throwBuf[0] = p;
                Vector3 sampleFrom = p;   // 射线采样起点：每 3 步打一次，但覆盖整段路径
                for (int i = 0; i < steps && n < _throwBuf.Length; i++)
                {
                    v += new Vector3(0f, _thrGrav != 0f ? _thrGrav : -9.81f, 0f) * dt;
                    if (_thrDrag > 0.0001f) v *= 1f / (1f + _thrDrag * dt);
                    Vector3 pn = p + v * dt;
                    Vector3 d = pn - p;
                    float len = d.magnitude;
                    if (len > 0.001f && (i % 3) == 0)
                    {
                        RaycastHit hit = new RaycastHit();
                        bool gotHit = Physics.Linecast(sampleFrom, pn, out hit, mask, QueryTriggerInteraction.Ignore)
                            && hit.distance >= 0.05f
                            && (pc == null || hit.collider == null || !hit.collider.transform.IsChildOf(pc.transform));
                        if (gotHit)
                        {
                            _throwBuf[n++] = hit.point;
                            break;
                        }
                        sampleFrom = pn;
                    }
                    p = pn;
                    _throwBuf[n++] = p;
                }

                if (ImplContext.DebugLog != null && ImplContext.DebugLog.Value && Time.frameCount % 300 == 0)
                {
                    _tpdLogs++;
                    LogInfo("[弹道学][THRP] item=" + (item == null ? "null" : item.GetType().Name)
                        + " maxR=" + maxR.ToString("F2") + " v0=" + v0.ToString("F2")
                        + " dir=" + dir.ToString("F2") + " src=" + src + " ck=" + ck + " nP=" + _thrRelP.Count + " srcDir=" + srcDir + " pv=" + pv.ToString("F2") + " gf=" + gf.ToString("F2") + " stam=" + stam.ToString("F2") + " vCalc=" + vCalc.ToString("F2")
                        + " origin=" + origin.ToString("F2") + " n=" + n
                        + " line=" + (_throwLine != null) + " cnt=" + (_throwLine != null ? _throwLine.positionCount : -1));
                }                if (ImplContext.DebugLog != null && ImplContext.DebugLog.Value && Time.frameCount % 300 == 0)
                {
                    _tpdLogs++;
                    LogInfo("[弹道学][THRP] item=" + (item == null ? "null" : item.GetType().Name)
                        + " maxR=" + maxR.ToString("F2") + " v0=" + v0.ToString("F2")
                        + " dir=" + dir.ToString("F2") + " src=" + src + " biasCal=" + _thrBiasCal
                        + " origin=" + origin.ToString("F2") + " n=" + n
                        + " line=" + (_throwLine != null) + " cnt=" + (_throwLine != null ? _throwLine.positionCount : -1));
                }                if (_throwLine == null)
                {
                    float w = ImplContext.TrailWidth == null ? 0.01f : ImplContext.TrailWidth.Value;
                    float tw = ImplContext.ThrowWidth == null ? 0.006f : ImplContext.ThrowWidth.Value;
                    _throwLine = SpawnLine(2, ThrowColorC(), Mathf.Clamp(tw, 0.001f, 0.2f));
                    try { _throwLine.gameObject.name = "dsh_throw"; } catch (Exception) { }
                    int li = Spawned.IndexOf(_throwLine);
                    if (li >= 0) Expiry[li] = float.PositiveInfinity;
                }
                if (_throwLine.positionCount != n) _throwLine.positionCount = n;
                for (int i = 0; i < n; i++) _throwLine.SetPosition(i, _throwBuf[i]);
                if (ImplContext.DebugLog != null && ImplContext.DebugLog.Value && Time.frameCount % 300 == 0) { DumpLines(); LogInfo("[弹道学][THRL] w=" + _throwLine.startWidth.ToString("F3") + " pts=" + _throwLine.positionCount + " col=" + _throwLine.startColor.ToString() + " p0=" + _throwLine.GetPosition(0).ToString("F2") + " lines=" + Spawned.Count); }
                if (ImplContext.DebugLog != null && ImplContext.DebugLog.Value && Time.frameCount % 180 == 0)
                {
                    Camera cd = Cam();
                    object lsd0 = PropOf(pl, "LookDirection");
                    LogInfo("[弹道学][THRCAM] cam=" + (cd == null ? "null" : cd.name) + " pos=" + (cd == null ? "" : cd.transform.position.ToString("F2"))
                        + " fwd=" + (cd == null ? "" : cd.transform.forward.ToString("F2"))
                        + " look=" + (lsd0 is Vector3 ? ((Vector3)lsd0).ToString("F2") : "?")
                        + " pv=" + _plVel.ToString("F2"));
                    int mk = BlastMask();
                    RaycastHit th;
                    bool hitAny = Physics.Raycast(cd == null ? Vector3.zero : cd.transform.position, cd == null ? Vector3.forward : cd.transform.forward, out th, 50f, mk, QueryTriggerInteraction.Ignore);
                    LogInfo("[弹道学][THRCAM] mask=0x" + mk.ToString("X8") + " default=0x" + Physics.DefaultRaycastLayers.ToString("X8") + " hit=" + (hitAny ? th.collider.name + "@" + th.distance.ToString("F1") : "none"));
                }                _lastPredO = origin; _lastPredD = dir; _lastPredV = v0; _lastPredEnd = _throwBuf[n - 1]; _predValid = true;
                Color tc = ThrowColorC();
                _throwLine.startColor = tc;
                _throwLine.endColor = tc;
            }
            catch (Exception e) { if (_tpdLogs < 20) { _tpdLogs++; LogInfo("[弹道学][THRPX] " + e.GetType().Name + ": " + e.Message); } HideThrowLine(); }
        }

        internal static void LiveTrailTick()
        {
            if (ImplContext.LiveTrail == null || !ImplContext.LiveTrail.Value)
            {
                if (_liveLine != null) { ReleaseLiveLine(); }
                return;
            }
            if (Time.time < _liveNext) return;
            _liveNext = Time.time;   // 每帧刷新

            _liveDrawn = false;
            try
            {
                object pl = LocalPlayerObject();
                if (pl == null) { LiveTrailLog("no local player"); return; }
                object fc = PropOf(pl, "HandsController");
                if (fc == null) { LiveTrailLog("no HandsController: " + pl.GetType().Name); return; }
                object weapon = PropOf(fc, "Item") ?? GetMember(fc, "Item");
                if (weapon == null) { LiveTrailLog("no Item on " + fc.GetType().Name); return; }
                object tpl = PropOf(weapon, "CurrentAmmoTemplate") ?? GetMember(weapon, "CurrentAmmoTemplate");
                if (tpl == null) { LiveTrailLog("no CurrentAmmoTemplate on " + weapon.GetType().Name); return; }

                float speed = Num(PropOf(tpl, "InitialSpeed"), 0f);
                // EFT 的字段名是 BallisticCoeficient（少一个 f）
                float bc = Num(PropOf(tpl, "BallisticCoeficient"), 0f);
                if (bc <= 0f) bc = Num(PropOf(tpl, "BallisticCoefficient"), 0f);
                float mass = Num(PropOf(tpl, "BulletMassGram"), 0f);
                float dia = Num(PropOf(tpl, "BulletDiameterMilimeters"), 0f);
                if (speed <= 1f || mass <= 0f || dia <= 0f)
                {
                    LiveTrailLog("参数不足 speed=" + speed + " bc=" + bc + " mass=" + mass + " dia=" + dia
                        + " tpl=" + tpl.GetType().Name + " members=" + ListMembers(tpl, "allist"));
                    return;
                }
                // 模板没写弹道系数时按"无阻力"预测（纯抛物线），比不画好，日志里标出来
                if (bc <= 0.001f)
                {
                    if (_liveFail < 3) { _liveFail++; LogInfo("[弹道学] live trail: bc=0（该弹药模板无弹道系数），按无阻力预测"); }
                    bc = 0f;
                }

                // 依次尝试：Player.Fireport → FirearmController.CurrentFireport → 武器物体 Transform
                string dirSrc = "?";
                Transform tr = AsTransform(PropOf(pl, "Fireport"));
                if (tr != null) dirSrc = "Player.Fireport";
                if (tr == null) { tr = AsTransform(PropOf(fc, "CurrentFireport")); if (tr != null) dirSrc = "CurrentFireport"; }
                if (tr == null) { tr = AsTransform(PropOf(weapon, "Transform")); if (tr != null) dirSrc = "Weapon.Transform"; }

                Vector3 originV; Vector3 dirV;
                object fpos = PropOf(fc, "FireportPosition");
                originV = (fpos is Vector3) ? (Vector3)fpos : (tr != null ? tr.position : Vector3.zero);

                // 方向优先级：武器真实朝向 → 本地发射朝向 → Transform.forward → 摄像机
                dirV = Vector3.zero;
                object wd = PropOf(fc, "WeaponDirection");
                if (wd is Vector3) { dirV = ((Vector3)wd).normalized; if (dirV.sqrMagnitude > 0.01f) dirSrc = "WeaponDirection"; }
                if (dirV.sqrMagnitude < 0.01f)
                {
                    object ld = PropOf(pl, "LocalShotDirection");
                    if (ld is Vector3) { dirV = ((Vector3)ld).normalized; if (dirV.sqrMagnitude > 0.01f) dirSrc = "LocalShotDirection"; }
                }
                if (dirV.sqrMagnitude < 0.01f && tr != null) { dirV = tr.forward; dirSrc = "Transform.forward"; }
                if (dirV.sqrMagnitude < 0.01f)
                {
                    Camera cam0 = Cam();
                    if (cam0 == null) { LiveTrailLog("no direction source"); return; }
                    dirV = cam0.transform.forward;
                    dirSrc = "Camera.forward";
                }
                if (originV == Vector3.zero) { LiveTrailLog("no fireport position"); return; }

                if (_tcType == null)
                {
                    _tcType = ImplContext.FindType("TrajectoryCalculator");
                    if (_tcType != null)
                    {
                        _tcInit = _tcType.GetMethod("Initialize");
                        MethodInfo drag = _tcType.GetMethod("CalculateG1DragCoefficient",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        if (drag != null)
                        {
                            try
                            {
                                if (_liveCalc == null) _liveCalc = Activator.CreateInstance(_tcType);
                            }
                            catch (Exception) { }
                            try
                            {
                                object tgt = drag.IsStatic ? null : _liveCalc;
                                _dragDel = (Func<float, float>)Delegate.CreateDelegate(typeof(Func<float, float>), tgt, drag);
                            }
                            catch (Exception ex)
                            {
                                _dragDel = null;
                                ParameterInfo[] psx = drag.GetParameters();
                                LogInfo("[弹道学][CALC] drag delegate failed: static=" + drag.IsStatic + " ret=" + drag.ReturnType.Name
                                    + " params=" + psx.Length + (psx.Length > 0 ? ("/" + psx[0].ParameterType.Name) : "")
                                    + " err=" + ex.GetType().Name);
                            }
                        }
                    }
                }
                if (_tcType == null || _tcInit == null)
                {
                    LiveTrailLog("TrajectoryCalculator API not found"); return;
                }

                // 只借它算一次常量（重力/空气阻力系数/质量项），之后自己积分
                if (!ReferenceEquals(tpl, _constTpl))
                {
                    if (_liveCalc == null)
                    {
                        try { _liveCalc = Activator.CreateInstance(_tcType); }
                        catch (Exception ex) { LiveTrailLog("create calc failed: " + ex.Message); return; }
                    }
                    try { _tcInit.Invoke(_liveCalc, new object[] { originV, dirV * speed, mass, dia, bc, false }); }
                    catch (Exception ex)
                    {
                        Exception inner = ex.InnerException ?? ex;
                        LiveTrailLog("Initialize threw: " + inner.GetType().Name + " " + inner.Message);
                        return;
                    }
                    _constGravity = Num(GetMember(_liveCalc, "gravity"), -9.81f);
                    _constSlow = Mathf.Abs(Num(GetMember(_liveCalc, "bulletSlowdown"), 0f));
                    _constMass2 = Mathf.Abs(Num(GetMember(_liveCalc, "bulletMassKgX2"), 0f));
                    if (_constMass2 < 1e-6f) _constMass2 = Mathf.Max(1e-6f, mass / 1000f * 2f);
                    float r0 = dia / 2000f;
                    if (_constSlow < 1e-9f) _constSlow = 0.5f * 1.225f * Mathf.PI * r0 * r0;
                    _constTpl = tpl;
                    LogInfo("[弹道学][CALC] gravity=" + _constGravity.ToString("F3")
                        + " slowdown=" + _constSlow.ToString("F6")
                        + " mass2=" + _constMass2.ToString("F5")
                        + " dragFn=" + (_dragDel != null));
                }

                bool geom = ImplContext.GeomOn == null || ImplContext.GeomOn.Value;   // 与记录范围同一开关
                int mask = geom ? BlastMask() : 0;

                if (_liveLine == null)
                {
                    _liveLine = SpawnLine(2, TrailBaseC(), ImplContext.TrailWidth == null ? 0.01f : ImplContext.TrailWidth.Value);
                    int li = Spawned.IndexOf(_liveLine);
                    if (li >= 0) Expiry[li] = float.PositiveInfinity;
                }
                Color liveC = LivePredictColor(tpl);
                _liveLine.startColor = _liveLine.endColor = liveC;

                const float dt = 0.01f;
                _liveRayAcc = 0f;
                _liveRays = 0;
                Vector3 pcur = originV;
                Vector3 vcur = dirV * speed;
                int count = 0;
                Vector3 p1 = Vector3.zero, p2 = Vector3.zero;
                PutLivePoint(count++, originV);

                int steps = 0;
                for (int i = 1; i <= 300; i++)
                {
                    float sp = vcur.magnitude;
                    float k = 0f;
                    if (bc > 0.001f && _dragDel != null)
                        k = _constSlow * _dragDel(sp) * bc * sp * sp / _constMass2;
                    Vector3 acc = new Vector3(0f, _constGravity, 0f) - vcur.normalized * k;
                    vcur += acc * dt;
                    Vector3 pnext = pcur + vcur * dt;

                    if (geom)
                    {
                        Vector3 d = pnext - pcur;
                        float len = d.magnitude;
                        if (len > 0.01f)
                        {
                            // 射线按累计距离节流，并限制每帧总量：原先 300 步每 2 步一次，最坏每帧 150 次
                            _liveRayAcc += len;
                            if (_liveRayAcc >= 2f && _liveRays < 24)
                            {
                                _liveRayAcc = 0f;
                                _liveRays++;
                                RaycastHit h;
                                if (Physics.Raycast(pcur, d / len, out h, len, mask, QueryTriggerInteraction.Ignore))
                                {
                                    PutLivePoint(count++, h.point);
                                    break;
                                }
                            }
                        }
                    }
                    if (i == 1) p1 = pnext; else if (i == 2) p2 = pnext;
                    PutLivePoint(count++, pnext);
                    pcur = pnext;
                    steps = i;
                }

                if (count != _liveLine.positionCount) _liveLine.positionCount = count;
                for (int i = 0; i < count; i++) _liveLine.SetPosition(i, _liveBuf[i]);
                _liveDrawn = true;

                if (Time.time - _liveDiagAt > 3f)
                {
                    _liveDiagAt = Time.time;
                    LogInfo("[弹道学] live diag: dir=" + dirSrc
                        + " origin=" + originV.ToString("F1") + " fwd=" + dirV.ToString("F2")
                        + " pts=" + count + " steps=" + steps
                        + " v0=" + (dirV * speed).ToString("F1")
                        + " p1=" + p1.ToString("F2") + " p2=" + p2.ToString("F2")
                        + " last=" + _liveBuf[count - 1].ToString("F1"));
                }
                if (_liveFail > 0) { _liveFail = 0; LogInfo("[弹道学] live trail: working, points=" + count); }
            }
            catch (Exception e) { SafeLog("LiveTrail: " + e.Message); }
            finally
            {
                if (!_liveDrawn) HideLiveLine();
            }
        }

        private static void HideLiveLine()
        {
            if (_liveLine == null) return;
            if (_liveLine.positionCount != 0) _liveLine.positionCount = 0;
        }

        private static readonly Vector3[] _liveBuf = new Vector3[512];
        private static float _liveRayAcc;
        private static int _liveRays;

        private static void PutLivePoint(int idx, Vector3 p)
        {
            if (idx < 0 || idx >= _liveBuf.Length) return;
            _liveBuf[idx] = p;
        }

        private static void ReleaseLiveLine()
        {
            try
            {
                int i = Spawned.IndexOf(_liveLine);
                if (i >= 0) ReleaseLine(i, "live");
            }
            catch (Exception) { }
            _liveLine = null;
        }

        // ---- 物理刚体（手雷 / 信号弹）路径采样 ----
        private static readonly Dictionary<int, LineRenderer> PhLine = new Dictionary<int, LineRenderer>();
        private static readonly Dictionary<int, Vector3> PhLast = new Dictionary<int, Vector3>();
        private static readonly Dictionary<int, float> PhSeen = new Dictionary<int, float>();

        private static readonly HashSet<int> _grenProbe = new HashSet<int>();

        // 只用来看"真正飞的那颗雷"的物理状态，验证我们抓的 force/pos 是否正确
        private static void GrenadeSpawnProbe(object inst)
        {
            try
            {
                MonoBehaviour mb = inst as MonoBehaviour;
                if (mb == null) return;
                if (mb.GetType().Name.IndexOf("Grenade", StringComparison.OrdinalIgnoreCase) < 0) return;
                int id = mb.GetInstanceID();
                if (!_grenProbe.Add(id)) return;
                Rigidbody rb = GetMember(inst, "Rigidbody") as Rigidbody;
                Vector3 vel = rb != null ? rb.velocity : Vector3.zero;
                object thv = GetMember(inst, "Velocity");
                string vs = thv is Vector3 ? ((Vector3)thv).ToString("F2") : "?";
                if (rb != null && rb.mass > 0.01f) _thrMass = rb.mass;
                LogInfo("[弹道学][GRV] pos=" + mb.transform.position.ToString("F2")
                    + " rbVel=" + vel.ToString("F2") + " |v|=" + vel.magnitude.ToString("F2")
                    + " throwVel=" + vs
                    + " drag=" + (rb != null ? rb.drag.ToString("F3") : "?")
                    + " grav=" + (rb != null ? rb.useGravity.ToString() : "?")
                    + " mass=" + (rb != null ? rb.mass.ToString("F2") : "?"));
            }
            catch (Exception) { }
        }
        public static void PhysicsTrackPostfix(object __instance)
        {
            try
            {
                GrenadeSpawnProbe(__instance);
                if (!ImplContext.ModOn.Value) return;
                if (ImplContext.PhysOn == null || !ImplContext.PhysOn.Value) return;
                MonoBehaviour mb = __instance as MonoBehaviour;
                if (mb == null) return;

                Vector3 p = mb.transform.position;
                int key = -Math.Abs(mb.GetInstanceID());

                LineRenderer lr;
                if (!PhLine.TryGetValue(key, out lr) || lr == null)
                {
                    lr = SpawnLine(2, PhysColorC(), ImplContext.TrailWidth.Value);
                    lr.SetPosition(0, p);
                    lr.SetPosition(1, p);
                    PhLine[key] = lr; PhLast[key] = p; PhSeen[key] = Time.time;
                    LiveBlastUpdate(key, p, __instance);
                    return;
                }

                PhSeen[key] = Time.time;
                LiveBlastUpdate(key, p, __instance);

                Vector3 last;
                if (!PhLast.TryGetValue(key, out last)) last = p;
                if (Vector3.Distance(last, p) < 0.05f) return;

                int n = lr.positionCount;
                lr.positionCount = n + 1;
                lr.SetPosition(n, p);
                PhLast[key] = p;
            }
            catch (Exception e) { SafeLog("PhysicsTrack: " + e.Message); }
        }

        private static bool IsLiveRing(LineRenderer lr)
        {
            if (lr == null || PhRingLr.Count == 0) return false;
            foreach (KeyValuePair<int, LineRenderer> kv in PhRingLr)
                if (kv.Value == lr) return true;
            return false;
        }

        internal static void PhysicsTick(float now)
        {
            if (PhSeen.Count == 0) return;
            List<int> gone = null;
            foreach (KeyValuePair<int, float> kv in PhSeen)
            {
                if (now - kv.Value <= 0.5f) continue;
                if (gone == null) gone = new List<int>();
                gone.Add(kv.Key);
            }
            if (gone == null) return;
            for (int i = 0; i < gone.Count; i++)
            {
                PhSeen.Remove(gone[i]);
                PhLine.Remove(gone[i]);
                PhLast.Remove(gone[i]);
                LiveBlastClear(gone[i]);
                PhDead[gone[i]] = now;
            }
        }

        private static bool IsGrenadeDamage(object damageInfo)
        {
            if (damageInfo == null) return false;
            object dt = GetMember(damageInfo, "DamageType");
            if (dt == null) return false;
            string v = dt.ToString();
            return v.IndexOf("Grenade", StringComparison.OrdinalIgnoreCase) >= 0
                || v.IndexOf("Explosion", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void MarkNearestGrenade(Vector3 hp)
        {
            int best = -1; float bestD = 5f;
            foreach (KeyValuePair<int, LineRenderer> kv in Trails)
            {
                LineRenderer l = kv.Value;
                if (l == null || l.positionCount < 1) continue;
                float d = Vector3.Distance(hp, l.GetPosition(l.positionCount - 1));
                if (d < bestD) { bestD = d; best = kv.Key; }
            }
            if (best >= 0) Grenade[best] = true;
        }

        internal static void OnHit(Vector3 hp)
        {
            // 只用于面板统计；几何一律由 RecordStep 用该 Shot 自己的 _hit 处理，
            // 避免"就近匹配轨迹"把 A 弹丸的命中点写到 B 弹丸的线上。
            try
            {
                if (!ImplContext.ModOn.Value) return;
            }
            catch (Exception e) { SafeLog("OnHit: " + e.Message); }
        }

        internal static int MaxPointsPerLineText() { return 2000; }

        private static int TrimToHitPoint(LineRenderer lr, Vector3 hp)
        {
            if (lr == null) return 0;
            int n = lr.positionCount;
            if (n < 2) return 0;

            // 以"到起点的距离"为准：删掉一切越过接触点的顶点，再把接触点作为末点
            Vector3 origin = lr.GetPosition(0);
            float hitDist = Vector3.Distance(origin, hp);
            int keep = 0;
            for (int i = 1; i < n; i++)
            {
                if (Vector3.Distance(origin, lr.GetPosition(i)) <= hitDist + 0.01f) keep = i;
                else break;
            }
            int dropped = n - 1 - keep;
            if (keep <= 0)
            {
                // 所有采样点都在命中点之外：保留"枪口 → 命中点"这一段
                lr.positionCount = 2;
                lr.SetPosition(0, origin);
                lr.SetPosition(1, hp);
                return n - 1;
            }
            lr.positionCount = keep + 1;
            lr.SetPosition(keep, hp);
            return dropped;
        }

        // 取这一帧的最新位置：PositionHistory 末项，取不到时退回 CurrentPosition
        private static bool TryLatestPosition(object shot, out Vector3 pos)
        {
            pos = Vector3.zero;
            if (shot == null) return false;
            try
            {
                IList ph = GetMember(shot, "PositionHistory") as IList;
                if (ph != null && ph.Count > 0 && ph[ph.Count - 1] is Vector3)
                {
                    pos = (Vector3)ph[ph.Count - 1];
                    if (pos.sqrMagnitude > 0.01f) return true;
                }
            }
            catch (Exception) { }
            return TryVector(shot, out pos, "CurrentPosition", "_currentPosition") && pos.sqrMagnitude > 0.01f;
        }

        private static bool TryGameHitPoint(object shot, out Vector3 hp)
        {
            hp = Vector3.zero;
            if (shot == null) return false;
            if (TryVector(shot, out hp, "HitPoint") && hp.sqrMagnitude > 0.01f) return true;
            object rh = GetMember(shot, "_hit");
            if (rh != null)
            {
                object pt = GetMember(rh, "point", "Point");
                if (pt is Vector3)
                {
                    Vector3 v = (Vector3)pt;
                    if (v.sqrMagnitude > 0.01f) { hp = v; return true; }
                }
            }
            return false;
        }

        private static void CloseTrail(int key)
        {
            LineRenderer lr;
            bool has = Trails.TryGetValue(key, out lr);
            int n = lr != null ? lr.positionCount : 0;
            bool byHit = ClosedByHit.ContainsKey(key);

            object shot;
            TrailShot.TryGetValue(key, out shot);
            if (!byHit && shot != null && lr != null)
            {
                Vector3 ghp;
                if (TryGameHitPoint(shot, out ghp))
                {
                    int dropped = TrimToHitPoint(lr, ghp);
                    n = lr.positionCount;
                    if (dropped > 0) LogInfo("[弹道学] close-trim: " + dropped + " vertex(es) past game hit point");
                }
            }

            if (lr != null && n >= 2 && _castLogs < 20)
            {
                int bi = 0; float bl = -1f;
                for (int i = 1; i < n; i++)
                {
                    float L = Vector3.Distance(lr.GetPosition(i - 1), lr.GetPosition(i));
                    if (L > bl) { bl = L; bi = i; }
                }
                Vector3 a = lr.GetPosition(bi - 1);
                Vector3 b = lr.GetPosition(bi);
                RaycastHit h1, h2;
                bool g1 = Physics.Linecast(a, b, out h1, ~0, QueryTriggerInteraction.Ignore);
                bool g2 = Physics.Linecast(a, b, out h2, ~0, QueryTriggerInteraction.Collide);
                _castLogs++;
                LogInfo("[弹道学][CAST] id=" + key + " len=" + bl.ToString("F1") + "m hitIgnore=" + g1
                    + (g1 ? "(" + (h1.collider == null ? "null" : h1.collider.name) + ")" : "")
                    + " hitTrigger=" + g2 + (g2 ? "(" + (h2.collider == null ? "null" : h2.collider.name) + ")" : "")
                    + " a=" + a + " b=" + b);
            }

            if (!byHit && n <= 2)
            {
                float len = 0f;
                if (lr != null && n == 2) len = Vector3.Distance(lr.GetPosition(0), lr.GetPosition(1));
                if (len < 2f)
                {
                    int idx = -1;
                    for (int k = 0; k < Spawned.Count; k++) if (Spawned[k] == lr) { idx = k; break; }
                    if (idx >= 0) ReleaseLine(idx, "stub");
                }
                else LogInfo("[弹道学] kept short trail len=" + len.ToString("F2") + " m");
            }
            else
            {
                LogInfo("[弹道学] closed " + key + " verts=" + n + (byHit ? " (hit)" : " (idle)"));
                if (lr != null && n >= 2 && _oddLogs < 40)
                {
                    Vector3 a0 = lr.GetPosition(0);
                    Vector3 b0 = lr.GetPosition(n - 1);
                    float span = Vector3.Distance(a0, b0);
                    if (span > 30f)
                    {
                        _oddLogs++;
                        bool g0;
                        Grenade.TryGetValue(key, out g0);
                        LogInfo("[弹道学][ODD] id=" + key + " span=" + span.ToString("F0") + "m"
                            + " a=" + a0 + " b=" + b0 + " gren=" + g0 + " verts=" + n);
                    }
                }
                if (lr != null && n >= 2 && _coordLogs < LogCap)
                {
                    _coordLogs++;
                    LogInfo("[弹道学][COORD] id=" + key + " a=" + lr.GetPosition(0) + " b=" + lr.GetPosition(n - 1) + " verts=" + n);
                }
                if (lr != null && n >= 2)
                {
                    float span2 = Vector3.Distance(lr.GetPosition(0), lr.GetPosition(n - 1));
                    if (span2 > 30f)
                        LogInfo("[弹道学][ODD] close id=" + key + " a=" + lr.GetPosition(0) + " b=" + lr.GetPosition(n - 1) + " span=" + span2.ToString("F0") + " m");
                }
                if (lr != null && Lifetime() > 0f)
                    for (int i = 0; i < Spawned.Count; i++)
                        if (Spawned[i] == lr) { Expiry[i] = Time.time + Lifetime(); break; }
            }

            if (has) Trails.Remove(key);
            TrailPts.Remove(key);
            TrailSeen.Remove(key);
            TrailShot.Remove(key);
            StartPos.Remove(key);
            Grenade.Remove(key);
            PendingHit.Remove(key);
            Pen.Remove(key);
            Dmg.Remove(key);
            PenBase.Remove(key);
            DmgBase.Remove(key);
            PenHist.Remove(key);
            ShotRole.Remove(key);
            RatioCurve.Remove(key);
            PlayPos.Remove(key);
            FullMode.Remove(key);
            PlayIdx.Remove(key);
            PenScale.Remove(key);
            DmgScale.Remove(key);
            DmgHist.Remove(key);
            ClosedByHit.Remove(key);
            PendingHitAt.Remove(key);
            RealPen.Remove(key); RealDmg.Remove(key); RealAt.Remove(key);
            ParentOf.Remove(key);
            if (shot != null && ShotStopped(shot)) ShotId.Remove(shot);
            {
                // 收尾：最后一条记录若还是“未知”，用游戏的停止信号定论
                HitRec lastRec;
                if (shot != null && LastRecByShot.TryGetValue(shot, out lastRec) && lastRec != null && !lastRec.Degraded)
                {
                    if (!ShotStopped(shot))
                    {
                        lastRec.PenAfter = Num(GetMember(shot, "PenetrationPower"), lastRec.PenBefore);
                        lastRec.DmgAfter = Num(GetMember(shot, "Damage"), lastRec.DmgBefore);
                        lastRec.Degraded = true;
                    }
                }
                if (shot != null) LastRecByShot.Remove(shot);
            }
        }

        private static readonly Dictionary<string, Color> _hexRgb = new Dictionary<string, Color>(StringComparer.Ordinal);

        internal static Color ParseHex(string hex, Color dflt, float alpha)
        {
            Color rgb;
            string key = hex ?? "";
            if (!_hexRgb.TryGetValue(key, out rgb))
            {
                rgb = new Color(dflt.r, dflt.g, dflt.b, 1f);
                try
                {
                    if (!string.IsNullOrEmpty(hex))
                    {
                        string s = hex.Trim().TrimStart('#');
                        if (s.Length >= 6)
                            rgb = new Color(Convert.ToInt32(s.Substring(0, 2), 16) / 255f,
                                            Convert.ToInt32(s.Substring(2, 2), 16) / 255f,
                                            Convert.ToInt32(s.Substring(4, 2), 16) / 255f, 1f);
                    }
                }
                catch (Exception) { }
                _hexRgb[key] = rgb;
            }
            rgb.a = alpha;
            return rgb;
        }

        private static int _blastMask = int.MinValue;
        internal static float MiniBlastRadius
        {
            get { return 0f; }   // 不再按半径过滤（破片级小爆炸也画）
        }

        // 与游戏一致的遮挡层：LayersMaskController.GrenadeAffectedMask
        internal static int BlastMask()
        {
            if (_blastMask != int.MinValue) return _blastMask;
            _blastMask = Physics.DefaultRaycastLayers;
            try
            {
                Type t = ImplContext.FindType("LayersMaskController");
                if (t != null)
                {
                    string[] cands = { "HighPolyWithTerrainMask", "HighPolyWithTerrainNoGrassMask", "HighPolyCollider", "GrenadeObstaclesColliderMask" };
                    for (int i = 0; i < cands.Length; i++)
                    {
                        FieldInfo f = t.GetField(cands[i],
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        if (f == null) continue;
                        object v = f.GetValue(null);
                        int got;
                        if (TryMaskInt(v, out got) && got != 0)
                        {
                            _blastMask = got;
                            LogInfo("[弹道学] blast mask = " + cands[i] + " (" + got + ")");
                            return _blastMask;
                        }
                    }
                }
                LogInfo("[弹道学] blast mask fallback default (" + _blastMask + ")");
            }
            catch (Exception e) { SafeLog("BlastMask: " + e.Message); }
            return _blastMask;
        }

        internal static bool TryMaskInt(object v, out int got)
        {
            got = 0;
            if (v == null) return false;
            if (v is int) { got = (int)v; return true; }
            Type vt = v.GetType();
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            FieldInfo f = vt.GetField("m_Mask", flags) ?? vt.GetField("value", flags);
            if (f != null)
            {
                object o = f.GetValue(v);
                if (o is int) { got = (int)o; return true; }
            }
            PropertyInfo pr = vt.GetProperty("value", flags);
            if (pr != null)
            {
                object o = pr.GetValue(v, null);
                if (o is int) { got = (int)o; return true; }
            }
            try { got = Convert.ToInt32(v); return true; } catch (Exception) { }
            return false;
        }

        // 用射线把球体贴到真实几何上：沿用 primitive 球的 Renderer/Material（可见性有保证），
        // 但换成高细分网格，避免内置球(24×24)在裁剪后棱角过粗。
        internal static Color PhysColorC()
        {
            string hex = ImplContext.PhysColor == null ? "#FFB733" : ImplContext.PhysColor.Value;
            return ParseHex(hex, new Color(1f, 0.72f, 0.2f), 1f);
        }

        internal static float AlphaV()
        {
            return ImplContext.TrailAlpha == null ? 1f : Mathf.Clamp01(ImplContext.TrailAlpha.Value);
        }

        internal static Color A(Color c)
        {
            c.a = AlphaV();
            return c;
        }

        internal static Color TrailBaseC()
        {
            string hex = ImplContext.TrailColor == null ? "#FF0000" : ImplContext.TrailColor.Value;
            return A(ParseHex(hex, new Color(1f, 0f, 0f), 1f));
        }

        internal static Color InnerColorC()
        {
            string hex = ImplContext.InnerColor == null ? "#FFC73F" : ImplContext.InnerColor.Value;
            float a = ImplContext.InnerAlpha == null ? 0.45f : ImplContext.InnerAlpha.Value;
            Color c = ParseHex(hex, new Color(1f, 0.78f, 0.25f), 1f);
            return new Color(c.r, c.g, c.b, Mathf.Clamp(a, 0.02f, 1f));
        }

        internal static Color StunColorC()
        {
            string hex = ImplContext.StunColor == null ? "#FFE659" : ImplContext.StunColor.Value;
            Color c = ParseHex(hex, new Color(1f, 0.9f, 0.35f), 1f);
            return new Color(c.r, c.g, c.b, 0.8f);
        }

        // 内径范围：几何绘制开启时同样做贴几何裁剪
        internal static void SpawnInner(Vector3 center, float r, float strength = 0f, float pen = 0f, float maxR = 0f, int density = 0)
        {
            if (r <= 0.1f) return;
            Color c = InnerColorC();
            if (strength > 0f && pen > 0f)
            {
                SpawnBlastVolume(center, r, c, density, strength, pen, r, maxR > 0.1f ? maxR : r);
                return;
            }
            if (ImplContext.GeomOn != null && ImplContext.GeomOn.Value) SpawnBlastVolume(center, r, c);
            else SpawnBlastSphere(center, r, c);
        }

        // 眩晕范围：几何绘制开启时按射线裁剪半径
        // 眩晕圈逐方向半径：实时与记录共用，含墙体吸收与 Contusion 换算
        internal static float StunRingRadiusAt(Vector3 center, Vector3 dir, float r, float cx, float cy, float cz, int mask)
        {
            RaycastHit hit;
            if (!Physics.Raycast(center, dir, out hit, r, mask, QueryTriggerInteraction.Ignore)) return r;
            float w = hit.distance;
            float f = WallAbsorbFactor(w);
            if (cz > 2f && cy > 0.1f && f > 0.0001f)
            {
                float d2 = cy - (cy - cx) * (2f / (cz * f));
                return Mathf.Clamp(d2, 0.05f, Mathf.Max(0.05f, w - 0.02f));
            }
            return Mathf.Max(0.05f, w - 0.02f);
        }
        private static readonly List<LineRenderer> StunRings = new List<LineRenderer>();   // 已画的眩晕环

        // 关掉可视化开关时立刻清掉已画出的范围/眩晕环（默认寿命无限，否则会一直留在场上）
        internal static void ClearBlastVisuals()
        {
            try
            {
                for (int i2 = 0; i2 < Blasts.Count; i2++) if (Blasts[i2] != null) UnityEngine.Object.Destroy(Blasts[i2]);
                Blasts.Clear(); BlastExpiry.Clear();
                for (int i2 = 0; i2 < StunRings.Count; i2++)
                {
                    LineRenderer lr = StunRings[i2];
                    if (lr == null) continue;
                    int idx = Spawned.IndexOf(lr);
                    if (idx >= 0) ReleaseLine(idx, "stun-off");
                    else UnityEngine.Object.Destroy(lr.gameObject);
                }
                StunRings.Clear();
                _blastDone.Clear();
            }
            catch (Exception) { }
        }

        internal static void SpawnStunRing(Vector3 center, float r, object srcObj = null)
        {
            if (r <= 0.1f) return;
            float cx, cy, cz;
            if (!ContusionVals(srcObj, out cx, out cy, out cz)) { cx = 0f; cy = 0f; cz = 0f; }
            Color c = StunColorC();
            if (ImplContext.GeomOn == null || !ImplContext.GeomOn.Value)
            {
                StunRings.Add(SpawnRing(center, r, c, 0.02f, 64));
                SetLastLineLifetime();
                return;
            }
            int seg = 64;
            int mask = BlastMask();
            LineRenderer lr = SpawnLine(seg + 1, c, Mathf.Max(0.02f, r * 0.012f));
            StunRings.Add(lr);
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float rr = StunRingRadiusAt(center, dir, r, cx, cy, cz, mask);

                lr.SetPosition(i, center + dir * rr + new Vector3(0f, 0.06f, 0f));
            }
            SetLastLineLifetime();
        }

        private static Shader _blastShader;
        private static readonly Dictionary<int, Material> _blastMats = new Dictionary<int, Material>();

        // Shader.Find 是重操作，缓存一次；材质按颜色复用
        internal static Material BlastMaterial(Color c)
        {
            int key = ((int)(c.r * 255f) << 16) | ((int)(c.g * 255f) << 8) | (int)(c.b * 255f);
            Material m;
            if (_blastMats.TryGetValue(key, out m) && m != null) return m;
            if (_blastShader == null)
            {
                _blastShader = Shader.Find("Sprites/Default");
                if (_blastShader == null) _blastShader = Shader.Find("Hidden/Internal-Colored");
            }
            m = new Material(_blastShader);
            m.hideFlags = HideFlags.HideAndDontSave;
            m.SetInt("_ZWrite", 0);
            m.SetInt("_Cull", 0);
            m.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            _blastMats[key] = m;
            return m;
        }

        internal static object InvokeObj(object o, string name)
        {
            if (o == null) return null;
            try
            {
                MethodInfo[] all = MethodsOf(o.GetType());
                for (int i = 0; i < all.Length; i++)
                {
                    MethodInfo m = all[i];
                    if (m.Name != name || m.GetParameters().Length != 0) continue;
                    return m.Invoke(o, null);
                }
            }
            catch (Exception) { }
            return null;
        }
        internal static float InvokeFloat(object o, string name)
        {
            if (o == null) return 0f;
            try
            {
                MethodInfo[] all = MethodsOf(o.GetType());
                for (int i = 0; i < all.Length; i++)
                {
                    MethodInfo m = all[i];
                    if (m.Name != name || m.GetParameters().Length != 0) continue;
                    object v = m.Invoke(o, null);
                    return v == null ? 0f : Convert.ToSingle(v);
                }
            }
            catch (Exception) { }
            return 0f;
        }

        internal static void BlastParamsOf(object item, out float strength, out float pen)
        {
            strength = 0f; pen = 0f;
            if (item == null) return;
            strength = Num(GetMember(item, "GetStrength"), 0f);
            if (strength <= 0f) strength = Num(GetMember(item, "Strength"), 0f);
            object src = item;
            object frag = InvokeObj(src, "CreateFragment");
            if (frag == null)
            {
                object inner = GetMember(src, "Item");
                if (inner == null) inner = GetMember(src, "WeaponSource");
                if (inner != null) { src = inner; frag = InvokeObj(src, "CreateFragment"); }
            }
            object ap = frag == null ? null : GetMember(frag, "PenetrationPower");
            object ammo = GetMember(src, "Ammo");
            if (ap == null && ammo != null) ap = GetMember(ammo, "PenetrationPower");
            if (strength <= 0f)
            {
                object tpl2 = GetMember(src, "Template");
                if (tpl2 != null) strength = Num(GetMember(tpl2, "GetStrength"), 0f);
            }
            if (ap != null) { try { pen = Convert.ToSingle(ap); } catch (Exception) { } }
            if (_bpLogs < 6)
            {
                _bpLogs++;
                LogInfo("[弹道学][BLASTP] type=" + item.GetType().Name
                    + " strength=" + strength.ToString("F1")
                    + " pen=" + pen.ToString("F1")
                    + " ammo=" + (ammo != null)
                    + " maxR=" + Num(GetMember(item, "MaxExplosionDistance"), -1f).ToString("F2"));
            }
        }

        private static int _bpLogs;
        private static readonly RaycastHit[] _blastHits = new RaycastHit[16];
        private static Type _bcType;

        internal static float BlastPenFactor(Vector3 from, Vector3 dir, float dist, float pen, int mask, out bool blocked)
        {
            blocked = false;
            if (pen <= 0f) return 1f;
            float sum = 0f;
            int n = Physics.RaycastNonAlloc(from, dir, _blastHits, dist, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider col = _blastHits[i].collider;
                if (col == null) continue;
                if (_bcType == null) _bcType = ImplContext.FindType("BallisticCollider");
                if (_bcType == null) break;
                Component bc = col.GetComponent(_bcType);
                if (bc == null && col.transform != null && col.transform.parent != null)
                    bc = col.transform.parent.GetComponent(_bcType);
                if (bc == null) continue;
                float level = Num(GetMember(bc, "PenetrationLevel"), 0f);
                float chance = Num(GetMember(bc, "PenetrationChance"), 1f);
                if (chance < 1e-45f || pen < level) { blocked = true; return 0f; }
                if (level >= 1e-45f) sum += level + 2f;
            }
            float t = Mathf.Clamp01(Mathf.Clamp(pen - sum, 0f, pen) / pen);
            return 1f - Mathf.Sqrt(Mathf.Max(0f, 1f - t * t));
        }

        // 三档半径（都为原版量的边界）：
        //   rFull   = 满伤害区：falloff = 1 且路径未被任何墙削弱 → min(minR, 第一层墙)
        //   rDirect = 直面冲击：路径未被削弱              → min(maxR, 第一层墙)
        //   rAny    = 伤害 > 0：距离衰减 + 隔墙衰减后仍 > 0 → 归零处或 maxR
        internal static void BlastBands(Vector3 from, Vector3 dir, float maxR, float minR, float pen, int mask,
            out float rFull, out float rDirect, out float rAny)
        {
            float firstWall = maxR;
            float zeroAt = -1f;
            int n = Physics.RaycastNonAlloc(from, dir, _blastHits, maxR, mask, QueryTriggerInteraction.Ignore);
            if (n > 0)
            {
                for (int i = 1; i < n; i++)
                {
                    RaycastHit key = _blastHits[i];
                    int j = i - 1;
                    while (j >= 0 && _blastHits[j].distance > key.distance) { _blastHits[j + 1] = _blastHits[j]; j--; }
                    _blastHits[j + 1] = key;
                }
                if (_bcType == null) _bcType = ImplContext.FindType("BallisticCollider");
                float sum = 0f;
                for (int i = 0; i < n; i++)
                {
                    Collider col = _blastHits[i].collider;
                    if (col == null || _bcType == null) continue;
                    Component bc = col.GetComponent(_bcType);
                    if (bc == null && col.transform != null && col.transform.parent != null)
                        bc = col.transform.parent.GetComponent(_bcType);
                    if (bc == null) continue;
                    float level = Num(GetMember(bc, "PenetrationLevel"), 0f);
                    float chance = Num(GetMember(bc, "PenetrationChance"), 1f);
                    if (level <= 1e-45f && chance >= 1e-45f) continue;
                    if (firstWall >= maxR) firstWall = _blastHits[i].distance;
                    if (chance < 1e-45f || pen < level) { zeroAt = _blastHits[i].distance; break; }
                    sum += level + 2f;
                    if (pen - sum <= 0f) { zeroAt = _blastHits[i].distance; break; }
                }
            }
            float wall = firstWall < maxR - 0.001f ? Mathf.Max(0.05f, firstWall - 0.01f) : maxR;
            rDirect = Mathf.Min(maxR, wall);
            rFull = Mathf.Max(0.02f, Mathf.Min(minR > 0.1f ? minR : rDirect, wall));
            rAny = zeroAt > 0f ? Mathf.Max(0.02f, zeroAt - 0.02f) : maxR;
        }

        // 该方向上"仍然会受到伤害"的最大距离：
        //   遇到挡死的遮挡体（chance<ε 或 pen<level）→ 在它那里截止，之后伤害为 0；
        //   否则整段都有伤害（只是被衰减），返回 rr。
        internal static Color DamageColor(float f, float baseAlpha)
        {
            f = Mathf.Clamp01(f);
            Color col = f <= 0.5f
                ? Color.Lerp(Color.white, Color.yellow, f * 2f)
                : Color.Lerp(Color.yellow, Color.red, (f - 0.5f) * 2f);
            col.a = f * baseAlpha;
            return col;
        }

        // 三档伤害面：红=满伤害区，橙=直面冲击，淡黄=伤害>0；全部按"伤害归零"裁剪。
        internal static void SpawnBlastShells(Vector3 center, float maxR, Color c, int density, float strength, float pen, float minR)
        {
            if (maxR <= 0.1f || strength <= 0f || pen <= 0f) return;
            int mask = BlastMask();
            int LAT = density > 0 ? Mathf.Clamp(density, 6, 40)
                : (ImplContext.GeomSegments == null ? 20 : Mathf.Clamp(ImplContext.GeomSegments.Value, 8, 40));
            int LON = LAT * 2;
            const int layers = 3;
            int ring = (LAT + 1) * (LON + 1);
            int vc = ring * layers;
            Color[] band = new Color[layers];
            band[0] = new Color(1f, 0.18f, 0.18f, c.a);     // 内：红
            band[1] = new Color(1f, 0.55f, 0.10f, c.a);     // 中：橙
            band[2] = new Color(1f, 0.94f, 0.60f, c.a);     // 外：淡黄

            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "dsh_blast_bands";
            Collider col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.DestroyImmediate(col);
            go.transform.position = center;
            go.transform.localScale = Vector3.one;

            Vector3[] vs = new Vector3[vc];
            Color[] cols = new Color[vc];
            bool[] hid = new bool[vc];

            int[] tris = new int[layers * LAT * LON * 6];
            int tIdx = 0;
            // 每个方向只算一次（BlastBands 内部要打射线），三层共用
            float[] rFulls = new float[ring], rDirs = new float[ring], rAnys = new float[ring];
            for (int i2 = 0; i2 <= LAT; i2++)
            {
                float th2 = Mathf.PI * i2 / LAT;
                float st2 = Mathf.Sin(th2), ct2 = Mathf.Cos(th2);
                for (int j2 = 0; j2 <= LON; j2++)
                {
                    float ph2 = Mathf.PI * 2f * j2 / LON;
                    Vector3 dir2 = new Vector3(st2 * Mathf.Cos(ph2), ct2, st2 * Mathf.Sin(ph2));
                    float rF2, rD2, rA2;
                    BlastBands(center, dir2, maxR, minR, pen, mask, out rF2, out rD2, out rA2);
                    int vi2 = i2 * (LON + 1) + j2;
                    rFulls[vi2] = rF2; rDirs[vi2] = rD2; rAnys[vi2] = rA2;
                }
            }
            for (int layer = 0; layer < layers; layer++)
            {
                int vBase = layer * ring;
                for (int i = 0; i <= LAT; i++)
                {
                    float theta = Mathf.PI * i / LAT;
                    float st = Mathf.Sin(theta), ct = Mathf.Cos(theta);
                    for (int j = 0; j <= LON; j++)
                    {
                        float phi = Mathf.PI * 2f * j / LON;
                        Vector3 dir = new Vector3(st * Mathf.Cos(phi), ct, st * Mathf.Sin(phi));
                        int idx = vBase + i * (LON + 1) + j;
                        int viDir = i * (LON + 1) + j;
                        float rFull = rFulls[viDir], rDirect = rDirs[viDir], rAny = rAnys[viDir];
                        float r = layer == 0 ? rFull : (layer == 1 ? rDirect : rAny);
                        float inner = layer == 0 ? 0f : (layer == 1 ? rFull : rDirect);
                        if (layer > 0 && r <= inner + 0.02f)
                        {
                            // 被内圈完全覆盖：这块外圈不要（否则两层共面会闪）
                            hid[idx] = true;
                            vs[idx] = dir * Mathf.Max(0.02f, inner);
                            cols[idx] = band[layer - 1];

                        }
                        else
                        {
                            vs[idx] = dir * Mathf.Max(0.02f, r);
                        }
                        if (!hid[idx]) cols[idx] = band[layer];
                    }
                }
                for (int i = 0; i < LAT; i++)
                {
                    for (int j = 0; j < LON; j++)
                    {
                        int a = vBase + i * (LON + 1) + j;
                        int b = a + 1;
                        int c2 = a + (LON + 1);
                        int d = c2 + 1;
                        if (hid[a] && hid[b] && hid[c2] && hid[d]) continue;
                        tris[tIdx++] = a; tris[tIdx++] = c2; tris[tIdx++] = b;
                        tris[tIdx++] = b; tris[tIdx++] = c2; tris[tIdx++] = d;
                    }
                }
            }

            Mesh mesh = new Mesh();
            mesh.vertices = vs;
            mesh.triangles = tris;
            mesh.colors = cols;
            mesh.RecalculateBounds();
            MeshFilter mf = go.GetComponent<MeshFilter>();
            if (mf != null) mf.mesh = mesh;
            Renderer rr2 = go.GetComponent<Renderer>();
            if (rr2 != null)
            {
                rr2.sharedMaterial = BlastMaterial(Color.white);
                rr2.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rr2.receiveShadows = false;
            }
            Blasts.Add(go);
            BlastExpiry.Add(BlastExpiryTime());
        }
        internal static void SpawnBlastVolume(Vector3 center, float radius, Color c, int density = 0,
            float strength = 0f, float pen = 0f, float minR = 0f, float maxR = 0f)
        {
            if (radius <= 0.1f) return;
            int mask = BlastMask();
            int LAT = density > 0 ? Mathf.Clamp(density, 6, 40)
                : (ImplContext.GeomSegments == null ? 20 : Mathf.Clamp(ImplContext.GeomSegments.Value, 8, 40));
            int LON = LAT * 2;
            float margin = 0.02f;

            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "dsh_blast_vol";
            Collider col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.DestroyImmediate(col);   // 立即销毁，避免同帧射线打到自己
            go.transform.position = center;
            go.transform.localScale = Vector3.one;      // 顶点直接写世界尺度半径

            int vc = (LAT + 1) * (LON + 1);
            Vector3[] vs = new Vector3[vc];
            bool dmgOn = strength > 0f && pen > 0f && maxR > 0.1f;
            Color[] cols = dmgOn ? new Color[vc] : null;
            if (cols != null) for (int i = 0; i < vc; i++) cols[i] = c;
            float sumR = 0f; int tiny = 0;
            for (int i = 0; i <= LAT; i++)
            {
                float theta = Mathf.PI * i / LAT;
                float st = Mathf.Sin(theta), ct = Mathf.Cos(theta);
                for (int j = 0; j <= LON; j++)
                {
                    float phi = Mathf.PI * 2f * j / LON;
                    Vector3 dir = new Vector3(st * Mathf.Cos(phi), ct, st * Mathf.Sin(phi));
                    float wr = radius;
                    RaycastHit hit;
                    if (Physics.Raycast(center, dir, out hit, radius, mask, QueryTriggerInteraction.Ignore))
                        wr = Mathf.Max(0.05f, hit.distance - margin);
                    sumR += wr;
                    if (wr < 0.3f) tiny++;
                    vs[i * (LON + 1) + j] = dir * wr;
                    if (cols != null)
                    {
                        bool blocked;
                        float pf = BlastPenFactor(center, dir, wr, pen, mask, out blocked);
                        float fall = Mathf.InverseLerp(maxR, minR, wr);
                        float f = pf <= 0f ? 0f : Mathf.Clamp01(fall * pf);
                        cols[i * (LON + 1) + j] = DamageColor(f, c.a);
                    }
                }
            }
            float avg = sumR / vc;
            if (tiny > vc * 0.95f || avg < radius * 0.05f)
            {
                for (int i = 0; i <= LAT; i++)
                {
                    float theta = Mathf.PI * i / LAT;
                    float st = Mathf.Sin(theta), ct = Mathf.Cos(theta);
                    for (int j = 0; j <= LON; j++)
                    {
                        float phi = Mathf.PI * 2f * j / LON;
                        vs[i * (LON + 1) + j] = new Vector3(st * Mathf.Cos(phi), ct, st * Mathf.Sin(phi)) * radius;
                    }
                }
                LogInfo("[弹道学] blast volume: 爆心被包围，保持理想球 r=" + radius.ToString("F2")
                    + " avg=" + avg.ToString("F2") + " tiny=" + tiny + "/" + vc);
            }

            int[] tris = new int[LAT * LON * 6];
            int k = 0;
            for (int i = 0; i < LAT; i++)
            {
                for (int j = 0; j < LON; j++)
                {
                    int a = i * (LON + 1) + j;
                    int b = a + 1;
                    int c2 = a + (LON + 1);
                    int d = c2 + 1;
                    tris[k++] = a; tris[k++] = c2; tris[k++] = b;
                    tris[k++] = b; tris[k++] = c2; tris[k++] = d;
                }
            }

            Mesh mesh = new Mesh();
            mesh.vertices = vs;
            mesh.triangles = tris;
            if (cols != null) mesh.colors = cols;
            mesh.RecalculateBounds();

            MeshFilter mf = go.GetComponent<MeshFilter>();
            if (mf != null) mf.mesh = mesh;      // 复用 primitive 的 Renderer，只换网格

            Renderer rr = go.GetComponent<Renderer>();
            if (rr != null)
            {
                rr.sharedMaterial = BlastMaterial(cols != null ? Color.white : c);
                rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rr.receiveShadows = false;
            }

            Blasts.Add(go);
            BlastExpiry.Add(BlastExpiryTime());
        }

        internal static void SpawnCircleXZ(Vector3 center, float r, Color c, float w, int seg)
        {
            LineRenderer lr = SpawnLine(seg + 1, c, w);
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                lr.SetPosition(i, center + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
            }
        }

        internal static void SpawnCircleXY(Vector3 center, float r, Color c, float w, int seg)
        {
            LineRenderer lr = SpawnLine(seg + 1, c, w);
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                lr.SetPosition(i, center + new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f));
            }
        }

        internal static void SpawnCircleYZ(Vector3 center, float r, Color c, float w, int seg)
        {
            LineRenderer lr = SpawnLine(seg + 1, c, w);
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                lr.SetPosition(i, center + new Vector3(0f, Mathf.Sin(a) * r, Mathf.Cos(a) * r));
            }
        }

        internal static void SpawnBlastSphere(Vector3 center, float radius, Color c)
        {
            if (radius <= 0.1f) return;
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "dsh_blast";
            Collider col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.DestroyImmediate(col);
            go.transform.position = center + new Vector3(0f, 0.05f, 0f);
            go.transform.localScale = new Vector3(radius * 2f, radius * 2f, radius * 2f);

            Renderer r = go.GetComponent<Renderer>();
            if (r != null)
            {
                r.sharedMaterial = BlastMaterial(c);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }

            Blasts.Add(go);
            BlastExpiry.Add(BlastExpiryTime());
        }

        internal static LineRenderer SpawnRing(Vector3 center, float radius, Color c, float width, int segments)
        {
            if (radius <= 0.1f) return null;
            LineRenderer lr = SpawnLine(segments + 1, c, width);
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                lr.SetPosition(i, center + new Vector3(Mathf.Cos(a) * radius, 0.06f, Mathf.Sin(a) * radius));
            }
            return lr;
        }

        private static readonly Dictionary<int, float> _blastDone = new Dictionary<int, float>();

        private static bool BlastSeen(int key)
        {
            float t;
            if (_blastDone.TryGetValue(key, out t) && Time.time - t < 3f) return true;
            _blastDone[key] = Time.time;
            if (_blastDone.Count > 256) _blastDone.Clear();
            return false;
        }

        public static void DamageInfoPostfix(object __instance, object[] __args, ref object __result)
        {
            try
            {
                if (__result == null) return;
                // GetDamageInfo 返回的才是"针对这块碰撞体算好的"伤害/穿透（含穿墙/护甲扣减）
                try
                {
                    object sh1 = (__args != null && __args.Length > 0) ? __args[0] : null;
                    object oPen = GetMember(__result, "PenetrationPower");
                    object oDmg = GetMember(__result, "Damage");
                    float fPen = oPen == null ? 0f : Convert.ToSingle(oPen);
                    float fDmg = oDmg == null ? 0f : Convert.ToSingle(oDmg);
                    object oMat = GetMember(__instance, "TypeOfMaterial");
                    if (ImplContext.DebugLog != null && ImplContext.DebugLog.Value)
                        LogInfo("[弹道学][DINFO] mat=" + (oMat == null ? "?" : oMat.ToString()) + " pen=" + fPen.ToString("F1") + " dmg=" + fDmg.ToString("F1"));
                    int hk2;
                    if (sh1 != null && ShotId.TryGetValue(sh1, out hk2) && fDmg > 0f)
                    {
                        int atp2 = TrailPts.ContainsKey(hk2) ? TrailPts[hk2] : 1;
                        if (!RealPen.ContainsKey(hk2)) { RealPen[hk2] = new List<float>(); RealDmg[hk2] = new List<float>(); RealAt[hk2] = new List<int>(); }
                        RealPen[hk2].Add(fPen);
                        RealDmg[hk2].Add(fDmg);
                        RealAt[hk2].Add(atp2);
                    }
                }
                catch (Exception) { }
                object dt = GetMember(__result, "DamageType");
                string dtName = dt == null ? "?" : dt.ToString();
                Vector3 center;
                bool has = TryVector(__result, out center, "MasterOrigin", "HitPoint");
                LogInfo("[弹道学] damageinfo fired type=" + dtName + " center=" + (has ? center.ToString() : "none"));
                if (!ImplContext.ModOn.Value || !ImplContext.BlastOn.Value || !has) return;

                object ammo = ResolveAmmoNear(center);
                if (ammo == null)
                {
                    if (ImplContext.DebugLog.Value)
                        LogInfo("[弹道学] blast skipped: 附近无命中弹药记录 type=" + dtName);
                    return;
                }
                DrawBlastAt(center,
                    Num(PropOf(ammo, "MaxExplosionDistance"), 0f),
                    Num(PropOf(ammo, "MinExplosionDistance"), 0f),
                    "ammo:" + AmmoLabel(ammo));

            }
            catch (Exception e) { SafeLog("DamageInfo: " + e.Message); }
        }

        public static void GrenadeBlowupPostfix(object __instance, object[] __args)
        {
            try
            {
                // 爆炸发生：按位置收掉实时预警，并阻止该手雷再生成预警
                Vector3 atPos = (__args != null && __args.Length >= 3 && __args[2] is Vector3)
                    ? (Vector3)__args[2]
                    : (__instance is Component ? ((Component)__instance).transform.position : Vector3.zero);
                LiveBlastClearAll(atPos);
                // 静态重载：Grenade.Explosion(grenade, grenadeItem, grenadePosition, …)
                // 静态重载 Grenade.Explosion(grenade, grenadeItem, pos, …)
                // 手雷爆炸时 grenade 非空；爆炸子弹由 GameWorld.ShotDelegate 以 grenade=null 调用，
                // 两者都是真实爆炸，按 grenadeItem 的半径绘制
                if (__instance == null && __args != null && __args.Length >= 3 && __args[2] is Vector3)
                {
                    Vector3 pos = (Vector3)__args[2];
                    object gItem = __args[1];
                    float gMax = Num(PropOf(gItem, "MaxExplosionDistance"), 0f);
                    float gMin = Num(PropOf(gItem, "MinExplosionDistance"), 0f);
                    if (gMax > 0.1f && ImplContext.BlastOn != null && ImplContext.BlastOn.Value)
                        DrawBlastAt(pos, gMax, gMin, "grenade:" + (gItem == null ? "?" : gItem.GetType().Name), gItem);
                    return;
                }

                MonoBehaviour mb = __instance as MonoBehaviour;
                if (mb == null) { LogInfo("[弹道学] blowup hook fired but instance is not a MonoBehaviour"); return; }
                LogInfo("[弹道学] blowup hook fired at " + mb.transform.position);
                if (!ImplContext.ModOn.Value || !ImplContext.BlastOn.Value) return;
                if (ImplContext.ModOn.Value && ImplContext.BlastOn != null && ImplContext.BlastOn.Value)
                    DrawBlast(mb.transform.position, __instance);
            }
            catch (Exception e) { SafeLog("Blowup: " + e.Message); }
        }

        private static float Num(object o, float dflt)
        {
            if (o == null) return dflt;
            try { return Convert.ToSingle(o); } catch (Exception) { return dflt; }
        }

        // 游戏自己的爆炸设置对象（IExplosiveItem）里就有真实半径
        public static void BlastVolumePrefix(object[] __args)
        {
            try
            {
            try
            {
                // 真实爆炸已触发：按位置收掉实时预警
                Vector3 bp = Vector3.zero; bool has = false;
                if (__args != null)
                {
                    for (int i = 0; i < __args.Length && i < 3; i++)
                        if (__args[i] is Vector3) { bp = (Vector3)__args[i]; has = true; break; }
                }
                LiveBlastClearAll(has ? (Vector3?)bp : null);
            }
            catch (Exception) { }
                if (!ImplContext.ModOn.Value || ImplContext.BlastOn == null || !ImplContext.BlastOn.Value) return;
                if (__args == null || __args.Length < 3) return;
                if (!(__args[2] is Vector3)) return;
                Vector3 pos = (Vector3)__args[2];
                object item = __args[1];

                float maxR = 0f, minR = 0f;
                string src = "expl";
                if (item != null)
                {
                    maxR = Num(PropOf(item, "MaxExplosionDistance"), 0f);
                    minR = Num(PropOf(item, "MinExplosionDistance"), 0f);
                    src = "expl:" + item.GetType().Name;
                }
                if (maxR < Mathf.Max(0.1f, MiniBlastRadius))
                {
                    // 破片伪爆炸（1 米级）：改查命中弹药记忆，拿真实半径
                    object ammo = ResolveAmmoNear(pos);
                    if (ammo == null)
                    {
                        if (ImplContext.DebugLog.Value)
                            LogInfo("[弹道学] blast skipped: expl item maxR=" + maxR.ToString("F2")
                                + " item=" + (item == null ? "null" : item.GetType().Name) + " 无弹药记录");
                        return;
                    }
                    maxR = Num(PropOf(ammo, "MaxExplosionDistance"), 0f);
                    minR = Num(PropOf(ammo, "MinExplosionDistance"), 0f);
                    src = "ammo:" + AmmoLabel(ammo);
                }
                DrawBlastAt(pos, maxR, minR, src, item);
            }
            catch (Exception e) { SafeLog("BlastVolume: " + e.Message); }
        }

        // 显式接口实现（EFT.IExplosiveItem.get_MaxExplosionDistance）用后缀匹配调用
        private static readonly Dictionary<Type, MethodInfo[]> _typeMethods = new Dictionary<Type, MethodInfo[]>();

        private static MethodInfo[] MethodsOf(Type t)
        {
            MethodInfo[] arr;
            if (_typeMethods.TryGetValue(t, out arr)) return arr;
            try { arr = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance); }
            catch (Exception) { arr = new MethodInfo[0]; }
            _typeMethods[t] = arr;
            return arr;
        }

        internal static object PropOf(object o, string prop)
        {
            if (o == null) return null;
            object direct = GetMember(o, prop);
            if (direct != null) return direct;
            try
            {
                string suffix = "get_" + prop;
                MethodInfo[] all = MethodsOf(o.GetType());
                for (int i = 0; i < all.Length; i++)
                {
                    MethodInfo m = all[i];
                    if (!m.Name.EndsWith(suffix, StringComparison.Ordinal)) continue;
                    if (m.GetParameters().Length != 0) continue;
                    object v = m.Invoke(o, null);
                    if (v != null) return v;
                }
            }
            catch (Exception e) { SafeLog("PropOf " + prop + ": " + e.Message); }
            return null;
        }

        // ---- 命中点 → 弹药模板 记忆（供爆炸子弹取真实半径） ----
        private const int HIT_MEM = 64;
        private static readonly bool _blastDebugOff = false;
        private static readonly Vector3[] _hitPos = new Vector3[HIT_MEM];
        private static readonly object[] _hitAmmo = new object[HIT_MEM];
        private static readonly float[] _hitTime = new float[HIT_MEM];
        private static int _hitCursor;

        internal static void RememberAmmoHit(Vector3 pos, object ammo)
        {
            if (ammo == null) return;
            int i = _hitCursor++ % HIT_MEM;
            _hitPos[i] = pos;
            _hitAmmo[i] = ammo;
            _hitTime[i] = Time.time;
        }

        // 只接受"本身带爆炸半径"的弹药：破片弹药(shrapnel)的半径是 0，不能用来画爆炸
        internal static object ResolveAmmoNear(Vector3 pos)
        {
            float now = Time.time;
            object best = null;
            float bestDist = 3.0f;
            for (int i = 0; i < HIT_MEM; i++)
            {
                object ammo = _hitAmmo[i];
                if (ammo == null) continue;
                if (now - _hitTime[i] > 1.0f) continue;
                float maxR = Num(PropOf(ammo, "MaxExplosionDistance"), 0f);
                if (maxR <= 0.1f) continue;
                float d = Vector3.Distance(pos, _hitPos[i]);
                if (d < bestDist) { bestDist = d; best = ammo; }
            }
            return best;
        }

        // 手雷与爆炸子弹共用同一套绘制
        private static readonly List<Vector3> _blastPts = new List<Vector3>();
        private static readonly List<float> _blastTimes = new List<float>();

        // 同一个爆炸往往从多条路径进来（命中 / Explosion / DamageInfo），这里做短时同点合并
        internal static Transform AsTransform(object o)
        {
            if (o == null) return null;
            Transform t = o as Transform;
            if (t != null) return t;
            Component c = o as Component;
            if (c != null) return c.transform;
            GameObject g = o as GameObject;
            if (g != null) return g.transform;
            return GetMember(o, "transform", "Transform") as Transform;
        }

        internal static bool BlastRecentlyDrawn(Vector3 center)
        {
            float now = Time.time;
            for (int i = _blastPts.Count - 1; i >= 0; i--)
            {
                if (now - _blastTimes[i] > 3f) { _blastPts.RemoveAt(i); _blastTimes.RemoveAt(i); continue; }
                if (Vector3.Distance(_blastPts[i], center) < 0.5f) return true;
            }
            _blastPts.Add(center);
            _blastTimes.Add(now);
            return false;
        }

        // 原版眩晕判定用 Contusion（Vector2：[内径, 外径]），距离 > y 就不施加眩晕
        internal static float StunRadius(object srcObj, float maxR)
        {
            float cx0, cy0, cz0;
            if (ContusionVals(srcObj, out cx0, out cy0, out cz0) && cz0 > 2f)
            {
                float rr0 = cy0 - (cy0 - cx0) * (2f / cz0);
                if (rr0 > 0.1f) return rr0;
            }
            float v = ContusionY(srcObj);
            if (v > 0.1f)
            {
                // 眩晕圈不应超过爆炸体积本身（破片级伪爆炸 maxR=1 时 Contusion=3 会显得突兀）
                if (v > maxR) v = maxR;
                if (_stunLogs < 12)
                {
                    _stunLogs++;
                    LogInfo("[弹道学][STUN] " + (srcObj == null ? "null" : srcObj.GetType().Name)
                        + " contusion.y=" + v.ToString("F2") + " maxR=" + maxR.ToString("F2") + " -> 用 Contusion");
                }
                return v;
            }

            // 显式接口 / 组件上的 Item.Template 再试一次
            if (srcObj is Component)
            {
                Component[] comps = ((Component)srcObj).GetComponents<Component>();
                for (int i = 0; i < comps.Length; i++)
                {
                    if (comps[i] == null || comps[i].GetType().Name != "Item") continue;
                    float v2 = ContusionY(GetMember(comps[i], "Template"));
                    if (v2 > 0.1f) return v2;
                    break;
                }
            }
            if (_stunLogs < 12)
            {
                _stunLogs++;
                LogInfo("[弹道学][STUN] " + (srcObj == null ? "null" : srcObj.GetType().Name)
                    + " 无 Contusion -> 兜底 maxR(" + maxR.ToString("F2") + ") × " + StunMul().ToString("F2"));
            }
            return maxR * StunMul();
        }

        private static int _contLogs;
        private static int _stunLogs;

        private static bool _wcaTried;
        private static bool _wcaOk;
        private static Vector3 _wca;

        internal static float WallAbsorbFactor(float dist)
        {
            if (!_wcaTried)
            {
                _wcaTried = true;
                try
                {
                    Type t = ImplContext.FindType("GlobalConfiguration");
                    if (t != null)
                    {
                        UnityEngine.Object o = UnityEngine.Object.FindObjectOfType(t) as UnityEngine.Object;
                        if (o != null)
                        {
                            object v = GetMember(o, "WallContusionAbsorption");
                            if (v is Vector3) { _wca = (Vector3)v; _wcaOk = true; }
                        }
                    }
                }
                catch (Exception) { }
            }
            if (!_wcaOk) return 1f;
            return Mathf.Clamp01(Mathf.InverseLerp(_wca.x, _wca.y, dist));
        }

        internal static bool ContusionVals(object srcObj, out float x, out float y, out float z)
        {
            x = 0f; y = 0f; z = 0f;
            object v = GetMember(srcObj, "Contusion");
            if (v is Vector3)
            {
                Vector3 c = (Vector3)v;
                x = c.x; y = c.y; z = c.z;
                return y > 0.1f;
            }
            return false;
        }
        internal static float ContusionY(object o)
        {
            if (o == null) return 0f;
            object cont = PropOf(o, "Contusion");
            if (cont == null) cont = GetMember(o, "Contusion");
            if (cont == null)
            {
                if (_contLogs < 8)
                {
                    _contLogs++;
                    LogInfo("[弹道学][CONT] " + o.GetType().Name + " 无 Contusion 成员；含 Contusion 的成员："
                        + string.Join(",", ContusionMembers(o)));
                }
                return 0f;
            }
            object y = GetMember(cont, "y");
            if (_contLogs < 8)
            {
                _contLogs++;
                LogInfo("[弹道学][CONT] " + o.GetType().Name + " Contusion=" + cont.GetType().Name
                    + " y=" + (y == null ? "null" : y.ToString()));
            }
            return y == null ? 0f : Num(y, 0f);
        }

        internal static string[] ContusionMembers(object o)
        {
            List<string> outNames = new List<string>();
            try
            {
                foreach (MemberInfo mi in o.GetType().GetMembers(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (mi.Name.IndexOf("Contusion", StringComparison.OrdinalIgnoreCase) >= 0)
                        outNames.Add(mi.MemberType + ":" + mi.Name);
                }
            }
            catch (Exception) { }
            return outNames.ToArray();
        }

        // 与弹道线共用同一个"线条寿命"配置
        internal static float BlastLifeSec()
        {
            return Lifetime();
        }

        internal static float BlastExpiryTime()
        {
            float life = BlastLifeSec();
            return life > 0f ? Time.time + life : float.PositiveInfinity;
        }

        internal static void SetLastLineLifetime()
        {
            if (Spawned.Count == 0) return;
            Expiry[Spawned.Count - 1] = BlastExpiryTime();
        }

        internal static float StunMul()
        {
            return 2f;   // Contusion 取不到时的兜底倍数
        }

        internal static void DrawBlastAt(Vector3 center, float maxR, float minR, string src, object srcObj = null)
        {
            try
            {
                if (_predValid)
                {
                    Vector3 de = center - _lastPredEnd;
                    LogInfo("[弹道学][THRX] blast=" + center.ToString("F2") + " predEnd=" + _lastPredEnd.ToString("F2")
                        + " d=" + de.magnitude.ToString("F2") + " (" + de.ToString("F2") + ")");
                    _predValid = false;
                }
            }
            catch (Exception) { }
            if (maxR < MiniBlastRadius)
            {
                if (ImplContext.DebugLog.Value)
                    LogInfo("[弹道学] blast skipped: " + src + " maxR=" + maxR.ToString("F2"));
                return;
            }
            if (BlastRecentlyDrawn(center)) return;

            Color baseCol = ParseHex(ImplContext.BlastColor.Value, new Color(1f, 0.3f, 0.1f), 1f);
            float alpha = ImplContext.BlastAlpha.Value;

            // 小半径（破片级）不做几何裁剪：贴在墙上时体积会被削没，改成理想球才看得见
            // 所有半径都走贴合几何的网格（小半径也能看出贴墙/贴地的形状）
            bool clip = ImplContext.GeomOn == null || ImplContext.GeomOn.Value;
            float dStr, dPen;
            BlastParamsOf(srcObj, out dStr, out dPen);            int dens = ImplContext.GeomSegments == null ? 20 : ImplContext.GeomSegments.Value;

            if (clip)
            {
                bool bands2 = ImplContext.BlastBandsOn == null || ImplContext.BlastBandsOn.Value;
                if (bands2 && dStr > 0f && dPen > 0f) SpawnBlastShells(center, maxR, new Color(baseCol.r, baseCol.g, baseCol.b, alpha), dens, dStr, dPen, minR);
                else SpawnBlastVolume(center, maxR, new Color(baseCol.r, baseCol.g, baseCol.b, alpha), 0, 0f, 0f, minR, maxR);
            }
            else
            {
                SpawnBlastSphere(center, maxR, new Color(baseCol.r, baseCol.g, baseCol.b, Mathf.Max(alpha, 0.45f)));
            }

            if (ImplContext.InnerOn != null && ImplContext.InnerOn.Value && minR > 0.1f)
                SpawnInner(center, minR, dStr, dPen, maxR, dens);

            if (ImplContext.StunOn != null && ImplContext.StunOn.Value)
            {
                SpawnStunRing(center, StunRadius(srcObj, maxR), srcObj);
                SetLastLineLifetime();
            }

            string where = "";
            try
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    Vector3 cp = cam.transform.position;
                    Vector3 dir = cam.transform.forward;
                    float dist = Vector3.Distance(cp, center);
                    float dot = Vector3.Dot((center - cp).normalized, dir);
                    where = " cam=(" + cp.ToString("F1") + ") dist=" + dist.ToString("F1")
                        + "m dot=" + dot.ToString("F2");
                }
            }
            catch (Exception) { }
            LogInfo("[弹道学] blast " + src + " at " + center + " maxR=" + maxR.ToString("F2")
                + " minR=" + minR.ToString("F2")
                + " stunR=" + StunRadius(srcObj, maxR).ToString("F2") + where);
        }

        internal static void DrawBlastFromGrenade(Vector3 center, object grenadeObj)
        {
            object item = grenadeObj == null ? null : GetMember(grenadeObj, "WeaponSource");
            object tpl = item == null ? null : GetMember(item, "Template");
            if (tpl == null) { if (ImplContext.DebugLog.Value) LogInfo("[弹道学] blast skipped: grenade template null"); return; }

            float maxR = Num(GetMember(tpl, "MaxExplosionDistance"), 0f);
            float minR = Num(GetMember(tpl, "MinExplosionDistance"), 0f);
            float strength = Num(GetMember(tpl, "Strength", "ExplosionStrength"), 0f);
            float frags = Num(GetMember(tpl, "FragmentsCount"), 0f);
            if (maxR < MiniBlastRadius)
            {
                if (ImplContext.DebugLog.Value)
                    LogInfo("[弹道学] blast skipped: grenade maxR=" + maxR.ToString("F2"));
                return;
            }
            if (BlastSeen(center.GetHashCode())) return;

            Color baseCol = ParseHex(ImplContext.BlastColor.Value, new Color(1f, 0.3f, 0.1f), 1f);
            float alpha = ImplContext.BlastAlpha.Value;
            // 基线：理想球（一直是可见的）
            SpawnBlastSphere(center, maxR, new Color(baseCol.r, baseCol.g, baseCol.b, alpha));
            // 可选：贴合几何的体积
            if (ImplContext.GeomOn != null && ImplContext.GeomOn.Value)
            {
                SpawnBlastVolume(center, maxR, new Color(baseCol.r, baseCol.g, baseCol.b, alpha));
            }
            if (ImplContext.InnerOn != null && ImplContext.InnerOn.Value && minR > 0.1f)
                SpawnInner(center, minR);
            if (ImplContext.StunOn != null && ImplContext.StunOn.Value)
            {
                SpawnStunRing(center, StunRadius(tpl, maxR), tpl);
                SetLastLineLifetime();
            }

            LogInfo("[弹道学] blast objects: blasts=" + Blasts.Count + " spawned=" + Spawned.Count
                + " trails=" + Trails.Count);
            LogInfo("[弹道学] blast grenade at " + center + " maxR=" + maxR.ToString("F2")
                + " minR=" + minR.ToString("F2") + " strength=" + strength.ToString("F0")
                + " frags=" + frags.ToString("F0"));
        }

        internal static void DrawBlastFromItem(Vector3 center, object item)
        {
            if (item == null) return;
            float maxR = Num(PropOf(item, "MaxExplosionDistance"), 0f);
            float minR = Num(PropOf(item, "MinExplosionDistance"), 0f);
            float strength = Num(PropOf(item, "GetStrength"), 0f);
            float frags = Num(PropOf(item, "FragmentsCount"), 0f);

            if (maxR <= 0.1f)
            {
                if (ImplContext.DebugLog.Value)
                    LogInfo("[弹道学] blast skipped: no MaxExplosionDistance on " + item.GetType().Name);
                return;
            }
            // 破片命中也会走 Explosion 入口，携带的是 1 米级的"伪爆炸"参数，不属于爆炸范围
            if (maxR < MiniBlastRadius)
            {
                if (ImplContext.DebugLog.Value)
                    LogInfo("[弹道学] blast skipped: fragment impact (maxR=" + maxR.ToString("F2") + ")");
                return;
            }
            if (BlastSeen(center.GetHashCode())) return;

            Color baseCol = ParseHex(ImplContext.BlastColor.Value, new Color(1f, 0.3f, 0.1f), 1f);
            float alpha = ImplContext.BlastAlpha.Value;

            SpawnBlastSphere(center, maxR, new Color(baseCol.r, baseCol.g, baseCol.b, alpha));
            if (ImplContext.GeomOn != null && ImplContext.GeomOn.Value)
            {
                SpawnBlastVolume(center, maxR, new Color(baseCol.r, baseCol.g, baseCol.b, alpha));
            }
            if (ImplContext.InnerOn != null && ImplContext.InnerOn.Value && minR > 0.1f)
                SpawnInner(center, minR);
            if (ImplContext.StunOn != null && ImplContext.StunOn.Value)
            {
                SpawnStunRing(center, StunRadius(item, maxR), item);
                SetLastLineLifetime();
            }

            LogInfo("[弹道学] blast " + item.GetType().Name + " at " + center
                + " maxR=" + maxR.ToString("F2") + " minR=" + minR.ToString("F2")
                + " strength=" + strength.ToString("F0") + " frags=" + frags.ToString("F0"));
        }

        internal static void DrawBlast(Vector3 center, object grenadeObj)
        {
            object item = grenadeObj == null ? null : GetMember(grenadeObj, "WeaponSource");
            object tpl = item == null ? null : GetMember(item, "Template", "TemplateId__");
            if (tpl == null)
            {
                if (ImplContext.DebugLog.Value) LogInfo("[弹道学] blast skipped: grenade template null");
                return;
            }
            DrawBlastAt(center,
                Num(GetMember(tpl, "MaxExplosionDistance"), 0f),
                Num(GetMember(tpl, "MinExplosionDistance"), 0f),
                "grenade");
        }


        private static string PenDmg(object shot)
        {
            object pen = GetMember(shot, "PenetrationPower", "CurrentPenetrationPower");
            object dmg = GetMember(shot, "Damage", "CurrentDamage");
            return (pen == null ? "?" : pen.ToString()) + "/" + (dmg == null ? "?" : dmg.ToString());
        }

        public static void PenDegradePre(object __instance)
        {
                    if (_degBefore.Count > 512) { _degBefore.Clear(); _degPenBefore.Clear(); _degDmgBefore.Clear(); }
            try { if (__instance != null) { _degBefore[__instance] = PenDmg(__instance); _degPenBefore[__instance] = Num(GetMember(__instance, "PenetrationPower", "CurrentPenetrationPower"), -1f); _degDmgBefore[__instance] = Num(GetMember(__instance, "Damage", "CurrentDamage"), -1f); } }
            catch (Exception) { }
        }

        public static void PenDegradePost(object __instance)
        {
            try
            {
                if (__instance == null) return;
                string before;
                if (!_degBefore.TryGetValue(__instance, out before)) before = "?";
                _degBefore.Remove(__instance);
                string after = PenDmg(__instance);
                // 把这一处穿透的损失回填到弹链记录里
                float pb, db;
                _degPenBefore.TryGetValue(__instance, out pb); _degPenBefore.Remove(__instance);
                _degDmgBefore.TryGetValue(__instance, out db); _degDmgBefore.Remove(__instance);
                float pa = Num(GetMember(__instance, "PenetrationPower", "CurrentPenetrationPower"), -1f);
                float da = Num(GetMember(__instance, "Damage", "CurrentDamage"), -1f);
                HitRec lastRec;
                if (LastRecByShot.TryGetValue(__instance, out lastRec) && lastRec != null && !lastRec.Degraded)
                {
                    lastRec.PenBefore = pb > 0f ? pb : lastRec.PenBefore;
                    lastRec.PenAfter = pa > 0f ? pa : lastRec.PenBefore;
                    lastRec.DmgBefore = db > 0f ? db : lastRec.DmgBefore;
                    lastRec.DmgAfter = da > 0f ? da : lastRec.DmgBefore;
                    lastRec.Degraded = true;
                }
                DegradeCount++;
                LastDegrade = before + "  →  " + after;
                if (DegradeCount <= 20) LogInfo("[弹道学] penetrated: pen/dmg " + LastDegrade);
            }
            catch (Exception e) { SafeLog("PenDegrade: " + e.Message); }
        }

        public static void PenetrationPostfix(object[] __args, ref object __result)
        {
            try
            {
                if (!ImplContext.ModOn.Value) return;
                if (__result is int) LastPen = (int)__result;
                else if (__result is float) LastPen = (int)(float)__result;
            }
            catch (Exception) { }
        }

        public static void ApplyHitPrefix(object __instance, object[] __args)
        {
            try
            {
                if (!ImplContext.ModOn.Value) return;
                Hits++;
                object mat = GetMember(__instance, "TypeOfMaterial", "_typeOfMaterial");
                if (mat != null) LastMaterial = mat.ToString();
                LastHitPen = -1f; LastHitDmg = -1f;
                if (__args != null && __args.Length > 0 && __args[0] != null)
                {
                    object pp = GetMember(__args[0], "PenetrationPower");
                    object dd = GetMember(__args[0], "Damage");
                    if (pp != null) { try { LastHitPen = Convert.ToSingle(pp); } catch (Exception) { } }
                    if (dd != null) { try { LastHitDmg = Convert.ToSingle(dd); } catch (Exception) { } }
                }
                bool grenadeHit = __args != null && __args.Length > 0 && IsGrenadeDamage(__args[0]);
                if (__args != null && __args.Length > 0 && __args[0] != null)
                {
                    Vector3 hp;
                    if (TryVector(__args[0], out hp, "HitPoint", "hitPoint", "_hitPoint", "Point", "point") && hp.sqrMagnitude > 0.01f)
                    {
                        if (grenadeHit)
                        {
                            MarkNearestGrenade(hp);
                        }
                        OnHit(hp);                        // 用命中点就近匹配当前活跃轨迹，把这次命中的真实数值挂上去
                        try
                        {
                            int bestKey = -1;
                            float bestD = 3f;
                            foreach (KeyValuePair<int, LineRenderer> kv0 in Trails)
                            {
                                if (kv0.Value == null) continue;
                                if (!TrailPts.ContainsKey(kv0.Key)) continue;
                                float seen0;
                                if (TrailSeen.TryGetValue(kv0.Key, out seen0) && Time.time - seen0 > 2f) continue;
                                int pi0 = Mathf.Max(0, TrailPts[kv0.Key] - 1);
                                if (pi0 >= kv0.Value.positionCount) continue;
                                float dd0 = Vector3.Distance(kv0.Value.GetPosition(pi0), hp);
                                if (dd0 < bestD) { bestD = dd0; bestKey = kv0.Key; }
                            }
                            if (bestKey >= 0 && (LastHitPen > 0f || LastHitDmg > 0f))
                            {
                                int atp0 = TrailPts.ContainsKey(bestKey) ? TrailPts[bestKey] : 1;
                                if (!RealPen.ContainsKey(bestKey)) { RealPen[bestKey] = new List<float>(); RealDmg[bestKey] = new List<float>(); RealAt[bestKey] = new List<int>(); }
                                RealPen[bestKey].Add(LastHitPen);
                                RealDmg[bestKey].Add(LastHitDmg);
                                RealAt[bestKey].Add(atp0);
                                if (ImplContext.DebugLog != null && ImplContext.DebugLog.Value)
                                    LogInfo("[弹道学][REAL] key=" + bestKey + " idx=" + atp0 + " pen=" + LastHitPen.ToString("F1") + " dmg=" + LastHitDmg.ToString("F1") + " d=" + bestD.ToString("F2"));
                            }
                        }
                        catch (Exception) { }

                    }
                }

                if (ImplContext.DebugLog != null && ImplContext.DebugLog.Value)
                {
                    string sd = "";
                    try
                    {
                        object sh0 = (__args != null && __args.Length > 1) ? __args[1] : null;
                        if (sh0 != null)
                        {
                            System.Reflection.PropertyInfo[] pis = sh0.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                            for (int q = 0; q < pis.Length; q++)
                            {
                                string nm0 = pis[q].Name;
                                if (nm0.IndexOf("Penetr", StringComparison.OrdinalIgnoreCase) < 0
                                    && nm0.IndexOf("Damage", StringComparison.OrdinalIgnoreCase) < 0
                                    && nm0.IndexOf("Drop", StringComparison.OrdinalIgnoreCase) < 0) continue;
                                object vv0 = null;
                                try { vv0 = pis[q].GetValue(sh0, null); } catch (Exception) { }
                                sd += nm0 + "=" + (vv0 == null ? "?" : vv0.ToString()) + " ";
                            }
                        }
                    }
                    catch (Exception) { }
                    LogInfo("[弹道学][HITVAL] mat=" + (GetMember(__instance, "TypeOfMaterial") ?? "?") + " pen=" + LastHitPen.ToString("F1") + " dmg=" + LastHitDmg.ToString("F1") + " | shot: " + sd);
                }
                object matObj = GetMember(__instance, "TypeOfMaterial", "_typeOfMaterial");
                LastHitMat = matObj == null ? "-" : matObj.ToString();
                object lvl0 = GetMember(__instance, "PenetrationLevel");
                LastHitLevel = lvl0 == null ? "-" : lvl0.ToString();
                LastHitLevelNum = lvl0 == null ? -1f : (float)Convert.ToSingle(lvl0);

                object lvl = GetMember(__instance, "PenetrationLevel");
                object chc = GetMember(__instance, "PenetrationChance");
                object htp = GetMember(__instance, "HitType");
                LastThresholdLevel = (lvl == null ? "?" : lvl.ToString());
                LastThresholdChance = (chc == null ? "" : chc.ToString());
                LastThresholdHitType = (htp == null ? "" : htp.ToString());


            }
            catch (Exception e) { SafeLog("ApplyHit: " + e.Message); }
        }


        // OnGUI 每帧会调用 Layout/Repaint 多次，按帧缓存避免重复拼字符串
        private static float _panelAt;
        private static string _panelCache = "";
        internal static string OverlayText()
        {
            if (!string.IsNullOrEmpty(_panelCache) && Time.time - _panelAt < 0.25f) return _panelCache;
            _panelAt = Time.time;

            string up = "?";
            try { up = ((int)(Time.realtimeSinceStartup - ImplContext.InstalledAt)) + "s"; } catch (Exception) { }

            string t = Lang.T("ui.panelTitle") + " v" + ImplContext.Version + " [" + BuildInfo.Tag + "]  "
                + Lang.T("ui.uptime") + "=" + up
                + "  " + Lang.T("ui.fired") + "=" + _nextVolley
                + " " + Lang.T("ui.shots") + "=" + Shots + (SubShots > 0 ? " (+" + Lang.T("ui.child") + SubShots + ")" : "")
                + " " + Lang.T("ui.trails") + "=" + Traced
                + " " + Lang.T("ui.hits") + "=" + Hits;

            t += "\n" + Lang.T("ui.round") + ": " + LastAmmo
                + "  - " + Lang.T("ui.threshold") + " " + (LastPen >= 0 ? LastPen.ToString() : "?");

            // 全部现场拼装：切换语言后面板立即变成新语言（不在命中时缓存文本）
            string thr = LastThresholdLevel;
            if (!string.IsNullOrEmpty(LastThresholdChance)) thr += "  (" + Lang.T("ui.chance") + " " + LastThresholdChance + ")";
            if (!string.IsNullOrEmpty(LastThresholdHitType)) thr += "  [" + LastThresholdHitType + "]";
            t += "\n" + Lang.T("ui.object") + ": " + MatName(LastMaterial) + "  - " + Lang.T("ui.threshold") + " " + thr;

            t += "\n" + Lang.T("ui.afterPenetration") + ": "
                + (DegradeCount > 0 ? LastDegrade + "  (x" + DegradeCount + ")" : Lang.T("ui.none"));

            t += "\n" + Lang.T("ui.lastHit") + ": " + MatName(LastHitMat)
                + "  " + Lang.T("ui.level") + " " + LastHitLevel
                + "  " + Lang.T("ui.pen") + " " + LastHitPen.ToString("F1")
                + "  " + Lang.T("ui.dmg") + " " + LastHitDmg.ToString("F1");

            if (!string.IsNullOrEmpty(LastFalloffId))
                t += "\n" + Lang.T("ui.falloff") + ": id=" + LastFalloffId
                    + "  " + Lang.T("ui.speed") + " " + LastFalloffSpeed.ToString("F1") + "%"
                    + "  x" + LastFalloffFactor.ToString("F3");

            // 本次击发：单发看这一发的每个穿透点；霰弹等多弹丸则逐颗列出
            List<int> volley;
            List<HitRec> hits;
            if (LastVolley > 0 && VolleyShots.TryGetValue(LastVolley, out volley) && volley != null && volley.Count > 1)
            {
                int totalHits = 0;
                for (int v = 0; v < volley.Count; v++)
                {
                    List<HitRec> rr;
                    if (ChainHits.TryGetValue(ChainRoot(volley[v]), out rr) && rr != null) totalHits += rr.Count;
                }
                t += "\n" + Lang.T("ui.volley") + " (" + volley.Count + " " + Lang.T("ui.pellets") + ", " + totalHits + " " + Lang.T("ui.hits") + "):";
                int shown = volley.Count;
                for (int v = 0; v < shown; v++)
                {
                    string line = "\n  #" + (v + 1) + "  ";
                    List<HitRec> recs2;
                    if (ChainHits.TryGetValue(ChainRoot(volley[v]), out recs2) && recs2 != null && recs2.Count > 0)
                    {
                        for (int q = 0; q < recs2.Count; q++)
                        {
                            if (q > 0) line += " | ";
                            line += recs2[q].Short();
                        }
                    }
                    else line += Lang.T("ui.miss");
                    t += line;
                }
            }
            else if (LastChainRoot > 0 && ChainHits.TryGetValue(LastChainRoot, out hits) && hits != null && hits.Count > 0)
            {
                t += "\n" + Lang.T("ui.hitList") + " (" + hits.Count + "):";
                for (int i2 = 0; i2 < hits.Count; i2++) t += "\n  " + hits[i2].Render(i2 + 1);
            }
            _panelCache = t;
            return t;
        }
    }
    }
