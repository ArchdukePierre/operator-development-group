// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Il2CppInterop.Runtime;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace MapLoader
{

    public static class MapInfils
    {
        public const string ObjectName = "ODG_INFIL";
        public const string PrefabName = "ODG_MAPPREFAB";

        static MelonLogger.Instance _log;

        public struct Point
        {
            public string Name;
            public float U, V;
            public Vector3 World;
        }

        static readonly Dictionary<string, List<Point>> _cache =
            new Dictionary<string, List<Point>>(StringComparer.OrdinalIgnoreCase);

        static readonly Dictionary<string, GameObject> _prefabs =
            new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        static GameObject _holder;

        static readonly List<GameObject> _made = new List<GameObject>();

        public static void Init(MelonLogger.Instance log) { _log = log; }

        public static List<Point> Load(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return null;
            List<Point> cached;
            if (_cache.TryGetValue(sceneName, out cached)) return cached;

            var pts = new List<Point>();
            try
            {
                string path = MapIdentity.InFolder(sceneName, "preview.txt")
                           ?? Path.Combine(MapPreview.PreviewDir, sceneName + ".txt");
                if (File.Exists(path))
                {
                    foreach (var raw in File.ReadAllLines(path))
                    {
                        var line = raw.Trim();
                        if (line.Length == 0 || line[0] == '#') continue;
                        var f = line.Split('\t');
                        if (f.Length < 7 || f[0] != "infil") continue;
                        pts.Add(new Point
                        {
                            Name = f[1],
                            U = P(f[2]), V = P(f[3]),
                            World = new Vector3(P(f[4]), P(f[5]), P(f[6])),
                        });
                    }
                    _log?.Msg("[infil] " + pts.Count + " insertion point(s) for '"
                            + sceneName + "'");
                    try { LogHostInfilMethods(); } catch { }
                }
                else _log?.Warning("[infil] no metadata at " + path);
            }
            catch (Exception e) { _log?.Warning("[infil] load: " + e.Message); }

            _cache[sceneName] = pts;
            return pts;
        }

        static float P(string s)
        {
            float v;
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)
                 ? v : 0f;
        }

        static bool _loggedMarkerRig;

        static readonly Dictionary<string, GameObject> _fallbacks =
            new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);

        public static GameObject BuildFallbackPrefab(string sceneName)
        {
            GameObject cached;
            if (_fallbacks.TryGetValue(sceneName, out cached) && cached != null)
                return cached;
            try
            {
                if (_holder == null)
                {
                    _holder = new GameObject("ODG_MAP_TEMPLATES");
                    _holder.SetActive(false);
                    UnityEngine.Object.DontDestroyOnLoad(_holder);
                }
                var root = new GameObject(PrefabName + "_" + sceneName + "_FALLBACK",
                    new Il2CppSystem.Type[]
                {
                    Il2CppType.Of<RectTransform>(),
                    Il2CppType.Of<CanvasRenderer>(),
                    Il2CppType.Of<RawImage>(),
                });
                root.transform.SetParent(_holder.transform, false);
                var rt = root.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                var raw = root.GetComponent<RawImage>();
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                var c = new Color32(16, 20, 24, 255);
                tex.SetPixels32(new Il2CppInterop.Runtime.InteropTypes.Arrays
                    .Il2CppStructArray<Color32>(new[] { c, c, c, c }));
                tex.Apply(false, false);
                raw.texture = tex;
                raw.color = Color.white;
                raw.raycastTarget = false;
                _fallbacks[sceneName] = root;
                _log?.Warning("[infil] using a PLACEHOLDER board map for '" + sceneName
                            + "': the real preview could not be built, so the panel is "
                            + "dark instead of showing another mission's map. Fix the "
                            + "preview files to get the plan view back.");
                return root;
            }
            catch (Exception e)
            {
                _log?.Error("[infil] BuildFallbackPrefab: " + e.Message);
                return null;
            }
        }

        public static GameObject BuildBoardPrefab(string sceneName)
        {
            GameObject cached2;
            if (_prefabs.TryGetValue(sceneName, out cached2) && cached2 != null)
                return cached2;

            var tex = MapPreview.Load(sceneName);
            var pts = Load(sceneName);
            var template = FindTemplate();
            if (tex == null || pts == null || pts.Count == 0 || template == null)
            {
                _log?.Warning("[infil] cannot build a map prefab for '" + sceneName
                            + "' (texture " + (tex != null) + ", points "
                            + (pts != null ? pts.Count : 0) + ", marker template "
                            + (template != null) + ")");
                return null;
            }

            try
            {
                if (_holder == null)
                {

                    _holder = new GameObject("ODG_MAP_TEMPLATES");
                    _holder.SetActive(false);
                    UnityEngine.Object.DontDestroyOnLoad(_holder);
                }

                var root = new GameObject(PrefabName + "_" + sceneName, new Il2CppSystem.Type[]
                {
                    Il2CppType.Of<RectTransform>(),
                    Il2CppType.Of<CanvasRenderer>(),
                    Il2CppType.Of<RawImage>(),
                });
                root.transform.SetParent(_holder.transform, false);
                var rt = root.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                var raw = root.GetComponent<RawImage>();
                raw.texture = tex;
                raw.color = Color.white;

                raw.raycastTarget = false;

                for (int i = 0; i < pts.Count; i++)
                    CloneMarker(template, root.transform, pts[i], i);

                _prefabs[sceneName] = root;
                _log?.Msg("[infil] built map prefab '" + root.name + "' with "
                        + pts.Count + " marker(s)");
                return root;
            }
            catch (Exception e)
            {
                _log?.Error("[infil] BuildBoardPrefab: " + e.Message);
                return null;
            }
        }

        public static int WireFromInstances(Il2Cpp.OperationBoardUI ui, string sceneName)
        {
            if (ui == null) return 0;
            int main = WireParent(ui, SafeParent(ui, true), sceneName, false);
            int full = WireParent(ui, SafeParent(ui, false), sceneName, true);
            if (main > 0)
            {
                _log?.Msg("[infil] native path: board instantiated our map ("
                        + main + " marker(s), fullscreen " + full + ")");

                if (full == 0)
                {
                    var fp = SafeParent(ui, false);
                    var pts = Load(sceneName);
                    var clones = BuildOn(fp, pts);
                    if (clones.Count > 0)
                    {
                        var farr = new Il2CppInterop.Runtime.InteropTypes.Arrays
                            .Il2CppReferenceArray<Il2Cpp.MapInfilMarker>(clones.Count);
                        for (int i = 0; i < clones.Count; i++) farr[i] = clones[i];
                        try { ui.FullscreenMapInfilMarkers = farr; } catch { }
                        _log?.Msg("[infil] fullscreen side built by hand ("
                                + clones.Count + " marker(s))");
                    }
                }
            }
            return main;
        }

        static int WireParent(Il2Cpp.OperationBoardUI ui, Transform parent,
                              string sceneName, bool fullscreen)
        {
            if (parent == null) return 0;
            try
            {

                GameObject inst = null;
                for (int i = parent.childCount - 1; i >= 0; i--)
                {
                    var c = parent.GetChild(i);
                    if (c == null || !c.name.StartsWith(PrefabName, StringComparison.Ordinal))
                        continue;
                    if (inst == null) inst = c.gameObject;
                    else UnityEngine.Object.Destroy(c.gameObject);
                }
                if (inst == null) return 0;
                inst.SetActive(true);

                try { inst.transform.SetAsLastSibling(); } catch { }
                MapPreview.HideOthers(parent, PrefabName);

                try
                {
                    var rt = inst.GetComponent<RectTransform>();
                    if (rt != null)
                    {
                        rt.anchorMin = Vector2.zero;
                        rt.anchorMax = Vector2.one;
                        rt.offsetMin = Vector2.zero;
                        rt.offsetMax = Vector2.zero;
                        rt.localScale = Vector3.one;
                        rt.localRotation = Quaternion.identity;
                    }
                }
                catch { }

                var markers = inst.GetComponentsInChildren<Il2Cpp.MapInfilMarker>(true);
                if (markers == null || markers.Length == 0) return 0;

                var list = new List<Il2Cpp.MapInfilMarker>();
                foreach (var m in markers) if (m != null) list.Add(m);
                list.Sort((a, b) => GetIdx(a).CompareTo(GetIdx(b)));

                var arr = new Il2CppInterop.Runtime.InteropTypes.Arrays
                    .Il2CppReferenceArray<Il2Cpp.MapInfilMarker>(list.Count);
                for (int i = 0; i < list.Count; i++) arr[i] = list[i];
                if (fullscreen) ui.FullscreenMapInfilMarkers = arr;
                else ui.MapInfilMarkers = arr;

                int armed = 0;
                for (int i = 0; i < list.Count; i++) if (ArmMarker(list[i])) armed++;
                if (armed != list.Count)
                    _log?.Warning("[infil] armed " + armed + " of " + list.Count
                                + " markers; the rest cannot be clicked");

                if (!fullscreen)
                {

                    int bits = list.Count > 62 ? 62 : list.Count;
                    long mask = bits >= 62 ? long.MaxValue : (1L << bits) - 1L;
                    try { ui.SetActiveInfilsMaskUI(mask); } catch { }

                    PublishInfilMask(bits);

                    var pts = Load(sceneName);
                    if (pts != null && pts.Count != list.Count)
                        _log?.Warning("[infil] AUDIT: sidecar has " + pts.Count
                                    + " points but " + list.Count + " markers wired");
                }
                return list.Count;
            }
            catch (Exception e)
            {
                _log?.Warning("[infil] WireParent: " + e.Message);
                return 0;
            }
        }

        static bool ArmMarker(Il2Cpp.MapInfilMarker m)
        {
            if (m == null) return false;
            bool any = false;
            var captured = m;
            try
            {
                var buttons = m.gameObject.GetComponentsInChildren<UnityEngine.UI.Button>(true);
                for (int i = 0; i < (buttons != null ? buttons.Length : 0); i++)
                {
                    var b = buttons[i];
                    if (b == null) continue;

                    if (!b.gameObject.activeSelf) continue;
                    try
                    {
                        b.onClick.RemoveAllListeners();
                        b.onClick.AddListener((UnityEngine.Events.UnityAction)
                            (() => HandleMarkerClick(captured)));
                        b.interactable = true;
                        any = true;
                    }
                    catch (Exception e) { _log?.Warning("[infil] arm button: " + e.Message); }
                }
            }
            catch (Exception e) { _log?.Warning("[infil] arm markers: " + e.Message); }

            try
            {
                var go = m.gameObject;
                var trig = go.GetComponent<UnityEngine.EventSystems.EventTrigger>();
                if (trig == null)
                    trig = go.AddComponent<UnityEngine.EventSystems.EventTrigger>();
                trig.triggers = new Il2CppSystem.Collections.Generic
                    .List<UnityEngine.EventSystems.EventTrigger.Entry>();
                var e = new UnityEngine.EventSystems.EventTrigger.Entry();
                e.eventID = UnityEngine.EventSystems.EventTriggerType.PointerClick;
                e.callback = new UnityEngine.EventSystems.EventTrigger.TriggerEvent();
                e.callback.AddListener(
                    (UnityEngine.Events.UnityAction<UnityEngine.EventSystems.BaseEventData>)
                    ((UnityEngine.EventSystems.BaseEventData _) => HandleMarkerClick(captured)));
                trig.triggers.Add(e);
                any = true;
            }
            catch (Exception e) { _log?.Warning("[infil] arm trigger: " + e.Message); }
            return any;
        }

        static void PublishInfilMask(int count)
        {
            if (count <= 0) return;
            try
            {
                bool server = false;
                try { server = Il2CppMirror.NetworkServer.active; } catch { }
                if (!server) return;
                var st = Il2Cpp.MissionLaptopNetworkState.instance;
                if (st == null) { _log?.Msg("[infil] no MissionLaptopNetworkState yet"); return; }
                for (int i = 0; i < count; i++)
                {
                    try { st.SetInfilActive(i, true); }
                    catch (Exception e)
                    { _log?.Warning("[infil] SetInfilActive(" + i + "): " + e.Message); break; }
                }
                _log?.Msg("[infil] published active-infil mask for " + count
                        + " infil(s); ActiveInfilMask=" + st.ActiveInfilMask);
            }
            catch (Exception e) { _log?.Warning("[infil] PublishInfilMask: " + e.Message); }
        }

        static int GetIdx(Il2Cpp.MapInfilMarker m)
        {
            try { return m.MarkerIndex; } catch { return 0; }
        }

        static Il2Cpp.MapInfilMarker FindTemplate()
        {
            try
            {
                var arr = Resources.FindObjectsOfTypeAll(
                    Il2CppType.Of<Il2Cpp.MapInfilMarker>());
                if (arr == null) return null;
                Il2Cpp.MapInfilMarker any = null;
                for (int i = 0; i < arr.Length; i++)
                {
                    var m = arr[i].TryCast<Il2Cpp.MapInfilMarker>();
                    if (m == null) continue;
                    GameObject go = null;
                    try { go = m.gameObject; } catch { }
                    if (go == null) continue;
                    if (go.name.StartsWith(ObjectName, StringComparison.Ordinal)) continue;
                    if (go.transform.root != null
                        && go.transform.root.name.StartsWith("ODG_", StringComparison.Ordinal))
                        continue;
                    if (any == null) any = m;
                    bool ground = false;
                    try { ground = m.IsGroundInfil; } catch { }
                    if (ground) return m;
                }
                return any;
            }
            catch (Exception e) { _log?.Warning("[infil] template: " + e.Message); }
            return null;
        }

        static Il2Cpp.MapInfilMarker CloneMarker(Il2Cpp.MapInfilMarker template,
                                                 Transform parent, Point pt, int index)
        {
            try
            {
                var go = UnityEngine.Object.Instantiate(template.gameObject, parent, false);
                go.name = ObjectName + "_" + index;
                go.SetActive(true);

                var rt = go.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.anchorMin = new Vector2(pt.U, pt.V);
                    rt.anchorMax = new Vector2(pt.U, pt.V);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = Vector2.zero;
                    rt.localScale = Vector3.one;
                    rt.localRotation = Quaternion.identity;
                }

                var m = go.GetComponent<Il2Cpp.MapInfilMarker>();
                if (m == null) { UnityEngine.Object.Destroy(go); return null; }

                try { m.MarkerIndex = index; } catch { }
                try { m.InfilName = pt.Name; } catch { }
                try { m.IsGroundInfil = true; } catch { }
                try { m.IsHeliInfil = false; } catch { }
                try { m.IsExfil = false; } catch { }
                try { m.isSelectedLocal = false; } catch { }
                try { m.MaxPlayers = 8; } catch { }
                try
                {
                    var t = m.TextUI;
                    if (t != null)
                    {
                        t.text = pt.Name;

                        var loc = t.GetComponent<Il2Cpp.LocalizationTMPEvent>();
                        if (loc != null) loc.enabled = false;
                    }
                }
                catch { }

                try
                {
                    var buttons = go.GetComponentsInChildren<UnityEngine.UI.Button>(true);
                    for (int bi = 0; bi < (buttons != null ? buttons.Length : 0); bi++)
                    {
                        var b = buttons[bi];
                        if (b == null) continue;
                        string bn = "";
                        try { bn = b.gameObject.name ?? ""; } catch { }

                        if (bn.IndexOf("exf", StringComparison.OrdinalIgnoreCase) >= 0
                            || bn.Equals("EP", StringComparison.OrdinalIgnoreCase)
                            || bn.IndexOf("_EP", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            try { b.gameObject.SetActive(false); } catch { }
                            continue;
                        }

                        try { b.interactable = true; } catch { }
                    }
                }
                catch (Exception be) { _log?.Warning("[infil] button rewire: " + be.Message); }

                try
                {
                    var cg = go.GetComponent<CanvasGroup>();
                    if (cg != null) { cg.interactable = true; cg.blocksRaycasts = true; cg.alpha = 1f; }

                    var graphics = go.GetComponentsInChildren<UnityEngine.UI.Graphic>(true);
                    int targets = 0;
                    for (int gi = 0; gi < (graphics != null ? graphics.Length : 0); gi++)
                    {
                        var gr = graphics[gi];
                        if (gr == null) continue;
                        if (gr.raycastTarget) targets++;
                    }

                    if (targets == 0)
                    {
                        var img = go.GetComponent<UnityEngine.UI.Image>();
                        if (img == null) img = go.AddComponent<UnityEngine.UI.Image>();
                        img.color = new Color(1f, 1f, 1f, 0f);
                        img.raycastTarget = true;
                        targets = 1;
                    }

                    if (!_loggedMarkerRig)
                    {
                        _loggedMarkerRig = true;
                        var btns = go.GetComponentsInChildren<UnityEngine.UI.Button>(true);
                        var names = new System.Text.StringBuilder();
                        for (int bi = 0; bi < (btns != null ? btns.Length : 0); bi++)
                        {
                            if (bi > 0) names.Append(", ");
                            try
                            {
                                names.Append(btns[bi].gameObject.name)
                                     .Append(btns[bi].interactable ? "" : "(NOT INTERACTABLE)");
                            }
                            catch { }
                        }
                        _log?.Msg("[infil] marker rig: " + (btns != null ? btns.Length : 0)
                                + " button(s) [" + names + "], "
                                + (graphics != null ? graphics.Length : 0) + " graphic(s), "
                                + targets + " raycast target(s), CanvasGroup="
                                + (cg != null ? "yes" : "no")
                                + ". If clicks still do not arrive, the blocker is above "
                                + "this object: a parent CanvasGroup or a full-panel "
                                + "raycast target drawn over the map.");
                    }
                }
                catch (Exception re) { _log?.Warning("[infil] marker raycast rig: " + re.Message); }

                try
                {
                    if (go.GetComponent<UnityEngine.EventSystems.EventTrigger>() == null)
                        go.AddComponent<UnityEngine.EventSystems.EventTrigger>();
                }
                catch { }
                return m;
            }
            catch (Exception e)
            {
                _log?.Warning("[infil] CloneMarker " + index + ": " + e.Message);
                return null;
            }
        }

        public static int Apply(Il2Cpp.OperationBoardUI ui, string sceneName)
        {
            if (ui == null) return 0;
            var pts = Load(sceneName);
            if (pts == null || pts.Count == 0) return 0;

            int n = 0;
            try
            {
                _made.Clear();
                var main = BuildOn(SafeParent(ui, true), pts);
                var full = BuildOn(SafeParent(ui, false), pts);
                n = main.Count;

                if (n > 0)
                {
                    var arr = new Il2CppInterop.Runtime.InteropTypes.Arrays
                        .Il2CppReferenceArray<Il2Cpp.MapInfilMarker>(n);
                    for (int i = 0; i < n; i++) arr[i] = main[i];
                    try { ui.MapInfilMarkers = arr; } catch (Exception e)
                    { _log?.Warning("[infil] register: " + e.Message); }
                }
                if (full.Count > 0)
                {
                    var farr = new Il2CppInterop.Runtime.InteropTypes.Arrays
                        .Il2CppReferenceArray<Il2Cpp.MapInfilMarker>(full.Count);
                    for (int i = 0; i < full.Count; i++) farr[i] = full[i];
                    try { ui.FullscreenMapInfilMarkers = farr; } catch { }
                }

                try { ui.SetActiveInfilsMaskUI(0L); } catch { }
                _log?.Msg("[infil] fallback overlay: placed " + n
                        + " insertion point(s) on the shared board");
            }
            catch (Exception e) { _log?.Error("[infil] Apply: " + e.Message); }
            return n;
        }

        static List<Il2Cpp.MapInfilMarker> BuildOn(Transform mapParent, List<Point> pts)
        {
            var made = new List<Il2Cpp.MapInfilMarker>();
            if (mapParent == null || pts == null || pts.Count == 0) return made;

            var template = FindTemplate();
            if (template == null)
            {
                _log?.Warning("[infil] no marker in memory to copy from yet");
                return made;
            }

            try
            {
                for (int i = mapParent.childCount - 1; i >= 0; i--)
                {
                    var c = mapParent.GetChild(i);
                    if (c != null && c.name.StartsWith(ObjectName, StringComparison.Ordinal))
                        UnityEngine.Object.Destroy(c.gameObject);
                }
            }
            catch { }

            for (int i = 0; i < pts.Count; i++)
            {
                var m = CloneMarker(template, mapParent, pts[i], i);
                if (m == null) continue;
                try { m.gameObject.transform.SetAsLastSibling(); } catch { }
                made.Add(m);
                _made.Add(m.gameObject);
            }
            return made;
        }

        static Transform SafeParent(Il2Cpp.OperationBoardUI ui, bool main)
        {
            try { return main ? ui.MapParent : ui.FullscreenMapParent; }
            catch { return null; }
        }

        public static void Invalidate(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return;
            _cache.Remove(sceneName);
            GameObject root;
            if (_prefabs.TryGetValue(sceneName, out root))
            {
                _prefabs.Remove(sceneName);
                try { if (root != null) UnityEngine.Object.Destroy(root); } catch { }
            }
            try { CustomOps.ForgetMapPrefab(sceneName); } catch { }
            try { MapRuntime.ForgetMarks(); } catch { }
            _log?.Msg("[infil] '" + sceneName + "': board picture, markers and marked spawn "
                    + "forgotten; rebuilt from disk on the next board open.");
        }

        public static void Clear()
        {
            if (_made.Count == 0) return;
            try
            {
                for (int i = 0; i < _made.Count; i++)
                {
                    var go = _made[i];
                    if (go != null) UnityEngine.Object.Destroy(go);
                }
            }
            catch { }
            _made.Clear();
        }

        static int _lastSelectFrame = -1;

        static string NameOf(Il2Cpp.MapInfilMarker m)
        {
            try { return m.InfilName ?? ""; } catch { return ""; }
        }

        public static bool HandleMarkerClick(Il2Cpp.MapInfilMarker m)
        {
            if (m == null) return false;
            GameObject go = null;
            try { go = m.gameObject; } catch { }
            if (go == null || !go.name.StartsWith(ObjectName, StringComparison.Ordinal))
                return false;

            bool individual = false;
            try { individual = m.individualSelectMode; } catch { }
            var ui = go.GetComponentInParent<Il2Cpp.OperationBoardUI>();
            if (individual || ui == null)
            {
                _log?.Msg("[infil] insertion-screen click on '" + NameOf(m)
                        + "' idx=" + GetIdx(m) + " -> handed to the game "
                        + "(individualSelectMode=" + individual
                        + ", boardAbove=" + (ui != null) + ")");
                return false;
            }

            if (Time.frameCount == _lastSelectFrame) return true;
            _lastSelectFrame = Time.frameCount;

            bool now = false;
            try { now = !m.isSelectedLocal; m.isSelectedLocal = now; } catch { }

            _log?.Msg("[infil] board click on '" + NameOf(m) + "' idx=" + GetIdx(m)
                    + " -> selected=" + now);

            if (ui != null)
            {
                long mask = 0;
                try
                {
                    var arr = ui.MapInfilMarkers;
                    if (arr != null)
                        for (int i = 0; i < arr.Length; i++)
                        {
                            var mm = arr[i];
                            if (mm == null) continue;
                            bool sel = false; int idx = 0;
                            try { sel = mm.isSelectedLocal; } catch { }
                            try { idx = mm.MarkerIndex; } catch { }
                            if (sel) mask |= 1L << idx;
                        }
                }
                catch { }

                try { ui.SetActiveInfilsMaskUI(mask); } catch { }
                string nm = ""; try { nm = m.InfilName ?? ""; } catch { }
                _log?.Msg("[infil] '" + nm + "' " + (now ? "selected" : "deselected")
                        + "  mask=" + mask);
            }
            return true;
        }

        public static void DestroyInstancesUnder(Transform parent)
        {
            if (parent == null) return;
            try
            {
                for (int i = parent.childCount - 1; i >= 0; i--)
                {
                    var c = parent.GetChild(i);
                    if (c != null
                        && c.name.StartsWith(PrefabName, StringComparison.Ordinal))
                        UnityEngine.Object.Destroy(c.gameObject);
                }
            }
            catch { }
        }

        public static void OnSceneGone()
        {
            _made.Clear();
            try
            {
                foreach (var kv in _prefabs)
                    if (kv.Value != null) UnityEngine.Object.Destroy(kv.Value);
            }
            catch { }
            _prefabs.Clear();
        }

        internal static void LogHostInfilMethods()
        {
            try
            {
                var im = Il2Cpp.InfiltrationManager.instance;
                if (im == null) { _log?.Msg("[infil] host methods: no InfiltrationManager yet"); return; }
                var arr = im.infilMethods;
                int n = arr != null ? arr.Length : 0;
                string veh = "none", anim = "none", exf = "none";
                try { if (im.InfiltrationVehicle != null) veh = im.InfiltrationVehicle.name; } catch { }
                try { if (im.InfiltrationAnimationPrefab != null) anim = im.InfiltrationAnimationPrefab.name; } catch { }
                try { if (im.ExfilHelicopterSpawn != null) exf = im.ExfilHelicopterSpawn.gameObject.name; } catch { }
                int va = 0; try { va = im.infilVehicleAnimations != null ? im.infilVehicleAnimations.Count : 0; } catch { }
                _log?.Msg("[infil] host methods: " + n + " entr(ies); InfiltrationVehicle='" + veh + "' InfiltrationAnimationPrefab='"
                        + anim + "' infilVehicleAnimations=" + va + " ExfilHelicopterSpawn='" + exf + "' SelectableExfiltration=" + im.SelectableExfiltration);
                for (int i = 0; i < n; i++)
                {
                    var e = arr[i]; if (e == null) continue;
                    string obj = "none", clips = "";
                    try { if (e.infilObject != null) obj = e.infilObject.name; } catch { }
                    try { var c = e.VehicleAnimatorInfiltrationClips; if (c != null) clips = string.Join("|", c); } catch { }
                    int ex = 0; try { ex = e.extracts != null ? e.extracts.Length : 0; } catch { }
                    _log?.Msg("[infil]   [" + i + "] '" + e.infilName + "' vehicle=" + e.VehicleInfiltration + " object='" + obj
                            + "' clips='" + clips + "' extracts=" + ex);
                }
            }
            catch (Exception ex) { _log?.Warning("[infil] host methods: " + ex.Message); }
        }

    }
}
