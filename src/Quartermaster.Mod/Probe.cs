// ODG Operations - Quartermaster
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Text;
using UnityEngine;
using Il2CppTMPro;

namespace Quartermaster
{

    internal static class Probe
    {
        static KeyCode _key = KeyCode.F8;
        static bool _parsed;

        internal static void Tick()
        {
#if ODG_PUBLIC

            return;
#endif
            if (!_parsed)
            {
                _parsed = true;
                try { KeyCode k; if (Enum.TryParse(Main.DumpKey.Value, true, out k)) _key = k; } catch { }
            }
            if (!Input.GetKeyDown(_key)) return;
            try { Directory.CreateDirectory(Main.UserDir); } catch { }
            int n = 0;
            try { n += Write("qm_catalog.txt", Catalog()); } catch (Exception e) { Main.Log("dump catalog: " + e.Message); }
            try { n += Write("qm_live.txt", Live()); } catch (Exception e) { Main.Log("dump live: " + e.Message); }
            try { n += Write("qm_bench.txt", BenchDump()); } catch (Exception e) { Main.Log("dump bench: " + e.Message); }
            Main.Log("dumped " + n + " file(s) to " + Main.UserDir + "   " + Handling.StateLine() + "   " + Mobility.StateLine()
                   + "   recoil " + Recoil.Mode + "   last round " + Rounds.LastRound);
        }

        static int Write(string name, string text)
        {
            File.WriteAllText(Path.Combine(Main.UserDir, name), text);
            return 1;
        }

        static string Catalog()
        {
            var sb = new StringBuilder();
            sb.AppendLine("QUARTERMASTER catalog dump " + DateTime.Now);
            var lm = Il2Cpp.LoadoutManager.singleton;
            if (lm == null) { sb.AppendLine("LoadoutManager.singleton is null"); return sb.ToString(); }
            try
            {
                var cache = lm.WeaponModCompatibilityCache;
                sb.AppendLine("--- WeaponModInfo (" + (lm.WeaponModInfo != null ? lm.WeaponModInfo.Length : 0) + ") : index | Name | DisplayName | type | compat | in table?");
                var infos = lm.WeaponModInfo;
                for (int i = 0; infos != null && i < infos.Length; i++)
                {
                    var inf = infos[i]; if (inf == null) { sb.AppendLine(i + " | <null>"); continue; }
                    string comp = "";
                    try { int c = inf.Compatibility; comp = cache != null && c >= 0 && c < cache.Length ? cache[c] : c.ToString(); } catch { }
                    var p = Tables.Part(inf.Name);
                    sb.AppendLine(i + " | " + inf.Name + " | " + inf.DisplayName + " | " + inf.AttachmentType + " | " + comp + " | " + (p != null ? p.Cls : "MISSING"));
                }
            }
            catch (Exception e) { sb.AppendLine("WeaponModInfo: " + e.Message); }
            try
            {
                var shells = lm.AllBulletShells;
                sb.AppendLine("--- AllBulletShells (" + (shells != null ? shells.Length : 0) + ") : index | name | caliber | misfire | template");
                for (int i = 0; shells != null && i < shells.Length; i++)
                {
                    var s = shells[i]; if (s == null) { sb.AppendLine(i + " | <null>"); continue; }
                    string tpl = ""; try { var bp = s.BulletPrefab; tpl = bp != null ? bp.name : "<none>"; } catch { }
                    sb.AppendLine(i + " | " + s.gameObject.name + " | " + s.CaliberType + " | " + s.misfireChance + " | " + tpl);
                }
            }
            catch (Exception e) { sb.AppendLine("AllBulletShells: " + e.Message); }
            try
            {
                var gw = lm.Game_Weapons;
                sb.AppendLine("--- Game_Weapons (" + (gw != null ? gw.Count : 0) + ") : index | name | in table?");
                for (int i = 0; gw != null && i < gw.Count; i++)
                {
                    var g = gw[i]; string n = g != null ? g.name : "<null>";
                    sb.AppendLine(i + " | " + n + " | " + (Tables.Weapon(n) != null ? "yes" : "MISSING"));
                }
            }
            catch (Exception e) { sb.AppendLine("Game_Weapons: " + e.Message); }
            return sb.ToString();
        }

