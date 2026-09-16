// ODG Operations - LoadoutPresets
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HarmonyLib;
using MelonLoader;
using Il2CppInterop.Runtime;
using UnityEngine;
using Il2CppList = Il2CppSystem.Collections.Generic.List<Il2Cpp.CharacterModParent>;

[assembly: MelonInfo(typeof(LoadoutPresets.LoadoutPresetsMod), "Loadout Presets", "1.11.1", "ArchdukePierre")]
[assembly: MelonGame(null, null)]

namespace LoadoutPresets
{

    public class LoadoutPresetsMod : MelonMod
    {

        const int SLOTS = 20;

        MelonPreferences_Category _cat;
        MelonPreferences_Entry<bool> _enabled;
        MelonPreferences_Entry<string> _skin;
        MelonPreferences_Entry<float> _panelX, _panelY, _uiScale;
        readonly MelonPreferences_Entry<string>[] _data  = new MelonPreferences_Entry<string>[SLOTS];
        readonly MelonPreferences_Entry<string>[] _label = new MelonPreferences_Entry<string>[SLOTS];

        static bool _open;
        internal static Il2Cpp.CharacterCustomisationController _ctrl;
        internal static Il2Cpp.CharacterCustomisation _cust;
        MelonPreferences_Entry<bool> _instrument;
        string _toast = ""; float _toastT = -9f;
        int _changed, _missing, _captured;

        GUIStyle _stTitle, _stHint, _stWarn, _stNum, _stField, _stBtn, _stBtnAcc, _stBtnDanger, _stBtnDangerOn, _stFoot, _stToast;
        Texture2D _texPanel, _texEdge, _texBar, _texRow, _texAcc;
        bool _stReady;

        static string GameRoot { get { try { return MelonLoader.Utils.MelonEnvironment.GameRootDirectory; } catch { return AppContext.BaseDirectory; } } }
        static string LogPath => Path.Combine(GameRoot, "Mods", "LoadoutPresets.log");

        struct Entry { public string mod; public int mat; }

        public override void OnInitializeMelon()
        {
            _cat = MelonPreferences.CreateCategory("LoadoutPresets");
            _enabled = _cat.CreateEntry("Enabled", true);

            _skin = _cat.CreateEntry("Skin", "Native");

            _panelX  = _cat.CreateEntry("PanelX", 40f);
            _panelY  = _cat.CreateEntry("PanelY", 0f);
            _uiScale = _cat.CreateEntry("UiScale", 0f);

            var oldLeft  = _cat.CreateEntry("PanelLeft", 40f);
            var oldScale = _cat.CreateEntry("PanelScale", 1f);
            if (Math.Abs(oldLeft.Value - 40f) > 0.01f && Math.Abs(_panelX.Value - 40f) < 0.01f)
            { _panelX.Value = oldLeft.Value; Log("carried PanelLeft " + oldLeft.Value + " over to PanelX"); }
            if (oldScale.Value > 0f && Math.Abs(oldScale.Value - 1f) > 0.01f && _uiScale.Value <= 0f)
            { _uiScale.Value = oldScale.Value; Log("carried PanelScale " + oldScale.Value + " over to UiScale"); }

            for (int i = 0; i < SLOTS; i++)
            {
                _data[i]  = _cat.CreateEntry("Slot" + (i + 1) + "_data",  "");
                _label[i] = _cat.CreateEntry("Slot" + (i + 1) + "_label", "");
            }

            try { File.AppendAllText(LogPath, "\n\n===== Loadout Presets v1.11  " + DateTime.Now + " =====\n"); } catch { }
            Log("loaded. 20-slot full-kit saver v1.11 (garments + attachments + camo; delete/clear-all; native uGUI panel, 10-row scroll window, draggable + resizable, cabinet palette; IMGUI fallback).");

            Hook(typeof(Il2Cpp.CharacterCustomisationController), "StartCustomisation", nameof(Post_StartCustomisation));
            Hook(typeof(Il2Cpp.CharacterCustomisationController), "button_StopCustomisation", nameof(Post_StopCustomisation));

            _instrument = _cat.CreateEntry("InstrumentV2", false);
            var legacy = _cat.CreateEntry("Instrument", false);
            if (legacy.Value)
            {
                legacy.Value = false;
                Log("retired the old Instrument flag (it was on). Equip-flow tracing is now "
                  + "InstrumentV2 and ships OFF: it wrote 40-50 unbuffered file appends per "
                  + "character dress and was the cause of multi-second menu stalls.");
            }

            if (_instrument.Value)
            {
                Log("equip-flow instrumentation ON (InstrumentV2). Expect multi-second menu "
                  + "stalls while dressing; this is a tracing mode, not a play mode.");
                try { Instr.Install(HarmonyInstance); }
                catch (Exception e) { Log("instr install err: " + e.Message); }
            }
        }

