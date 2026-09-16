// ODG Operations - FireteamRoles
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using UnityEngine;

namespace FireteamRoles
{

    internal static class Downed
    {
        internal class Rec
        {
            internal ulong Id;
            internal Il2Cpp.Health Health;
            internal float Deadline;
            internal int RevivesLeft;
            internal ulong Dragger;
            internal int LastShooter;
            internal int SelfHealsCharged;
            internal int MissedTicks;
            internal int OpenWounds = -1;
            internal float TimeGranted;
            internal int EpiCharged;

            internal int WoundsAtDown;
        }

        static readonly Dictionary<ulong, Rec> _host = new Dictionary<ulong, Rec>();
        static readonly Dictionary<ulong, int> _revives = new Dictionary<ulong, int>();

        internal static bool AllowDeath;

        internal static bool AmDowned;

        static void RefreshAmDowned()
        {
            var me = State.Me;
            AmDowned = me != null && me.Downed && State.Enabled;
        }

        internal static void RefreshAmDownedPublic() { RefreshAmDowned(); }

        internal static int SelfHealsUsed;

        internal static int EpiReceived;
        internal static bool SelfHealsLeft
        {
            get { return SelfHealsUsed < UnityEngine.Mathf.Max(0, Cfg.DownedSelfHeals.Value); }
        }

        internal static bool OnHostDamage(Il2Cpp.Health h, float damage, Il2Cpp.HealthBodyPart part,
                                          Il2Cpp.HealthBodyPart crossed, int shooterId)
        {
            if (AllowDeath) return false;
            if (!Cfg.DownedEnabled.Value || !State.Enabled) return false;

            try { if (h == null || h.isBot || h.isDead) return false; }
            catch { return false; }

            var peer = State.FindByHealth(h);
            if (peer == null) return false;

            Rec rec;
            if (_host.TryGetValue(peer.Id, out rec)) { rec.Health = h; return true; }
            return false;
        }

        internal static bool OnHostDie(Il2Cpp.Health h, Il2Cpp.HealthBodyPart cause, int shooterId)
        {
            if (AllowDeath) return false;
            if (!State.IsHost) return false;

            if (!Cfg.DownedEnabled.Value) { Refused(h, "DownedEnabled is off"); return false; }
            if (!State.Enabled) { Refused(h, "mod disabled for this session (pvp=" + State.IsPvp + ")"); return false; }

            try { if (h == null || h.isBot || h.isDead) return false; }
            catch { return false; }

            var peer = State.FindByHealth(h);
            if (peer == null) { Refused(h, "no roster entry for this Health"); return false; }
            if (_host.ContainsKey(peer.Id)) return true;

            if (!peer.HasMod) { Refused(h, State.NameOf(peer.Id) + " has no mod (K_VER mismatch or vanilla)"); return false; }
            if (RevivesLeft(peer.Id) <= 0) { Refused(h, State.NameOf(peer.Id) + " has no revives left"); return false; }
            if (Cfg.DownedNeedsRescuer.Value && !AnyRescuer(peer.Id))
            { Refused(h, "nobody able to rescue " + State.NameOf(peer.Id) + " (" + RescuerCensus(peer.Id) + ")"); return false; }
            if (Cfg.HeadshotInstantDeath.Value && cause == Il2Cpp.HealthBodyPart.Head)
            { Refused(h, "headshot, instant death is on"); return false; }

            Log.Msg("intercepted death of " + State.NameOf(peer.Id) + " (cause " + cause + ", shooter " + shooterId + ")");
            Down(peer, h, shooterId);
            return true;
        }

        static readonly HashSet<string> _refusedOnce = new HashSet<string>();

        static void Refused(Il2Cpp.Health h, string why)
        {
            string who = "?";
            try { var p = State.FindByHealth(h); who = p != null ? State.NameOf(p.Id) : (h != null ? h.name : "?"); } catch { }
            if (!_refusedOnce.Add(who + "|" + why)) return;
            Log.Warn("death of " + who + " NOT intercepted: " + why);
            Log.Fired("down-refused");
        }

