// ODG Operations - M107
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using UnityEngine;

namespace M107
{

    internal static class Feel
    {

        const float ShotPitch = 0.60f;

        const float ShotMinDist = 4.0f;
        const float ShotMaxDistMul = 1.8f;
        const float ShotMaxDistCap = 1500f;

        const float MechMinDist = 2.5f;
        const float MechMaxDistMul = 1.4f;

        const float BoltTravel = 0.155f;
        const float BoltBack = 0.035f;
        const float BoltReturn = 0.075f;

        class Bolt
        {
            internal Transform T;
            internal Vector3 Home;
            internal float Started = -1f;
        }

        static readonly List<Bolt> _bolts = new List<Bolt>();
        static float _shotLogAt = -100f;
        static readonly HashSet<int> _setup = new HashSet<int>();

        internal static void Reset() { _bolts.Clear(); _setup.Clear(); _zeroSeen.Clear(); }

        internal static void SetupWeapon(GameObject weapon)
        {
            if (weapon == null) return;
            int id;
            try { id = weapon.GetInstanceID(); } catch { return; }
            if (!_setup.Add(id)) return;

            int shells = SwapBrass(weapon);
            int ball = TuneBallistics(weapon);
            int bolts = FindBolt(weapon);
            int audio = TuneAudio(weapon);
            int rail = Parts.MakeTopRail(weapon);

            M107Mod.Log("feel: " + weapon.name + " - " + shells + " round(s) re-cased to 12.7mm, " +
                        bolts + " bolt carrier(s) found, " + audio + " audio source(s) retuned, " +
                        rail + " dovetail(s) converted to a top rail, " +
                        ball + " round(s) given .50 ballistics");
        }

        const float GravityScale = 0.5f;
        const float MuzzleVelocity = 900f;

        internal static int TuneBallistics(GameObject weapon)
        {
            int n = 0;
            try
            {
                var rounds = weapon.GetComponentsInChildren<Il2Cpp.Bullet>(true);
                if (rounds == null) return 0;
                for (int i = 0; i < rounds.Length; i++)
                {
                    var b = rounds[i]; if (b == null) continue;
                    try { if (b.damage >= 9000) { n++; continue; } } catch { }

                    try { b.GRAVITY_MULTIPLIER = b.GRAVITY_MULTIPLIER * GravityScale; } catch { }
                    try { b.speed = Mathf.Max(b.speed, MuzzleVelocity); } catch { }
                    try { b.dragAmount = b.dragAmount * 0.6f; } catch { }
                    try { b.Mass = Mathf.Max(b.Mass, 2.0f); } catch { }
                    try { b.bulletLifeTime = Mathf.Max(b.bulletLifeTime, 12f); } catch { }

                    try { b.damage = 9999; } catch { }
                    try { b.armourPenetration = 100f; } catch { }
                    try { b.penetrationAmount = 25; } catch { }
                    try { b.armorDamage = 100f; } catch { }
                    try { b.VehicleDamage = 100f; } catch { }
                    try { b.doorMultiplier = 10f; } catch { }
                    try { b.bluntDamageMultiplier = 10f; } catch { }
                    try { b.armDamageMultipiler = 10f; } catch { }

                    try { b.GoreLevel = 100; } catch { }
                    try { b.FractureChance = 100; } catch { }
                    try { b.fragmentationChance = 1f; } catch { }
                    try { b.maxFragmentationMultiplier = 10f; } catch { }
                    try { b.bleedChance = 100; } catch { }
                    try { b.HeavyBleedChance = 100; } catch { }
                    try { b.bleedDamage = 25f; } catch { }

                    try { b.SuppressionAmount = 3f; } catch { }
                    try { b.lethalRange = Mathf.Max(b.lethalRange, 2500f); } catch { }
                    try { b.EnableTracer = true; } catch { }
                    n++;
                }
            }
            catch (Exception e) { M107Mod.Log("ballistics err: " + e.Message); }
            return n;
        }

