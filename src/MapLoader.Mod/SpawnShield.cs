// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace MapLoader
{

    static class SpawnShield
    {
        const string PrefEnabled = "SpawnShieldEnabled";
        const string PrefGrace = "SpawnShieldGraceSeconds";
        const string PrefMax = "SpawnShieldMaxSeconds";

        static MelonLogger.Instance _log;
        static bool _enabled = true;
        static float _grace = 5f;
        static float _max = 120f;
        static bool _installed;
        static int _hooks;

        class Rec
        {
            public uint Id;
            public readonly HashSet<int> Ids = new HashSet<int>();
            public string Name;
            public Il2Cpp.MasterController Body;
            public Il2Cpp.Health Health;
            public float ArmedAt;
            public float InputAt = -1f;
            public string InputWhy;
            public float EndedAt = -1f;
            public string EndWhy;
            public Vector3 LastPos;
            public float LastYaw;
            public float Walked, Turned;
            public int Blocked;
            public float BlockedDamage;
            public bool SaidBlock;
            public int Wounds;

            public float HardUntil;
            public bool Active { get { return EndedAt < 0f; } }
        }

        static readonly Dictionary<uint, Rec> _recs = new Dictionary<uint, Rec>();
        static readonly HashSet<uint> _armedEver = new HashSet<uint>();
        static readonly List<Rec> _order = new List<Rec>();
        static float _nextScan, _nextTick;
        static string _scene = "";
        static int _blockedTotal;

        public static void Install(HarmonyLib.Harmony h, MelonLogger.Instance log)
        {
            _log = log;
            ReadPrefs();
            _hooks = 0;

            Hook(h, typeof(Il2Cpp.Health), "UserCode_CMDTakeDamage__Single__HealthBodyPart__HealthBodyPart__Int32", null, nameof(Pre_DamageCmd), "damage command");
            Hook(h, typeof(Il2Cpp.Health), "UserCode_CMDTakeDamageFromPosition__Single__HealthBodyPart__HealthBodyPart__Int32__Vector3", null, nameof(Pre_DamageCmdPos), "positioned damage command");

            Hook(h, typeof(Il2Cpp.Health), "TakeDamage",
                 new Type[] { typeof(float), typeof(Il2Cpp.HealthBodyPart), typeof(Il2Cpp.HealthBodyPart), typeof(int), typeof(Vector3) },
                 nameof(Pre_Sink), "damage sink");

            Hook(h, typeof(Il2Cpp.Health), "UserCode_CMD_Bleed__HealthBodyPart__Single__Int32", null, nameof(Pre_Bleed), "bleed command");
            Hook(h, typeof(Il2Cpp.Health), "UserCode_CMD_Fracture__HealthBodyPart__Int32", null, nameof(Pre_Fracture), "fracture command");

            Hook(h, typeof(Il2Cpp.PlayerNetworking), "UserCode_CMD_PushFireButton", null, nameof(Pre_Fire), "fire button");
            Hook(h, typeof(Il2Cpp.PlayerNetworking), "UserCode_CMDSetSprint__Boolean", null, nameof(Pre_Sprint), "sprint");
            Hook(h, typeof(Il2Cpp.PlayerNetworking), "UserCode_CMDSetADS__Boolean", null, nameof(Pre_Ads), "aim");

            Hook(h, typeof(Il2Cpp.InfiltrationManager), "RespawnPlayers", null, nameof(Pre_Redeploy), "re-deploy");
            Hook(h, typeof(Il2Cpp.InfiltrationManager), "Server_AllPlayersLoaded", null, nameof(Pre_Redeploy), "ready gate");
            Hook(h, typeof(Il2Cpp.InfiltrationManager), "RPC_SetSpawnPosition", null, nameof(Pre_SetSpawnPosition), "re-deploy teleport");

            _installed = _hooks >= 3;
            _log?.Msg("[shield] spawn shield " + (_enabled ? "ON" : "OFF (" + PrefEnabled + "=false)") + ": " + _hooks
                    + "/11 server hooks; no damage to a player from their deploy until their first input + "
                    + _grace.ToString("0.#") + " s, cap " + _max.ToString("0") + " s, ends at once on firing or hitting anyone; host only, every map");
        }

        static void Hook(HarmonyLib.Harmony h, Type t, string name, Type[] args, string prefix, string what)
        {
            try
            {
                var m = args == null ? AccessTools.Method(t, name) : AccessTools.Method(t, name, args);
                if (m == null) { _log?.Warning("[shield] " + t.Name + "." + name + " not found; the " + what + " is not covered"); return; }
                h.Patch(m, prefix: new HarmonyMethod(typeof(SpawnShield), prefix) { priority = HarmonyLib.Priority.First });
                _hooks++;
            }
            catch (Exception e) { _log?.Warning("[shield] " + what + " hook: " + e.Message); }
        }

        static void ReadPrefs()
        {
            try
            {
                var cfg = MelonLoader.MelonPreferences.GetCategory("MapLoader")
                       ?? MelonLoader.MelonPreferences.CreateCategory("MapLoader");
                if (cfg.GetEntry<bool>(PrefEnabled) == null)
                    cfg.CreateEntry(PrefEnabled, true,
                        description: "Spawn shield (host only): a player takes no damage from their deploy until "
                                   + "their first input plus the grace. Firing or hitting anyone ends it at once.");
                if (cfg.GetEntry<float>(PrefGrace) == null)
                    cfg.CreateEntry(PrefGrace, 5f,
                        description: "Seconds of shield left after a player's first input (walk, turn, sprint, aim).");
                if (cfg.GetEntry<float>(PrefMax) == null)
                    cfg.CreateEntry(PrefMax, 120f,
                        description: "Hard cap in seconds from the deploy; the shield ends then whatever the player did.");
                _enabled = cfg.GetEntry<bool>(PrefEnabled).Value;
                _grace = Mathf.Clamp(cfg.GetEntry<float>(PrefGrace).Value, 0f, 30f);
                _max = Mathf.Clamp(cfg.GetEntry<float>(PrefMax).Value, 10f, 600f);
            }
            catch (Exception e) { _log?.Warning("[shield] prefs: " + e.Message); }
        }

        static void WritePref<T>(string key, T value)
        {
            try
            {
                var cfg = MelonLoader.MelonPreferences.GetCategory("MapLoader");
                var e = cfg != null ? cfg.GetEntry<T>(key) : null;
                if (e != null) { e.Value = value; MelonLoader.MelonPreferences.Save(); }
            }
            catch (Exception ex) { _log?.Warning("[shield] pref write: " + ex.Message); }
        }

        public static void ResetForNewLevel(string why)
        {
            _redeployUntil = 0f; _redeployRefused = 0;
            if (_recs.Count > 0 || _armedEver.Count > 0)
                _log?.Msg("[shield] cleared " + _recs.Count + " record(s) (" + why + "); " + _blockedTotal + " hit(s) blocked so far this session");
            _recs.Clear(); _armedEver.Clear(); _order.Clear();
            _nextScan = 0f; _nextTick = 0f; _nextFallbackScan = 0f;
        }

        static bool IsMenuScene(string n)
        {
            if (string.IsNullOrEmpty(n)) return true;
            if (n == "3d Menu" || n == "Operation Room" || n == "ApplicationSplashScreen") return true;
            string l = n.ToLowerInvariant();
            return l.Contains("menu") || l.Contains("lobby") || l.Contains("splash")
                || l.Contains("operation room") || l.Contains("loading") || l.Contains("mainmenu") || l.Contains("ready");
        }

        static bool AtHoldingPoint(Vector3 p)
        {
            return Mathf.Abs(p.x) < 1.5f && Mathf.Abs(p.z) < 1.5f && p.y < -20f;
        }

        public static void Tick()
        {
            if (!_installed) return;
            float now = Time.realtimeSinceStartup;
            if (now < _nextTick) return;
            _nextTick = now + 0.25f;
            string scene = "";
            try { scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; } catch { }
            if (scene != _scene)
            {
                ResetForNewLevel("scene changed to '" + scene + "'");
                _scene = scene;
            }
            bool server = false;
            try { server = Il2CppMirror.NetworkServer.active; } catch { }
            if (!server || IsMenuScene(scene)) return;
            if (now >= _nextScan) { _nextScan = now + 1f; Scan(now); }
            for (int i = 0; i < _order.Count; i++)
            {
                var r = _order[i];
                if (!r.Active) continue;
                try { Watch(r, now); }
                catch (Exception e) { End(r, "watch error: " + e.Message); }
            }
        }

        static float _nextFallbackScan;

        static void Scan(float now)
        {
            int seen = 0;
            try
            {
                var conns = Il2CppMirror.NetworkServer.connections;
                if (conns != null)
                {
                    foreach (var conn in conns.Values)
                    {
                        try
                        {
                            if (conn == null) continue;
                            var ni = conn.identity;
                            if (ni == null) continue;
                            var pm = ni.GetComponent<Il2Cpp.PlayerMaster>();
                            var pn = pm != null ? pm.PlayerSpawnedObject : null;
                            if (pn == null) continue;
                            var mc = pn.masterController;
                            var h = pn.health;
                            if (mc == null || h == null) continue;
                            seen++;
                            Consider(mc, h, pn, now);
                        }
                        catch { }
                    }
                }
            }
            catch { }
            if (seen > 0 || now < _nextFallbackScan) return;

            _nextFallbackScan = now + 10f;
            Il2Cpp.MasterController[] all = null;
            try { all = UnityEngine.Object.FindObjectsOfType<Il2Cpp.MasterController>(); } catch { }
            for (int i = 0; i < (all != null ? all.Length : 0); i++)
            {
                try
                {
                    var mc = all[i];
                    if (mc == null) continue;
                    var h = mc.health;
                    if (h == null) continue;
                    Consider(mc, h, mc.playerNetworking, now);
                }
                catch { }
            }
        }

        static void Consider(Il2Cpp.MasterController mc, Il2Cpp.Health h, Il2Cpp.PlayerNetworking pn, float now)
        {
            uint id = h.netId;
            if (id == 0) return;
            Rec have;
            if (_recs.TryGetValue(id, out have)) { have.Body = mc; have.Health = h; return; }
            if (_armedEver.Contains(id)) return;
            if (!_enabled) return;
            var p = mc.transform.position;
            if (AtHoldingPoint(p)) return;
            if (h.isDead) return;
            var r = new Rec { Id = id, Body = mc, Health = h, ArmedAt = now, LastPos = p, LastYaw = mc.transform.eulerAngles.y };
            r.Ids.Add((int)id);
            try { r.Ids.Add((int)mc.netId); } catch { }
            try { if (pn != null) r.Ids.Add((int)pn.netId); } catch { }
            r.Name = NameOf(mc, pn, h);
            _recs[id] = r; _armedEver.Add(id); _order.Add(r);
            _log?.Msg("[shield] '" + r.Name + "' (netId " + id + ") deployed at (" + p.x.ToString("0.0") + ", " + p.y.ToString("0.0")
                    + ", " + p.z.ToString("0.0") + ") on '" + _scene + "': shielded from all damage until their first input + "
                    + _grace.ToString("0.#") + " s (cap " + _max.ToString("0") + " s)");
        }

        static string NameOf(Il2Cpp.MasterController mc, Il2Cpp.PlayerNetworking pn, Il2Cpp.Health h)
        {
            string nm = null;
            try
            {
                var pm = pn != null ? pn.playerMaster : null;
                if (pm != null) { nm = pm.NetworkthisPlayerName; if (string.IsNullOrEmpty(nm)) nm = pm.thisPlayerName; }
            }
            catch { }
            if (string.IsNullOrEmpty(nm)) { try { nm = h.gameObject.name; } catch { nm = "player"; } }
            bool host = false;
            try { host = pn != null ? pn.isOwned : mc.isOwned; } catch { }
            return host ? nm + " (host)" : nm;
        }

        static void Watch(Rec r, float now)
        {
            if (r.Body == null) { End(r, "body gone"); return; }
            var t = r.Body.transform;
            var p = t.position;
            if (AtHoldingPoint(p)) { r.LastPos = p; return; }

            float dx = p.x - r.LastPos.x, dz = p.z - r.LastPos.z;
            float step = Mathf.Sqrt(dx * dx + dz * dz);
            if (step <= 3f) r.Walked += step;
            r.LastPos = p;
            float yaw = t.eulerAngles.y;
            float dy = Mathf.Abs(Mathf.DeltaAngle(r.LastYaw, yaw));
            if (dy <= 30f) r.Turned += dy;
            r.LastYaw = yaw;
            if (r.InputAt < 0f)
            {
                if (r.Walked >= 1.5f) Input(r, "walked " + r.Walked.ToString("0.0") + " m");
                else if (r.Turned >= 60f) Input(r, "turned " + r.Turned.ToString("0") + " degrees");
            }
            if (r.InputAt >= 0f && now - r.InputAt >= _grace) { End(r, "first input + " + _grace.ToString("0.#") + " s"); return; }
            if (now - r.ArmedAt >= _max) { End(r, "the " + _max.ToString("0") + " s cap, no input seen"); return; }
            try { if (r.Health != null && r.Health.isDead) End(r, "dead"); } catch { }
        }

        static void Input(Rec r, string why)
        {
            if (r == null || !r.Active || r.InputAt >= 0f) return;
            r.InputAt = Time.realtimeSinceStartup;
            r.InputWhy = why;
            _log?.Msg("[shield] '" + r.Name + "': first input (" + why + ") " + (r.InputAt - r.ArmedAt).ToString("0.0")
                    + " s after deploy; shield ends in " + _grace.ToString("0.#") + " s");
        }

        static void End(Rec r, string why)
        {
            if (r == null || !r.Active) return;
            r.EndedAt = Time.realtimeSinceStartup;
            r.EndWhy = why;
            _log?.Msg("[shield] '" + r.Name + "': shield ended (" + why + ") " + (r.EndedAt - r.ArmedAt).ToString("0.0") + " s after deploy"
                    + (r.Blocked > 0 ? "; blocked " + r.Blocked + " hit(s), " + r.BlockedDamage.ToString("0") + " damage" : "")
                    + (r.Wounds > 0 ? ", " + r.Wounds + " wound(s)" : ""));
        }

        static Rec ByAnyId(int id)
        {
            if (id <= 0) return null;
            for (int i = 0; i < _order.Count; i++)
                if (_order[i].Ids.Contains(id)) return _order[i];
            return null;
        }

        static Rec Of(Il2Cpp.Health h)
        {
            try
            {
                if (h == null || _recs.Count == 0) return null;
                Rec r;
                return _recs.TryGetValue(h.netId, out r) ? r : null;
            }
            catch { return null; }
        }

        static Rec Of(Il2Cpp.PlayerNetworking pn)
        {
            try
            {
                if (pn == null || _order.Count == 0) return null;
                var r = ByAnyId((int)pn.netId);
                if (r != null) return r;
                var h = pn.health;
                return h != null ? Of(h) : null;
            }
            catch { return null; }
        }

        static bool Gate(Il2Cpp.Health victim, float dmg, Il2Cpp.HealthBodyPart part, int shooterID, string via)
        {
            try
            {
                if (!_enabled || _order.Count == 0) return true;
                var v = Of(victim);

                var s = ByAnyId(shooterID);
                if (s != null && s.Active && s != v)
                    End(s, "hit someone for " + dmg.ToString("0") + " (" + part + ")");
                if (v == null || (!v.Active && Time.realtimeSinceStartup >= v.HardUntil)) return true;
                v.Blocked++; v.BlockedDamage += dmg; _blockedTotal++;
                if (!v.SaidBlock || dmg >= 60f)
                {
                    v.SaidBlock = true;
                    _log?.Msg("[shield] '" + v.Name + "' shielded: " + dmg.ToString("0") + " to " + part + " from shooter " + shooterID
                            + " refused (" + via + ", " + (Time.realtimeSinceStartup - v.ArmedAt).ToString("0.0") + " s after deploy)");
                }
                return false;
            }
            catch { return true; }
        }

        static bool GateWound(Il2Cpp.Health victim, string kind, Il2Cpp.HealthBodyPart part, int shooterID)
        {
            try
            {
                if (!_enabled || _order.Count == 0) return true;
                var v = Of(victim);
                if (v == null || (!v.Active && Time.realtimeSinceStartup >= v.HardUntil)) return true;
                v.Wounds++;
                _log?.Msg("[shield] '" + v.Name + "' shielded: " + kind + " on " + part + " from shooter " + shooterID + " refused");
                return false;
            }
            catch { return true; }
        }

        static bool Pre_DamageCmd(Il2Cpp.Health __instance, float _damage, Il2Cpp.HealthBodyPart _bodyPart,
                                  Il2Cpp.HealthBodyPart crossedDamage, int shooterID)
        {
            return Gate(__instance, _damage, _bodyPart, shooterID, "command");
        }

        static bool Pre_DamageCmdPos(Il2Cpp.Health __instance, float _damage, Il2Cpp.HealthBodyPart _bodyPart,
                                     Il2Cpp.HealthBodyPart crossedDamage, int shooterID)
        {
            return Gate(__instance, _damage, _bodyPart, shooterID, "positioned command");
        }

        static bool Pre_Sink(Il2Cpp.Health __instance, float _damage, Il2Cpp.HealthBodyPart _bodyPart,
                             Il2Cpp.HealthBodyPart crossedDamage, int shooterID)
        {
            return Gate(__instance, _damage, _bodyPart, shooterID, "sink");
        }

        static bool Pre_Bleed(Il2Cpp.Health __instance, Il2Cpp.HealthBodyPart _bodyPart, float _damage, int shooterID)
        {
            return GateWound(__instance, "bleed", _bodyPart, shooterID);
        }

        static bool Pre_Fracture(Il2Cpp.Health __instance, Il2Cpp.HealthBodyPart _bodyPart, int shooterID)
        {
            return GateWound(__instance, "fracture", _bodyPart, shooterID);
        }

        static void Pre_Fire(Il2Cpp.PlayerNetworking __instance)
        {
            try { var r = Of(__instance); if (r != null && r.Active) End(r, "fired a weapon"); } catch { }
        }

        static void Pre_Sprint(Il2Cpp.PlayerNetworking __instance, bool sprintState)
        {
            try { if (sprintState) Input(Of(__instance), "sprinted"); } catch { }
        }

        static void Pre_Ads(Il2Cpp.PlayerNetworking __instance, bool adsState)
        {
            try { if (adsState) Input(Of(__instance), "aimed"); } catch { }
        }

        public static void ReArmAll(string why, float seconds)
        {
            if (!_enabled) return;
            try
            {
                float now = Time.realtimeSinceStartup;
                _nextScan = 0f;
                int n = 0;
                for (int i = 0; i < _order.Count; i++)
                {
                    var r = _order[i];
                    if (r == null) continue;
                    r.HardUntil = now + seconds;
                    n++;
                }
                _log?.Msg("[shield] " + why + ": every one of the " + n + " player(s) on the field is "
                        + "refused all damage for " + seconds.ToString("0") + " s. That chain kills the "
                        + "bodies that are already standing before it makes new ones, and a player who "
                        + "deployed a minute ago has no shield left of their own.");
            }
            catch (Exception e) { _log?.Warning("[shield] re-arm: " + e.Message); }
        }

        const float RedeployAge = 15f;
        static float _redeployUntil;
        static int _redeployRefused;

        public static bool Redeploying { get { return Time.realtimeSinceStartup < _redeployUntil; } }

        public static bool Pre_Redeploy()
        {
            try
            {
                float now = Time.realtimeSinceStartup;
                bool second = false;
                for (int i = 0; i < _order.Count; i++)
                {
                    var r = _order[i];
                    if (r != null && now - r.ArmedAt > RedeployAge) { second = true; break; }
                }
                if (second)
                {
                    _redeployUntil = now + 10f;
                    _redeployRefused = 0;
                    ReArmAll("the game re-ran its deploy", 8f);
                }
            }
            catch (Exception e) { _log?.Warning("[shield] re-deploy: " + e.Message); }
            return true;
        }

        public static bool Pre_SetSpawnPosition(Il2CppMirror.NetworkConnectionToClient __0)
        {
            try
            {
                if (!Redeploying) return true;
                var t = BodyOf(__0);
                if (t == null) return true;
                var p = t.position;

                if (Mathf.Abs(p.x) < 1.5f && Mathf.Abs(p.z) < 1.5f && p.y < -20f) return true;
                _redeployRefused++;
                if (_redeployRefused <= 4)
                    _log?.Msg("[shield] refused a re-deploy teleport: this player is at ("
                            + p.x.ToString("0.0") + ", " + p.y.ToString("0.0") + ", " + p.z.ToString("0.0")
                            + "), out in the level and already placed. The game's second deploy would have "
                            + "put them back on the insertion point mid-mission. Anyone still on the "
                            + "holding point is moved as normal.");
                return false;
            }
            catch { }
            return true;
        }

        public static bool EveryoneIsDeployed(out int total, out int waiting)
        {
            total = 0; waiting = 0;
            try
            {
                var conns = Il2CppMirror.NetworkServer.connections;
                if (conns == null) return false;
                foreach (var conn in conns.Values)
                {
                    try
                    {
                        if (conn == null) continue;
                        total++;
                        var t = BodyOf(conn);
                        if (t == null) { waiting++; continue; }
                        var p = t.position;
                        if (Mathf.Abs(p.x) < 1.5f && Mathf.Abs(p.z) < 1.5f && p.y < -20f) waiting++;
                    }
                    catch { waiting++; }
                }
            }
            catch { return false; }
            return total > 0 && waiting == 0;
        }

        static Transform BodyOf(Il2CppMirror.NetworkConnectionToClient conn)
        {
            try
            {
                if (conn == null) return null;
                var ni = conn.identity;
                if (ni == null) return null;
                var pm = ni.GetComponent<Il2Cpp.PlayerMaster>();
                var pn = pm != null ? pm.PlayerSpawnedObject : null;
                if (pn == null) return null;
                var mc = pn.masterController;
                return mc != null ? mc.transform : null;
            }
            catch { return null; }
        }

        public static string Console(string arg)
        {
            arg = (arg ?? "").Trim();
            if (arg == "off") { _enabled = false; WritePref(PrefEnabled, false); EndAll("switched off"); return "spawn shield OFF (saved)"; }
            if (arg == "on") { _enabled = true; WritePref(PrefEnabled, true); return "spawn shield ON (saved); new deploys are covered"; }
            if (arg.StartsWith("grace ", StringComparison.Ordinal))
            {
                float v;
                if (float.TryParse(arg.Substring(6).Trim(), out v)) { _grace = Mathf.Clamp(v, 0f, 30f); WritePref(PrefGrace, _grace); return "grace after first input: " + _grace.ToString("0.#") + " s (saved)"; }
                return "usage: odg shield grace <seconds>";
            }
            if (arg.StartsWith("max ", StringComparison.Ordinal))
            {
                float v;
                if (float.TryParse(arg.Substring(4).Trim(), out v)) { _max = Mathf.Clamp(v, 10f, 600f); WritePref(PrefMax, _max); return "cap from deploy: " + _max.ToString("0") + " s (saved)"; }
                return "usage: odg shield max <seconds>";
            }
            var sb = new System.Text.StringBuilder();
            bool server = false;
            try { server = Il2CppMirror.NetworkServer.active; } catch { }
            sb.Append("spawn shield ").Append(_enabled ? "ON" : "OFF").Append(", ").Append(_hooks).Append("/8 hooks, grace ")
              .Append(_grace.ToString("0.#")).Append(" s, cap ").Append(_max.ToString("0")).Append(" s; ")
              .Append(server ? "this machine is the host: it decides" : "this machine is a client: the host decides")
              .Append("; ").Append(_blockedTotal).Append(" hit(s) blocked this session");
            float now = Time.realtimeSinceStartup;
            for (int i = 0; i < _order.Count; i++)
            {
                var r = _order[i];
                sb.Append("\n  ").Append(r.Name).Append(": ");
                if (r.Active)
                {
                    sb.Append("SHIELDED ").Append((now - r.ArmedAt).ToString("0")).Append(" s, ");
                    if (r.InputAt >= 0f) sb.Append("input (").Append(r.InputWhy).Append("), ends in ").Append(Mathf.Max(0f, _grace - (now - r.InputAt)).ToString("0.0")).Append(" s");
                    else sb.Append("no input yet (walked ").Append(r.Walked.ToString("0.0")).Append(" m, turned ").Append(r.Turned.ToString("0")).Append("), cap in ").Append(Mathf.Max(0f, _max - (now - r.ArmedAt)).ToString("0")).Append(" s");
                }
                else sb.Append("ended (").Append(r.EndWhy).Append(") after ").Append((r.EndedAt - r.ArmedAt).ToString("0.0")).Append(" s");
                if (r.Blocked > 0) sb.Append("; blocked ").Append(r.Blocked).Append(" hit(s), ").Append(r.BlockedDamage.ToString("0")).Append(" damage");
            }
            if (_order.Count == 0) sb.Append("\n  nobody deployed on this scene yet");
            return sb.ToString();
        }

        static void EndAll(string why)
        {
            for (int i = 0; i < _order.Count; i++) if (_order[i].Active) End(_order[i], why);
        }
    }
}
