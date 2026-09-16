// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace MapLoader
{

    public static class LoadVeil
    {
        static MelonLogger.Instance _log;
        public static void Init(MelonLogger.Instance log) { _log = log; }

        static GameObject _go;
        static Image _fill;
        static Text _label;
        static float _shownAt;

        static float _target;
        static float _shown;
        static string _phase = "";

        static bool _buildDone;

        public static bool Showing => _go != null;

        public static void Show(string why)
        {
            if (_go != null) return;
            try
            {
                _target = 0f; _shown = 0f; _phase = "preparing"; _buildDone = false;

                _go = new GameObject("MAP_LOAD_VEIL");
                UnityEngine.Object.DontDestroyOnLoad(_go);

                var canvas = _go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;

                canvas.sortingOrder = 32760;

                var scaler = _go.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);

                Panel(_go.transform, Color.black, Vector2.zero, Vector2.one,
                      Vector2.zero, Vector2.zero);

                var trackGo = Panel(_go.transform, new Color(1f, 1f, 1f, 0.10f),
                                    new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                                    new Vector2(-380f, 120f), new Vector2(380f, 126f));

                var fillGo = new GameObject("fill");
                fillGo.transform.SetParent(trackGo.transform, false);
                _fill = fillGo.AddComponent<Image>();
                _fill.color = new Color(0.78f, 0.82f, 0.90f, 0.95f);
                _fill.raycastTarget = false;
                var fr = _fill.rectTransform;
                fr.anchorMin = new Vector2(0f, 0f);
                fr.anchorMax = new Vector2(0f, 1f);
                fr.offsetMin = Vector2.zero;
                fr.offsetMax = Vector2.zero;
                fr.pivot = new Vector2(0f, 0.5f);

                _label = MakeLabel(_go.transform);

                _shownAt = Time.realtimeSinceStartup;
                _log?.Msg("[veil] screen covered (" + why + "). Nothing of the build "
                        + "will be visible, and it stays down until the player is "
                        + "standing in the map, not merely until the build reports done.");
            }
            catch (Exception e)
            {
                _log?.Warning("[veil] Show: " + e.Message);
                _go = null;
            }
        }

        static GameObject Panel(Transform parent, Color c, Vector2 aMin, Vector2 aMax,
                                Vector2 oMin, Vector2 oMax)
        {
            var go = new GameObject("panel");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.offsetMin = oMin; rt.offsetMax = oMax;
            return go;
        }

        static Text MakeLabel(Transform parent)
        {
            try
            {
                var go = new GameObject("label");
                go.transform.SetParent(parent, false);
                var t = go.AddComponent<Text>();

                try { t.font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
                if (t.font == null)
                { try { t.font = Font.CreateDynamicFontFromOSFont("Arial", 22); } catch { } }
                t.fontSize = 22;
                t.alignment = TextAnchor.MiddleCenter;
                t.color = new Color(0.75f, 0.79f, 0.86f, 0.9f);
                t.raycastTarget = false;
                var rt = t.rectTransform;
                rt.anchorMin = new Vector2(0.5f, 0f);
                rt.anchorMax = new Vector2(0.5f, 0f);
                rt.offsetMin = new Vector2(-420f, 70f);
                rt.offsetMax = new Vector2(420f, 108f);
                return t;
            }
            catch (Exception e) { _log?.Warning("[veil] label: " + e.Message); return null; }
        }

        public static void Progress(float t, string phase)
        {
            if (_go == null) return;
            if (t > _target) _target = Mathf.Clamp01(t);
            if (!string.IsNullOrEmpty(phase)) _phase = phase;
        }

        public static void BuildFinished()
        {
            if (_go == null) return;
            _buildDone = true;
            _buildDoneAt = Time.realtimeSinceStartup;
            _lastWaitLog = 0f;
            Progress(0.97f, "Deploying to Mission");
        }

        static float _buildDoneAt;
        static float _lastWaitLog;

        public static void Tick()
        {
            if (_go == null) return;
            try
            {

                _shown = Mathf.Min(_target, _shown + Mathf.Max(0.0015f,
                                   (_target - _shown) * Time.unscaledDeltaTime * 3f));

                if (_fill != null)
                {
                    var fr = _fill.rectTransform;
                    fr.anchorMax = new Vector2(Mathf.Clamp01(_shown), 1f);
                }
                if (_label != null)
                    _label.text = _phase + "   " + Mathf.RoundToInt(_shown * 100f) + "%";

                bool arrived = _buildDone
                            && PlayerBody.HasMovableRig
                            && MapRuntime.PlayerIsOnOurFloor();
                if (arrived)
                {

                    Vector3 bp;
                    string offWhy;
                    if (PlayerBody.TryPosition(out bp) && SpawnTruth.IsOutOfMap(bp, out offWhy))
                        arrived = false;
                }

                bool heliRiding = BlackHawk.Riding;
                bool heliHold = !heliRiding && BlackHawk.HoldsVeil(_buildDone, _buildDone ? Time.realtimeSinceStartup - _buildDoneAt : 0f);
                if (heliRiding)
                {
                    Progress(1f, "airborne");
                    if (_shown > 0.995f && BlackHawk.RidingFor > 1.0f) Hide("aboard the Black Hawk");
                }
                else if (heliHold)
                {
                    Progress(Mathf.Max(_target, 0.97f), "boarding");
                }
                else if (arrived)
                {
                    Progress(1f, "ready");

                    if (_shown > 0.995f) Hide("player is standing on our floor");
                }
                else if (_buildDone)
                {

                    float wait = Time.realtimeSinceStartup - _buildDoneAt;
                    if (wait - _lastWaitLog >= 8f)
                    {
                        _lastWaitLog = wait;
                        Vector3 dp; string dwhy = null;
                        bool outside = PlayerBody.TryPosition(out dp)
                                    && SpawnTruth.IsOutOfMap(dp, out dwhy);
                        _log?.Msg("[veil] still down " + wait.ToString("F0")
                                + " s after build: rig=" + PlayerBody.LastVia
                                + (PlayerBody.HasMovableRig ? "" : " (NOT MOVABLE -- the "
                                   + "avatar has not spawned yet; this is the wait, not a stall)")
                                + ", floorTest="
                                + MapRuntime.PlayerIsOnOurFloor()
                                + (outside ? (", OUT of map: " + dwhy) : ", inside the map")
                                + "; " + MapRuntime.FloorBlockerReport());
                    }
                    if (wait > 25f)
                    {

                        Vector3 bp2; string why2;
                        bool inside = PlayerBody.HasMovableRig
                                   && PlayerBody.TryPosition(out bp2)
                                   && !SpawnTruth.IsOutOfMap(bp2, out why2);
                        if (inside)
                        {
                            Progress(1f, "ready");
                            if (_shown > 0.995f)
                                Hide("OVERRIDE after " + wait.ToString("F0")
                                   + " s: build done and the player is inside the "
                                   + "map, but the floor test never passed -- "
                                   + MapRuntime.FloorBlockerReport());
                        }
                    }
                }

                if (Time.realtimeSinceStartup - _shownAt > 120f)
                {

                    Vector3 bp; string offWhy = "unknown";
                    string wh = PlayerBody.TryPosition(out bp)
                              ? ("player body at (" + bp.x.ToString("F1") + ", " + bp.y.ToString("F1")
                                 + ", " + bp.z.ToString("F1") + "), "
                                 + (SpawnTruth.IsOutOfMap(bp, out offWhy) ? offWhy : "inside the level"))
                              : "the player body could not be read at all";
                    Hide("SAFETY TIMEOUT -- never confirmed arrival; " + wh);
                }
            }
            catch (Exception e) { _log?.Warning("[veil] Tick: " + e.Message); }
        }

        public static void Hide(string why)
        {
            if (_go == null) return;
            try
            {
                float held = Time.realtimeSinceStartup - _shownAt;
                UnityEngine.Object.Destroy(_go);
                _log?.Msg("[veil] lifted after " + held.ToString("F1") + " s (" + why + ")");
            }
            catch (Exception e) { _log?.Warning("[veil] Hide: " + e.Message); }
            _go = null; _fill = null; _label = null; _buildDone = false;
        }

        public static void Forget() { _go = null; _fill = null; _label = null; _buildDone = false; }

        static GUIStyle _guiLabel;

        public static void DrawGUI()
        {
            if (_go == null) return;
            var prevColor = GUI.color;
            var prevDepth = GUI.depth;
            try
            {
                GUI.depth = -32000;
                float w = Screen.width, h = Screen.height;

                GUI.color = Color.black;
                GUI.DrawTexture(new Rect(0f, 0f, w, h), Texture2D.whiteTexture);

                float bw = Mathf.Min(760f, w * 0.6f), bh = 6f;
                float bx = (w - bw) * 0.5f, by = h - 120f;
                GUI.color = new Color(1f, 1f, 1f, 0.10f);
                GUI.DrawTexture(new Rect(bx, by, bw, bh), Texture2D.whiteTexture);
                GUI.color = new Color(0.78f, 0.82f, 0.90f, 0.95f);
                GUI.DrawTexture(new Rect(bx, by, bw * Mathf.Clamp01(_shown), bh),
                                Texture2D.whiteTexture);

                if (_guiLabel == null)
                {

                    var st = new GUIStyle(GUI.skin.label);
                    st.alignment = TextAnchor.MiddleCenter;
                    st.fontSize = 20;
                    _guiLabel = st;
                }
                GUI.color = new Color(0.75f, 0.79f, 0.86f, 0.9f);
                GUI.Label(new Rect(bx - 100f, by - 46f, bw + 200f, 36f),
                          _phase + "   " + Mathf.RoundToInt(_shown * 100f) + "%",
                          _guiLabel);
            }
            catch { }
            finally
            {
                GUI.color = prevColor;
                GUI.depth = prevDepth;
            }
        }
    }
}