        void Hook(Type t, string method, string handler)
        {
            try
            {
                var mi = AccessTools.Method(t, method);
                if (mi == null) { Log("HOOK MISS " + t.Name + "." + method); return; }
                HarmonyInstance.Patch(mi, postfix: new HarmonyMethod(typeof(LoadoutPresetsMod), handler));
                Log("hooked " + t.Name + "." + method);
            }
            catch (Exception e) { Log("HOOK FAIL " + t.Name + "." + method + ": " + e.Message); }
        }

        static void Post_StartCustomisation(Il2Cpp.CharacterCustomisationController __instance)
        {
            try
            {
                _ctrl = __instance;
                _open = true;
                _cust = ResolveLocalCust(__instance);
                Log("cabinet opened - CharacterCustomisation " + (_cust != null ? "resolved (" + ParentCount(_cust) + " top sockets)" : "NOT FOUND"));
            }
            catch (Exception e) { Log("StartCustomisation post err: " + e.Message); }
        }

        static void Post_StopCustomisation()
        {
            _open = false; _ctrl = null; _cust = null;
            try { NativeUI.Teardown(); } catch { }
            try { MelonPreferences.Save(); } catch { }
        }

        static int ParentCount(Il2Cpp.CharacterCustomisation c)
        { try { var p = c.modParents; return p != null ? p.Count : 0; } catch { return 0; } }

        static Il2Cpp.CharacterCustomisation ResolveLocalCust(Il2Cpp.CharacterCustomisationController ctrl)
        {
            Il2Cpp.PlayerNetworking pn = null;
            try { pn = ctrl != null ? ctrl.Player : null; } catch { }
            try
            {
                if (pn != null)
                {
                    var c = pn.GetComponentInChildren<Il2Cpp.CharacterCustomisation>(true);
                    if (c != null) return c;
                }
            }
            catch { }
            try
            {
                var all = Resources.FindObjectsOfTypeAll(Il2CppType.Of<Il2Cpp.CharacterCustomisation>());
                Il2Cpp.CharacterCustomisation first = null;
                for (int i = 0; i < all.Length; i++)
                {
                    var c = all[i].TryCast<Il2Cpp.CharacterCustomisation>();
                    if (c == null) continue;
                    if (first == null) first = c;
                    try { if (pn != null && c.playerNetworking != null && c.playerNetworking.Pointer == pn.Pointer) return c; } catch { }
                }
                return first;
            }
            catch { }
            return null;
        }

        void SaveSlot(int i)
        {
            try
            {
                if (_cust == null) { Toast("open cabinet first"); return; }
                var parents = _cust.modParents;
                if (parents == null) { Toast("no sockets"); return; }
                var sb = new StringBuilder();
                _captured = 0;

                for (int s = 0; s < TOP_SLOTS.Length; s++)
                {
                    var cur = TopMod(s);
                    string mod = ""; int mat = -1;
                    if (cur != null)
                    {
                        try { var info = cur.ModInfo; if (info != null) mod = info.ModName ?? ""; } catch { }
                        try { mat = cur.materialIndex; } catch { }
                        if (mod.Length > 0) _captured++;
                    }
                    sb.Append("T:").Append(TOP_SLOTS[s]).Append('\t').Append(mod.Replace("\t", "").Replace("\n", "")).Append('\t').Append(mat).Append('\n');
                    Log("  [save] T:" + TOP_SLOTS[s] + " -> '" + mod + "' mat=" + mat);
                }

                foreach (var kv in ChildSockets())
                {
                    var cur = kv.Value.currentMod;
                    string mod = ""; int mat = -1;
                    if (cur != null)
                    {
                        try { var info = cur.ModInfo; if (info != null) mod = info.ModName ?? ""; } catch { }
                        try { mat = cur.materialIndex; } catch { }
                        if (mod.Length > 0) _captured++;
                    }
                    sb.Append(kv.Key).Append('\t').Append(mod.Replace("\t", "").Replace("\n", "")).Append('\t').Append(mat).Append('\n');
                    Log("  [save] " + kv.Key + " -> '" + mod + "' mat=" + mat);
                }

                _data[i].Value = B64(sb.ToString());
                if (string.IsNullOrEmpty(_label[i].Value)) _label[i].Value = "Preset " + (i + 1);
                try { MelonPreferences.Save(); } catch { }
                Toast("Saved (" + _captured + " items)");
                Log("SAVE slot " + (i + 1) + ": " + _captured + " items (" + TOP_SLOTS.Length + " garment slots + " + parents.Count + " child sockets)");
            }
            catch (Exception e) { Log("SaveSlot err: " + e.Message); Toast("save failed"); }
        }