        internal static int SwapBrass(GameObject weapon)
        {
            int n = 0;
            try
            {
                if (Parts.ShellMesh == null && Parts.BulletMesh == null) return 0;
                var rounds = weapon.GetComponentsInChildren<Il2Cpp.BulletShell>(true);
                if (rounds == null) return 0;
                for (int i = 0; i < rounds.Length; i++)
                {
                    var r = rounds[i]; if (r == null) continue;
                    try { if (Parts.ShellMesh != null) r.Mesh_Shell = Parts.ShellMesh; } catch { }
                    try { if (Parts.BulletMesh != null) r.Mesh_Bullet = Parts.BulletMesh; } catch { }

                    try { r.ejectSpeed = Mathf.Max(r.ejectSpeed, 3.2f); } catch { }
                    try { r.spinImpulse = Mathf.Max(r.spinImpulse, 1.4f); } catch { }
                    n++;
                }
            }
            catch (Exception e) { M107Mod.Log("feel: brass swap err: " + e.Message); }
            return n;
        }

        internal static void DetectScope(GameObject weapon)
        {
            bool scope = false;
            try
            {
                var wm = weapon.GetComponent<Il2Cpp.WeaponMods>();
                if (wm == null) wm = weapon.GetComponentInChildren<Il2Cpp.WeaponMods>(true);
                if (wm != null) { try { scope = wm.hasOptic; } catch { } }

                if (!scope)
                {
                    var mods = weapon.GetComponentsInChildren<Il2Cpp.WeaponMod>(true);
                    if (mods != null)
                        for (int i = 0; i < mods.Length; i++)
                        {
                            var m = mods[i]; if (m == null) continue;
                            try { if (m.isOptic) { scope = true; break; } } catch { }
                        }
                }
            }
            catch { }

            if (scope != Alignment.ScopeFitted)
            {
                Alignment.ScopeFitted = scope;
                M107Mod.Log("aim: switched to the " + (scope ? "SCOPED" : "IRON SIGHT") + " aim position");
                Alignment.InvalidateAds();
            }

            if (scope) TuneScopeZero(weapon);
        }

        static readonly HashSet<int> _zeroSeen = new HashSet<int>();

        static void TuneScopeZero(GameObject weapon)
        {
            try
            {
                var setters = weapon.GetComponentsInChildren<Il2Cpp.ScopeInputSetter>(true);
                if (setters == null) return;
                for (int i = 0; i < setters.Length; i++)
                {
                    var s = setters[i]; if (s == null) continue;
                    int iid; try { iid = s.GetInstanceID(); } catch { continue; }
                    if (_zeroSeen.Contains(iid)) continue;

                    Il2Cpp.ScopeZero sz = null;
                    try { sz = s.scopeZero; } catch { }
                    if (sz == null) { _zeroSeen.Add(iid); continue; }

                    bool does = false; UnityEngine.KeyCode up = default, down = default;
                    float clicks = 0, total = 0, orig = 0;
                    try { does = sz.doesZero; } catch { }
                    try { up = sz.AdjustUp; } catch { }
                    try { down = sz.AdjustDown; } catch { }
                    try { clicks = sz.ClicksAdjustment; } catch { }
                    try { total = sz.totalAdjustment; } catch { }
                    try { orig = sz.originalZero; } catch { }

                    if (!does) { try { sz.doesZero = true; } catch { } }
                    if (up == UnityEngine.KeyCode.None || down == UnityEngine.KeyCode.None)
                    {

                        try { sz.AdjustUp = UnityEngine.KeyCode.RightBracket; up = UnityEngine.KeyCode.RightBracket; } catch { }
                        try { sz.AdjustDown = UnityEngine.KeyCode.LeftBracket; down = UnityEngine.KeyCode.LeftBracket; } catch { }
                    }

                    Alignment.ReserveKeys(up, down);
                    _zeroSeen.Add(iid);
                    M107Mod.Log("zero: scope '" + s.gameObject.name + "' - doesZero was " + does +
                                ", keys " + up + "/" + down + ", " + clicks + " per click, total=" + total +
                                ", original=" + orig + (does ? "" : "  (enabled it)"));
                }
            }
            catch (Exception e) { M107Mod.Log("zero: err " + e.Message); }
        }