        static string RescuerCensus(ulong victimId)
        {
            int total = 0, mod = 0, live = 0, down = 0;
            try
            {
                foreach (var kv in State.Peers)
                {
                    var p = kv.Value;
                    if (p == null || p.Id == victimId) continue;
                    total++;
                    if (p.HasMod) mod++;
                    if (p.Live) live++;
                    if (p.Downed || _host.ContainsKey(p.Id)) down++;
                }
            }
            catch { }
            return "others=" + total + " withMod=" + mod + " live=" + live + " down=" + down;
        }

        static void Down(Peer peer, Il2Cpp.Health h, int shooterId)
        {
            var rec = new Rec
            {
                Id = peer.Id,
                Health = h,
                Deadline = Time.realtimeSinceStartup + Mathf.Max(5f, Cfg.DownBleedoutSeconds.Value),
                RevivesLeft = RevivesLeft(peer.Id),
                LastShooter = shooterId,

                SelfHealsCharged = peer.SelfHeals,
                EpiCharged = peer.EpiReceived
            };
            _host[peer.Id] = rec;

            try
            {
                if (h.HeadHP <= 2f) h.NetworkHeadHP = 5f;
                if (h.ChestHP <= 2f) h.NetworkChestHP = 5f;
                float total = h.HeadHP + h.ChestHP + h.StomachHP + h.LArmHP + h.RArmHP + h.LLegHP + h.RLegHP;
                if (total <= 0f) h.NetworkStomachHP = 5f;
                h.NetworkTotalHP = h.HeadHP + h.ChestHP + h.StomachHP + h.LArmHP + h.RArmHP + h.LLegHP + h.RLegHP;
            }
            catch (Exception e) { Log.Detail("down: hp floor failed: " + e.Message); }

            SeedWounds(h, shooterId);
            rec.OpenWounds = Wounds.OpenCount(h);
            rec.WoundsAtDown = rec.OpenWounds;

            SetAiTargeting(peer, false);
            Publish(true);
            Log.Msg("DOWN  " + State.NameOf(peer.Id) + "  " + Cfg.DownBleedoutSeconds.Value + "s, " +
                    rec.OpenWounds + " open wound(s): " + Wounds.OpenSummary(h));
        }

        static bool AnyRescuer(ulong victimId)
        {
            foreach (var kv in State.Peers)
            {
                var p = kv.Value;
                if (p == null || p.Id == victimId) continue;
                if (!p.HasMod || !p.Live || p.Downed) continue;
                if (_host.ContainsKey(p.Id)) continue;
                try { if (p.Health.isDead) continue; } catch { continue; }
                return true;
            }
            return false;
        }

        static void SeedWounds(Il2Cpp.Health h, int shooterId)
        {
            int want = Mathf.Max(0, Cfg.DownedMinimumWounds.Value);
            if (want == 0) return;

            int open = Wounds.OpenCount(h);
            if (open >= want) return;

            float rate = Mathf.Max(0.5f, Cfg.DownedSeededBleedRate.Value);

            var order = new List<Il2Cpp.HealthBodyPart>(Wounds.Parts);
            order.Sort((a, b) => Wounds.HP(h, a).CompareTo(Wounds.HP(h, b)));

            for (int i = 0; i < order.Count && open < want; i++)
            {
                var p = order[i];
                if (p == Il2Cpp.HealthBodyPart.Head) continue;
                if (Wounds.Bleed(h, p) > 0.001f) continue;
                Wounds.SetBleed(h, p, rate);
                open++;
                Log.Detail("seeded a bleed on " + Wounds.Label(p));
            }
        }

        static bool HostNeedsEpi(ulong id)
        {
            var peer = State.Find(id);
            if (peer == null) return false;
            float threshold = Cfg.EpiRequiredBelowHeartRate.Value;
            if (threshold <= 0f) return false;
            if (peer.Hr <= 0f) return false;
            return peer.Hr < threshold && peer.EpiReceived <= 0;
        }