        void LoadSlot(int i)
        {
            try
            {
                if (_cust == null) { Toast("open cabinet first"); return; }
                var map = Parse(UnB64(_data[i].Value));
                if (map.Count == 0) { Toast("slot empty"); return; }
                var lm = Il2Cpp.LoadoutManager.singleton;
                if (lm == null) { Toast("no loadout mgr"); return; }

                _changed = 0; _missing = 0;

                for (int s = 0; s < TOP_SLOTS.Length; s++) ApplyTop(s, map, lm);

                foreach (var kv in ChildSockets()) ApplyChild(kv.Key, kv.Value, map, lm);

                Toast(_missing > 0 ? ("Loaded (" + _changed + ", " + _missing + " missing)") : ("Loaded (" + _changed + " changes)"));
                Log("LOAD slot " + (i + 1) + ": " + _changed + " changed, " + _missing + " missing");
            }
            catch (Exception e) { Log("LoadSlot err: " + e.Message); Toast("load failed"); }
        }

        static readonly string[] TOP_SLOTS =
            { "HeadGear", "EyeWear", "EarPro", "Face", "Voice", "Shirt", "Gloves",
              "Wrist", "Pants", "Shoes", "Vest", "Belt", "Backpack", "Tattoo" };

        Il2Cpp.CharacterMod TopMod(int i)
        {
            try
            {
                switch (i)
                {
                    case 0:  return _cust.HeadGear;
                    case 1:  return _cust.EyeWear;
                    case 2:  return _cust.EarPro;
                    case 3:  return _cust.Face;
                    case 4:  return _cust.Voice;
                    case 5:  return _cust.Shirt;
                    case 6:  return _cust.Gloves;
                    case 7:  return _cust.Wrist;
                    case 8:  return _cust.Pants;
                    case 9:  return _cust.Shoes;
                    case 10: return _cust.Vest;
                    case 11: return _cust.Belt;
                    case 12: return _cust.Backpack;
                    case 13: return _cust.Tattoo;
                }
            }
            catch { }
            return null;
        }

        List<KeyValuePair<string, Il2Cpp.CharacterModParent>> ChildSockets()
        {
            var outl = new List<KeyValuePair<string, Il2Cpp.CharacterModParent>>();
            var used = new HashSet<string>();
            try
            {
                var ps = _cust.modParents; if (ps == null) return outl;
                int n = ps.Count;
                for (int k = 0; k < n; k++)
                {
                    var p = ps[k]; if (p == null) continue;
                    string owner = "";
                    try { var pm = p.parentMod; if (pm != null) { var mi = pm.ModInfo; owner = mi != null ? (mi.ModName ?? "") : ""; } } catch { }
                    string key = "C:" + owner + ">" + NameKey(p);
                    if (!used.Add(key))
                    {
                        int d = 2; string k2;
                        do { k2 = key + "#" + d++; } while (!used.Add(k2) && d < 50);
                        key = k2;
                    }
                    outl.Add(new KeyValuePair<string, Il2Cpp.CharacterModParent>(key, p));
                }
            }
            catch { }
            return outl;
        }

