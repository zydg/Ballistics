using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using Dsh.BallisticsDebug.Shared;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Dsh.BallisticsDebug.Loader
{
    public static class Shell
    {
        public static Func<string> Text;
        public static Action Tick;
    }

    public class ShellHost : MonoBehaviour
    {
        private static void Log(string s)
        {
            try { if (Loader.Log != null) Loader.Log.LogInfo("[弹道学][UI] " + s); } catch (Exception) { }
        }

        private void Awake() { Log("ShellHost awake, id=" + GetInstanceID()); }
        private void OnEnable() { Log("ShellHost enabled"); }
        private void OnDisable() { Log("ShellHost disabled"); }
        private void OnDestroy() { Log("ShellHost destroyed, id=" + GetInstanceID()); }

        private void Update()
        {
            Action t = Shell.Tick;
            if (t == null) return;
            try { t(); } catch (Exception) { }
        }

        private static bool _guiFirst;
        private static int _guiErrors;

        private void OnGUI()
        {
            if (Loader.ShowOverlay == null || Shell.Text == null) return;
            if (!_guiFirst)
            {
                _guiFirst = true;
                Log("OnGUI first call, text=" + (Shell.Text != null) + " show=" + Loader.ShowOverlay.Value
                    + " screen=" + Screen.width + "x" + Screen.height);
            }
            try
            {
                if (!Loader.ShowOverlay.Value) return;
                if (Shell.Text == null) return;
                string text = Shell.Text();
                if (string.IsNullOrEmpty(text)) return;

                int fs = Loader.FontSize.Value;
                Color col;
                if (!ColorUtility.TryParseHtmlString(Loader.TextColor.Value, out col)) col = Color.white;
                float x = Loader.PosX.Value;
                float y = Loader.PosY.Value;

                GUIStyle style = new GUIStyle(GUI.skin.label);
                style.fontSize = fs;
                style.wordWrap = false;
                style.normal.textColor = col;

                Vector2 content = style.CalcSize(new GUIContent(text));
                float w = Mathf.Min(content.x + 16f, Screen.width - x - 4f);
                float h = content.y + 8f;

                if (Loader.Background.Value) GUI.Box(new Rect(x, y, w, h), "");
                GUI.Label(new Rect(x + 8f, y + 4f, w - 16f, h - 8f), text, style);
            }
            catch (Exception e)
            {
                if (_guiErrors++ < 5) Log("OnGUI threw: " + e.GetType().Name + ": " + e.Message);
            }
        }


    }

    [BepInPlugin(Guid, "zydg666-Ballistics", LoaderBuildInfo.Version)]
    public class Loader : BaseUnityPlugin
    {
        public const string Guid = "com.zydg666.ballistics";
        internal static Loader Instance;
        internal static ManualLogSource Log;
#if !STATIC_BUILD
        private ConfigEntry<KeyCode> _reloadKey;
#endif
        internal static ConfigEntry<bool> ShowOverlay;
        internal static ConfigEntry<int> FontSize;
        internal static ConfigEntry<int> PosX;
        internal static ConfigEntry<int> PosY;
        internal static ConfigEntry<bool> Background;
        internal static ConfigEntry<string> TextColor;
        internal static ConfigEntry<string> Language;
        private Harmony _harmony;
        private object _impl;
#if !STATIC_BUILD
        private string _implPath;
#endif
        private int _reloads;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            _harmony = new Harmony(Guid);
#if !STATIC_BUILD
            _implPath = Path.Combine(Paths.PluginPath, Path.Combine("Ballistics", Path.Combine("Impl", "BallisticsImpl.bytes")));
#endif

            Language = Config.Bind("General", "Language", "Auto", new ConfigDescription(Lang.T("cfg.language"), new AcceptableValueList<string>("Auto", "English", "简体中文", "Русский")));
            Lang.Configure(Language.Value);
            Lang.Init(Path.Combine(Paths.PluginPath, Path.Combine("Ballistics", "Lang")));

            // 运行中改 General → Language 立刻生效：面板/提示文本即时切换；
            // cfg 里的条目描述是绑定那一刻生成的字符串，需要重启游戏才会变成新语言。
            Language.SettingChanged += (s, e) =>
            {
                Lang.Configure(Language.Value);
                RelocalizeConfig();
                Logger.LogInfo("[弹道学] 语言切换 -> " + Lang.Code() + "（cfg 描述需重启后更新）");
            };

            // ---------- 调试 Debug ----------

            ShellHost host = gameObject.AddComponent<ShellHost>();
            Logger.LogInfo("[弹道学][UI] shell host created on plugin go=" + gameObject.name + " id=" + host.GetInstanceID()
                + " active=" + gameObject.activeInHierarchy + " scene=" + gameObject.scene.name);
#if STATIC_BUILD
            Logger.LogInfo("[弹道学] 载入完成 | impl = 内置（静态构建） | 语言 = " + Lang.Code());
#else
            Logger.LogInfo("[弹道学] 载入完成 | impl = " + _implPath + " | 重载热键 = " + _reloadKey.Value + " | 语言 = " + Lang.Code());
#endif
            Reload();

            // 面板配置由 Impl 通过反射赋值；万一没赋上（版本/程序集差异），这里补绑一次
            if (ShowOverlay == null)
            {
                ShowOverlay = Config.Bind("Debug", "ShowOverlayPanel", false, new ConfigDescription(Lang.T("cfg.debug.showOverlayPanel")));
                FontSize = Config.Bind("Debug", "PanelFontSize", 16, new ConfigDescription(Lang.T("cfg.debug.panelFontSize"), new AcceptableValueRange<int>(8, 48)));
                PosX = Config.Bind("Debug", "PanelX", 20, new ConfigDescription(Lang.T("cfg.debug.panelX"), new AcceptableValueRange<int>(0, 3840)));
                PosY = Config.Bind("Debug", "PanelY", 10, new ConfigDescription(Lang.T("cfg.debug.panelY"), new AcceptableValueRange<int>(0, 2160)));
                Background = Config.Bind("Debug", "PanelBackground", true, Lang.T("cfg.debug.panelBackground"));
                TextColor = Config.Bind("Debug", "PanelTextColor", "#FFFFFF", Lang.T("cfg.debug.panelTextColor"));
            }
        }

        private bool _staticMode;
#if !STATIC_BUILD
        private DateTime _implStamp;
#endif
#if !STATIC_BUILD
        private long _implLen;
#endif

        private void Update()
        {
#if !STATIC_BUILD
            if (_reloadKey != null && Input.GetKeyDown(_reloadKey.Value))
            {
                try { Config.Reload(); } catch (Exception e) { Logger.LogWarning("config reload: " + e.Message); }
                Reload();
                return;
            }
#endif
            // 自动重载已关闭：只在按热键（默认 F10）时重载，避免运行中意外换掉实现
        }

        private void Reload()
        {
            Unload();
            _reloads++;
            try
            {
#if STATIC_BUILD
                // 发布版：实现与壳编在同一程序集；DLL 内不含任何运行时程序集加载代码。
                Assembly asm = null;
                Type t = GetType().Assembly.GetType("Dsh.BallisticsDebug.Impl.ImplPlugin");
                _staticMode = true;
                if (t == null) { Logger.LogError("ImplPlugin type missing (static build)"); return; }
#else
                // 调试版：壳 + Impl\*.bytes，从磁盘加载字节流以支持 F10 热重载。
                Assembly asm = null;
                Type t = GetType().Assembly.GetType("Dsh.BallisticsDebug.Impl.ImplPlugin");
                if (t != null)
                {
                    _staticMode = true;
                }
                else
                {
                    if (!File.Exists(_implPath)) { Logger.LogError("impl not found: " + _implPath); return; }
                    byte[] bytes = File.ReadAllBytes(_implPath);
                    FileInfo fi = new FileInfo(_implPath);
                    _implStamp = fi.LastWriteTimeUtc;
                    _implLen = bytes.Length;
                    asm = Assembly.Load(bytes);
                    t = asm.GetType("Dsh.BallisticsDebug.Impl.ImplPlugin");
                    if (t == null) { Logger.LogError("ImplPlugin type missing"); return; }
                }
#endif
                MethodInfo install = null;
                foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    if (m.Name == "Install") { install = m; break; }
                if (install == null) { Logger.LogError("Install missing"); return; }

                Logger.LogInfo("[弹道学] 实现模式: " + (_staticMode ? "静态单 DLL" : "bytes 热重载"));
                ParameterInfo[] ps = install.GetParameters();
                object[] args = new object[ps.Length];
                for (int i = 0; i < ps.Length; i++)
                {
                    string n = (ps[i].Name ?? "").ToLowerInvariant();
                    if (n.Contains("seq") || n.Contains("reload")) args[i] = _reloads;
                    else if (n.Contains("harmony")) args[i] = _harmony;
                    else if (n.Contains("log")) args[i] = Logger;
                    else args[i] = this;
                }
                _impl = install.Invoke(null, args);
                Logger.LogInfo("[弹道学] 实现已载入 #" + _reloads + ": " + (asm != null ? asm.FullName : "同程序集（静态构建）") + " (" + ps.Length + " 个参数)");
            }
            catch (Exception e)
            {
                Exception inner = e;
                while (inner.InnerException != null) inner = inner.InnerException;
                Logger.LogError("reload failed: " + inner.GetType().Name + ": " + inner.Message);
            }
        }

        private void Unload()
        {
            Shell.Text = null;
            Shell.Tick = null;
            if (_impl != null)
            {
                try
                {
                    MethodInfo un = _impl.GetType().GetMethod("Uninstall", BindingFlags.Public | BindingFlags.Instance);
                    if (un != null) un.Invoke(_impl, null);
                }
                catch (Exception e) { Logger.LogWarning("impl uninstall failed: " + e.Message); }
                _impl = null;
            }
            try { _harmony.UnpatchSelf(); } catch (Exception) { }
            try
            {
                GameObject[] all = UnityEngine.Object.FindObjectsOfType<GameObject>();
                int killed = 0;
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] == null || all[i].name == null) continue;
                    if (all[i] == gameObject) continue;
                    if (all[i].name == "dsh_traj" || all[i].name.StartsWith("dsh_traj")) { UnityEngine.Object.Destroy(all[i]); killed++; }
                }
                if (killed > 0) Logger.LogInfo("cleaned " + killed + " trajectory line(s)");
            }
            catch (Exception) { }
        }

        // 语言切换后重刷 cfg 里每条描述：BepInEx 的 ConfigEntryBase.Description 是绑定那一刻
        // 生成的只读字符串，只能改它的私有 backing field，再让 ConfigurationManager 重建列表。
        internal static void RelocalizeConfig()
        {
            try
            {
                FieldInfo fi = typeof(ConfigEntryBase).GetField("<Description>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
                if (fi == null) return;
                foreach (ConfigDefinition def in Instance.Config.Keys)
                {
                    ConfigEntryBase e = Instance.Config[def];
                    if (e == null) continue;
                    string id = (def.Section == "General" && def.Key == "Language")
                        ? "cfg.language"
                        : "cfg." + def.Section.ToLowerInvariant() + "." + char.ToLowerInvariant(def.Key[0]) + def.Key.Substring(1);
                    string text = Lang.T(id);
                    if (string.Equals(text, id, StringComparison.Ordinal)) continue;
                    ConfigDescription old = fi.GetValue(e) as ConfigDescription;
                    fi.SetValue(e, new ConfigDescription(text, old == null ? null : old.AcceptableValues, old == null ? null : old.Tags));
                }
                RefreshConfigManager();
            }
            catch (Exception e) { Log.LogWarning("[弹道学] relocalize failed: " + e.Message); }
        }

        // ConfigurationManager 已生成的列表不会自动重读描述，能叫到就让它重建一次
        private static void RefreshConfigManager()
        {
            try
            {
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type t = a.GetType("ConfigurationManager.ConfigurationManager") ?? a.GetType("ConfigurationManager.ConfigurationManagerPlugin");
                    if (t == null) continue;
                    MethodInfo mi = t.GetMethod("BuildSettingList", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
                    if (mi == null) continue;
                    object inst = null;
                    if (!mi.IsStatic)
                    {
                        UnityEngine.Object[] objs = Resources.FindObjectsOfTypeAll(t);
                        if (objs != null && objs.Length > 0) inst = objs[0];
                    }
                    if (mi.IsStatic || inst != null) mi.Invoke(inst, null);
                    return;
                }
            }
            catch (Exception) { }
        }

        private void OnDestroy() { Unload(); Instance = null; }
    }
}