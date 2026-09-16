// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using NetworkClient = Il2CppMirror.NetworkClient;
using NetworkIdentity = Il2CppMirror.NetworkIdentity;

namespace MapLoader
{

    public static class DoorSpawnRegistry
    {
        static MelonLogger.Instance _log;
        static readonly Dictionary<uint, string> _state = new Dictionary<uint, string>();
        static readonly Dictionary<uint, GameObject> _templates = new Dictionary<uint, GameObject>();
        static GameObject _holder;

        public static void Init(MelonLogger.Instance log) { _log = log; }

        static void SanitiseTemplates()
        {
            try
            {
                foreach (var kv in _templates)
                {
                    var tpl = kv.Value;
                    if (tpl == null) continue;
                    try { if (tpl.activeSelf) tpl.SetActive(false); } catch { }
                    try
                    {
                        if (_holder != null && tpl.transform.parent != _holder.transform)
                            tpl.transform.SetParent(_holder.transform, false);
                    }
                    catch { }
                }
            }
            catch { }
        }

        public static void EnsureForSceneDoors(string why)
        {
            try
            {
                SanitiseTemplates();
                var doors = MapDoors.SceneDoors(false);
                var seen = new List<uint>();
                for (int i = 0; i < doors.Count; i++)
                {
                    try
                    {
                        var d = doors[i];
                        var ni = d.GetComponent<NetworkIdentity>();
                        if (ni == null) continue;
                        uint id = ni.assetId;
                        if (id == 0 || seen.Contains(id)) continue;
                        seen.Add(id);
                        Ensure(id, d.gameObject, why);
                    }
                    catch { }
                }
            }
            catch (Exception e) { _log?.Warning("[doors] registry: " + e.Message); }
        }

        public static string Ensure(uint assetId, GameObject source, string why)
        {
            if (assetId == 0) return "assetId 0";
            string state;
            try
            {
                var prefabs = NetworkClient.prefabs;
                var handlers = NetworkClient.spawnHandlers;
                GameObject have;
                if (prefabs != null && prefabs.ContainsKey(assetId)
                    && (have = prefabs[assetId]) != null
                    && !(_templates.ContainsKey(assetId) && _templates[assetId] == have))
                {
                    state = "registered by the game as prefab '" + have.name + "'";
                }
                else if (handlers != null && handlers.ContainsKey(assetId))
                {
                    state = "spawn handler present";
                }
                else if (_templates.ContainsKey(assetId) && _templates[assetId] != null
                         && prefabs != null && prefabs.ContainsKey(assetId))
                {
                    state = "our door template is registered as its prefab";
                }
                else
                {
                    var tpl = MakeTemplate(assetId, source);
                    if (tpl == null)
                    {
                        state = "NOT registered by the game, and no shipped door could be copied "
                              + "as a template -- the host's clones cannot appear here";
                    }
                    else
                    {
                        NetworkClient.RegisterPrefab(tpl, assetId);
                        _templates[assetId] = tpl;
                        bool ok = false;
                        try { ok = NetworkClient.prefabs != null && NetworkClient.prefabs.ContainsKey(assetId); } catch { }
                        state = "NOT registered by the game -- an inactive defused copy of '"
                              + source.name + "' is now its prefab (RegisterPrefab "
                              + (ok ? "confirmed" : "NOT confirmed by readback")
                              + "), so the host's door clones can be built here";
                    }
                }
            }
            catch (Exception e)
            {
                state = "registry failed: " + e.Message;
            }
            string prev;
            if (!_state.TryGetValue(assetId, out prev) || prev != state)
            {
                _state[assetId] = state;
                _log?.Msg("[doors] client registry (" + why + "): door assetId " + assetId
                        + " is " + state + ".");
            }
            return state;
        }

        static float _nextStray;
        static readonly List<GameObject> _strayHidden = new List<GameObject>();
        static float _strayLastLog;