        void ApplyTop(int s, Dictionary<string, Entry> map, Il2Cpp.LoadoutManager lm)
        {
            string key = "T:" + TOP_SLOTS[s];
            try
            {
                if (!map.TryGetValue(key, out Entry want)) return;
                var cur = TopMod(s);
                string have = "";
                try { if (cur != null) { var info = cur.ModInfo; if (info != null) have = info.ModName ?? ""; } } catch { }

                if (want.mod != have)
                {
                    if (want.mod.Length > 0)
                    {
                        Il2Cpp.CharacterMod prefab = null;
                        try { prefab = lm.GetCharacterMod(want.mod); } catch { }
                        if (prefab == null) { _missing++; Log("apply: item not in catalog: " + want.mod + " @ " + key); }
                        else
                        {
                            try { _cust.SpawnMod(prefab, null); _changed++; Log("  [load] " + key + " '" + have + "' -> '" + want.mod + "'"); }
                            catch (Exception e) { Log("spawn err " + want.mod + ": " + e.Message); }
                        }
                    }
                    else if (cur != null)
                    {
                        try { _cust.RemoveMod(cur.modSlot); _changed++; Log("  [load] " + key + " cleared"); } catch { }
                    }
                }

                var neu = TopMod(s);
                if (neu != null && want.mat >= 0)
                {
                    try { if (neu.materialIndex != want.mat) { neu.materialIndex = want.mat; neu.ActuallySetMaterial(); } }
                    catch (Exception e) { Log("mat err @ " + key + ": " + e.Message); }
                }
            }
            catch (Exception e) { Log("ApplyTop err @ " + key + ": " + e.Message); }
        }

        void ApplyChild(string key, Il2Cpp.CharacterModParent p, Dictionary<string, Entry> map, Il2Cpp.LoadoutManager lm)
        {
            try
            {
                if (!map.TryGetValue(key, out Entry want)) return;

                var cur = p.currentMod;
                string have = "";
                try { if (cur != null) { var info = cur.ModInfo; if (info != null) have = info.ModName ?? ""; } } catch { }

                if (want.mod != have)
                {
                    if (want.mod.Length > 0)
                    {
                        Il2Cpp.CharacterMod prefab = null;
                        try { prefab = lm.GetCharacterMod(want.mod); } catch { }
                        if (prefab == null) { _missing++; Log("apply: item not in catalog: " + want.mod + " @ " + key); }
                        else
                        {
                            try { if (cur != null) _cust.RemoveMod(cur.modSlot); } catch { }
                            try { _cust.SpawnMod(prefab, p); _changed++; Log("  [load] " + key + " -> '" + want.mod + "' (child)"); }
                            catch (Exception e) { Log("spawn err " + want.mod + ": " + e.Message); }
                        }
                    }
                    else if (cur != null)
                    {
                        try { _cust.RemoveMod(cur.modSlot); _changed++; } catch { }
                    }
                }

                var neu = p.currentMod;
                if (neu != null && want.mat >= 0)
                {
                    try { if (neu.materialIndex != want.mat) { neu.materialIndex = want.mat; neu.ActuallySetMaterial(); } }
                    catch (Exception e) { Log("mat err @ " + key + ": " + e.Message); }
                }
            }
            catch (Exception e) { Log("ApplyChild err: " + e.Message); }
        }

        static string NameKey(Il2Cpp.CharacterModParent p)
        {
            try { var n = p.ModParentName; return string.IsNullOrEmpty(n) ? "" : n.Replace("\t", "").Replace("\n", ""); }
            catch { return ""; }
        }

        bool Filled(int i) => _data[i].Value != null && _data[i].Value.Length > 0;

        void Toast(string s) { _toast = s; _toastT = Time.time; }

        internal int SlotCount => SLOTS;
        internal bool IsFilled(int i) => i >= 0 && i < SLOTS && Filled(i);
        internal string GetLabel(int i) { try { return _label[i].Value ?? ""; } catch { return ""; } }
        internal void SetLabel(int i, string v) { try { _label[i].Value = v ?? ""; } catch { } }
        internal void SavePrefs() { try { MelonPreferences.Save(); } catch { } }
        internal void DoSave(int i) { SaveSlot(i); SavePrefs(); }
        internal void DoLoad(int i) { LoadSlot(i); }
        internal void DoDelete(int i) { DeleteSlot(i); }
        internal void DoClearAll() { DeleteAll(); }
        internal string ToastText => _toast;
        internal float ToastTime => _toastT;
        internal float PanelX  { get { try { return _panelX.Value;  } catch { return 40f; } } }
        internal float PanelY  { get { try { return _panelY.Value;  } catch { return 0f;  } } }
        internal float UiScale { get { try { return _uiScale.Value; } catch { return 0f;  } } }