        static string Live()
        {
            var sb = new StringBuilder();
            sb.AppendLine("QUARTERMASTER live dump " + DateTime.Now + "   tables " + Tables.Hash);
            var pm = Il2Cpp.PlayerMaster.MyPlayerMaster;
            var pn = pm != null ? pm.PlayerSpawnedObject : null;
            if (pn == null) { sb.AppendLine("no local PlayerNetworking (not in a mission)"); return sb.ToString(); }
            try
            {
                var mc = pn.masterController;
                if (mc != null)
                {
                    sb.AppendLine("MasterController: WalkSpeed " + mc.WalkSpeed + " JogSpeed " + mc.JogSpeed + " SprintSpeed " + mc.SprintSpeed
                                + " SprintSpeedHighReady " + mc.SprintSpeedHighReady + " SpeedModifer " + mc.SpeedModifer + " totalWeight " + mc.totalWeight
                                + " runState " + mc.runState + " walker.movementSpeed " + (mc.walkerController != null ? mc.walkerController.movementSpeed.ToString("0.000") : "?"));
                }
            }
            catch (Exception e) { sb.AppendLine("MasterController: " + e.Message); }
            sb.AppendLine("Mobility: " + Mobility.StateLine() + "   items: " + Mobility.Heavy(12));
            Weapon(sb, "primary", Safe(() => pn.primaryWeapon));
            Weapon(sb, "secondPrimary", Safe(() => pn.secondPrimaryWeapon));
            Weapon(sb, "secondary", Safe(() => pn.secondaryWeapon));
            Weapon(sb, "specialPurpose", Safe(() => pn.specialPurposeWeapon));
            try
            {
                var pouch = pn.MagPouch;
                var mags = pouch != null ? pouch.GetComponentsInChildren<Il2Cpp.Magazine>(true) : null;
                sb.AppendLine("--- MagPouch magazines (" + (mags != null ? mags.Length : 0) + ") : name | ammo/max | BaseWeight | BulletWeight | table mass");
                for (int i = 0; mags != null && i < mags.Length; i++)
                {
                    var m = mags[i]; if (m == null) continue;
                    sb.AppendLine(m.gameObject.name + " | " + m.ammoAmount + "/" + m.ammoAmountMax + " | " + m.BaseWeight + " | " + m.BulletWeight + " | " + Mobility.MagMass(m).ToString("0.000"));
                }
            }
            catch (Exception e) { sb.AppendLine("MagPouch: " + e.Message); }
            try
            {
                var cust = pn.GetComponentInChildren<Il2Cpp.CharacterCustomisation>(true);
                sb.AppendLine("--- garments: " + (cust != null ? "found" : "none"));
                if (cust != null)
                {
                    Garment(sb, "HeadGear", cust.HeadGear); Garment(sb, "EyeWear", cust.EyeWear); Garment(sb, "EarPro", cust.EarPro);
                    Garment(sb, "Face", cust.Face); Garment(sb, "Shirt", cust.Shirt); Garment(sb, "Gloves", cust.Gloves);
                    Garment(sb, "Wrist", cust.Wrist); Garment(sb, "Pants", cust.Pants); Garment(sb, "Shoes", cust.Shoes);
                    Garment(sb, "Vest", cust.Vest); Garment(sb, "Belt", cust.Belt); Garment(sb, "Backpack", cust.Backpack);
                    var ps = cust.modParents;
                    for (int k = 0; ps != null && k < ps.Count; k++) { var p = ps[k]; if (p == null) continue; Garment(sb, "child:" + p.ModParentName, p.currentMod); }
                }
            }
            catch (Exception e) { sb.AppendLine("garments: " + e.Message); }
            try { sb.AppendLine("grenades " + (pn.GrenadesList != null ? pn.GrenadesList.Count : 0)); } catch { }
            return sb.ToString();
        }

        static T Safe<T>(Func<T> f) where T : class { try { return f(); } catch { return null; } }