        internal static int RevivesLeft(ulong id)
        {
            int v;
            if (_revives.TryGetValue(id, out v)) return v;
            v = Mathf.Max(0, Cfg.MaxRevivesPerPlayer.Value);
            _revives[id] = v;
            return v;
        }

        static float _nextPublish;

        static bool _wasHost;

        internal static void HostTick()
        {

            bool host = State.IsHost;
            if (_wasHost && !host)
            {
                _wasHost = false;
                if (_host.Count > 0) Log.Msg("dropping " + _host.Count + " downed record(s): no longer the server");
                _host.Clear();
                _revives.Clear();
                _bledOut.Clear();
                Net.SetSelf(Net.K_STATE, "");
            }
            _wasHost = host;

            if (!host) return;

            if (_host.Count > 0) Prune();

            if (_host.Count > 0)
            {
                List<ulong> expired = null;
                List<ulong> healed = null;
                foreach (var kv in _host)
                {
                    var r = kv.Value;

                    var peer = State.Find(kv.Key);
                    if (peer != null && peer.SelfHeals > r.SelfHealsCharged)
                    {
                        int newUses = peer.SelfHeals - r.SelfHealsCharged;
                        r.SelfHealsCharged = peer.SelfHeals;

                        float secs = Mathf.Max(0f, Cfg.DownedSelfHealTimeCost.Value) * newUses;
                        r.Deadline -= secs;

                        float hp = Mathf.Max(0f, Cfg.DownedSelfHealHPCost.Value) * newUses;
                        if (hp > 0f && r.Health != null)
                        {
                            try
                            {
                                float chest = Mathf.Max(3f, r.Health.ChestHP - hp);
                                r.Health.NetworkChestHP = chest;
                                r.Health.NetworkTotalHP = r.Health.HeadHP + chest + r.Health.StomachHP +
                                    r.Health.LArmHP + r.Health.RArmHP + r.Health.LLegHP + r.Health.RLegHP;
                            }
                            catch (Exception e) { Log.Detail("self-heal hp cost failed: " + e.Message); }
                        }

                        Log.Msg("self-heal by " + State.NameOf(kv.Key) + " cost " + secs + "s and " + hp + " hp");
                        Publish(true);
                    }

                    if (peer != null && peer.EpiReceived > r.EpiCharged)
                    {
                        int vials = peer.EpiReceived - r.EpiCharged;
                        r.EpiCharged = peer.EpiReceived;
                        float grant = Mathf.Max(0f, Cfg.EpiTimeBonus.Value) * vials;
                        float room = Mathf.Max(0f, Cfg.TreatmentTimeBonusCap.Value - r.TimeGranted);
                        grant = Mathf.Min(grant, room);
                        if (grant > 0f)
                        {
                            r.Deadline += grant;
                            r.TimeGranted += grant;
                            Log.Msg("epinephrine bought " + State.NameOf(kv.Key) + " " + grant.ToString("0") + "s");
                            Publish(true);
                        }
                    }

                    if (r.Health != null)
                    {
                        int nowOpen = Wounds.OpenCount(r.Health);
                        if (r.OpenWounds < 0) r.OpenWounds = nowOpen;
                        else if (nowOpen < r.OpenWounds)
                        {
                            int closed = r.OpenWounds - nowOpen;
                            float grant = Mathf.Max(0f, Cfg.TreatmentTimeBonus.Value) * closed;
                            float room = Mathf.Max(0f, Cfg.TreatmentTimeBonusCap.Value - r.TimeGranted);
                            grant = Mathf.Min(grant, room);
                            if (grant > 0f)
                            {
                                r.Deadline += grant;
                                r.TimeGranted += grant;
                                Log.Msg("treatment bought " + State.NameOf(kv.Key) + " " + grant.ToString("0") + "s (" +
                                        closed + " wound(s) closed, " + nowOpen + " left)");
                                Publish(true);
                            }
                        }
                        r.OpenWounds = nowOpen;
                    }

                    if (Cfg.AutoReviveWhenStabilised.Value && r.Health != null && r.WoundsAtDown > 0 &&
                        Wounds.IsClean(r.Health) && !HostNeedsEpi(kv.Key))
                    {
                        (healed ?? (healed = new List<ulong>())).Add(kv.Key);
                        continue;
                    }

                    if (Time.realtimeSinceStartup < r.Deadline) continue;
                    (expired ?? (expired = new List<ulong>())).Add(kv.Key);
                }

                if (healed != null)
                    for (int i = 0; i < healed.Count; i++) HostRevive(healed[i], 0UL);
                if (expired != null)
                    for (int i = 0; i < expired.Count; i++) Kill(expired[i]);
            }

            if (Time.realtimeSinceStartup >= _nextPublish)
            {
                _nextPublish = Time.realtimeSinceStartup + 1f;
                Publish(false);
            }
        }