        internal void SavePlacement(float x, float y, float scale)
        {
            try { _panelX.Value = x; _panelY.Value = y; _uiScale.Value = scale; SavePrefs(); }
            catch (Exception e) { Log("SavePlacement: " + e.Message); }
        }

        static Dictionary<string, Entry> Parse(string txt)
        {
            var d = new Dictionary<string, Entry>();
            if (string.IsNullOrEmpty(txt)) return d;
            var lines = txt.Split('\n');
            foreach (var ln in lines)
            {
                if (ln.Length == 0) continue;
                var parts = ln.Split('\t');
                if (parts.Length < 3) continue;
                int mat = -1; int.TryParse(parts[2], out mat);
                d[parts[0]] = new Entry { mod = parts[1], mat = mat };
            }
            return d;
        }

        static readonly Color C_PANEL  = new Color(0.043f, 0.051f, 0.059f, 0.95f);
        static readonly Color C_EDGE   = new Color(0.29f, 0.33f, 0.36f, 1f);
        static readonly Color C_BAR    = new Color(0.075f, 0.086f, 0.098f, 1f);
        static readonly Color C_ROW    = new Color(0.086f, 0.098f, 0.110f, 0.55f);
        static readonly Color C_FIELD  = new Color(0.020f, 0.024f, 0.028f, 1f);
        static readonly Color C_BTN    = new Color(0.130f, 0.145f, 0.160f, 1f);
        static readonly Color C_BTNHOV = new Color(0.200f, 0.220f, 0.235f, 1f);
        static readonly Color C_ACC    = new Color(0.831f, 0.655f, 0.290f, 1f);
        static readonly Color C_ACCDIM = new Color(0.180f, 0.150f, 0.080f, 1f);
        static readonly Color C_DANGER = new Color(0.290f, 0.090f, 0.090f, 1f);
        static readonly Color C_DANGHV = new Color(0.470f, 0.130f, 0.130f, 1f);
        static readonly Color C_TEXT   = new Color(0.800f, 0.824f, 0.840f, 1f);
        static readonly Color C_DIM    = new Color(0.450f, 0.480f, 0.510f, 1f);

        int _confirmDel = -1;
        float _confirmT = -9f;

        bool ShouldShow()
        {
            if (!_open || _cust == null) return false;
            if (_enabled != null && !_enabled.Value) return false;
            try { if (_ctrl != null && !_ctrl.isLocalCustomising) return false; } catch { }
            return true;
        }

        bool WantNative
        {
            get { return _skin == null || !string.Equals(_skin.Value, "Classic", StringComparison.OrdinalIgnoreCase); }
        }

        public override void OnUpdate()
        {
            try
            {
                if (WantNative && !NativeUI.GaveUp) NativeUI.Tick(this, _ctrl, ShouldShow());
                else if (NativeUI.Active) NativeUI.Teardown();
            }
            catch (Exception e) { Log("OnUpdate: " + e.Message); }
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            try { NativeUI.Teardown(); } catch { }
        }

