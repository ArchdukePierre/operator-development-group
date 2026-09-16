// ODG Operations - SpectatorName
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using UnityEngine;

namespace SpectatorName
{

    internal static class Roster
    {

        internal static Il2Cpp.PlayerMaster Local()
        {
            try { return Il2Cpp.PlayerMaster.MyPlayerMaster; }
            catch { return null; }
        }

        internal static Il2Cpp.PlayerNetworking SpawnedObject(Il2Cpp.PlayerMaster pm)
        {
            if (pm == null) return null;
            try
            {
                var pn = pm.PlayerSpawnedObject;
                return pn == null ? null : pn;
            }
            catch { return null; }
        }

        internal static Il2CppSystem.Collections.Generic.List<Il2Cpp.PlayerMaster> All()
        {
            try
            {
                var l = Il2Cpp.PlayerMaster.AllPlayerMasters;
                if (l != null && l.Count > 0) return l;
            }
            catch { }

            try
            {
                var gmn = Il2Cpp.GameManagerNetwork.instance;
                if (gmn != null) return gmn.playerMasters;
            }
            catch { }

            return null;
        }

        internal static string SpectatedName()
        {
            var list = All();
            if (list == null) return null;

            int count;
            try { count = list.Count; } catch { return null; }

            string fallback = null;

            for (int i = 0; i < count; i++)
            {
                Il2Cpp.PlayerMaster pm = null;
                try { pm = list[i]; } catch { continue; }
                if (pm == null) continue;

                var pn = SpawnedObject(pm);
                if (pn == null) continue;

                GameObject cam = null;
                try { cam = pn.HelmetCam; } catch { }
                if (cam == null) continue;

                bool on = false;
                try { on = cam.activeSelf; } catch { }
                if (!on) continue;

                bool alive = false;
                try { alive = pm.currentlySpawnedAndAlive; } catch { }

                if (alive) return NameOf(pm, pn);
                if (fallback == null) fallback = NameOf(pm, pn);
            }

            return fallback;
        }

        private static string NameOf(Il2Cpp.PlayerMaster pm, Il2Cpp.PlayerNetworking pn)
        {
            string n = null;
            try { n = pm.thisPlayerName; } catch { }
            if (!string.IsNullOrEmpty(n) && n.Trim().Length > 0) return n.Trim();

            try { n = pn.gameObject.name; } catch { n = null; }
            if (!string.IsNullOrEmpty(n) && n.Trim().Length > 0) return n.Trim();

            return "Operator";
        }
    }
}
