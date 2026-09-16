// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace MapLoader
{

    public static class MapPreview
    {
        public const string ObjectName = "ODG_MAP_PREVIEW";

        static MelonLogger.Instance _log;
        static readonly System.Collections.Generic.Dictionary<string, Texture2D> _cache =
            new System.Collections.Generic.Dictionary<string, Texture2D>(
                StringComparer.OrdinalIgnoreCase);

        public static string PreviewDir = ResolvePreviewDir();

        static string ResolvePreviewDir()
        {
            try
            {
                var root = MelonLoader.Utils.MelonEnvironment.GameRootDirectory;
                if (!string.IsNullOrEmpty(root))
                {
                    var shipped = System.IO.Path.Combine(root, "Mods", "MapLoader_Assets", "preview");
                    if (System.IO.Directory.Exists(shipped)) return shipped;
                }
            }
            catch { }
            return @"D:\OperatorHSR\maps\preview";
        }

        static readonly System.Collections.Generic.List<GameObject> _hidden =
            new System.Collections.Generic.List<GameObject>();

        static readonly System.Collections.Generic.List<GameObject> _ours =
            new System.Collections.Generic.List<GameObject>();

        public static void Init(MelonLogger.Instance log) { _log = log; }

        static bool IsOurs(string n)
        {
            if (string.IsNullOrEmpty(n)) return false;
            return n == ObjectName
                || n.StartsWith(MapInfils.ObjectName, StringComparison.Ordinal)
                || n.StartsWith(MapInfils.PrefabName, StringComparison.Ordinal);
        }

        public static void OnSceneGone()
        {
            _hidden.Clear();
            _ours.Clear();
        }

        public static void HideOthers(Transform parent, string keepPrefix)
        {
            if (parent == null) return;
            try
            {
                for (int i = 0; i < parent.childCount; i++)
                {
                    var c = parent.GetChild(i);
                    if (c == null || c.name == ObjectName) continue;
                    if (c.name.StartsWith(keepPrefix, StringComparison.Ordinal)) continue;
                    if (!c.gameObject.activeSelf) continue;
                    c.gameObject.SetActive(false);
                    _hidden.Add(c.gameObject);
                }
            }
            catch { }
        }

        public static void Restore()
        {
            try
            {
                for (int i = 0; i < _hidden.Count; i++)
                {
                    var go = _hidden[i];
                    if (go != null) go.SetActive(true);
                }
            }
            catch (Exception e) { _log?.Warning("[preview] restore: " + e.Message); }
            _hidden.Clear();

            try
            {
                for (int i = 0; i < _ours.Count; i++)
                {
                    var go = _ours[i];
                    if (go != null && go.activeSelf) go.SetActive(false);
                }
            }
            catch { }
        }

        public static void RestoreFor(Transform mapParent, Transform fullscreenParent)
        {
            RestoreUnder(_hidden, mapParent, fullscreenParent, true);
            RestoreUnder(_ours, mapParent, fullscreenParent, false);
        }

        static void RestoreUnder(System.Collections.Generic.List<GameObject> list,
                                 Transform a, Transform b, bool activate)
        {
            try
            {
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var go = list[i];
                    if (go == null) { list.RemoveAt(i); continue; }
                    var t = go.transform;
                    bool under = (a != null && t.IsChildOf(a))
                              || (b != null && t.IsChildOf(b));
                    if (!under) continue;
                    if (activate) { if (!go.activeSelf) go.SetActive(true); }
                    else { if (go.activeSelf) go.SetActive(false); }
                    list.RemoveAt(i);
                }
            }
            catch (Exception e) { _log?.Warning("[preview] RestoreFor: " + e.Message); }
        }

        public static Texture2D Load(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return null;
            Texture2D cached;
            if (_cache.TryGetValue(sceneName, out cached)) return cached;

            string path = MapIdentity.InFolder(sceneName, "preview.png")
                       ?? Path.Combine(PreviewDir, sceneName + ".png");
            Texture2D tex = null;
            try
            {
                if (File.Exists(path))
                {
                    tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    var bytes = File.ReadAllBytes(path);
                    if (!ImageConversion.LoadImage(tex, bytes))
                    {
                        UnityEngine.Object.Destroy(tex);
                        tex = null;
                        _log?.Warning("[preview] could not decode " + path);
                    }
                    else
                    {
                        tex.wrapMode = TextureWrapMode.Clamp;
                        tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
                        _log?.Msg("[preview] loaded " + Path.GetFileName(path)
                                + "  " + tex.width + "x" + tex.height);
                    }
                }
                else _log?.Warning("[preview] no image at " + path);
            }
            catch (Exception e) { _log?.Warning("[preview] load: " + e.Message); }

            _cache[sceneName] = tex;
            return tex;
        }

        public static bool Show(Transform mapParent, string sceneName)
        {
            if (mapParent == null) return false;
            var tex = Load(sceneName);
            if (tex == null) return false;

            try
            {

                for (int i = 0; i < mapParent.childCount; i++)
                {
                    var c = mapParent.GetChild(i);
                    if (c == null || IsOurs(c.name)) continue;
                    if (!c.gameObject.activeSelf) continue;
                    c.gameObject.SetActive(false);
                    _hidden.Add(c.gameObject);
                }

                var existing = mapParent.Find(ObjectName);
                if (existing != null)
                {
                    existing.gameObject.SetActive(true);
                    Apply(existing.gameObject, tex);
                    return true;
                }

                bool ui = mapParent.GetComponentInParent<Canvas>() != null;
                var go = ui ? BuildUi(tex) : BuildQuad(tex);
                if (go == null) return false;
                go.name = ObjectName;
                go.transform.SetParent(mapParent, false);

                go.transform.SetAsFirstSibling();
                _ours.Add(go);
                _log?.Msg("[preview] showing plan view for '" + sceneName + "' as "
                        + (ui ? "UI image" : "world quad"));
                return true;
            }
            catch (Exception e)
            {
                _log?.Error("[preview] Show: " + e.Message);
                return false;
            }
        }

        static void Apply(GameObject go, Texture2D tex)
        {
            try
            {
                var raw = go.GetComponent<RawImage>();
                if (raw != null) { raw.texture = tex; return; }
                var r = go.GetComponent<Renderer>();
                if (r != null && r.material != null) r.material.mainTexture = tex;
            }
            catch { }
        }

        static GameObject BuildUi(Texture2D tex)
        {
            var go = new GameObject("preview", new Il2CppSystem.Type[]
            {
                Il2CppInterop.Runtime.Il2CppType.Of<RectTransform>(),
                Il2CppInterop.Runtime.Il2CppType.Of<CanvasRenderer>(),
                Il2CppInterop.Runtime.Il2CppType.Of<RawImage>(),
            });
            var rt = go.GetComponent<RectTransform>();

            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var raw = go.GetComponent<RawImage>();
            raw.texture = tex;
            raw.color = Color.white;
            return go;
        }

        static GameObject BuildQuad(Texture2D tex)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            var col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.Destroy(col);

            var r = go.GetComponent<Renderer>();
            if (r != null)
            {

                var sh = Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Texture")
                      ?? Shader.Find("HDRP/Lit");
                if (sh != null)
                {
                    var m = new Material(sh);
                    m.mainTexture = tex;
                    try { m.SetTexture("_UnlitColorMap", tex); } catch { }
                    try { m.SetTexture("_BaseColorMap", tex); } catch { }
                    r.material = m;
                }
            }
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            return go;
        }
    }
}