        static float _nextPrune;

        static void Prune()
        {
            if (Time.realtimeSinceStartup < _nextPrune) return;
            _nextPrune = Time.realtimeSinceStartup + 1f;

            List<ulong> drop = null;
            List<ulong> members = Net.Online ? Net.Members() : null;

            foreach (var kv in _host)
            {
                var r = kv.Value;

                bool dead = false;
                try { dead = r.Health != null && r.Health.isDead; }
                catch { dead = false; }
                if (dead) { (drop ?? (drop = new List<ulong>())).Add(kv.Key); continue; }

                if (members != null)
                {
                    if (!members.Contains(kv.Key))
                    {
                        r.MissedTicks++;
                        if (r.MissedTicks >= 3) (drop ?? (drop = new List<ulong>())).Add(kv.Key);
                    }
                    else r.MissedTicks = 0;
                }
            }

            if (drop == null) return;
            for (int i = 0; i < drop.Count; i++)
            {
                Log.Msg("downed record dropped for " + State.NameOf(drop[i]) + " (dead or left)");
                _host.Remove(drop[i]);
                var peer = State.Find(drop[i]);
                if (peer != null) { peer.Downed = false; SetAiTargeting(peer, true); }
            }
            Publish(true);
        }

        static void Publish(bool immediate)
        {
            if (!State.IsHost) return;
            var sb = new System.Text.StringBuilder();
            foreach (var kv in _host)
            {
                var r = kv.Value;
                int remaining = Mathf.Max(0, Mathf.CeilToInt(r.Deadline - Time.realtimeSinceStartup));
                if (sb.Length > 0) sb.Append(';');
                sb.Append(r.Id).Append(':').Append(remaining).Append(':').Append(r.RevivesLeft).Append(':').Append(r.Dragger);
            }
            Net.SetSelf(Net.K_STATE, sb.ToString());
            if (immediate) _nextPublish = Time.realtimeSinceStartup + 1f;
        }

        static void Kill(ulong id)
        {
            Rec r;
            if (!_host.TryGetValue(id, out r)) return;
            _host.Remove(id);
            _revives[id] = 0;
            var peer = State.Find(id);
            if (peer != null) SetAiTargeting(peer, true);

            Log.Msg("BLEEDOUT  " + State.NameOf(id));
            _bledOut.Add(id);

            AllowDeath = true;
            try
            {

                bool alive = false;
                try { alive = r.Health != null && !r.Health.isDead; } catch { alive = false; }
                if (alive) Err.Risky("downed.die", () => r.Health.Die(Il2Cpp.HealthBodyPart.Chest, r.LastShooter));
                else Log.Msg("bleedout: " + State.NameOf(id) + " was already gone, no Die() call");
            }
            catch (Exception e) { Log.Err("bleedout death failed: " + e.Message); }
            finally { AllowDeath = false; }

            Publish(true);
        }