        public override void OnGUI()
        {
            if (!ShouldShow()) return;

            if (WantNative && NativeUI.Suppress)
            {
                var ev = Event.current;
                if (NativeUI.Editing && ev != null && ev.type == EventType.KeyDown)
                    if (NativeUI.FeedKey(this, ev)) ev.Use();
                return;
            }

            EnsureStyles();

            const float w = 306f, rowH = 30f, pad = 12f;
            float headH = 30f, subH = 16f, footH = 30f;
            float h = pad + headH + subH + 8f + SLOTS * rowH + 10f + footH + pad + 16f;

            float x = 20f;
            float y = Mathf.Clamp(Screen.height - h - 8f, 8f, 96f);
            bool armedExpired = Time.time - _confirmT > 3f;
            if (armedExpired) _confirmDel = -1;

            GUI.DrawTexture(new Rect(x - 1f, y - 1f, w + 2f, h + 2f), _texEdge);
            GUI.DrawTexture(new Rect(x, y, w, h), _texPanel);

            GUI.DrawTexture(new Rect(x, y, w, headH), _texBar);
            GUI.Label(new Rect(x + pad, y, w - pad * 2, headH), "L O A D O U T   P R E S E T S", _stTitle);
            GUI.DrawTexture(new Rect(x, y + headH - 1f, w, 1f), _texAcc);

            float cy = y + headH + 2f;
            GUI.Label(new Rect(x + pad, cy, w - pad * 2, subH),
                      _confirmDel != -1 ? "click again to confirm" : "name a slot  ·  SAVE kit  ·  LOAD to re-apply",
                      _confirmDel != -1 ? _stWarn : _stHint);
            cy += subH + 8f;

            for (int i = 0; i < SLOTS; i++)
            {
                float rx = x + pad;
                bool filled = Filled(i);
                var row = new Rect(x + 4f, cy - 2f, w - 8f, rowH - 2f);
                if (i % 2 == 0) GUI.DrawTexture(row, _texRow);
                if (filled) GUI.DrawTexture(new Rect(x + 4f, cy - 2f, 2f, rowH - 2f), _texAcc);

                GUI.Label(new Rect(rx, cy, 18f, 24f), (i + 1).ToString("00"), _stNum);

                string cur = _label[i].Value ?? "";
                GUI.SetNextControlName("slotname" + i);
                string edited = GUI.TextField(new Rect(rx + 20f, cy, 118f, 24f), cur, 22, _stField);
                if (edited != cur) _label[i].Value = edited;

                if (GUI.Button(new Rect(rx + 142f, cy, 44f, 24f), "SAVE", _stBtnAcc))
                { SaveSlot(i); _confirmDel = -1; try { MelonPreferences.Save(); } catch { } }

                GUI.enabled = filled;
                if (GUI.Button(new Rect(rx + 189f, cy, 44f, 24f), "LOAD", _stBtn)) { LoadSlot(i); _confirmDel = -1; }
                bool armed = _confirmDel == i;
                if (GUI.Button(new Rect(rx + 236f, cy, 26f, 24f), armed ? "!" : "X", armed ? _stBtnDangerOn : _stBtnDanger))
                {
                    if (armed) { DeleteSlot(i); _confirmDel = -1; }
                    else { _confirmDel = i; _confirmT = Time.time; }
                }
                GUI.enabled = true;

                cy += rowH;
            }

            cy += 8f;
            GUI.DrawTexture(new Rect(x + pad, cy - 4f, w - pad * 2, 1f), _texEdge);

            int used = 0; for (int i = 0; i < SLOTS; i++) if (Filled(i)) used++;
            GUI.Label(new Rect(x + pad, cy, 110f, footH), used + " / " + SLOTS + " saved", _stFoot);

            GUI.enabled = used > 0;
            bool allArmed = _confirmDel == -2;
            if (GUI.Button(new Rect(x + w - pad - 110f, cy + 3f, 110f, 24f),
                           allArmed ? "CONFIRM CLEAR" : "CLEAR ALL", allArmed ? _stBtnDangerOn : _stBtnDanger))
            {
                if (allArmed) { DeleteAll(); _confirmDel = -1; }
                else { _confirmDel = -2; _confirmT = Time.time; }
            }
            GUI.enabled = true;
            cy += footH;

            if (Time.time - _toastT < 3f)
                GUI.Label(new Rect(x, cy - 6f, w, 16f), _toast, _stToast);
        }

        void DeleteSlot(int i)
        {
            try
            {
                _data[i].Value = ""; _label[i].Value = "";
                try { MelonPreferences.Save(); } catch { }
                Toast("Slot " + (i + 1) + " deleted");
                Log("DELETE slot " + (i + 1));
            }
            catch (Exception e) { Log("DeleteSlot err: " + e.Message); }
        }

        void DeleteAll()
        {
            try
            {
                int n = 0;
                for (int i = 0; i < SLOTS; i++) { if (Filled(i)) n++; _data[i].Value = ""; _label[i].Value = ""; }
                try { MelonPreferences.Save(); } catch { }
                Toast("Cleared " + n + " preset" + (n == 1 ? "" : "s"));
                Log("DELETE ALL (" + n + " slots)");
            }
            catch (Exception e) { Log("DeleteAll err: " + e.Message); }
        }