        static void Weapon(StringBuilder sb, string slot, Il2Cpp.WeaponV3 w)
        {
            if (w == null) { sb.AppendLine("--- " + slot + ": none"); return; }
            try
            {
                var gs = w.gunStats;
                var b = Handling.BaseOf(w);
                var f = Handling.FactorsFor(w);
                var rd = Handling.SeatedRound(w);
                sb.AppendLine("--- " + slot + ": " + w.gameObject.name + " '" + w.displayName + "' owned " + w.isOwned + " table " + (Tables.Weapon(w.gameObject.name) != null));
                if (gs != null) sb.AppendLine("  gunStats MOA " + gs.MOA + " TBR " + gs.TimeBetweenRounds + " FireRate " + gs.FireRate + " Ergo " + gs.Ergonomics + " Weight " + gs.Weight + " range " + gs.gunshotRange + " suppressed " + gs.isSuppressed + " " + gs.SuppressedAmount);
                if (b != null) sb.AppendLine("  baseline MOA " + b.Moa + " Ergo " + b.Ergo + " Ads " + b.Ads + " Stance " + b.Stance + " Weight " + b.Weight + " known " + b.Known);
                sb.AppendLine("  factors V " + f.V + " H " + f.H + " MOA " + f.Moa + " Vel " + f.Vel + " Ergo " + f.Ergo + " parts " + f.Parts + " unknown " + f.Unknown + " partsKg " + f.Weight);
                sb.AppendLine("  round " + (rd != null ? rd.Shell + " (" + rd.Cal + ")" : "none"));
                try { var a = w.ADS_Fixed; if (a != null) sb.AppendLine("  ADS adsSpeed " + a.adsSpeed + " adsSpeedModifer " + a.adsSpeedModifer + " CurrentAdsAmount " + a.CurrentAdsAmount); } catch { }
                try { var s = w.weaponStance; if (s != null) sb.AppendLine("  stance AnimatorSpeed " + s.AnimatorSpeed + " adsMultipilier " + s.adsMultipilier); } catch { }
                try
                {
                    var wr = w.weaponRecoil; var ra = wr != null ? wr.Recoily : null;
                    sb.AppendLine("  recoil RecoilMultiplier " + (ra != null ? ra.RecoilMultiplier.ToString("0.000") : "?") + " data " + (wr != null && wr.recoilData != null ? wr.recoilData.name : "?")
                                + " return " + (wr != null ? wr.RecoilModiferReturnSpeed.ToString() : "?") + " mount " + (w.weaponCollision != null ? w.weaponCollision.weaponMountStablization.ToString("0.00") : "?"));
                }
                catch { }
                try
                {
                    var gf = w.gunFunction;
                    var mag = gf != null ? gf.currentMagazine : null;
                    var shell = gf != null ? gf.currentRoundInChamber : null;
                    sb.AppendLine("  seated mag " + (mag != null ? mag.gameObject.name + " " + mag.ammoAmount + "/" + mag.ammoAmountMax + " base " + mag.BaseWeight + " bullet " + mag.BulletWeight : "none")
                                + "   chamber " + (shell != null ? shell.gameObject.name : "empty"));
                }
                catch { }
                var names = new System.Collections.Generic.List<string>();
                var unknown = new System.Collections.Generic.List<string>();
                var parts = Handling.PartsOf(w, names, unknown);
                sb.AppendLine("  parts (" + parts.Count + "): name | type | cls | w | v h m e s | live W/E/R");
                var cur = w.mods != null ? w.mods.CurrentMods : null;
                for (int i = 0; i < parts.Count; i++)
                {
                    var p = parts[i];
                    string live = "";
                    try { if (cur != null && i < cur.Count && cur[i] != null) live = cur[i].Weight + "/" + cur[i].Ergonomics + "/" + cur[i].Recoil + " " + cur[i].attachmentType; } catch { }
                    sb.AppendLine("    " + p.Name + " | " + p.Type + " | " + p.Cls + " | " + p.Weight + " | " + p.V + " " + p.H + " " + p.Moa + " " + p.Ergo + " " + p.Vel + " | " + live);
                }
                if (unknown.Count > 0) sb.AppendLine("  NOT IN TABLE: " + string.Join(", ", unknown.ToArray()));
            }
            catch (Exception e) { sb.AppendLine("  weapon dump: " + e.Message); }
        }

        static void Garment(StringBuilder sb, string slot, Il2Cpp.CharacterMod cm)
        {
            if (cm == null) return;
            try
            {
                string n = cm.gameObject.name;
                var g = Tables.Garment(n);
                sb.AppendLine("  " + slot + ": " + n + " | live W " + cm.Weight + " armour " + cm.isArmour + " " + cm.armourRating + " | table " + (g != null ? g.Weight + " kg " + g.Rating : "MISSING"));
            }
            catch (Exception e) { sb.AppendLine("  " + slot + ": " + e.Message); }
        }