        internal static void HostRevive(ulong victim, ulong medic)
        {
            if (!State.IsHost) return;
            Rec r;
            if (!_host.TryGetValue(victim, out r)) return;

            var peer = State.Find(victim);
            Il2Cpp.Health h = null;
            try { h = (peer != null && peer.Health != null) ? peer.Health : r.Health; } catch { h = r.Health; }
            if (h == null) { Log.Detail("revive deferred for " + State.NameOf(victim) + ": no Health resolved yet"); return; }

            if (Cfg.ReviveRequiresStabilization.Value && !Wounds.IsClean(h))
            {
                Log.Msg("revive refused for " + State.NameOf(victim) + ": " + Wounds.OpenSummary(h));
                return;
            }

            _host.Remove(victim);
            _revives[victim] = Mathf.Max(0, r.RevivesLeft - 1);

            bool full = Cfg.ReviveFullHealth.Value;
            try
            {
                float head = full ? Pick(h.initHeadHP, Cfg.ReviveHeadHP.Value) : Cfg.ReviveHeadHP.Value;
                float chest = full ? Pick(h.initChestHP, Cfg.ReviveChestHP.Value) : Cfg.ReviveChestHP.Value;
                float stom = full ? Pick(h.initStomachHP, Cfg.ReviveLimbHP.Value) : Cfg.ReviveLimbHP.Value;
                float larm = full ? Pick(h.initArmLHP, Cfg.ReviveLimbHP.Value) : Cfg.ReviveLimbHP.Value;
                float rarm = full ? Pick(h.initArmRHP, Cfg.ReviveLimbHP.Value) : Cfg.ReviveLimbHP.Value;
                float lleg = full ? Pick(h.initLegsLHP, Cfg.ReviveLimbHP.Value) : Cfg.ReviveLimbHP.Value;
                float rleg = full ? Pick(h.initLegsRHP, Cfg.ReviveLimbHP.Value) : Cfg.ReviveLimbHP.Value;

                h.NetworkHeadHP = head;
                h.NetworkChestHP = chest;
                h.NetworkStomachHP = stom;
                h.NetworkLArmHP = larm;
                h.NetworkRArmHP = rarm;
                h.NetworkLLegHP = lleg;
                h.NetworkRLegHP = rleg;

                float total = head + chest + stom + larm + rarm + lleg + rleg;
                if (full)
                {
                    float initTotal = 0f;
                    try { initTotal = h.initTotalHP; } catch { }
                    if (initTotal > 0f) total = initTotal;
                }
                h.NetworkTotalHP = total;

                for (int i = 0; i < Wounds.Parts.Length; i++) Wounds.SetBleed(h, Wounds.Parts[i], 0f);

                Log.Msg("revive HP: head " + head.ToString("0") + " chest " + chest.ToString("0") +
                        " limbs " + larm.ToString("0") + " total " + total.ToString("0") +
                        (full ? " (full)" : " (partial)"));
            }
            catch (Exception e) { Log.Err("revive hp restore failed: " + e.Message); }

            SetAiTargeting(peer, true);
            Publish(true);
            Log.Fired("auto-revive");
            Log.Msg("REVIVED  " + State.NameOf(victim) + " by " + (medic == 0UL ? "treatment" : State.NameOf(medic)) +
                    "  (" + _revives[victim] + " revives left)");
            Ui.Toast(medic == 0UL ? State.NameOf(victim) + " is back up" : State.NameOf(victim) + " revived by " + State.NameOf(medic));
        }

        internal static void HostSetDragger(ulong victim, ulong dragger)
        {
            if (!State.IsHost) return;
            Rec r;
            if (!_host.TryGetValue(victim, out r)) return;
            if (r.Dragger == dragger) return;
            if (dragger != 0UL && r.Dragger != 0UL && r.Dragger != dragger) return;
            r.Dragger = dragger;
            Log.Msg(dragger == 0UL
                ? "drag released on " + State.NameOf(victim)
                : "drag confirmed: " + State.NameOf(dragger) + " has " + State.NameOf(victim));
            Log.Fired("drag-host-set");
            Publish(true);
        }