        static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            t.SetPixel(0, 0, c);
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            t.wrapMode = TextureWrapMode.Clamp;
            return t;
        }

        void EnsureStyles()
        {
            if (_stReady) return;

            _texPanel = Solid(C_PANEL); _texEdge = Solid(C_EDGE); _texBar = Solid(C_BAR);
            _texRow = Solid(C_ROW); _texAcc = Solid(C_ACC);

            var texField = Solid(C_FIELD);
            var texBtn = Solid(C_BTN); var texBtnHov = Solid(C_BTNHOV);
            var texAccDim = Solid(C_ACCDIM);
            var texDanger = Solid(C_DANGER); var texDangerHov = Solid(C_DANGHV);

            _stTitle = new GUIStyle(GUI.skin.label)
            { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, fontSize = 12 };
            _stTitle.normal.textColor = C_ACC;

            _stHint = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 10 };
            _stHint.normal.textColor = C_DIM;

            _stWarn = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 10, fontStyle = FontStyle.Bold };
            _stWarn.normal.textColor = new Color(0.90f, 0.45f, 0.40f);

            _stNum = new GUIStyle(GUI.skin.label) { fontSize = 10, alignment = TextAnchor.MiddleLeft };
            _stNum.normal.textColor = C_DIM;

            _stField = new GUIStyle(GUI.skin.textField)
            { fontSize = 11, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(6, 4, 0, 0) };
            _stField.normal.background = texField; _stField.focused.background = texField;
            _stField.hover.background = texField;  _stField.active.background = texField;
            _stField.normal.textColor = C_TEXT;    _stField.focused.textColor = Color.white;
            _stField.hover.textColor = C_TEXT;     _stField.active.textColor = Color.white;

            _stBtn = new GUIStyle(GUI.skin.button)
            { fontSize = 10, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(0, 0, 0, 0) };
            _stBtn.normal.background = texBtn; _stBtn.hover.background = texBtnHov; _stBtn.active.background = texBtnHov;
            _stBtn.normal.textColor = C_TEXT;  _stBtn.hover.textColor = Color.white; _stBtn.active.textColor = Color.white;

            _stBtnAcc = new GUIStyle(_stBtn);
            _stBtnAcc.normal.background = texAccDim; _stBtnAcc.hover.background = texBtnHov; _stBtnAcc.active.background = texBtnHov;
            _stBtnAcc.normal.textColor = C_ACC;      _stBtnAcc.hover.textColor = Color.white;

            _stBtnDanger = new GUIStyle(_stBtn);
            _stBtnDanger.normal.background = texBtn; _stBtnDanger.hover.background = texDanger; _stBtnDanger.active.background = texDangerHov;
            _stBtnDanger.normal.textColor = new Color(0.62f, 0.42f, 0.42f); _stBtnDanger.hover.textColor = Color.white;

            _stBtnDangerOn = new GUIStyle(_stBtn);
            _stBtnDangerOn.normal.background = texDanger; _stBtnDangerOn.hover.background = texDangerHov; _stBtnDangerOn.active.background = texDangerHov;
            _stBtnDangerOn.normal.textColor = Color.white; _stBtnDangerOn.hover.textColor = Color.white;

            _stFoot = new GUIStyle(GUI.skin.label) { fontSize = 10, alignment = TextAnchor.MiddleLeft };
            _stFoot.normal.textColor = C_DIM;

            _stToast = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 10 };
            _stToast.normal.textColor = C_ACC;

            _stReady = true;
        }

        static string B64(string s)
        { try { return string.IsNullOrEmpty(s) ? "" : Convert.ToBase64String(Encoding.UTF8.GetBytes(s)); } catch { return ""; } }
        static string UnB64(string s)
        { try { return string.IsNullOrEmpty(s) ? "" : Encoding.UTF8.GetString(Convert.FromBase64String(s)); } catch { return ""; } }

        internal static void Log(string s)
        {
            try { File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s + "\n"); } catch { }
            try { MelonLogger.Msg("[LoadoutPresets] " + s); } catch { }
        }
    }
}
