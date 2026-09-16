// ODG Operations - M107
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using UnityEngine;

namespace M107
{

    internal static class WeaponTemplate
    {
        internal const string ObjectName = "M107A1";
        internal const string DisplayName = "Barrett M107A1";
        const string DonorName = "SVD";

        internal static GameObject Template { get; private set; }
        internal static int RegisteredIndex { get; private set; } = -1;
        internal static Transform ArtAnchor { get; private set; }
        internal static Transform MuzzleEnd { get; private set; }
        internal static Transform TemplateArt { get; private set; }

        internal static bool Build(Il2Cpp.LoadoutManager lm)
        {
            if (Template != null) return true;
            if (lm == null) return false;

            GameObject donor = null;
            try
            {
                var gw = lm.Game_Weapons;
                if (gw == null) { M107Mod.Log("Game_Weapons is null"); return false; }
                for (int i = 0; i < gw.Count; i++)
                {
                    var g = gw[i];
                    if (g != null && g.name == DonorName) { donor = g; break; }
                }
            }
            catch (Exception e) { M107Mod.Log("donor lookup err: " + e.Message); return false; }

            if (donor == null) { M107Mod.Log("donor '" + DonorName + "' not in Game_Weapons"); return false; }

            try
            {

                var holder = new GameObject("M107_TemplateHolder");
                UnityEngine.Object.DontDestroyOnLoad(holder);
                holder.SetActive(false);

                var tpl = UnityEngine.Object.Instantiate(donor, holder.transform).TryCast<GameObject>();
                if (tpl == null) { M107Mod.Log("template Instantiate returned null"); return false; }
                tpl.name = ObjectName;
                tpl.transform.localPosition = Vector3.zero;
                tpl.transform.localRotation = Quaternion.identity;

                Template = tpl;
                M107Mod.Log("template: cloned " + DonorName + " -> " + ObjectName);

                FindRig(tpl);
                Identify(tpl);
                Tune(tpl);
                DressTemplate();

                return true;
            }
            catch (Exception e) { M107Mod.Log("template build failed: " + e); return false; }
        }

        static void FindRig(GameObject tpl)
        {
            try
            {
                var all = tpl.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    string n; try { n = all[i].name; } catch { continue; }
                    if (ArtAnchor == null && n == "Weapon") ArtAnchor = all[i];
                    if (MuzzleEnd == null && n == "MuzzleEnd") MuzzleEnd = all[i];
                }

                if (ArtAnchor == null)
                    for (int i = 0; i < all.Length; i++)
                    {
                        string n; try { n = all[i].name; } catch { continue; }
                        if (n.EndsWith("_RIG", StringComparison.OrdinalIgnoreCase)) { ArtAnchor = all[i]; break; }
                    }
            }
            catch (Exception e) { M107Mod.Log("rig scan err: " + e.Message); }

            M107Mod.Log("template rig: ArtAnchor=" + (ArtAnchor == null ? "NOT FOUND" : BarrettArt.Path(ArtAnchor)) +
                        "   MuzzleEnd=" + (MuzzleEnd == null ? "NOT FOUND" : BarrettArt.F(MuzzleEnd.localPosition)));

            Alignment.LearnTemplateAnchors(Template);
        }

        static void Identify(GameObject tpl)
        {
            try
            {
                var w3 = tpl.GetComponent<Il2Cpp.WeaponV3>();
                if (w3 == null) { M107Mod.Log("no WeaponV3 on template!"); return; }
                w3.displayName = DisplayName;
                w3.IsProduction = true;
                M107Mod.Log("template: displayName='" + w3.displayName + "' slot=" + w3.weaponSlotType + " IsProduction=" + w3.IsProduction);
            }
            catch (Exception e) { M107Mod.Log("identify err: " + e.Message); }

            try
            {
                var wm = tpl.GetComponent<Il2Cpp.WeaponMods>();
                if (wm != null)
                    M107Mod.Log("template mods: family='" + wm.WeaponFamily + "' magCompat='" + wm.magazineCompatibility +
                                "' baseJsonLen=" + (wm.BASE_JSON_MOD_STRING == null ? 0 : wm.BASE_JSON_MOD_STRING.Length));
            }
            catch { }
        }

        static void Tune(GameObject tpl)
        {
            try
            {
                var w3 = tpl.GetComponent<Il2Cpp.WeaponV3>();
                var gs = w3 == null ? null : w3.gunStats;
                if (gs == null) { M107Mod.Log("no GunStats to tune"); return; }

                gs.FireRate = 300f;
                gs.FireRateSuppressed = 300f;
                gs.MOA = 0.9f;
                gs.Weight = 8.5f;
                gs.Ergonomics = 35f;
                gs.gunshotRange = 400f;
                gs.lockBoltOnEmpty = true;
                gs.fullAuto = false;
                gs.semiAutomatic = true;
                gs.burst = false;

                M107Mod.Log("tuned: FireRate=" + gs.FireRate + " MOA=" + gs.MOA + " Weight=" + gs.Weight +
                            " Ergo=" + gs.Ergonomics + " range=" + gs.gunshotRange + " caliber=" + gs.weaponCaliber);
            }
            catch (Exception e) { M107Mod.Log("tune err: " + e.Message); }
        }