        public static void TickStray()
        {
            if (Time.time < _nextStray) return;
            _nextStray = Time.time + 1f;
            try
            {
                bool server = false;
                try { server = Il2CppMirror.NetworkServer.active; } catch { }
                if (server) return;
                bool built = false;
                try { built = MapRuntime.Done && MapRuntime.Root != null; } catch { }
                if (built)
                {
                    if (_strayHidden.Count > 0) ShowStray("this machine's map now stands");
                    return;
                }
                var all = Resources.FindObjectsOfTypeAll<Il2Cpp.DoorV2>();
                int n = 0;
                for (int i = 0; all != null && i < all.Length; i++)
                {
                    GameObject go = null;
                    try { go = all[i].gameObject; } catch { continue; }
                    if (go == null) continue;
                    string nm = null;
                    try { nm = go.name; } catch { continue; }
                    if (nm == null || !nm.StartsWith("ODG_DoorTemplate", StringComparison.Ordinal)) continue;
                    bool live = false;
                    try { live = go.activeInHierarchy; } catch { }
                    if (!live) continue;
                    if (_strayHidden.Contains(go)) continue;
                    SetParts(go, false);
                    _strayHidden.Add(go);
                    n++;
                }
                if (n > 0 && Time.time - _strayLastLog > 5f)
                {
                    _strayLastLog = Time.time;
                    _log?.Warning("[doors] client: " + n + " host door clone(s) arrived with NO map "
                                + "built on this machine (" + _strayHidden.Count + " hidden in all). "
                                + "Either this machine has no data for the host's map or its build "
                                + "never armed -- read the [mapid] data= and [coop] lines above. Hidden "
                                + "and intangible, because a door at map coordinates over the host "
                                + "level is the floating-doors report; they show the moment the map "
                                + "stands here.");
                }
            }
            catch (Exception e) { _log?.Warning("[doors] stray: " + e.Message); }
        }

        static void SetParts(GameObject go, bool on)
        {
            try
            {
                var rs = go.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; rs != null && i < rs.Length; i++) { try { rs[i].enabled = on; } catch { } }
                var cs = go.GetComponentsInChildren<Collider>(true);
                for (int i = 0; cs != null && i < cs.Length; i++) { try { cs[i].enabled = on; } catch { } }
            }
            catch { }
        }

        static void ShowStray(string why)
        {
            int n = 0;
            for (int i = 0; i < _strayHidden.Count; i++)
            {
                var go = _strayHidden[i];
                if (go == null) continue;
                SetParts(go, true); n++;
            }
            _strayHidden.Clear();
            if (n > 0) _log?.Msg("[doors] client: " + why + "; " + n + " hidden clone(s) shown");
        }

        public static void ForgetStray()
        {
            _strayHidden.Clear();
            _strayLastLog = 0f;
        }

        static GameObject MakeTemplate(uint assetId, GameObject source)
        {
            try
            {
                if (source == null) return null;
                if (_holder == null)
                {
                    _holder = new GameObject("ODG_DoorTemplates");
                    UnityEngine.Object.DontDestroyOnLoad(_holder);
                    _holder.SetActive(false);
                }
                bool wasActive = true;
                try { wasActive = source.activeSelf; } catch { }
                try { source.SetActive(false); } catch { }
                GameObject go = null;
                try
                {
                    go = UnityEngine.Object.Instantiate(source, _holder.transform);
                }
                finally
                {
                    try { source.SetActive(wasActive); } catch { }
                }
                if (go == null) return null;
                go.name = "ODG_DoorTemplate_" + assetId;
                go.SetActive(false);
                try
                {
                    var ni = go.GetComponent<NetworkIdentity>();
                    if (ni != null)
                    {
                        ni.hasSpawned = false;
                        ni.sceneId = 0;
                        ni.assetId = assetId;
                    }
                }
                catch (Exception de) { _log?.Warning("[doors] template defuse: " + de.Message); }
                return go;
            }
            catch (Exception e)
            {
                _log?.Warning("[doors] template: " + e.Message);
                return null;
            }
        }
    }
}
