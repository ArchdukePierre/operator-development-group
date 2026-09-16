// ODG Operations - VKS
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Text;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(VKS.VKSMod), "VKS", "0.1.0", "ArchdukePierre")]
[assembly: MelonGame(null, null)]

namespace VKS
{

    public class VKSMod : MelonMod
    {
        internal static VKSMod Instance;

        internal static string GameRoot
        {
            get { try { return MelonLoader.Utils.MelonEnvironment.GameRootDirectory; } catch { return AppContext.BaseDirectory; } }
        }
        static string LogPath => Path.Combine(GameRoot, "Mods", "VKS.log");

        int _throttle;
        bool _done;
        bool _reported;
        float _logFlushAt;
        GameObject _template;

        public override void OnInitializeMelon()
        {
            Instance = this;
            try { File.AppendAllText(LogPath, "\n\n========== VKS v0.1  " + DateTime.Now + " ==========\n"); } catch { }
            Log("loaded. waiting for the armoury.");
        }

        public override void OnDeinitializeMelon()
        {
            try { FlushLog(); } catch { }
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {

            Prop.Reset();
            _template = null;
            _done = false;
            _throttle = 0;
        }

        public override void OnUpdate()
        {
            try
            {
                if (Prop.Built) Prop.Tick();

                if (Time.unscaledTime >= _logFlushAt) { _logFlushAt = Time.unscaledTime + 0.5f; FlushLog(); }

                if (_done || ++_throttle < 30) return;
                _throttle = 0;

                if (!ArmouryLoaded())
                {
                    if (Time.timeSinceLevelLoad > 120f) { _done = true; Log("no armoury in this scene, standing down."); }
                    return;
                }

                if (Time.timeSinceLevelLoad > 180f) { _done = true; Log("gave up: setup did not complete in 180s."); return; }

                Il2Cpp.LoadoutManager lm = null;
                try { lm = Il2Cpp.LoadoutManager.singleton; } catch { }
                if (lm == null) return;

                if (_template == null)
                {
                    _template = FindTemplate(lm);
                    if (_template == null) return;
                    if (!_reported) { _reported = true; Report(lm, _template); }
                }

                if (Prop.TryBuild(_template))
                {
                    _done = true;
                    Log("setup complete: the VKS is assembling beside the OPFOR rack and will be placed once its parts are in.");
                }
            }
            catch (Exception e) { Log("update err: " + e.Message); }
        }

        static GameObject FindTemplate(Il2Cpp.LoadoutManager lm)
        {
            try
            {
                var gw = lm.Game_Weapons;
                if (gw == null) return null;
                for (int i = 0; i < gw.Count; i++)
                {
                    var g = gw[i];
                    if (g != null && g.name == "VKS") { Log("template: Game_Weapons[" + i + "] '" + g.name + "'"); return g; }
                }
                Log("no 'VKS' entry in Game_Weapons (" + gw.Count + " entries)");
            }
            catch (Exception e) { Log("template lookup err: " + e.Message); }
            return null;
        }

        static void Report(Il2Cpp.LoadoutManager lm, GameObject template)
        {
            var sb = new StringBuilder();
            sb.Append("---- VKS report ----\n");

            try
            {
                var w3 = template.GetComponent<Il2Cpp.WeaponV3>();
                if (w3 != null)
                {
                    sb.Append("  displayName    : ").Append(w3.displayName).Append('\n');
                    sb.Append("  IsProduction   : ").Append(w3.IsProduction).Append("   (not changed by this mod)\n");
                    sb.Append("  weaponSlotType : ").Append(w3.weaponSlotType.ToString()).Append('\n');
                    sb.Append("  WeaponIsComplete: ").Append(w3.WeaponIsComplete).Append('\n');
                    try
                    {
                        var gs = w3.gunStats;
                        if (gs != null)
                            sb.Append("  gunStats       : fireMode=").Append(gs.fireMode.ToString())
                              .Append(" MOA=").Append(gs.MOA)
                              .Append(" weight=").Append(gs.Weight)
                              .Append(" suppressed=").Append(gs.isSuppressed)
                              .Append(" caliber=").Append(gs.weaponCaliber.ToString()).Append('\n');
                    }
                    catch { }
                    try { EmptyAnimStrings(w3, sb); } catch { }
                }
                else sb.Append("  NO WeaponV3 on the template\n");
            }
            catch (Exception e) { sb.Append("  weapon read failed: ").Append(e.Message).Append('\n'); }

            try
            {
                var prod = lm.Game_Weapons_Production;
                if (prod == null) sb.Append("  Game_Weapons_Production: null\n");
                else
                {
                    bool listed = false;
                    var all = new StringBuilder();
                    for (int i = 0; i < prod.Length; i++)
                    {
                        var s = prod[i];
                        if (s == null) continue;
                        if (all.Length > 0) all.Append(", ");
                        all.Append(s);
                        if (string.Equals(s, "VKS", StringComparison.OrdinalIgnoreCase)) listed = true;
                    }
                    sb.Append("  Game_Weapons_Production: ").Append(prod.Length)
                      .Append(" entries, VKS listed = ").Append(listed).Append('\n');
                    sb.Append("    [").Append(all.ToString()).Append("]\n");
                }
            }
            catch (Exception e) { sb.Append("  Game_Weapons_Production read failed: ").Append(e.Message).Append('\n'); }

            try
            {
                var na = template.GetComponent<Il2CppMirror.NetworkAnimator>();
                sb.Append("  Mirror.NetworkAnimator on root: ").Append(na != null ? "present" : "ABSENT").Append('\n');
            }
            catch (Exception e) { sb.Append("  NetworkAnimator check failed: ").Append(e.Message).Append('\n'); }

            try
            {
                sb.Append("  root components: ");
                var comps = template.GetComponents<Component>();
                for (int i = 0; i < comps.Length; i++)
                {
                    if (comps[i] == null) continue;
                    if (i > 0) sb.Append(", ");
                    try { sb.Append(comps[i].GetIl2CppType().FullName); } catch { sb.Append('?'); }
                }
                sb.Append('\n');
            }
            catch { }

            sb.Append("--------------------");
            Log(sb.ToString());
        }

        static void EmptyAnimStrings(Il2Cpp.WeaponV3 w3, StringBuilder sb)
        {
            var wa = w3.weaponAnimation;
            if (wa == null) { sb.Append("  weaponAnimation: null\n"); return; }

            var empties = new StringBuilder();
            var t = wa.GetIl2CppType();
            var fields = t.GetFields();
            for (int i = 0; i < fields.Length; i++)
            {
                try
                {
                    if (fields[i].FieldType == null || fields[i].FieldType.FullName != "System.String") continue;
                    var v = fields[i].GetValue(wa);
                    var s = v == null ? null : v.ToString();
                    if (!string.IsNullOrEmpty(s)) continue;
                    if (empties.Length > 0) empties.Append(", ");
                    empties.Append(fields[i].Name);
                }
                catch { }
            }
            sb.Append("  empty animation strings: ").Append(empties.Length == 0 ? "none" : empties.ToString()).Append('\n');
        }

        static bool ArmouryLoaded()
        {
            try
            {
                for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                {
                    var s = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                    if (!s.isLoaded) continue;
                    var n = s.name;
                    if (!string.IsNullOrEmpty(n) && n.IndexOf("Operation Room", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
            }
            catch { }
            return false;
        }

        static readonly StringBuilder _logBuf = new StringBuilder();

        internal static void Log(string s)
        {
            try
            {
                lock (_logBuf)
                {
                    _logBuf.Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append("  ").Append(s).Append('\n');
                    if (_logBuf.Length > 16384) FlushLocked();
                }
            }
            catch { }
            try { MelonLogger.Msg("[VKS] " + s); } catch { }
        }

        static void FlushLog() { try { lock (_logBuf) FlushLocked(); } catch { } }

        static void FlushLocked()
        {
            try
            {
                if (_logBuf.Length == 0) return;
                File.AppendAllText(LogPath, _logBuf.ToString());
                _logBuf.Length = 0;
            }
            catch { }
        }
    }
}
