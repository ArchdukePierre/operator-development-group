// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace MapLoader
{

    public static class MapExfil
    {
        static MelonLogger.Instance _log;

        sealed class Moved
        {
            public GameObject Go;
            public Transform Parent;
            public Vector3 LocalPos;
            public Quaternion LocalRot;
        }

        static readonly List<Moved> _moved = new List<Moved>();

        public static void Init(MelonLogger.Instance log) { _log = log; }

        public static int Count { get { return _moved.Count; } }

        static List<Vector3> Targets(string sceneName)
        {
            var outp = new List<Vector3>();
            try
            {
                var pts = MapInfils.Load(sceneName);
                if (pts != null)
                    for (int i = 0; i < pts.Count; i++)
                        outp.Add(pts[i].World);
            }
            catch (Exception e) { _log?.Warning("[exfil] insertion points: " + e.Message); }
            if (outp.Count == 0)
            {
                Vector3 v;
                try
                {
                    if (MapRuntime.TryMarkedSpawn(out v)) outp.Add(v);
                }
                catch { }
            }
            return outp;
        }

        public static int Place(string sceneName)
        {
            Restore();
            int moved = 0;
            try
            {
                var targets = Targets(sceneName);
                if (targets.Count == 0)
                {
                    _log?.Warning("[exfil] no insertion point and no marked spawn for '"
                                + sceneName + "'; the host level's extraction zones are "
                                + "left where they are, which is not on this map.");
                    return 0;
                }

                var zones = Resources.FindObjectsOfTypeAll<Il2Cpp.ExfilZone>();
                if (zones == null || zones.Length == 0)
                {
                    _log?.Warning("[exfil] this host level ships NO ExfilZone at all, so "
                                + "there is nowhere to extract from however the mission ends. "
                                + "Extraction will open and lead nowhere.");
                    return 0;
                }

                var ourRoot = MapRuntime.Root != null ? MapRuntime.Root.transform : null;
                for (int i = 0; i < zones.Length; i++)
                {
                    var z = zones[i];
                    try
                    {
                        if (z == null || z.gameObject == null) continue;
                        if (!z.gameObject.scene.IsValid()) continue;
                        if (ourRoot != null && z.transform.IsChildOf(ourRoot)) continue;

                        var go = z.gameObject;
                        var was = go.transform.position;
                        _moved.Add(new Moved
                        {
                            Go = go,
                            Parent = go.transform.parent,
                            LocalPos = go.transform.localPosition,
                            LocalRot = go.transform.localRotation,
                        });

                        go.transform.SetParent(null, true);
                        go.transform.position = targets[moved % targets.Count];
                        moved++;
                        _log?.Msg("[exfil] zone '" + go.name + "' moved from ("
                                + was.x.ToString("0.0") + ", " + was.y.ToString("0.0") + ", "
                                + was.z.ToString("0.0") + ") -- the host level's own extraction "
                                + "point -- to our insertion at ("
                                + go.transform.position.x.ToString("0.0") + ", "
                                + go.transform.position.y.ToString("0.0") + ", "
                                + go.transform.position.z.ToString("0.0")
                                + "). activeSelf=" + go.activeSelf
                                + " (the game switches it on when extraction opens).");
                    }
                    catch (Exception ze) { _log?.Warning("[exfil] zone move: " + ze.Message); }
                }

                if (moved > 0)
                    _log?.Msg("[exfil] " + moved + " extraction zone(s) now stand on this map's "
                            + targets.Count + " insertion point(s). Every briefing says "
                            + "'Extraction: same as infiltration', and now it is.");
            }
            catch (Exception e) { _log?.Warning("[exfil] Place: " + e.Message); }
            return moved;
        }

        public static void ParkAll(Vector3 at)
        {
            for (int i = 0; i < _moved.Count; i++)
            {
                try { var go = _moved[i].Go; if (go != null) { go.transform.SetParent(null, true); go.transform.position = at; } } catch { }
            }
        }
        public static void PlaceAllOn(Transform carrier)
        {
            for (int i = 0; i < _moved.Count; i++)
            {
                try { var go = _moved[i].Go; if (go != null) { go.transform.SetParent(carrier, false); go.transform.localPosition = new Vector3(0f, 0.8f, 3.5f); } } catch { }
            }
        }

        public static void Restore()
        {
            int n = 0;
            for (int i = 0; i < _moved.Count; i++)
            {
                var m = _moved[i];
                try
                {
                    if (m == null || m.Go == null) continue;
                    m.Go.transform.SetParent(m.Parent, false);
                    m.Go.transform.localPosition = m.LocalPos;
                    m.Go.transform.localRotation = m.LocalRot;
                    n++;
                }
                catch { }
            }
            _moved.Clear();
            if (n > 0) _log?.Msg("[exfil] " + n + " extraction zone(s) put back on the host level");
        }
    }
}
