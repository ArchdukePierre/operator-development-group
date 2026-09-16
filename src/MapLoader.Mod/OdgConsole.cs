// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace MapLoader
{

    internal static class OdgConsole
    {

        public static Func<string, string> External;
        const string Prefix = "odg";
        static MelonLogger.Instance _log;
        static bool _installed;

        public static bool ShowCounter;
        static float _nextCount;

        static readonly System.Collections.Generic.Dictionary<int, Il2Cpp.Health> _healthOf =
            new System.Collections.Generic.Dictionary<int, Il2Cpp.Health>();
        static int _alive, _total, _hvtAlive;
        static string _line = "";

        static bool _armed, _exfilOpened;

        static float _nextWaitReport;

        static string DescribeAlive(int max)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                int shown = 0;
                var bots = UnityEngine.Object.FindObjectsOfType<Il2Cpp.BrainAI>();
                for (int i = 0; i < (bots != null ? bots.Length : 0) && shown < max; i++)
                {
                    var b = bots[i];
                    if (b == null) continue;
                    Il2Cpp.Health hp = null;
                    int id = 0;
                    try { id = b.GetInstanceID(); } catch { }
                    if (id == 0 || !_healthOf.TryGetValue(id, out hp)) continue;
                    bool dead = false;
                    try { if (hp != null) dead = hp.isDead; } catch { }
                    if (dead) continue;
                    var p = b.transform.position;
                    if (shown > 0) sb.Append("; ");
                    sb.Append(b.gameObject.name).Append(" at (")
                      .Append(p.x.ToString("0.0")).Append(", ")
                      .Append(p.y.ToString("0.0")).Append(", ")
                      .Append(p.z.ToString("0.0")).Append(")");
                    shown++;
                }
                return sb.Length == 0 ? "(no live bot could be described)" : sb.ToString();
            }
            catch (Exception e) { return "(describe failed: " + e.Message + ")"; }
        }

        public static void Tick()
        {
            try { CustomOps.ObservePrepTimer(); } catch { }
            try { FreeCamProbe.Tick(); } catch { }
            try { RimWatch.Tick(); } catch { }
            try
            {
                if (_exfilOpened) return;
                bool server = false;
                try { server = Il2CppMirror.NetworkServer.active; } catch { }
                if (!server) return;
                if (!MapRuntime.Done)
                {
                    float nowB = 0f;
                    try { nowB = Time.realtimeSinceStartup; } catch { }
                    if (_armed && nowB >= _nextWaitReport)
                    {
                        _nextWaitReport = nowB + 30f;
                        _log?.Msg("[exfil] not open: the map does not report BUILT, so the "
                                + "clear check never runs.");
                    }
                    return;
                }

                Recount(false);
                if (_alive > 0)
                {
                    _armed = true;

                    float now2 = 0f;
                    try { now2 = Time.realtimeSinceStartup; } catch { }
                    if (now2 >= _nextWaitReport)
                    {
                        _nextWaitReport = now2 + 30f;
                        _log?.Msg("[exfil] not open yet: " + _alive + " of " + _total
                                + " enemies still alive. " + DescribeAlive(4));
                    }
                    return;
                }
                if (!_armed) return;

                _exfilOpened = true;
                var raid = Il2Cpp.RaidManager.singleton;
                if (raid == null)
                {
                    _log?.Warning("[exfil] the map is clear but there is no RaidManager to "
                                + "open extraction on.");
                    return;
                }
                int zones = 0;
                try { var z = raid.exfilZones; if (z != null) zones = z.Count; } catch { }
                raid.EnableGlobalExtraction();
                _log?.Msg("[exfil] map clear, extraction opened. " + _total
                        + " enemies spawned and all are down. " + zones + " exfil zone(s) in "
                        + "the scene. This is the game's own EnableGlobalExtraction, whose "
                        + "RPC_CanExfil has a UserCode twin, so Mirror tells every client "
                        + "rather than us trying to.");
                if (zones == 0)
                    _log?.Warning("[exfil] ...but RaidManager.exfilZones is EMPTY, so there is "
                                + "nowhere to walk to. Extraction is enabled and unreachable. "
                                + "If this is a day launch, the chosen host ships no zones: set "
                                + "[MapLoader] HostSceneDay to the surveyed backup "
                                + "'Afghan_Compounds_Day' and restart -- the host is a "
                                + "preference now, not a rebuild.");
            }
            catch (Exception e) { _log?.Warning("[exfil] " + e.Message); }
        }

        public static void ResetForNewLevel()
        {
            _armed = false; _exfilOpened = false; _nextWaitReport = 0f;
            _healthOf.Clear();
            _alive = _total = _hvtAlive = 0;
            _line = "";
            _nextCount = 0f;
        }

        public static void Install(HarmonyLib.Harmony h, MelonLogger.Instance log)
        {
            _log = log;
            if (_installed) return;
            try
            {
                var m = AccessTools.Method(typeof(Il2CppQFSW.QC.QuantumConsoleProcessor),
                                           "InvokeCommand", new[] { typeof(string) });
                if (m == null)
                {
                    log.Warning("[odgcmd] no QuantumConsoleProcessor.InvokeCommand(string); "
                              + "the ~ console cannot be extended on this build");
                    return;
                }
                h.Patch(m, prefix: new HarmonyMethod(typeof(OdgConsole), nameof(Pre_Invoke)));
                _installed = true;
                log.Msg("[odgcmd] ~ console: type 'odg' for the command list. "
                      + "'odg enemies' toggles the enemy counter.");
            }
            catch (Exception e) { log.Warning("[odgcmd] install: " + e.Message); }
        }

        static bool Pre_Invoke(string __0, ref Il2CppSystem.Object __result)
        {
            try
            {
                var raw = (__0 ?? "").Trim();
                if (raw.Length == 0) return true;
                var low = raw.ToLowerInvariant();
                if (!low.StartsWith(Prefix, StringComparison.Ordinal)) return true;

                if (low.Length > Prefix.Length && low[Prefix.Length] != ' '
                                               && low[Prefix.Length] != '-') return true;

                string rest = raw.Substring(Math.Min(Prefix.Length + 1, raw.Length)).Trim();
                string reply = Handle(rest.ToLowerInvariant(), rest);

                try
                {
                    __result = new Il2CppSystem.Object(
                        Il2CppInterop.Runtime.IL2CPP.ManagedStringToIl2Cpp(reply));
                }
                catch { }
                try
                {
                    var qc = UnityEngine.Object.FindObjectOfType<Il2CppQFSW.QC.QuantumConsole>();
                    if (qc != null) qc.LogToConsole(reply, true);
                }
                catch { }
                try { _log?.Msg("[odgcmd] " + raw + " -> " + reply.Replace("\n", " | ")); } catch { }
                return false;
            }
            catch (Exception e)
            {
                try { _log?.Warning("[odgcmd] " + e.Message); } catch { }
                return true;
            }
        }

        static string DayPreset()
        {
            var sb = new System.Text.StringBuilder();
            MapRuntime.SunElevation = 38f;
            MapRuntime.SunKelvin = 5400f;
            sb.AppendLine(ShellSwap.RetuneHostSun(38f, MapRuntime.SunAzimuth, 5400f, null));
            MapRuntime.HostShadowDimmer = 0.88f;
            sb.AppendLine(ShellSwap.SoftenHostShadows(0.88f, null));
            sb.AppendLine(SkyRig.SetAmbientLights(1300f));
            sb.AppendLine(SkyRig.SetFill(3.4f));
            sb.AppendLine(SkyRig.SetBloom(0.16f));
            sb.AppendLine(MapRuntime.SetLook(10f, 6f));
            sb.AppendLine(MapRuntime.SetCool(4f));
            return sb.ToString().TrimEnd();
        }

        static string Handle(string cmd, string original)
        {
#if ODG_PUBLIC

            return null;
#endif
            if (cmd.Length == 0 || cmd == "help" || cmd == "?")
                return "ODG OPERATIONS\n"
                     + "  odg enemies        toggle the enemy counter on screen\n"
                     + "  odg enemies now    print the count once, without the overlay\n"
                     + "  odg exfil          is extraction open, and if not why not\n"
                     + "  odg exfil now      open extraction by hand (host only)\n"
                     + "  odg where          your position, for reporting a spot\n"
                     + "  odg unstick        wedged in geometry? move to the nearest open floor\n"
                     + "  odg barriers       invisible wall? disarm the host's and list what is solid but unseen\n"
                     + "  odg doors          every door on this machine: at its marker, spawned, drawn\n"
                     + "  odg fall [mps]     fall damage on ODG maps: threshold, and every landing is logged\n"
                     + "  odg shield         spawn shield: who is covered now; 'off|on', 'grace N', 'max N' (host)\n"
                     + "  odg slots          spawn slots the host handed the game this mission, and whether the deploy was held\n"
                     + "  odg doorsync [on|off]  doors this client asked the host about, and how many were open here and shut there\n"
                     + "  odg hits           what bullets stopped on that was not drawn (this map); 'odg hits reset'\n"
                     + "  odg lod0 [off]     force LOD0 on every LOD group in the world: collision and visual agree (test)\n"
                     + "  odg trunks [on]    switch our tree-trunk collision off / on (test)\n"
                     + "  odg bots           bot census: where each bot is and where its body draws\n"
                     + "  odg stale          files earlier packs left behind that this one no longer ships\n"
                     + "  odg stale delete   remove them (only ever under Mods)\n"
                     + "  odg spawn          list the insertion points you can pick\n"
                     + "  odg spawn <n>      insert at point n, without clicking the map\n"
                     + "  odg sky            show the sky and sun, and how to change them\n"
                     + "  odg look <sat> [con]  colour: more vibrant, or '0 0' for the host's own\n"
                     + "  odg day            the cinematic day grade: status and its dials, live\n"
                     + "  odg foliage        mip bias, cutoff, normals, tree shadows: live foliage dials\n"
                     + "  odg aa             the camera's anti-aliasing: TAA sharpen, mode, flicker, live\n"
                     + "  odg apv [on|off]   force the APV frame bit off (city indirect unlock)\n"
                     + "  odg shade [on|off|<0..2>]  ambient-shaping depth: 0 off, 1 shipped, live\n"
                     + "  odg ssgi [on|off]  screen-space GI override on the day volume\n"
                     + "  odg albedo <a>     city albedo the exposure math assumes (0.05-0.9)\n"
                     + "  odg shot run [v]   queue the screenshot harness from the menu\n"
                     + "  odg lights         the map's own fixture lights: status, or odg lights <n>\n"
                     + "  odg amb [on|off|vol <0..1>]  the city's birdsong\n"
                     + "  odg dump [radius]  hand over the lights, stairs and surfaces\n"
                     + "                     around you (default 25 m) as a report file\n"
                     + "  odg detail [n|off] surface-detail tiling; suspect #1 for jitter" + (char)10
                     + "  odg lights scale <x>  fixture brightness multiplier, live" + (char)10
                     + "  odg version        which build of the pack you are running";

            if (cmd.StartsWith("spawn", StringComparison.Ordinal))
            {
                string sarg = cmd.Length > 5 ? cmd.Substring(5).Trim() : "";
                var sop = CustomOps.CurrentOp();
                string[] snames = sop != null && sop.Infils != null ? sop.Infils : null;
                int scount = snames != null ? snames.Length : 0;

                if (sarg.Length == 0 || sarg == "status")
                {
                    var sb = new System.Text.StringBuilder();
                    sb.Append("insertion points you can pick:\n");
                    for (int i = 0; i < scount; i++)
                        sb.Append("  ").Append(i + 1).Append("  ").Append(snames[i]).Append('\n');
                    if (scount == 0) sb.Append("  (none registered for this mission)\n");
                    sb.Append("  odg spawn <n>   insert at that point");
                    return sb.ToString();
                }

                int sidx;
                if (!int.TryParse(sarg, out sidx))
                    return "odg spawn <n>   (odg spawn on its own lists them)";
                sidx -= 1;
                if (sidx < 0 || (scount > 0 && sidx >= scount))
                    return "there is no insertion point " + sarg + " on this mission.";

                var lpm = LocalPlayerMaster();
                if (lpm == null)
                    return "no local player yet -- this works from the insertion "
                         + "screen onwards, not in the lobby.";
                try { lpm.CMD_RequestInfilTarget(sidx); }
                catch (Exception se)
                {
                    _log?.Warning("[odgcmd] CMD_RequestInfilTarget(" + sidx + "): " + se.Message);
                    return "the game refused that insertion request: " + se.Message;
                }
                return "asked to insert at "
                     + (scount > 0 ? snames[sidx] : ("point " + sarg))
                     + ". The marker turns green when the server answers.";
            }

            if (cmd.StartsWith("enemies", StringComparison.Ordinal))
            {
                Recount(true);
                string arg = cmd.Length > 7 ? cmd.Substring(7).Trim() : "";
                if (arg == "now" || arg == "once")
                    return Describe();
                if (arg == "on")  { ShowCounter = true;  return "enemy counter ON.  " + Describe(); }
                if (arg == "off") { ShowCounter = false; return "enemy counter OFF. " + Describe(); }
                ShowCounter = !ShowCounter;
                return "enemy counter " + (ShowCounter ? "ON" : "OFF") + ".  " + Describe();
            }

            if (cmd.StartsWith("exfil", StringComparison.Ordinal))
            {
                string arg = cmd.Length > 5 ? cmd.Substring(5).Trim() : "";
                if (arg.Length == 0 || arg == "status")
                {
                    Recount(true);
                    return "extraction " + (_exfilOpened ? "OPEN" : "closed") + ", "
                         + _alive + " of " + _total + " enemies alive"
                         + (_armed ? "" : "  (not armed yet: no live enemy seen, so a count of "
                                        + "zero here means the wave has not spawned rather than "
                                        + "that the map is clear)")
                         + ". It opens by itself once the last one is down.";
                }
                if (arg == "now" || arg == "open" || arg == "force")
                {
                    bool server = false;
                    try { server = Il2CppMirror.NetworkServer.active; } catch { }
                    if (!server) return "only the host can open extraction.";
                    var raid = Il2Cpp.RaidManager.singleton;
                    if (raid == null) return "no RaidManager in this scene.";
                    _exfilOpened = true;
                    try { raid.EnableGlobalExtraction(); }
                    catch (Exception e) { return "EnableGlobalExtraction threw: " + e.Message; }
                    _log?.Msg("[exfil] opened by hand from the console.");
                    return "extraction opened by hand. Everyone can exfil.";
                }
                return "odg exfil [status|now]";
            }

            if (cmd.StartsWith("look", StringComparison.Ordinal))
            {
                string lr = cmd.Length > 4 ? cmd.Substring(4).Trim() : "";
                if (lr.StartsWith("ev", StringComparison.Ordinal))
                {
                    float ev;
                    string evv = lr.Length > 2 ? lr.Substring(2).Trim() : "";
                    if (!float.TryParse(evv, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out ev))
                        return "odg look ev <stops>   brightness trim on top of the host's "
                             + "exposure (now " + MapRuntime.PostExposure.ToString("F1")
                             + "). Negative is darker. Try -1.5 if it is washed out.";
                    return MapRuntime.SetExposure(ev);
                }
                if (lr.StartsWith("cool", StringComparison.Ordinal))
                {
                    float ct;
                    string cv = lr.Length > 4 ? lr.Substring(4).Trim() : "";
                    if (!float.TryParse(cv, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out ct))
                        return "odg look cool <t>   how cool the frame is (now "
                             + MapRuntime.CoolTemp.ToString("F0")
                             + "). More negative is cooler; 0 is the host's own balance.";
                    return MapRuntime.SetCool(ct);
                }
                var lp = lr.Length > 0
                    ? lr.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    : new string[0];
                float ls = 0f, lc = 0f;
                bool okS = lp.Length > 0 && float.TryParse(lp[0],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out ls);
                bool okC = lp.Length > 1 && float.TryParse(lp[1],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out lc);
                if (!okS)
                    return "odg look <saturation> [contrast]   now "
                         + MapRuntime.Saturation.ToString("F0") + " / "
                         + MapRuntime.Contrast.ToString("F0")
                         + "\n  odg look cool <t>                  now "
                         + MapRuntime.CoolTemp.ToString("F0")
                         + " (more negative is cooler)";
                return MapRuntime.SetLook(ls, okC ? lc : MapRuntime.Contrast);
            }

            if (cmd.StartsWith("day", StringComparison.Ordinal))
            {
                var dp = cmd.Substring(3).Trim()
                    .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                string dv = dp.Length > 0 ? dp[0] : "";
                string dw = dp.Length > 1 ? dp[1] : "";
                float df = 0f;
                bool hasD = dp.Length > 1 && float.TryParse(dp[1],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out df);
                if (dv.Length == 0 || dv == "status")
                    return DayArt.Describe()
                         + "\n  odg day on|off             the whole grade volume"
                         + "\n  odg day tone aces|neutral  the curve; ACES is the UE look"
                         + "\n  odg day fog <m>            haze: 0 off, 300 thick, 700 default, 1500 faint"
                         + "\n  odg day vignette <0..1>    default 0.16"
                         + "\n  odg day micro <0..1>       normal-map self-shadowing, default 0.55"
                         + "\n  odg day cool <0..0.4>      blue lean in the shadows, default 0.06"
                         + "\n  odg day warm <0..0.4>      warm lean in the highlights, default 0.05"
                         + "\n  odg day taa on|off         temporal AA on the camera (the game ships NONE)"
                         + "\n  odg day taa sharpen <x>    default 0.15; higher re-sharpens the grain"
                         + "\n  odg day taa flicker <x>    default 0.85; higher is steadier and softer"
                         + "\n  odg day preset             re-apply the sun/fill/look preset live"
                         + "\n  (sun: odg sky sun <elev> [K] | shadows: odg sky shadows <0..1>"
                         + "\n   fill: odg sky fill <x> / odg sky ambient <lux> | look: odg look <sat> [con], odg look cool <t>)";
                if (dv == "on")  return DayArt.SetEnabled(true);
                if (dv == "off") return DayArt.SetEnabled(false);
                if (dv == "tone")
                    return dw == "aces" ? DayArt.SetTone(true)
                         : dw == "neutral" ? DayArt.SetTone(false)
                         : "odg day tone aces|neutral   (now " + (DayArt.UseAces ? "ACES" : "Neutral") + ")";
                if (dv == "fog" || dv == "haze")
                    return hasD ? DayArt.SetFog(df)
                                : "odg day fog <m>   mean free path in metres; 0 = off (now "
                                  + DayArt.FogM.ToString("F0") + ")";
                if (dv == "vignette" || dv == "vig")
                    return hasD ? DayArt.SetVignette(df)
                                : "odg day vignette <0..1>  (now " + DayArt.Vignette.ToString("F2") + ")";
                if (dv == "micro")
                    return hasD ? DayArt.SetMicro(df)
                                : "odg day micro <0..1>  (now " + DayArt.Micro.ToString("F2") + ")";
                if (dv == "cool")
                    return hasD ? DayArt.SetShadowCool(df)
                                : "odg day cool <0..0.4>  (now " + DayArt.ShadowCool.ToString("F2") + ")";
                if (dv == "warm")
                    return hasD ? DayArt.SetHighWarm(df)
                                : "odg day warm <0..0.4>  (now " + DayArt.HighWarm.ToString("F2") + ")";
                if (dv == "taa")
                {
                    if (dw == "on")  return DayArt.SetTaa(true);
                    if (dw == "off") return DayArt.SetTaa(false);
                    if (dw == "sharpen" && dp.Length > 2)
                    {
                        float sv;
                        if (float.TryParse(dp[2], System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out sv))
                            return DayArt.SetTaaSharpen(sv);
                    }
                    if (dw == "flicker" && dp.Length > 2)
                    {
                        float fv2;
                        if (float.TryParse(dp[2], System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out fv2))
                            return DayArt.SetTaaFlicker(fv2);
                    }
                    return "odg day taa on|off | taa sharpen <0..2> | taa flicker <0..1>   (now "
                         + (DayArt.Taa ? "TAA" : "off") + ")";
                }
                if (dv == "preset") return DayPreset();
                return "odg day [on|off|tone|fog|vignette|micro|cool|warm|preset]";
            }

            if (cmd.StartsWith("foliage", StringComparison.Ordinal))
            {
                var fp = cmd.Substring(7).Trim()
                    .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                string fv = fp.Length > 0 ? fp[0] : "";
                string fw = fp.Length > 1 ? fp[1] : "";
                float ff = 0f;
                bool hasF = fp.Length > 1 && float.TryParse(fp[1],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out ff);
                if (fv == "mip")
                    return hasF ? MeshFactory.SetFoliageMipBias(ff)
                                : "odg foliage mip <0..4>   mip bias on cutout foliage (now +"
                                  + MeshFactory.FoliageMipBiasNow.ToString("F2")
                                  + "). Villa took 2.0 and the grain stopped; 0.5 keeps range crisp.";
                if (fv == "shadows" || fv == "cast")
                {
                    if (fw == "on")  return MapRuntime.SetFoliageShadowsLive(true);
                    if (fw == "off") return MapRuntime.SetFoliageShadowsLive(false);
                    return "odg foliage shadows on|off   mature trees in or out of the shadow pass";
                }
                if (fv == "recv" || fv == "receive")
                {
                    if (fw == "on")  return MapRuntime.SetFoliageReceiveLive(true);
                    if (fw == "off") return MapRuntime.SetFoliageReceiveLive(false);
                    return "odg foliage recv on|off   whether foliage receives the sun's shadow map";
                }
                if (fv == "cutoff" || fv == "clip")
                    return hasF ? MeshFactory.SetFoliageCutoff(ff)
                                : "odg foliage cutoff <0.05..0.95>   alpha cutoff on cutout foliage (now "
                                  + MeshFactory.FoliageCutoffNow.ToString("F2") + "). Lower = thicker leaves.";
                if (fv == "normal" || fv == "normals")
                {
                    if (fw == "on")  return MeshFactory.SetFoliageNormal(true);
                    if (fw == "off") return MeshFactory.SetFoliageNormal(false);
                    return "odg foliage normal on|off   normal maps on cutout foliage (grain diagnostic)";
                }
                return "odg foliage mip <x>          cutout mip bias, now +"
                     + MeshFactory.FoliageMipBiasNow.ToString("F2") + " (steadies the salt-and-pepper crawl)"
                     + "\n  odg foliage cutoff <x>       alpha cutoff, now " + MeshFactory.FoliageCutoffNow.ToString("F2")
                     + " (lower = thicker leaves, fewer slivers)"
                     + "\n  odg foliage normal on|off    leaf/grass normal maps (grain diagnostic)"
                     + "\n  odg foliage shadows on|off   mature trees cast (dappled light; costs fps)"
                     + "\n  odg foliage recv on|off      foliage receives shadows (grain diagnostic)"
                     + "\n  odg aa                       the camera's anti-aliasing: mode, TAA sharpen, flicker";
            }

            if (cmd == "aa" || cmd.StartsWith("aa ", StringComparison.Ordinal))
            {
                var ap = cmd.Substring(2).Trim()
                    .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                string av = ap.Length > 0 ? ap[0] : "";
                string aw = ap.Length > 1 ? ap[1] : "";
                float af = 0f;
                bool hasA = ap.Length > 1 && float.TryParse(ap[1],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out af);
                if (av.Length == 0 || av == "status")
                    return AaDials.Describe()
                         + "\n  odg aa sharpen <0..2>      TAA sharpening; 0 is the softest, grain-free image"
                         + "\n  odg aa mode taa|smaa|fxaa|none"
                         + "\n  odg aa flicker <0..1>      TAA anti-flicker"
                         + "\n  odg aa quality low|medium|high"
                         + "\n  (not kept: the game's own settings own these; this is for finding the cause)";
                if (av == "sharpen" || av == "sharp")
                    return hasA ? AaDials.SetSharpen(af) : "odg aa sharpen <0..2>";
                if (av == "mode") return AaDials.SetMode(aw);
                if (av == "flicker")
                    return hasA ? AaDials.SetAntiFlicker(af) : "odg aa flicker <0..1>";
                if (av == "quality") return AaDials.SetQuality(aw);
                return "odg aa [sharpen|mode|flicker|quality]";
            }

            if (cmd.StartsWith("sky", StringComparison.Ordinal))
            {
                string arg = cmd.Length > 3 ? cmd.Substring(3).Trim() : "";
                if (arg.Length == 0 || arg == "status")
                    return SkyRig.Describe()
                         + "\n  odg sky sun <lux>        30000 is the city default, 52241 was desert noon"
                         + "\n  odg sky angle <deg> [az] sun elevation; lower is longer shadows"
                         + "\n  odg sky ev <value>       exposure; HIGHER is DARKER (pairs with sun: 30000@14.6)"
                         + "\n  odg sky bloom <0..1>     glow around bright things; 0.08 default"
                         + "\n  odg sky dome <ev>        sky dome trim; +1 = brighter sky AND brighter shadows"
                         + "\n  odg sky mode hdri|gradient   our painted sky, or the flat safe one"
                         + "\n  odg sky hd on|off        photographic sky.bin vs the generated dome"
                         + "\n  odg sky flip             rebake the sky the other way up (grey cube walls)"
                         + "\n  odg sky clouds on|off"
                         + "\n  odg sky night on|off     our night sky instead of the host's";

                var p = arg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                string verb = p[0];

                float f1 = 0f, f2 = 0f;
                bool has1 = p.Length > 1 && float.TryParse(p[1],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out f1);
                bool has2 = p.Length > 2 && float.TryParse(p[2],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out f2);

                if (verb == "sun")
                {

                    if (MapRuntime.UseHostLighting)
                    {
                        if (!has1)
                            return "odg sky sun <elevation> [kelvin]   re-aim the host's sun (now "
                                 + MapRuntime.SunElevation.ToString("F0") + " deg, "
                                 + MapRuntime.SunKelvin.ToString("F0") + " K). Higher elevation "
                                 + "lights the streets; higher kelvin is cooler.";
                        MapRuntime.SunElevation = Mathf.Clamp(f1, 5f, 90f);
                        if (has2) MapRuntime.SunKelvin = Mathf.Clamp(f2, 2000f, 12000f);
                        return ShellSwap.RetuneHostSun(MapRuntime.SunElevation,
                                   MapRuntime.SunAzimuth, MapRuntime.SunKelvin, null);
                    }
                    return has1 ? SkyRig.SetSunLux(f1) : "odg sky sun <lux>  (now " + SkyRig.SunLux.ToString("F0") + ")";
                }
                if (verb == "angle" || verb == "elev" || verb == "elevation")
                    return has1 ? SkyRig.SetSunAngle(f1, has2 ? f2 : SkyRig.SunAzimuth)
                                : "odg sky angle <elevation> [azimuth]  (now "
                                  + SkyRig.SunElevation.ToString("F0") + " / "
                                  + SkyRig.SunAzimuth.ToString("F0") + ")";
                if (verb == "ev" || verb == "exposure")
                {
                    if (!has1) return "odg sky ev <value>  (now " + MapRuntime.DayEV.ToString("F2")
                                    + "; higher is darker)";

                    MapRuntime.AdjustDayEV(f1 - MapRuntime.DayEV);
                    SkyRig.CityDayEV = MapRuntime.DayEV;
                    return "day exposure EV100 = " + MapRuntime.DayEV.ToString("F2");
                }
                if (verb == "bloom")
                    return has1 ? SkyRig.SetBloom(f1)
                                : "odg sky bloom <0..1>  (now " + SkyRig.BloomIntensity.ToString("F2") + ")";
                string onoff = p.Length > 1 ? p[1] : "";
                if (verb == "clouds")
                {
                    if (onoff == "on")  return SkyRig.SetClouds(true);
                    if (onoff == "off") return SkyRig.SetClouds(false);
                    return SkyRig.SetClouds(!SkyRig.Clouds);
                }
                if (verb == "fill")
                    return has1 ? SkyRig.SetFill(f1)
                                : "odg sky fill <x>   how hard the sky lights what the sun "
                                + "misses (now " + SkyRig.SkyFill.ToString("F1") + "x). "
                                + "Try 2, 4, 6.";
                if (verb == "ambient" || verb == "lights")
                    return has1 ? SkyRig.SetAmbientLights(f1)
                                : "odg sky ambient <lux>   four shadowless fill lights, the "
                                + "one kind of fill that cannot silently not apply (now "
                                + SkyRig.FillLux.ToString("F0") + "). Try 3500, 7000, 0.";
                if (verb == "shadows" || verb == "shadow")
                {
                    if (!has1)
                        return "odg sky shadows <0..1>   how dark the host sun's shadows are "
                             + "(now " + MapRuntime.HostShadowDimmer.ToString("F2")
                             + "). 1 = black, 0 = none. Try 0.45, 0.25.";
                    MapRuntime.HostShadowDimmer = f1;
                    return ShellSwap.SoftenHostShadows(f1, null);
                }
                if (verb == "flip") return SkyRig.FlipSky();
                if (verb == "hd")
                {
                    if (onoff == "on")  return SkyRig.SetHdSky(true);
                    if (onoff == "off") return SkyRig.SetHdSky(false);
                    return "odg sky hd on|off   (now "
                         + (SkyRig.UseHdSky ? "sky.bin when present" : "generated") + ")";
                }
                if (verb == "mode")
                {
                    string mv = onoff;
                    if (mv == "hdri" || mv == "ours" || mv == "cubemap")
                        return SkyRig.SetSkyMode(true);
                    if (mv == "gradient" || mv == "flat" || mv == "safe")
                        return SkyRig.SetSkyMode(false);
                    return "odg sky mode hdri|gradient   (now "
                         + (SkyRig.UseHdriDay ? "hdri" : "gradient") + ")";
                }
                if (verb == "dome")
                    return has1 ? SkyRig.SetDomeTrim(f1)
                                : "odg sky dome <ev>  (now " + SkyRig.DomeTrimEV.ToString("F2")
                                + "; the dome and the ambient it feeds, not the camera)";
                if (verb == "night" || verb == "nightsky")
                {
                    if (onoff == "on")  return SkyRig.SetNightSky(true);
                    if (onoff == "off") return SkyRig.SetNightSky(false);
                    return SkyRig.SetNightSky(!SkyRig.NightSky);
                }
                return "odg sky [sun|angle|ev|clouds|night]";
            }

            if (cmd.StartsWith("infil", StringComparison.Ordinal))
            {
                string arg = cmd.Length > 5 ? cmd.Substring(5).Trim() : "";
                var uis = UnityEngine.Object.FindObjectsOfType<Il2Cpp.OperationBoardUI>(true);
                if (uis == null || uis.Length == 0)
                    return "no operation board is open. Open the laptop and the mission first.";

                var op = CustomOps.CurrentOp();
                string[] names = op != null && op.Infils != null ? op.Infils : null;
                int count = names != null ? names.Length : 0;

                if (arg.Length == 0 || arg == "status")
                {
                    var sb = new System.Text.StringBuilder();
                    sb.Append("insertion points on this mission");
                    if (count == 0) sb.Append(" (none registered)");
                    sb.Append(":\n");
                    var st = Il2Cpp.MissionLaptopNetworkState.instance;
                    for (int i = 0; i < count; i++)
                    {
                        bool on = false;
                        try { if (st != null) on = st.IsInfilActive(i); } catch { }
                        sb.Append("  ").Append(i + 1).Append("  ")
                          .Append(names[i]).Append(on ? "   ACTIVE" : "").Append('\n');
                    }
                    sb.Append("  odg infil <n>        toggle one\n");
                    sb.Append("  odg infil <n> on|off set one\n");
                    sb.Append("  odg infil all        every insertion on");
                    return sb.ToString();
                }

                var p = arg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                bool all = p[0] == "all";
                int idx = -1;
                if (!all && !int.TryParse(p[0], out idx))
                    return "odg infil [<n>|all] [on|off]";
                idx -= 1;
                if (!all && (idx < 0 || (count > 0 && idx >= count)))
                    return "there is no insertion point " + p[0] + " on this mission.";

                bool want = true;
                if (p.Length > 1 && p[1] == "off") want = false;
                else if (p.Length == 1 && !all)
                {
                    try
                    {
                        var st = Il2Cpp.MissionLaptopNetworkState.instance;
                        if (st != null) want = !st.IsInfilActive(idx);
                    }
                    catch { }
                }

                int applied = 0;
                for (int i = 0; i < count; i++)
                {
                    if (!all && i != idx) continue;
                    for (int u = 0; u < uis.Length; u++)
                    {
                        if (uis[u] == null) continue;
                        try { uis[u].InfilTargetButtonPressed(i, want); applied++; }
                        catch (Exception e)
                        { _log?.Warning("[odgcmd] InfilTargetButtonPressed(" + i + "): " + e.Message); }
                    }

                    try
                    {
                        bool server = false;
                        try { server = Il2CppMirror.NetworkServer.active; } catch { }
                        var st = Il2Cpp.MissionLaptopNetworkState.instance;
                        if (server && st != null) st.SetInfilActive(i, want);
                    }
                    catch { }
                }
                return (all ? "every insertion" : "insertion " + p[0] + " (" + names[idx] + ")")
                     + " -> " + (want ? "ACTIVE" : "off")
                     + "   (" + applied + " board call(s) across " + uis.Length + " laptop UI)";
            }

            if (cmd.StartsWith("apv", StringComparison.Ordinal))
            {
                string arg = cmd.Length > 3 ? cmd.Substring(3).Trim() : "";
                return ApvGate.Console(arg);
            }

            if (cmd.StartsWith("shade", StringComparison.Ordinal))
                return MapRuntime.ShadeConsole(cmd.Length > 5 ? cmd.Substring(5).Trim() : "");

            if (cmd.StartsWith("here", StringComparison.Ordinal))
                return MapRuntime.MarkHere(cmd.Length > 4 ? cmd.Substring(4).Trim() : "");

            if (cmd.StartsWith("lm", StringComparison.Ordinal))
                return MapRuntime.LightmapConsole(cmd.Length > 2 ? cmd.Substring(2).Trim() : "");

            if (cmd.StartsWith("volumes", StringComparison.Ordinal))
                return MapRuntime.VolumeConsole(cmd.Length > 7 ? cmd.Substring(7).Trim() : "");

            if (cmd.StartsWith("shadow", StringComparison.Ordinal))
                return MapRuntime.ShadowConsole(cmd.Length > 6 ? cmd.Substring(6).Trim() : "");

            if (cmd.StartsWith("water", StringComparison.Ordinal))
                return MapWater.Console(cmd.Length > 5 ? cmd.Substring(5).Trim() : "");

            if (cmd.StartsWith("decals", StringComparison.Ordinal))
                return MapRuntime.DecalConsole(cmd.Length > 6 ? cmd.Substring(6).Trim() : "");

            if (cmd.StartsWith("amb", StringComparison.Ordinal)
                && !cmd.StartsWith("ambient", StringComparison.Ordinal))
                return MapRuntime.AmbConsole(cmd.Length > 3 ? cmd.Substring(3).Trim() : "");

            if (cmd.StartsWith("what", StringComparison.Ordinal))
                return MapRuntime.WhatConsole();

            if (cmd.StartsWith("stream", StringComparison.Ordinal))
                return MapRuntime.StreamConsole(cmd.Length > 6 ? cmd.Substring(6).Trim() : "");

            if (cmd.StartsWith("ssgi", StringComparison.Ordinal))
            {
                string arg = cmd.Length > 4 ? cmd.Substring(4).Trim() : "";
                if (arg == "on") return MapRuntime.SetSsgi(true);
                if (arg == "off") return MapRuntime.SetSsgi(false);
                return "odg ssgi on|off   screen-space GI on the day volume. It replaces "
                     + "probe indirect where the screen has data; the screenshot harness "
                     + "is the judge of whether that helps here.";
            }

            if (cmd.StartsWith("lights", StringComparison.Ordinal))
                return MapLights.HandleConsole(cmd.Length > 6 ? cmd.Substring(6).Trim() : "");

            if (cmd.StartsWith("amb", StringComparison.Ordinal))
            {
                string aarg = cmd.Length > 3 ? cmd.Substring(3).Trim() : "";
                if (aarg == "on")  { MapAmbience.SetEnabled(true);  return "ambience ON.  " + MapAmbience.Describe(); }
                if (aarg == "off") { MapAmbience.SetEnabled(false); return "ambience OFF (fading out). " + MapAmbience.Describe(); }
                if (aarg.StartsWith("vol", StringComparison.Ordinal))
                {
                    float v;
                    if (float.TryParse(aarg.Substring(3).Trim(), System.Globalization.NumberStyles.Float,
                                       System.Globalization.CultureInfo.InvariantCulture, out v))
                    { MapAmbience.SetVolume(v); return "ambience volume " + Mathf.Clamp01(v).ToString("F2") + ".  " + MapAmbience.Describe(); }
                    return "odg amb vol <0..1>";
                }
                return MapAmbience.Describe();
            }

            if (cmd.StartsWith("albedo", StringComparison.Ordinal))
            {
                string arg = cmd.Length > 6 ? cmd.Substring(6).Trim() : "";
                float a;
                if (!float.TryParse(arg, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out a))
                    return "odg albedo <0.05-0.9>   what the exposure math assumes the city "
                         + "reflects (now " + MapRuntime.CityAlbedo.ToString("F2")
                         + "). Lower = brighter frame; takes effect on the next build.";
                return MapRuntime.SetCityAlbedo(a);
            }

            if (cmd.StartsWith("shot", StringComparison.Ordinal))
            {
                string arg = cmd.Length > 4 ? cmd.Substring(4).Trim() : "";
                if (arg.StartsWith("run", StringComparison.Ordinal))
                {
                    string variants = arg.Length > 3 ? arg.Substring(3).Trim() : "";
                    MapRuntime.HarnessRunRequest = variants.Length > 0 ? variants : "*";
                    return "shot matrix queued (" + (variants.Length > 0 ? variants : "all variants")
                         + "). It runs from the menu scene; if you are in a mission it waits "
                         + "for the next menu.";
                }
                return "odg shot run [A,B,E,...]   queue the lighting screenshot matrix";
            }

            if (cmd.StartsWith("dump", StringComparison.Ordinal))
            {
                string darg = cmd.Length > 4 ? cmd.Substring(4).Trim() : "";
                float rad = 25f;
                if (darg.Length > 0)
                    float.TryParse(darg, System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out rad);
                return MapDump.Run(rad);
            }

            if (cmd.StartsWith("detail", StringComparison.Ordinal))
            {
                string targ = cmd.Length > 6 ? cmd.Substring(6).Trim() : "";
                if (targ.Length == 0)
                    return "detail tile is " + MeshFactory.DetailTile.ToString("F1")
                         + " repeats per material (this map's own value is "
                         + MapIdentity.DetailTile.ToString("F1")
                         + "). 'odg detail <n>' to change, 'odg detail off' to remove it.";
                float tv;
                if (targ == "off") tv = 0f;
                else if (!float.TryParse(targ, System.Globalization.NumberStyles.Float,
                                         System.Globalization.CultureInfo.InvariantCulture, out tv)
                         || tv < 0f || tv > 64f)
                    return "give a number 0-64, or 'off'. e.g. 'odg detail 3'";
                int touched = MeshFactory.RetileDetail(tv);
                return (tv <= 0f ? "surface detail REMOVED from " : "detail tile set to "
                        + tv.ToString("F1") + " on ")
                     + touched + " material(s). If the jitter stops, this was it.";
            }

            if (cmd == "rays" || cmd.StartsWith("rays ", StringComparison.Ordinal))
            {
                string ra = cmd.Length > 4 ? cmd.Substring(4).Trim() : "";
                bool on = !(ra == "off" || ra == "0" || ra == "false");
                return MapRuntime.SetRays(on);
            }

            if (cmd == "unstick")
                return MapUnstick.Rescue();

            if (cmd == "barriers")
                return MapBarriers.Sweep("console");

            if (cmd == "doors")
                return DoorCensus.Take("console") + "\n" + DoorCensus.State("console");

            if (cmd == "slots") return SpawnSlots.Console();

            if (cmd == "hits" || cmd.StartsWith("hits ", StringComparison.Ordinal))
                return BulletWatch.Console("hits", cmd.Length > 4 ? cmd.Substring(4) : "");
            if (cmd == "lod0" || cmd.StartsWith("lod0 ", StringComparison.Ordinal))
                return BulletWatch.Console("lod0", cmd.Length > 4 ? cmd.Substring(4) : "");
            if (cmd == "trunks" || cmd.StartsWith("trunks ", StringComparison.Ordinal))
                return BulletWatch.Console("trunks", cmd.Length > 6 ? cmd.Substring(6) : "");

            if (cmd == "trees" || cmd.StartsWith("trees ", StringComparison.Ordinal))
            {
                var a = cmd.Length > 5 ? cmd.Substring(5).Trim().ToLowerInvariant() : "";
                if (a == "off") { HostVeg.DropEnabled = false; return "host tree drop off. Rebuild the map (F9) to see the level's own heights again."; }
                if (a == "on")  { HostVeg.DropEnabled = true;  return "host tree drop on. Rebuild the map (F9) to apply it."; }
                return "host tree drop is " + (HostVeg.DropEnabled ? "on" : "off")
                     + ". It moves only the trees standing inside the skirt; the rest are the horizon. "
                     + "The [hostveg] host trees line in the log says how many came down and how far.";
            }

            if (cmd == "heli" || cmd.StartsWith("heli ", StringComparison.Ordinal))
                return BlackHawk.Console(cmd.Length > 4 ? cmd.Substring(4) : "");

            if (cmd.StartsWith("doorsync", StringComparison.Ordinal))
                return DoorSync.Console(cmd.Length > 8 ? cmd.Substring(8).Trim() : "");

            if (cmd.StartsWith("shield", StringComparison.Ordinal))
                return SpawnShield.Console(cmd.Length > 6 ? cmd.Substring(6).Trim() : "");

            if (cmd.StartsWith("fall", StringComparison.Ordinal))
                return FallGuard.Console(cmd.Length > 4 ? cmd.Substring(4).Trim() : "");

            if (cmd == "bots")
            {
                MapAI.DumpBotCensus("console");
                return "bot census written to the log ([census] lines; MESH-ADRIFT marks a body drawing away from its bot)";
            }

            if (cmd.StartsWith("stale", StringComparison.Ordinal))
                return StaleFiles.Console(cmd.Length > 5 ? cmd.Substring(5).Trim() : "");

            if (cmd == "where")
            {
                var cam = Camera.main;
                if (cam == null) return "no camera";
                var p = cam.transform.position;
                return "you are at (" + p.x.ToString("F1") + ", " + p.y.ToString("F1")
                     + ", " + p.z.ToString("F1") + ")";
            }

            if (cmd == "version")
                return "ODG Operations. Map built: " + MapRuntime.Done
                     + ", markers placed: " + MapRuntime.Placed;

            try
            {
                var ext = External;
                if (ext != null)
                {
                    var r = ext(original);
                    if (r != null) return r;
                }
            }
            catch (Exception e) { return "external command failed: " + e.Message; }

            return "unknown: 'odg " + original + "'. Type 'odg' for the list.";
        }

        static Il2Cpp.PlayerMaster LocalPlayerMaster()
        {
            try
            {
                var pm = Il2Cpp.PlayerMaster.MyPlayerMaster;
                if (pm != null) return pm;
            }
            catch { }
            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<Il2Cpp.PlayerMaster>(true);
                for (int i = 0; i < (all != null ? all.Length : 0); i++)
                {
                    var p = all[i];
                    if (p == null) continue;
                    bool mine = false;
                    try { mine = p.isLocalPlayer; } catch { }
                    if (mine) return p;
                }
                if (all != null && all.Length == 1) return all[0];
            }
            catch (Exception e) { _log?.Warning("[odgcmd] PlayerMaster: " + e.Message); }
            return null;
        }

        static string Describe()
        {
            if (_total == 0)
                return "no enemies in the scene. If the mission just started they may still "
                     + "be spawning; if the map is built and this stays 0, the wave failed.";
            return _alive + " of " + _total + " enemies alive"
                 + (_hvtAlive > 0 ? ", target still up" : ", target down or not spawned yet");
        }

        public static void Recount(bool force)
        {
            if (!force && Time.realtimeSinceStartup < _nextCount) return;
            _nextCount = Time.realtimeSinceStartup + 0.5f;
            int alive = 0, total = 0, hvt = 0;
            try
            {
                var bots = UnityEngine.Object.FindObjectsOfType<Il2Cpp.BrainAI>();
                for (int i = 0; i < (bots != null ? bots.Length : 0); i++)
                {
                    var b = bots[i];
                    if (b == null) continue;
                    total++;

                    Il2Cpp.Health hp = null;
                    int id = 0;
                    try { id = b.GetInstanceID(); } catch { }
                    if (id != 0 && _healthOf.TryGetValue(id, out hp)) { }
                    else
                    {
                        try
                        {
                            hp = b.GetComponentInParent<Il2Cpp.Health>();
                            if (hp == null) hp = b.GetComponentInChildren<Il2Cpp.Health>(true);
                        }
                        catch { }
                        if (id != 0) _healthOf[id] = hp;
                    }

                    bool dead = false;
                    try { if (hp != null) dead = hp.isDead; } catch { }
                    if (dead) continue;
                    alive++;
                    try { if (b.isHVT) hvt++; } catch { }
                }
            }
            catch { }
            _alive = alive; _total = total; _hvtAlive = hvt;
            _line = _alive + " / " + _total + " enemies" + (_hvtAlive > 0 ? "   TARGET UP" : "");
        }

        static string _vramLine;
        static float  _vramUntil;

        public static void WarnVram(double residentMB, int vramMB)
        {
            if (vramMB <= 0 || residentMB <= vramMB) { _vramLine = null; return; }
            _vramLine = "VRAM " + residentMB.ToString("F0") + " / " + vramMB + " MB ("
                      + (residentMB * 100.0 / vramMB).ToString("F0")
                      + "%). This map is over the card. Long freezes are possible.";
            try { _vramUntil = Time.realtimeSinceStartup + 30f; } catch { _vramUntil = 0f; }
        }

        public static string MissingMap;

        public static void DrawMissingMapWarning()
        {
            if (string.IsNullOrEmpty(MissingMap)) return;
            try
            {
                var style = new GUIStyle(GUI.skin.label);
                style.fontSize = 16;
                style.alignment = TextAnchor.UpperCenter;
                style.wordWrap = true;
                style.normal.textColor = new Color(1f, 0.45f, 0.35f, 1f);
                var r = new Rect(Screen.width * 0.5f - 400f, 78f, 800f, 78f);
                GUI.color = new Color(0f, 0f, 0f, 0.78f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(r, "'" + MissingMap + "' IS NOT INSTALLED ON THIS MACHINE.\n"
                          + "You are standing in the host level with none of the map on it, "
                          + "and everyone else can see terrain you cannot.\n"
                          + "Close the game and run the ODG launcher to get the map data.",
                          style);
            }
            catch { }
        }

        public static void DrawVramWarning()
        {
            if (string.IsNullOrEmpty(_vramLine)) return;
            try
            {
                if (Time.realtimeSinceStartup > _vramUntil) { _vramLine = null; return; }
                if (!MapRuntime.Done) return;
                var style = new GUIStyle(GUI.skin.label);
                style.fontSize = 15;
                style.alignment = TextAnchor.MiddleCenter;
                style.normal.textColor = new Color(1f, 0.55f, 0.35f, 1f);
                var r = new Rect(Screen.width * 0.5f - 340f, 44f, 680f, 26f);
                GUI.color = new Color(0f, 0f, 0f, 0.65f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(r, _vramLine, style);
            }
            catch { }
        }

        public static void Draw()
        {
            if (!ShowCounter) return;

            try { if (!MapRuntime.Done) return; } catch { return; }
            try
            {
                Recount(false);
                if (_total == 0) return;
                var style = new GUIStyle(GUI.skin.label);
                style.fontSize = 16;
                style.alignment = TextAnchor.UpperRight;
                style.normal.textColor = Color.white;
                var r = new Rect(Screen.width - 320f, 12f, 300f, 26f);
                GUI.color = new Color(0f, 0f, 0f, 0.55f);
                GUI.DrawTexture(new Rect(r.x + 190f, r.y - 2f, 118f, 24f), Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(r, _line, style);
            }
            catch { }
        }
    }
}