        static void DressTemplate()
        {
            if (ArtAnchor == null) { M107Mod.Log("cannot dress template: no art anchor"); return; }
            var art = BarrettArt.Attach(ArtAnchor, "M107A1_Body");
            if (art == null) { M107Mod.Log("cannot dress template: art attach failed"); return; }
            if (MuzzleEnd != null) BarrettArt.AlignMuzzle(art, MuzzleEnd);
            if (BarrettArt.Bounds(art, out var b))
                M107Mod.Log("template dressed: art extent " + BarrettArt.F(b.size) + " (longest " +
                            Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)).ToString("F3") + "m)");

            Alignment.SeedHeld(art.transform);
            Alignment.Register(art.transform, false);
            TemplateArt = art.transform;
        }

        internal static bool MakeItsOwn()
        {
            if (Template == null) return false;
            if (!Parts.FullSet)
            {
                M107Mod.Log("identity: staying on the donor's parts list - the M107 set is incomplete.");
                return false;
            }
            try
            {
                var wm = Template.GetComponent<Il2Cpp.WeaponMods>();
                if (wm == null) { M107Mod.Log("identity: no WeaponMods on template"); return false; }

                string donorJson = null;
                try { donorJson = wm.BASE_JSON_MOD_STRING; } catch { }
                string ours = Parts.BuildBaseJson(donorJson);
                if (string.IsNullOrEmpty(ours))
                { M107Mod.Log("identity: base build remap produced nothing, staying on the donor's."); return false; }

                wm.BASE_JSON_MOD_STRING = ours;
                try { wm.WeaponFamily = Parts.Compat; } catch { }
                try { wm.magazineCompatibility = Parts.Compat; } catch { }

                int n = Parts.RetagSockets(Template);
                M107Mod.Log("identity: family='" + Safe(() => wm.WeaponFamily) + "' magCompat='" +
                            Safe(() => wm.magazineCompatibility) + "', " + n + " socket(s) retagged.");
                M107Mod.Log("identity: base build is now  " + ours);

                if (TemplateArt != null)
                {
                    TemplateArt.gameObject.SetActive(false);
                    M107Mod.Log("identity: one-piece body switched off - the rifle is assembled from its own parts now.");
                }

                StripDonorBody();
                return true;
            }
            catch (Exception e) { M107Mod.Log("identity err: " + e); return false; }
        }

        static void StripDonorBody()
        {
            int off = 0;
            try
            {
                var rends = Template.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < rends.Length; i++)
                {
                    var r = rends[i]; if (r == null) continue;
                    if (UnderOurArt(r.transform)) continue;
                    try { r.enabled = false; r.forceRenderingOff = true; off++; } catch { }
                }
            }
            catch (Exception e) { M107Mod.Log("strip err: " + e.Message); }
            M107Mod.Log("identity: stripped " + off + " donor renderer(s) off the skeleton itself");
        }

        static bool UnderOurArt(Transform t)
        {
            int g = 0;
            while (t != null && g++ < 40)
            {
                string n; try { n = t.name; } catch { return false; }
                if (n != null && (n.EndsWith("_art", StringComparison.Ordinal) ||
                                  n.StartsWith("M107A1_Body", StringComparison.Ordinal))) return true;
                t = t.parent;
            }
            return false;
        }

        static string Safe(Func<string> f) { try { return f() ?? "null"; } catch { return "<err>"; } }

        internal static bool Register(Il2Cpp.LoadoutManager lm)
        {
            if (Template == null || lm == null) return false;
            if (RegisteredIndex >= 0) return true;
            try
            {
                var gw = lm.Game_Weapons;
                for (int i = 0; i < gw.Count; i++)
                {
                    var g = gw[i];
                    if (g != null && g.name == ObjectName)
                    { RegisteredIndex = i; M107Mod.Log("already registered at [" + i + "]"); return true; }
                }

                gw.Add(Template);
                int id = lm.GetWeaponID(ObjectName);
                RegisteredIndex = id;
                M107Mod.Log("REGISTERED '" + ObjectName + "' -> Game_Weapons[" + id + "]  (list is now " + gw.Count + ")");
                return id >= 0;
            }
            catch (Exception e) { M107Mod.Log("register failed: " + e); return false; }
        }
    }
}
