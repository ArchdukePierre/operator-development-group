// ODG Operations - OperatorCompass
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

[assembly: MelonInfo(typeof(OperatorCompass.OperatorCompassMod), "Operator Compass", "1.1.0", "Logan")]
[assembly: MelonGame(null, null)]

namespace OperatorCompass
{

    public class OperatorCompassMod : MelonMod
    {

        private const int Rev = 1;

        private MelonPreferences_Category _cat;
        private MelonPreferences_Entry<int> _revision;
        private MelonPreferences_Entry<bool> _visibleCfg;
        private MelonPreferences_Entry<string> _toggleKeyCfg;
        private MelonPreferences_Entry<float> _topOffset;
        private MelonPreferences_Entry<float> _scale;
        private MelonPreferences_Entry<bool> _showHeadingNumber;
        private MelonPreferences_Entry<bool> _hideWhenCursorFree;

        private KeyCode _toggleKey = KeyCode.V;
        private bool _visible = true;

        private CompassController _compass;
        private Canvas _ownCanvas;
        private Transform _playerTransform;

        private float _nextBindAttempt;
        private const float BindRetrySeconds = 0.25f;

        private string _boundCameraName;

        public override void OnInitializeMelon()
        {
            _cat = MelonPreferences.CreateCategory("OperatorCompass");

            _revision = _cat.CreateEntry("Revision", 0,
                description: "config revision, used to apply one-time migrations. Do not edit");
            _visibleCfg = _cat.CreateEntry("Visible", true,
                description: "Whether the compass is showing. The toggle key flips this.");
            _toggleKeyCfg = _cat.CreateEntry("ToggleKey", "V",
                description: "Key that shows/hides the compass. Unity KeyCode name.");
            _topOffset = _cat.CreateEntry("TopOffset", 18f,
                description: "Pixels down from the top edge (1080p reference).");
            _scale = _cat.CreateEntry("Scale", 1f,
                description: "Overall size multiplier for the tape.");
            _showHeadingNumber = _cat.CreateEntry("ShowHeadingNumber", true,
                description: "Draw the numeric heading (e.g. 057) under the centre caret.");
            _hideWhenCursorFree = _cat.CreateEntry("HideWhenCursorFree", false,
                description: "Hide the compass whenever the cursor is unlocked (menus, the laptop). "
                           + "Off by default because it depends on the game locking the cursor in play.");

            Migrate();

            _toggleKey = ParseKey(_toggleKeyCfg.Value, KeyCode.V);
            _visible = _visibleCfg.Value;

            LoggerInstance.Msg("Operator Compass ready at the top of the screen. "
                             + _toggleKey + " toggles it. Showing: " + _visible + ".");
        }

        private void Migrate()
        {
            if (_revision.Value >= Rev) return;

            if (_revision.Value < 1)
            {

                if (string.Equals((_toggleKeyCfg.Value ?? "").Trim(), "F9", StringComparison.OrdinalIgnoreCase))
                {
                    _toggleKeyCfg.Value = "V";
                    LoggerInstance.Msg("Compass moved off F9 onto V.");
                }

                _visibleCfg.Value = true;
            }

            _revision.Value = Rev;
            try { MelonPreferences.Save(); } catch { }
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {

            _playerTransform = null;
            _boundCameraName = null;
            _nextBindAttempt = 0f;
        }

        public override void OnUpdate()
        {
            try { Tick(); }
            catch (Exception e) { LoggerInstance.Warning("compass tick: " + e.Message); }
        }

        private void Tick()
        {
            if (_toggleKey != KeyCode.None && Input.GetKeyDown(_toggleKey))
                Toggle();

            if (!_visible)
            {
                if (_compass != null) _compass.SetVisible(false);
                return;
            }

            if (_hideWhenCursorFree.Value && Cursor.lockState != CursorLockMode.Locked)
            {
                if (_compass != null) _compass.SetVisible(false);
                return;
            }

            if (!BindPlayer())
            {

                if (_compass != null) _compass.SetVisible(false);
                return;
            }

            if (_compass == null)
            {
                _compass = new CompassController(EnsureCanvas(), _playerTransform);
                LoggerInstance.Msg("Compass built, tracking camera '" + (_boundCameraName ?? "?") + "'.");
            }

            _compass.Apply(_topOffset.Value, _scale.Value, _showHeadingNumber.Value);
            _compass.SetVisible(true);
            _compass.Tick();
        }

        private void Toggle()
        {
            _visible = !_visible;
            _visibleCfg.Value = _visible;
            try { MelonPreferences.Save(); } catch { }

            LoggerInstance.Msg("Compass " + (_visible ? "on" : "off") + ".");
        }

        private bool BindPlayer()
        {
            if (_playerTransform != null) return true;

            if (Time.unscaledTime < _nextBindAttempt) return false;
            _nextBindAttempt = Time.unscaledTime + BindRetrySeconds;

            var cam = FindHeadingCamera();
            if (cam == null) return false;

            _playerTransform = cam.transform;
            _boundCameraName = cam.name;

            if (_compass != null) _compass.SetPlayer(_playerTransform);
            return true;
        }

        private static Camera FindHeadingCamera()
        {
            try
            {
                var main = Camera.main;
                if (main != null && main.isActiveAndEnabled) return main;
            }
            catch { }

            try
            {
                var all = Camera.allCameras;
                if (all != null)
                    for (int i = 0; i < all.Length; i++)
                        if (all[i] != null && all[i].isActiveAndEnabled) return all[i];
            }
            catch { }

            return null;
        }

        private Transform EnsureCanvas()
        {
            if (_ownCanvas != null) return _ownCanvas.transform;

            var canvasObj = new GameObject("OperatorCompass_Canvas");
            GameObject.DontDestroyOnLoad(canvasObj);

            _ownCanvas = canvasObj.AddComponent<Canvas>();
            _ownCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _ownCanvas.sortingOrder = 400;

            var scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            return _ownCanvas.transform;
        }

        private static KeyCode ParseKey(string s, KeyCode fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            try { KeyCode k; if (Enum.TryParse<KeyCode>(s.Trim(), true, out k)) return k; } catch { }
            return fallback;
        }
    }
}