        internal static void HostClearAll(string why)
        {
            _refusedOnce.Clear();
            _bledOut.Clear();
            if (!State.IsHost)
            {

                Net.SetSelf(Net.K_STATE, "");
                _host.Clear();
                _revives.Clear();
                return;
            }
            if (_host.Count > 0) Log.Msg("clearing " + _host.Count + " downed (" + why + ")");
            foreach (var kv in _host)
            {
                var peer = State.Find(kv.Key);
                if (peer != null) SetAiTargeting(peer, true);
            }
            _host.Clear();
            _revives.Clear();
            Publish(true);
        }

        static float Pick(float init, float fallback)
        {
            return init > 0.01f ? init : fallback;
        }

        static void SetAiTargeting(Peer peer, bool targetable)
        {
            if (peer == null || peer.Root == null) return;
            try
            {
                var teams = Il2Cpp.TeamManager.OperatorTeams;
                if (teams == null) return;
                for (int teamId = 0; teamId <= 8; teamId++)
                {
                    Il2Cpp.TeamRuntimeData trd = null;
                    try { if (!teams.TryGetValue(teamId, out trd)) continue; }
                    catch { continue; }
                    if (trd == null) continue;
                    try
                    {
                        if (targetable) trd.AddTarget(peer.Root);
                        else trd.RemoveTarget(peer.Root);
                    }
                    catch { }
                }
                Log.Fired("ai-targeting " + (targetable ? "restore" : "remove"));
            }
            catch (Exception e) { Log.Detail("SetAiTargeting failed: " + e.Message); }
        }

        internal static void ConsumeState()
        {
            ulong host = FindHostId();
            string raw = host != 0UL ? Net.Get(host, Net.K_STATE) : "";

            foreach (var kv in State.Peers)
            {
                kv.Value.Downed = false;
                kv.Value.Dragger = 0UL;
            }

            if (!string.IsNullOrEmpty(raw))
            {
                var rows = raw.Split(';');
                for (int i = 0; i < rows.Length; i++)
                {
                    if (string.IsNullOrEmpty(rows[i])) continue;
                    var f = rows[i].Split(':');
                    if (f.Length < 3) continue;
                    ulong id = Net.ULong(f[0]);
                    if (id == 0UL) continue;
                    var p = State.Ensure(id);
                    p.Downed = true;
                    p.Remaining = Net.Int(f[1]);
                    p.Revives = Net.Int(f[2], 1);
                    p.Dragger = f.Length >= 4 ? Net.ULong(f[3]) : 0UL;
                }
            }

            AnnounceEdges();
        }

        static readonly HashSet<ulong> _announced = new HashSet<ulong>();

        static void AnnounceEdges()
        {
            foreach (var kv in State.Peers)
            {
                var p = kv.Value;
                if (p == null || p.Id == Net.Self) continue;
                if (p.Downed)
                {
                    if (_announced.Add(p.Id))
                    {
                        Ui.Toast("MAN DOWN - " + p.Name);
                        Ui.ManDownUntil = Time.realtimeSinceStartup + 3.5f;
                        Log.Msg("man down: " + p.Name);
                    }
                }
                else _announced.Remove(p.Id);
            }
        }

        static ulong FindHostId()
        {
            if (State.IsHost) return Net.Self;
            ulong owner = Net.LobbyOwner();
            if (owner != 0UL) return owner;

            var members = Net.Members();
            for (int i = 0; i < members.Count; i++)
                if (Net.Get(members[i], Net.K_STATE).Length > 0) return members[i];
            return 0UL;
        }

        static bool _localDown;
        static float _savedSpeed = -1f;
        static float _savedJump = -1f;
        static float _nextAgony;

