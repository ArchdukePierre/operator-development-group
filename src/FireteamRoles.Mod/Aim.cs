// ODG Operations - FireteamRoles
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using UnityEngine;

namespace FireteamRoles
{

    internal static class Aim
    {
        internal static bool GiveOpen;
        internal static Peer GiveTarget;

        static Camera Cam()
        {
            try
            {
                var c = Camera.main;
                if (c != null) return c;
            }
            catch { }
            return null;
        }

        internal static Peer Look(float range, bool downedOnly)
        {
            var cam = Cam();
            var me = State.Me;
            Vector3 origin, forward;

            if (cam != null) { origin = cam.transform.position; forward = cam.transform.forward; }
            else if (me != null && me.Root != null) { origin = me.Position + Vector3.up * 1.5f; forward = me.Root.forward; }
            else return null;

            Peer best = null;
            float bestScore = -1f;

            foreach (var kv in State.Peers)
            {
                var p = kv.Value;
                if (p == null || p.Id == Net.Self || !p.Live) continue;
                if (downedOnly && !p.Downed) continue;

                Vector3 to = (p.Position + Vector3.up * 0.4f) - origin;
                float dist = to.magnitude;
                if (dist > range) continue;

                float dot = Vector3.Dot(forward, to.normalized);
                if (dot < 0.72f) continue;

                float score = dot - dist * 0.05f;
                if (score > bestScore) { bestScore = score; best = p; }
            }
            return best;
        }

        internal static string PromptText()
        {
            if (Treat.Open) return "";
            var me = State.Me;
            if (me != null && me.Downed) return "";

            string ladder = Ladder.PromptText();
            if (!string.IsNullOrEmpty(ladder)) return ladder;

            if (Drag.Dragging)
                return "[" + Cfg.GrabKey.Value + "] release   -   dragging " + State.NameOf(Drag.Victim);

            var t = Look(Mathf.Max(Cfg.GrabRangeMeters.Value, Cfg.TreatRangeMeters.Value), true);
            if (t == null) return "";

            if (t.Dragger != 0UL && t.Dragger != Net.Self)
                return State.NameOf(t.Dragger) + " is dragging " + t.Name;

            string s = "[" + Cfg.GrabKey.Value + "] grab";
            if (Vector3.Distance(me != null ? me.Position : Vector3.zero, t.Position) <= Cfg.TreatRangeMeters.Value)
                s += "     [" + Cfg.TreatKey.Value + "] treat";
            return s + "     " + t.Name + "  " + Mathf.CeilToInt(t.Remaining) + "s";
        }

        internal static void Update()
        {
            if (!State.Enabled) return;

            var panelKey = Cfg.Key(Cfg.RolePanelKey, KeyCode.F6);
            if (Input.GetKeyDown(panelKey)) SetPicker(!Ui.PickerOpen);

            if (Ui.PickerOpen && Time.realtimeSinceStartup > _pickerIdleUntil)
            {
                SetPicker(false);
                Ui.Toast("role picker closed");
            }

            if (Input.GetKeyDown(Cfg.Key(Cfg.DumpKey, KeyCode.F7))) Err.Guard("diag.dump", Diag.Dump);
            if (Input.GetKeyDown(Cfg.Key(Cfg.BarCollapseKey, KeyCode.F8))) Err.Guard("bar.toggle", Bar.Toggle);

            var me = State.Me;
            bool amDown = me != null && me.Downed;

            if (GiveOpen && (amDown || Treat.Open || !Input.GetKey(Cfg.Key(Cfg.GiveKey, KeyCode.H))))
                CloseGive(!amDown && !Treat.Open);

            if (Treat.Open) { TreatKeys(); return; }
            if (Ui.PickerOpen) PickerKeys();
            if (amDown) return;

            GrabKeys();
            GiveKeys();
        }

        internal const float PickerIdleSeconds = 20f;
        static float _pickerIdleUntil;

