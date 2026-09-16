// ODG Operations - FireteamRoles
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using UnityEngine;

namespace FireteamRoles
{

    internal static class Pouch
    {
        static float _next;
        static bool _doneThisRaid;
        static string _lastScene = "";
        static float _liveSince = -1f;

        internal static void Remember(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            try
            {
                if (Cfg.BreacherLastExplosive.Value == name) return;
                Cfg.BreacherLastExplosive.Value = name;
                MelonLoader.MelonPreferences.Save();
            }
            catch { }
        }

        internal static void Reset(string why)
        {
            _doneThisRaid = false;
            _liveSince = -1f;
            _queueLeft = 0;
            _queueDone = 0;
            _queueName = null;
            Log.Detail("pouch top-up armed again (" + why + ")");
        }

        internal static void Tick()
        {

            Err.Guard("pouch.queue", PumpQueue);

            if (Time.realtimeSinceStartup < _next) return;
            _next = Time.realtimeSinceStartup + 2f;
            if (!State.Enabled || !Cfg.BreacherTopUp.Value) return;

            string scene = "";
            try { scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; } catch { }
            if (scene != _lastScene) { _lastScene = scene; _doneThisRaid = false; _liveSince = -1f; }

            if (scene == "Operation Room") { LearnFromPouch(); return; }

            if (_doneThisRaid) return;

            var me = State.Me;
            if (me == null || !me.Live || me.Pn == null) { _liveSince = -1f; return; }

            if (_liveSince < 0f) _liveSince = Time.realtimeSinceStartup;
            if (Time.realtimeSinceStartup - _liveSince < Mathf.Max(0f, Cfg.BreacherTopUpDelay.Value)) return;

            var pm = MyMaster();
            if (pm == null) return;

            Il2Cpp.SavableStringList pouch = null;
            try { pouch = pm.Grenades; } catch { }
            if (pouch == null) return;

            int live = LiveGrenades(me.Pn);
            int have = 0;
            try { have = pouch.Count; } catch { }
            if (have > 0 && live <= 0) return;

            _doneThisRaid = true;
            Report(pm, pouch, me.Pn, live, have);

            if (State.MyRole != Role.Breacher) return;

            int cap = Mathf.Max(1, Cfg.BreacherExplosives.Value);
            int missing = cap - live;
            if (missing <= 0)
            {
                Log.Msg("breacher pouch already at " + live + "/" + cap + ", nothing to top up");
                return;
            }

            if (live <= 0)
            {
                Log.Warn("breacher top-up skipped: the loadout spawned no explosives. Put explosives " +
                         "in the loadout - the game only keeps four there, and the other " +
                         (cap - 4) + " are added here once you are in.");
                return;
            }

            string name = ChooseExplosive(pouch);
            if (string.IsNullOrEmpty(name))
            {
                Log.Warn("breacher is short " + missing + " explosive(s) but no name could be resolved.");
                return;
            }

            _queueName = name;
            _queueLeft = missing;
            _queueDone = 0;
            _queueCap = cap;
            Log.Msg("breacher top-up queued: " + missing + " x " + name + " (had " + live + ", cap " + cap + ")");
        }

        static string _queueName;
        static int _queueLeft, _queueDone, _queueCap;
        static float _nextSpawn;

        static void PumpQueue()
        {
            if (_queueLeft <= 0 || string.IsNullOrEmpty(_queueName)) return;
            if (Time.realtimeSinceStartup < _nextSpawn) return;
            _nextSpawn = Time.realtimeSinceStartup + Mathf.Max(0.05f, Cfg.BreacherTopUpSpacing.Value);

            var me = State.Me;
            if (me == null || !me.Live || me.Pn == null) { _queueLeft = 0; return; }

            string name = _queueName;
            var pn = me.Pn;
            bool ok = false;

            Err.Risky("pouch.spawn#" + _queueDone, () =>
            {
                pn.SpawnWeapon(name, "", Il2Cpp.PlayerNetworking.WeaponSlot.Grenade);
                ok = true;
            });

            _queueLeft--;
            if (ok) _queueDone++;
            else { Log.Warn("breacher top-up stopped: SpawnWeapon refused '" + name + "'"); _queueLeft = 0; }

            if (_queueLeft <= 0)
            {
                if (_queueDone > 0)
                {
                    Log.Msg("breacher topped up: +" + _queueDone + " x " + name + " (cap " + _queueCap + ")");
                    Log.Fired("breacher-topup");
                    Ui.Toast("breacher kit: " + _queueDone + " extra explosives");
                }
                _queueName = null;
            }
        }

        static int LiveGrenades(Il2Cpp.PlayerNetworking pn)
        {
            try
            {
                var list = pn.GrenadesList;
                return list == null ? 0 : list.Count;
            }
            catch { return 0; }
        }

        static string ChooseExplosive(Il2Cpp.SavableStringList pouch)
        {

            try
            {
                string want = Cfg.BreacherTopUpItem.Value;
                if (!string.IsNullOrEmpty(want)) return want;
            }
            catch { }

            try
            {
                var vals = pouch.values;
                if (vals != null && vals.Count > 0)
                {
                    string last = vals[vals.Count - 1];
                    if (!string.IsNullOrEmpty(last)) { Remember(last); return last; }
                }
            }
            catch { }

            try
            {
                string remembered = Cfg.BreacherLastExplosive.Value;
                if (!string.IsNullOrEmpty(remembered)) return remembered;
            }
            catch { }
            return null;
        }

        static void LearnFromPouch()
        {
            try
            {
                var pm = MyMaster();
                if (pm == null) return;
                var vals = pm.Grenades != null ? pm.Grenades.values : null;
                if (vals == null || vals.Count == 0) return;
                Remember(vals[vals.Count - 1]);
            }
            catch { }
        }

        static void Report(Il2Cpp.PlayerMaster pm, Il2Cpp.SavableStringList pouch,
                           Il2Cpp.PlayerNetworking pn, int live, int have)
        {
            try
            {
                var names = new List<string>();
                try
                {
                    var vals = pouch.values;
                    if (vals != null)
                        for (int i = 0; i < vals.Count; i++) names.Add(vals[i]);
                }
                catch { }

                int maxNades = -1, loadoutNum = -1;
                try { maxNades = Il2Cpp.PlayerMaster.MaxNades; } catch { }
                try { loadoutNum = pm.CurrentLoadoutNum; } catch { }

                Log.Msg("POUCH  role=" + RoleInfo.Name(State.MyRole) +
                        "  preset=" + have + "  live=" + live +
                        "  cap=" + Mathf.Max(1, Cfg.BreacherExplosives.Value) +
                        "  MaxNades=" + maxNades + "  loadoutSlot=" + loadoutNum +
                        "  [" + string.Join(", ", names.ToArray()) + "]");
            }
            catch (Exception e) { Log.Detail("pouch report failed: " + e.Message); }
        }

        static Il2Cpp.PlayerMaster MyMaster()
        {
            try { return Il2Cpp.PlayerMaster.MyPlayerMaster; }
            catch { return null; }
        }
    }
}