        internal static void LocalTick()
        {
            RefreshAmDowned();
            var me = State.Me;
            bool down = AmDowned;

            if (down && !_localDown) EnterLocal();
            else if (!down && _localDown) ExitLocal();
            _localDown = down;

            if (!down)
            {
                if (_standForceAt > 0f) ForceStandIfStuck(me != null ? me.Pn : null);
                return;
            }

            var pn = me.Pn;
            if (pn == null) return;

            try
            {
                var walker = Walker(pn);
                if (walker != null)
                {
                    walker.movementSpeed = Mathf.Max(0f, Cfg.DownedCrawlSpeed.Value);
                    walker.jumpSpeed = 0f;
                }
            }
            catch { }

            HoldProne(pn);
            ForceProneIfStuck(pn);

            try
            {
                var med = me.Med;
                if (med != null)
                {

                    if (!Epi.Boosting) med.targetHeartRate = Mathf.Max(med.minBpm, 30f);
                    med.PainLevel = 1f;
                }
            }
            catch { }

            if (Time.realtimeSinceStartup >= _nextAgony)
            {
                _nextAgony = Time.realtimeSinceStartup + UnityEngine.Random.Range(9f, 15f);
                try { if (me.Health != null) me.Health.PlayAgony_NearDeath(); } catch { }
            }

            PublishVitals(me);
        }

        static void EnterLocal()
        {
            Log.Msg("you are down");
            SelfHealsUsed = 0;
            EpiReceived = 0;
            _nextProneTry = 0f;
            _proneForceAt = 0f;
            EpiReceived = 0;
            Ui.DramaStart = Time.realtimeSinceStartup;
            var me = State.Me;

            try
            {
                var mc = me != null && me.Pn != null ? me.Pn.masterController : null;
                if (mc != null && mc.CameraHitShake != null) mc.CameraHitShake.PlayFromStart();
            }
            catch { }
            try { if (me != null && me.Med != null) me.Med.ProcessDamage(70f); } catch { }
            try { if (me != null && me.Health != null) me.Health.PlayAgony_HurtBadly(); } catch { }
            _nextAgony = Time.realtimeSinceStartup + 12f;
            try
            {
                var walker = Walker(me != null ? me.Pn : null);
                if (walker != null)
                {
                    if (_savedSpeed < 0f) _savedSpeed = walker.movementSpeed;
                    if (_savedJump < 0f) _savedJump = walker.jumpSpeed;
                }
            }
            catch { }
            Ui.Toast("DOWN - hold on, a teammate has to reach you");
        }

        static readonly HashSet<ulong> _bledOut = new HashSet<ulong>();

        internal static bool DiedNotRevived(ulong id)
        {
            return _bledOut.Contains(id);
        }

        static void ExitLocal()
        {
            var me = State.Me;

            bool died = false;
            try { died = me != null && me.Health != null && me.Health.isDead; }
            catch { }

            if (died)
            {
                Log.Msg("you bled out");

                _savedSpeed = -1f; _savedJump = -1f;
                return;
            }

            Log.Msg("you are up");
            try
            {
                var walker = Walker(me != null ? me.Pn : null);
                if (walker != null)
                {
                    if (_savedSpeed > 0f) walker.movementSpeed = _savedSpeed;
                    if (_savedJump > 0f) walker.jumpSpeed = _savedJump;
                }
            }
            catch { }
            _savedSpeed = -1f; _savedJump = -1f;

            _proneForceAt = 0f;
            _nextProneTry = 0f;
            StandUp(me != null ? me.Pn : null);

            try
            {
                var med = me != null ? me.Med : null;
                if (med != null)
                {
                    med.targetHeartRate = 95f;

                    med.PainLevel = Cfg.ReviveFullHealth.Value ? 0f : 0.4f;
                }
            }
            catch { }
            Ui.Toast("back up - you are on your last life");
        }

        static float _nextProneTry;