        internal static int FindBolt(GameObject weapon)
        {
            int n = 0;
            try
            {
                var all = weapon.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    var t = all[i]; string nm;
                    try { nm = t.name; } catch { continue; }
                    if (nm == null || nm.IndexOf("bolt_carrier", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    bool known = false;
                    foreach (var b in _bolts) if (b.T == t) { known = true; break; }
                    if (known) continue;
                    _bolts.Add(new Bolt { T = t, Home = t.localPosition });
                    n++;
                }
                _bolts.RemoveAll(b => b.T == null);
            }
            catch (Exception e) { M107Mod.Log("feel: bolt scan err: " + e.Message); }
            return n;
        }

        static int TuneAudio(GameObject weapon)
        {
            int n = 0;
            try
            {
                var srcs = weapon.GetComponentsInChildren<AudioSource>(true);
                if (srcs == null) return 0;
                for (int i = 0; i < srcs.Length; i++)
                {
                    var s = srcs[i]; if (s == null) continue;

                    if (IsGunshot(s.transform))
                    {
                        try { s.pitch = ShotPitch; } catch { }
                        try { s.volume = 1f; } catch { }
                        try { if (s.minDistance < ShotMinDist) s.minDistance = ShotMinDist; } catch { }
                        try { s.maxDistance = Mathf.Min(s.maxDistance * ShotMaxDistMul, ShotMaxDistCap); } catch { }
                        n++;
                    }
                    else if (IsMechanical(s.transform))
                    {

                        try { s.volume = 1f; } catch { }
                        try { if (s.minDistance < MechMinDist) s.minDistance = MechMinDist; } catch { }
                        try { s.maxDistance = s.maxDistance * MechMaxDistMul; } catch { }
                        n++;
                    }
                }
            }
            catch (Exception e) { M107Mod.Log("feel: audio err: " + e.Message); }
            return n;
        }

        static bool IsGunshot(Transform t)
        {
            int g = 0;
            while (t != null && g++ < 8)
            {
                string n; try { n = t.name; } catch { return false; }
                if (n != null && n.IndexOf("Gunshot", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                t = t.parent;
            }
            return false;
        }

        static readonly string[] MechNames = { "Foley", "Trigger", "Empty" };

        static bool IsMechanical(Transform t)
        {
            try
            {
                string n = t.name;
                if (n == null) return false;
                foreach (var k in MechNames)
                    if (n.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            catch { }
            return false;
        }

        internal static void OnFired(GameObject weapon)
        {
            try
            {
                int kicked = 0;
                foreach (var b in _bolts)
                    if (b.T != null) { b.Started = Time.time; kicked++; }
                if (weapon != null) TuneAudio(weapon);

                if (Time.time - _shotLogAt > 10f)
                { _shotLogAt = Time.time; M107Mod.Log("feel: shot - kicking " + kicked + " bolt carrier(s)"); }
            }
            catch { }
        }

        internal static void Tick()
        {
            try
            {
                for (int i = _bolts.Count - 1; i >= 0; i--)
                {
                    var b = _bolts[i];
                    if (b.T == null) { _bolts.RemoveAt(i); continue; }
                    if (b.Started < 0f) continue;

                    float e = Time.time - b.Started;
                    float back;
                    if (e <= BoltBack) back = e / BoltBack;
                    else if (e <= BoltBack + BoltReturn) back = 1f - (e - BoltBack) / BoltReturn;
                    else { b.T.localPosition = b.Home; b.Started = -1f; continue; }

                    b.T.localPosition = b.Home + new Vector3(0f, 0f, -BoltTravel * back);
                }
            }
            catch { }
        }
    }
}
