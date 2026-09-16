// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MapLoader
{

    public static class LaptopTrace
    {
        public static bool Enabled;

        static MelonLogger.Instance _log;
        static bool _installed;
        static int _patched, _failed;
        static int _depth;

        static readonly Type[] Targets =
        {
            typeof(Il2Cpp.MissionLaptop),
            typeof(Il2Cpp.CerebusUiBase),
            typeof(Il2Cpp.CerebusOperation),
            typeof(Il2Cpp.CerebusTargetPackage),
            typeof(Il2Cpp.CerebusOpboard),
            typeof(Il2Cpp.OperationBoardUI),
            typeof(Il2Cpp.OperationSelectionUI),
        };

        static readonly HashSet<string> Skip = new HashSet<string>(StringComparer.Ordinal)
        {
            "Update", "LateUpdate", "FixedUpdate", "OnGUI", "OnEnable", "OnDisable",
            "OnDestroy", "OnUpdate", "ToString", "GetHashCode", "Equals", "Finalize",
            "get_enabled", "set_enabled", "get_name", "set_name",

            "Weaved", "SerializeSyncVars", "DeserializeSyncVars",
            "OnSerialize", "OnDeserialize", "ClearAllDirtyBits",
            "OnStartServer", "OnStopServer", "OnStartClient", "OnStopClient",
            "OnStartLocalPlayer", "OnStartAuthority", "OnStopAuthority",
        };

        static bool IsNetworkBehaviour(Type t)
        {
            try
            {
                for (var b = t; b != null; b = b.BaseType)
                    if (b.Name == "NetworkBehaviour") return true;
            }
            catch { }
            return false;
        }

        public static void Init(MelonLogger.Instance log) { _log = log; }

        static HarmonyLib.Harmony _harmony;

        public static void Install(HarmonyLib.Harmony unused, MelonLogger.Instance log)
        {
            _log = log;
            log.Msg("[trace] ready (not patched). Shift+F6 arms the laptop tracer, "
                  + "F6 again removes it.");
        }

        static void Patch()
        {
            if (_installed) return;
            _installed = true;
            _harmony = new HarmonyLib.Harmony("MapLoader.LaptopTrace");
            var log = _log;

            var prefix = new HarmonyMethod(typeof(LaptopTrace), nameof(Universal));

            foreach (var t in Targets)
            {
                if (t == null) continue;
                if (IsNetworkBehaviour(t))
                {
                    log?.Warning("[trace] skipping " + t.Name
                               + ": NetworkBehaviour, patching it can break hosting");
                    continue;
                }
                MethodInfo[] methods;
                try
                {
                    methods = t.GetMethods(BindingFlags.Instance | BindingFlags.Static
                                         | BindingFlags.Public | BindingFlags.NonPublic
                                         | BindingFlags.DeclaredOnly);
                }
                catch (Exception e)
                {
                    log.Warning("[trace] cannot enumerate " + t.Name + ": " + e.Message);
                    continue;
                }

                foreach (var m in methods)
                {
                    try
                    {
                        if (m.IsAbstract || m.IsGenericMethod) continue;
                        if (Skip.Contains(m.Name)) continue;

                        if (m.Name.StartsWith("InvokeUserCode_", StringComparison.Ordinal)) continue;
                        if (m.Name.StartsWith("UserCode_", StringComparison.Ordinal)) continue;
                        if (m.Name.StartsWith("CMD", StringComparison.OrdinalIgnoreCase)) continue;
                        if (m.Name.StartsWith("RPC", StringComparison.OrdinalIgnoreCase)) continue;
                        if (m.Name.StartsWith("Network", StringComparison.Ordinal)) continue;
                        if (m.Name.Contains("<") || m.Name.Contains("__")) continue;

                        if ((m.Name.StartsWith("get_", StringComparison.Ordinal) ||
                             m.Name.StartsWith("set_", StringComparison.Ordinal))) continue;
                        if (m.GetMethodBody() == null && !m.IsAbstract)
                        {

                        }

                        _harmony.Patch(m, prefix: prefix);
                        _patched++;
                    }
                    catch (Exception e)
                    {
                        _failed++;
                        if (_failed <= 5)
                            log.Warning("[trace] " + t.Name + "." + m.Name + ": " + e.Message);
                    }
                }
            }

            log.Msg("[trace] patched " + _patched + " method(s) across "
                  + Targets.Length + " type(s), " + _failed + " skipped");
        }

        static void Unpatch()
        {
            if (!_installed) return;
            try { if (_harmony != null) _harmony.UnpatchSelf(); }
            catch (Exception e) { _log?.Warning("[trace] unpatch: " + e.Message); }
            _harmony = null;
            _installed = false;
            _patched = 0;
            _failed = 0;
            _log?.Msg("[trace] patches removed; the game is back to stock behaviour");
        }

        static void Universal(object __instance, MethodBase __originalMethod, object[] __args)
        {
            if (!Enabled) return;
            try
            {
                var sb = new StringBuilder();
                sb.Append(' ', Math.Min(_depth, 8) * 2);
                sb.Append(__originalMethod.DeclaringType != null
                    ? __originalMethod.DeclaringType.Name : "?");
                sb.Append('.').Append(__originalMethod.Name).Append('(');
                if (__args != null)
                {
                    var ps = __originalMethod.GetParameters();
                    for (int i = 0; i < __args.Length; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        if (i < ps.Length) sb.Append(ps[i].Name).Append('=');
                        sb.Append(Describe(__args[i]));
                    }
                }
                sb.Append(')');
                if (__instance != null) sb.Append("   on ").Append(Describe(__instance));
                _log?.Msg("[trace] " + sb);

                Detail(__instance, __originalMethod.Name);
            }
            catch { }
        }

        static void Detail(object inst, string method)
        {
            try
            {
                var board = inst as Il2Cpp.CerebusOpboard;
                if (board != null)
                {
                    _log?.Msg("[trace]     board: MinAI=" + board.MinAI + " MaxAI=" + board.MaxAI
                            + " prep=" + board.PrepTimeInSeconds
                            + " infilTime=" + board.SelectedInfiltrationTime
                            + " infils=" + (board.AvailableInfils != null
                                            ? board.AvailableInfils.Length : -1)
                            + " locked=" + board.isLocked
                            + " sim=" + board.isSimulation
                            + " map=" + (board.MapPrefab != null ? board.MapPrefab.name : "<none>"));
                    return;
                }

                var op = inst as Il2Cpp.CerebusOperation;
                if (op != null)
                {
                    var pk = op.TargetPackages;
                    _log?.Msg("[trace]     operation: name='" + op.name + "' sim=" + op.isSimulation
                            + " locked=" + op.isLocked + " index=" + op.OperationIndex
                            + " packages=" + (pk != null ? pk.Length : -1));
                    return;
                }

                var pkg = inst as Il2Cpp.CerebusTargetPackage;
                if (pkg != null)
                {
                    _log?.Msg("[trace]     package: name='" + pkg.name + "' AO='"
                            + pkg.OPERATION_AREA_OF_OPERATION + "' locked=" + pkg.isLocked
                            + " index=" + pkg.TargetPackageIndex
                            + " board=" + (pkg.TargetPackage != null
                                           ? pkg.TargetPackage.name : "<none>"));
                    return;
                }
            }
            catch { }
        }

        static string Describe(object o)
        {
            if (o == null) return "null";
            try
            {
                var uo = o as UnityEngine.Object;
                if (uo != null)
                {
                    var go = o as GameObject;
                    if (go != null) return "GameObject'" + Path(go.transform) + "'";
                    var comp = o as Component;
                    if (comp != null)
                        return comp.GetIl2CppType().Name + "'" + Path(comp.transform) + "'";
                    return uo.GetIl2CppType().Name + "'" + uo.name + "'";
                }
                if (o is string) return "\"" + o + "\"";
                return o.ToString();
            }
            catch { return o.GetType().Name; }
        }

        static string Path(Transform t)
        {
            if (t == null) return "<null>";
            var sb = new StringBuilder(t.name);
            var p = t.parent;
            int guard = 0;
            while (p != null && guard++ < 12)
            {
                sb.Insert(0, p.name + "/");
                p = p.parent;
            }
            return sb.ToString();
        }

        public static void Tick()
        {
            if (!Enabled) return;
            if (!Input.GetMouseButtonDown(0) && !Input.GetMouseButtonDown(1)) return;
            try
            {
                var es = EventSystem.current;
                if (es == null) { _log?.Msg("[trace] click: no EventSystem"); return; }

                var ped = new PointerEventData(es);
                ped.position = Input.mousePosition;
                var hits = new Il2CppSystem.Collections.Generic.List<RaycastResult>();
                es.RaycastAll(ped, hits);

                _log?.Msg("[trace] ===== CLICK at " + Input.mousePosition
                        + "  hits=" + hits.Count + " =====");
                for (int i = 0; i < hits.Count && i < 6; i++)
                {
                    var go = hits[i].gameObject;
                    if (go == null) continue;
                    _log?.Msg("[trace]   [" + i + "] " + Path(go.transform));
                    _log?.Msg("[trace]       components: " + Components(go));
                    var tmp = go.GetComponent<Il2CppTMPro.TextMeshProUGUI>();
                    if (tmp != null) _log?.Msg("[trace]       text: \"" + tmp.text + "\"");
                }
            }
            catch (Exception e) { _log?.Warning("[trace] click: " + e.Message); }
        }

        static string Components(GameObject go)
        {
            try
            {
                var cs = go.GetComponents<Component>();
                var sb = new StringBuilder();
                for (int i = 0; i < cs.Length; i++)
                {
                    if (cs[i] == null) continue;
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(cs[i].GetIl2CppType().Name);
                }
                return sb.ToString();
            }
            catch { return "?"; }
        }

        public static void Toggle()
        {
            if (!Enabled)
            {
                Patch();
                Enabled = true;
                _log?.Msg("[trace] ON -- click through the laptop now; "
                        + "every call and click is being logged. Shift+F6 again to stop.");
            }
            else
            {
                Enabled = false;
                Unpatch();
                _log?.Msg("[trace] OFF");
            }
        }

        public static void DumpRow(Transform row)
        {
            if (row == null) { _log?.Msg("[trace] no row"); return; }
            _log?.Msg("[trace] ===== ROW " + Path(row) + " =====");
            var tmps = row.GetComponentsInChildren<Il2CppTMPro.TextMeshProUGUI>(true);
            for (int i = 0; i < tmps.Length; i++)
            {
                if (tmps[i] == null) continue;
                var rt = tmps[i].transform?.TryCast<RectTransform>();
                _log?.Msg("[trace]   text[" + i + "] '" + tmps[i].gameObject.name
                        + "' = \"" + tmps[i].text + "\""
                        + (rt != null ? "  x=" + rt.anchoredPosition.x
                                      + " w=" + rt.rect.width : "")
                        + "  active=" + tmps[i].gameObject.activeInHierarchy);
            }
        }
    }
}