        static void HoldProne(Il2Cpp.PlayerNetworking pn)
        {
            if (pn == null) return;
            if (Time.realtimeSinceStartup < _nextProneTry) return;
            _nextProneTry = Time.realtimeSinceStartup + 1.5f;
            try
            {
                var mc = pn.masterController;
                if (mc == null) return;
                var stance = mc.playerStance;
                if (stance == null) return;
                if (stance.isProne) return;

                stance.ProneTransition = true;
                Err.Risky("downed.prone", () => mc.StartCoroutine(mc.transitionProne(stance.isCrouched)));
                Log.Fired("forced-prone");

                _proneForceAt = Time.realtimeSinceStartup + 1.2f;
            }
            catch (Exception e) { Err.Note("downed.prone", e); }
        }

        static void StandUp(Il2Cpp.PlayerNetworking pn)
        {
            if (pn == null) return;
            try
            {
                var mc = pn.masterController;
                var stance = mc != null ? mc.playerStance : null;
                if (stance == null || !stance.isProne) return;

                stance.ProneTransition = true;
                Err.Risky("downed.standup", () => mc.StartCoroutine(mc.transitionProne(stance.isCrouched)));
                Log.Fired("stand-up");
                _standForceAt = Time.realtimeSinceStartup + 1.2f;
            }
            catch (Exception e) { Err.Note("downed.standup", e); }
        }

        static float _standForceAt;

        static void ForceStandIfStuck(Il2Cpp.PlayerNetworking pn)
        {
            if (_standForceAt <= 0f || Time.realtimeSinceStartup < _standForceAt) return;
            _standForceAt = 0f;
            try
            {
                var mc = pn != null ? pn.masterController : null;
                var stance = mc != null ? mc.playerStance : null;
                if (stance == null || !stance.isProne) return;
                stance.isProne = false;
                stance.ProneTransition = false;
                Log.Msg("stand transition did not take, stance forced");
            }
            catch (Exception e) { Log.Detail("hard stand failed: " + e.Message); }
        }

        static float _proneForceAt;

        static void ForceProneIfStuck(Il2Cpp.PlayerNetworking pn)
        {
            if (_proneForceAt <= 0f || Time.realtimeSinceStartup < _proneForceAt) return;
            _proneForceAt = 0f;
            try
            {
                var mc = pn != null ? pn.masterController : null;
                var stance = mc != null ? mc.playerStance : null;
                if (stance == null || stance.isProne) return;
                stance.isCrouched = false;
                stance.isProne = true;
                stance.ProneTransition = false;
                Log.Msg("prone transition did not take, stance forced");
                Log.Fired("forced-prone-hard");
            }
            catch (Exception e) { Log.Detail("hard prone failed: " + e.Message); }
        }

        internal static Il2CppCMF.AdvancedWalkerController Walker(Il2Cpp.PlayerNetworking pn)
        {
            if (pn == null) return null;
            try
            {
                var mc = pn.masterController;
                if (mc != null && mc.walkerController != null) return mc.walkerController;
            }
            catch { }
            try { return pn.transform.root.GetComponentInChildren<Il2CppCMF.AdvancedWalkerController>(); }
            catch { return null; }
        }

        static float _nextVitals;

        static void PublishVitals(Peer me)
        {
            if (Time.realtimeSinceStartup < _nextVitals) return;
            _nextVitals = Time.realtimeSinceStartup + 1f;
            try
            {
                var med = me.Med;
                if (med == null) return;
                int tq = 0;
                if (med.Tourniquet_LArm) tq |= 1;
                if (med.Tourniquet_RArm) tq |= 2;
                if (med.Tourniquet_LLeg) tq |= 4;
                if (med.Tourniquet_RLeg) tq |= 8;
                if (med.Tourniquet_Head) tq |= 16;
                Net.SetSelf(Net.K_VITALS, Net.Join(Net.F(med.HeartRate), Net.F(med.PainLevel), tq, SelfHealsUsed, EpiReceived));
            }
            catch { }
        }
    }
}