        static string BenchDump()
        {
            var sb = new StringBuilder();
            sb.AppendLine("QUARTERMASTER bench dump " + DateTime.Now);
            var wms = Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<Il2Cpp.WeaponModding>());
            sb.AppendLine("WeaponModding objects: " + (wms != null ? wms.Length : 0));
            for (int i = 0; wms != null && i < wms.Length; i++)
            {
                var wm = wms[i].TryCast<Il2Cpp.WeaponModding>(); if (wm == null) continue;
                try
                {
                    sb.AppendLine("--- " + wm.gameObject.name + " active " + wm.gameObject.activeInHierarchy + " acceptInput " + wm.acceptInput
                                + " weapon " + (wm.Weapon != null && wm.Weapon.Weapon != null ? wm.Weapon.Weapon.gameObject.name : "none"));
                    var up = wm.UI_Parent;
                    if (up != null)
                    {
                        var cv = up.GetComponentInParent<Canvas>();
                        sb.AppendLine("  UI_Parent " + TPath(up) + " canvas " + (cv != null ? cv.gameObject.name + " " + cv.renderMode + " sort " + cv.sortingOrder : "none"));
                        Tree(sb, up, 0, 3);
                    }
                    var mdp = wm.Mod_Dropdown_Parent;
                    if (mdp != null)
                    {
                        var rows = mdp.GetComponentsInChildren<Il2Cpp.ModUI>(true);
                        sb.AppendLine("  ModUI rows: " + (rows != null ? rows.Length : 0));
                        for (int k = 0; rows != null && k < rows.Length; k++)
                        {
                            var mu = rows[k]; if (mu == null) continue;
                            var rt = mu.GetComponent<RectTransform>();
                            sb.AppendLine("   row '" + mu.attachmentTypeName + "' active " + mu.gameObject.activeInHierarchy + " rect " + (rt != null ? rt.rect.width.ToString("0") + "x" + rt.rect.height.ToString("0") : "?")
                                        + " socket " + (mu.modParent != null ? mu.modParent.ModParentName : "?") + " fitted " + (mu.modParent != null && mu.modParent.AttachedMod != null ? Handling.PartName(mu.modParent.AttachedMod) : "-"));
                            try
                            {
                                var dd = mu.dropdown;
                                var av = mu.availableModIndicies;
                                sb.AppendLine("     dropdown expanded " + (dd != null && dd.IsExpanded) + " value " + (dd != null ? dd.value : -1) + " options " + (dd != null && dd.options != null ? dd.options.Count : 0) + " indices " + (av != null ? av.Count : 0));
                                if (dd != null && dd.options != null)
                                    for (int o = 0; o < dd.options.Count && o < 60; o++)
                                        sb.AppendLine("       [" + o + "] " + dd.options[o].text + (av != null && o < av.Count ? "  -> catalog " + av[o] : ""));
                                var mis = mu.ModInfoSection;
                                sb.AppendLine("     ModInfoSection " + (mis != null ? mis.rect.width.ToString("0") + "x" + mis.rect.height.ToString("0") + " children " + mis.childCount : "null"));
                                if (mis != null) Tree(sb, mis, 3, 2);
                                var tt = mu.attachmentTypeText;
                                if (tt != null) sb.AppendLine("     font " + (tt.font != null ? tt.font.name : "?") + " size " + tt.fontSize + " color " + tt.color);
                                GameObject list = null; try { list = dd != null ? dd.m_Dropdown : null; } catch { }
                                if (list != null) { sb.AppendLine("     Dropdown List live:"); Tree(sb, list.transform, 3, 4); }
                            }
                            catch (Exception e) { sb.AppendLine("     dropdown: " + e.Message); }
                        }
                    }
                    var mm = wm.MagManager;
                    if (mm != null)
                    {
                        sb.AppendLine("  MagManager active " + mm.gameObject.activeInHierarchy);
                        var rws = mm.GetComponentsInChildren<Il2Cpp.MagManagerMagUI>(true);
                        for (int k = 0; rws != null && k < rws.Length; k++)
                        {
                            var r = rws[k]; if (r == null) continue;
                            sb.AppendLine("   mag row " + (r.MagNameText != null ? r.MagNameText.text : "?") + " | " + (r.AmmoNameText != null ? r.AmmoNameText.text : "?"));
                        }
                    }
                }
                catch (Exception e) { sb.AppendLine("  bench: " + e.Message); }
            }
            return sb.ToString();
        }

        static string TPath(Transform t)
        {
            var sb = new StringBuilder();
            int guard = 0;
            while (t != null && guard++ < 20) { sb.Insert(0, "/" + t.name); t = t.parent; }
            return sb.ToString();
        }

        static void Tree(StringBuilder sb, Transform t, int depth, int max)
        {
            if (t == null || depth > max) return;
            var rt = t.TryCast<RectTransform>();
            string comps = "";
            try
            {
                var cs = t.GetComponents<Component>();
                for (int i = 0; cs != null && i < cs.Length && i < 6; i++) if (cs[i] != null) comps += (comps.Length > 0 ? "," : "") + cs[i].GetIl2CppType().Name;
            }
            catch { }
            string txt = "";
            try { var tm = t.GetComponent<TextMeshProUGUI>(); if (tm != null) txt = " text='" + (tm.text ?? "").Replace("\n", " ") + "'"; } catch { }
            sb.AppendLine(new string(' ', depth * 2) + t.name + (t.gameObject.activeSelf ? "" : " (inactive)") + " [" + comps + "]"
                        + (rt != null ? " " + rt.rect.width.ToString("0") + "x" + rt.rect.height.ToString("0") : "") + txt);
            int n = t.childCount;
            for (int i = 0; i < n && i < 40; i++) Tree(sb, t.GetChild(i), depth + 1, max);
        }
    }
}