        internal static void SetPicker(bool open)
        {
            Ui.PickerOpen = open;
            _pickerIdleUntil = Time.realtimeSinceStartup + PickerIdleSeconds;
        }

        static void PickerKeys()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) { SetPicker(false); return; }
            if (State.RolesLocked) return;

            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) Choose(Role.Assaulter);
            else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) Choose(Role.Breacher);
            else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) Choose(Role.Sniper);
            else if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4)) Choose(Role.Medic);
            else if (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5)) Choose(Role.Jtac);
            else if (Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.Keypad0)) Choose(Role.None);
        }

        internal static void PickRole(Role r) { Choose(r); }

        static void Choose(Role r)
        {
            State.MyRole = r;
            SetPicker(false);
            Ui.Toast("role: " + RoleInfo.Name(r));
        }

        static void GrabKeys()
        {
            var grab = Cfg.Key(Cfg.GrabKey, KeyCode.F);
            var treat = Cfg.Key(Cfg.TreatKey, KeyCode.E);

            if (Input.GetKeyDown(grab))
            {
                if (Drag.Dragging) Drag.Stop("released");
                else
                {
                    var t = Look(Cfg.GrabRangeMeters.Value, true);
                    if (t != null) Drag.Toggle(t);
                }
            }

            if (Input.GetKeyDown(treat) && !Drag.Dragging)
            {
                var t = Look(Cfg.TreatRangeMeters.Value, true);
                if (t != null) Treat.OpenOn(t);
            }
        }

        internal static void CloseGive(bool deliver)
        {
            var t = GiveTarget;
            GiveOpen = false;
            GiveTarget = null;
            if (!deliver || t == null) return;
            if (Ui.ItemIndex >= Medical.AllTypes.Length) Give.Send(t, Il2Cpp.InventoryItem.ItemType.Empty, true);
            else Give.Send(t, Medical.AllTypes[Mathf.Clamp(Ui.ItemIndex, 0, Medical.AllTypes.Length - 1)], false);
        }

        static void GiveKeys()
        {
            var give = Cfg.Key(Cfg.GiveKey, KeyCode.H);

            if (Input.GetKey(give) && State.MyRole == Role.Medic)
            {
                var t = Look(Cfg.GiveRangeMeters.Value, false);
                if (t != null && Give.CanGive(t))
                {
                    if (!GiveOpen) { GiveOpen = true; Ui.ItemIndex = 0; }
                    GiveTarget = t;
                    Wheel(Medical.AllTypes.Length + 1);
                    return;
                }
            }

            if (GiveOpen && !Input.GetKey(give)) CloseGive(true);
        }

        static void TreatKeys()
        {
            var treat = Cfg.Key(Cfg.TreatKey, KeyCode.E);
            if (Input.GetKeyDown(treat) || Input.GetKeyDown(KeyCode.Escape)) { Treat.Close(); return; }

            for (int i = 0; i < 7; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) Ui.LimbIndex = i;

            Wheel(Medical.AllTypes.Length + 1);

            if (Input.GetKeyDown(KeyCode.Space))
            {
                int idx = Mathf.Clamp(Ui.ItemIndex, 0, Medical.AllTypes.Length);
                var limb = Wounds.Parts[Mathf.Clamp(Ui.LimbIndex, 0, Wounds.Parts.Length - 1)];
                if (Treat.CanApply(idx)) Treat.ApplyIndex(idx, limb);
                else Treat.LastResult = "none left";
            }

            if (Input.GetKey(KeyCode.R)) Treat.HoldRevive(Time.deltaTime);
            else Treat.ReleaseRevive();
        }

        static void Wheel(int count)
        {
            float w = 0f;
            try { w = Input.mouseScrollDelta.y; } catch { }
            if (Mathf.Abs(w) < 0.01f) return;
            Ui.ItemIndex += w > 0f ? 1 : -1;
            if (Ui.ItemIndex < 0) Ui.ItemIndex = count - 1;
            if (Ui.ItemIndex >= count) Ui.ItemIndex = 0;
        }
    }
}
