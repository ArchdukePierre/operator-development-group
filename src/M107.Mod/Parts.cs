// ODG Operations - M107
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Text;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace M107
{

    internal static class Parts
    {
        internal const string Compat = "M107";
        internal const string Prefix = "M107 ";

        internal static bool Registered;
        internal static bool FullSet;
        internal static readonly Dictionary<int, GameObject> ById = new Dictionary<int, GameObject>();
        internal static readonly Dictionary<string, int> IdByName = new Dictionary<string, int>(StringComparer.Ordinal);

        internal static readonly Dictionary<string, string> NameMap = new Dictionary<string, string>(StringComparer.Ordinal);

        internal static readonly Dictionary<int, int> IdMap = new Dictionary<int, int>();

        internal struct Offer
        {
            internal int Id;
            internal Il2Cpp.WeaponMod.AttachmentType Type;
            internal bool Exclusive;
            internal string Name;
        }

        internal static int AmmoId = -1;
        internal static string AmmoOurName, AmmoDonorName;
        internal static readonly List<Offer> Offers = new List<Offer>();

        internal static readonly Dictionary<int, string> NameById = new Dictionary<int, string>();

        internal static readonly Dictionary<string, string> NameByPath =
            new Dictionary<string, string>(StringComparer.Ordinal);

        internal static void BuildPathMap()
        {
            try
            {
                NameByPath.Clear();
                foreach (var d in Defs)
                {
                    if (d.SocketPath == null || !IdByName.ContainsKey(d.Name)) continue;
                    NameByPath[string.Join("/", d.SocketPath)] = d.Name;
                }
            }
            catch { }
        }

        class Def
        {
            internal string Name;
            internal string Display;
            internal string Donor;
            internal string[] MeshKeys;
            internal bool InBaseBuild;

            internal bool Exclusive;
            internal string[] SocketPath;

            internal string[] IconMeshes;
            internal Vector3 IconView;
            internal Color IconTint;
        }

        static readonly Vector3 SideOn = new Vector3(0f, 90f, 0f);

        const int IconSize = 256;

        internal struct IconJob
        {
            internal int Id;
            internal string[] Meshes;
            internal Vector3 View;
            internal Color Tint;
            internal string Name;
        }
        internal static readonly Queue<IconJob> IconQueue = new Queue<IconJob>();

        internal static bool RenderOneQueuedIcon()
        {
            try
            {
                if (IconQueue.Count == 0) return false;
                var j = IconQueue.Dequeue();

                var lm = Il2Cpp.LoadoutManager.singleton;
                var infos = lm == null ? null : lm.WeaponModInfo;
                if (infos == null || j.Id < 0 || j.Id >= infos.Length) return true;
                var info = infos[j.Id];
                if (info == null) return true;

                var spr = Icons.Render(j.Meshes, IconSize, j.View, j.Tint, j.Name);
                if (spr != null) { try { info.UiIcon = spr; } catch { } }
                return true;
            }
            catch { return true; }
        }

        static readonly Def[] Defs =
        {

            new Def { Name = Prefix + "12.7x99mm NATO", Display = "12.7x99mm NATO",
                      Donor = "762x54mmR PS", InBaseBuild = true, Exclusive = true,
                      SocketPath = new[] { "MOD_MAGAZINE", "MOD_Ammo" },
                      MeshKeys = new[] { "ammo_bullet", "ammo_case" },
                      IconMeshes = new[] { "ammo_bullet", "ammo_case" },
                      IconView = SideOn, IconTint = new Color(0.86f, 0.69f, 0.31f) },

            new Def { Name = Prefix + "10rnd Magazine", Display = "M107A1 10rnd .50 Magazine",
                      Donor = "SVD 10rnd Mag", InBaseBuild = true, Exclusive = true,
                      SocketPath = new[] { "MOD_MAGAZINE" },
                      MeshKeys = new[] { "magazine_body", "magazine_follower", "magazine_floor_plate" },
                      IconMeshes = new[] { "magazine_body", "magazine_floor_plate" },
                      IconView = SideOn, IconTint = new Color(0.70f, 0.72f, 0.76f) },

            new Def { Name = Prefix + "Front Sight", Display = "M107A1 Folding Front Sight",
                      Donor = "SVD Front Iron sight", InBaseBuild = true,
                      SocketPath = new[] { "MOD_Barrel", "MOD_Muzzle", "MOD_Ironsight" },
                      MeshKeys = new[] { "front_sight" } },

            new Def { Name = Prefix + "Rear Sight", Display = "M107A1 Folding Rear Sight",
                      Donor = "SVD Rear Ironsight", InBaseBuild = true,
                      SocketPath = new[] { "MOD_Upper", "MOD_RearIronsight" },
                      MeshKeys = new[] { "rear_sight" } },

            new Def { Name = Prefix + "Muzzle Brake", Display = "M107A1 Arrowhead Brake",
                      Donor = "SVD Flashhider", InBaseBuild = true, Exclusive = true,
                      SocketPath = new[] { "MOD_Barrel", "MOD_Muzzle" },
                      MeshKeys = new[] { "muzle_brake" } },

            new Def { Name = Prefix + "Barrel", Display = "M107A1 29in Fluted Barrel",
                      Donor = "SVD 560mm Barrel", InBaseBuild = true, Exclusive = true,
                      SocketPath = new[] { "MOD_Barrel" },
                      MeshKeys = new[] { "barrel_body", "barrel_spring" } },

            new Def { Name = Prefix + "Receiver", Display = "M107A1 Upper and Lower",
                      Donor = "SVD ChassisMK1", InBaseBuild = true, Exclusive = true,
                      SocketPath = new[] { "MOD_Upper" },
                      MeshKeys = null },
        };

        static readonly string[] NeverInclude =
        {
            "silencer",
            "supressor",
            "suppressor",
            "stsc",
            "riflescope",
        };

        static Def ClaimantOf(string meshName)
        {
            foreach (var x in NeverInclude)
                if (meshName.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0) return null;
            foreach (var d in Defs)
            {
                if (d.MeshKeys == null) return d;
                foreach (var k in d.MeshKeys)
                    if (meshName.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return d;
            }
            return null;
        }

        internal static void Probe(Il2Cpp.LoadoutManager lm, GameObject weapon)
        {
            var sb = new StringBuilder();
            sb.Append("\n===== PARTS PROBE =====\n");
            try
            {
                sb.Append("GetNumWeaponMods() = ").Append(Il2Cpp.LoadoutManager.GetNumWeaponMods()).Append('\n');
                var infos = lm.WeaponModInfo;
                var cache = lm.WeaponModCompatibilityCache;
                var stripped = lm.AllStrippedWeaponMods;
                sb.Append("WeaponModInfo len = ").Append(infos == null ? -1 : infos.Length)
                  .Append("   CompatCache len = ").Append(cache == null ? -1 : cache.Length)
                  .Append("   AllStripped len = ").Append(stripped == null ? -1 : stripped.Length).Append('\n');

                if (cache != null)
                {
                    sb.Append("compat cache (all ").Append(cache.Length).Append("):\n");
                    for (int i = 0; i < cache.Length; i++)
                    {
                        sb.Append(" [").Append(i).Append("]").Append(cache[i]);
                        if ((i % 8) == 7) sb.Append('\n');
                    }
                    sb.Append('\n');
                }

                try
                {
                    var gw = lm.Game_Weapons;
                    for (int i = 0; i < gw.Count; i++)
                    {
                        var g = gw[i]; if (g == null) continue;
                        string gn = g.name;
                        if (gn != "SR-25" && gn != "MK18" && gn != "HK416A5" && gn != "R700") continue;
                        var ss = g.GetComponentsInChildren<Il2Cpp.WeaponModParent>(true);
                        sb.Append("reference '").Append(gn).Append("' sockets: ").Append(ss == null ? 0 : ss.Length).Append('\n');
                        if (ss == null) continue;
                        for (int k = 0; k < ss.Length; k++)
                        {
                            var s2 = ss[k]; if (s2 == null) continue;
                            sb.Append("    ").Append(Safe(() => s2.ModParentName))
                              .Append("  compat='").Append(Safe(() => s2.Mod_Compatibility)).Append("'")
                              .Append("  acceptNull=").Append(Safe(() => s2.acceptNullCompatibility.ToString())).Append('\n');
                        }
                    }
                }
                catch (Exception e) { sb.Append("reference socket probe err: ").Append(e.Message).Append('\n'); }

                if (infos != null)
                    foreach (var d in Defs)
                    {
                        int idx = IndexOfInfo(infos, d.Donor);
                        sb.Append("  donor '").Append(d.Donor).Append("' -> ");
                        if (idx < 0) sb.Append("NOT FOUND\n");
                        else sb.Append("id ").Append(idx).Append("  type=").Append(infos[idx].AttachmentType)
                               .Append("  compat='").Append(SafeCompat(infos[idx])).Append("'\n");
                    }
            }
            catch (Exception e) { sb.Append("registry probe err: ").Append(e).Append('\n'); }

            try
            {
                if (weapon != null)
                {
                    var sockets = weapon.GetComponentsInChildren<Il2Cpp.WeaponModParent>(true);
                    sb.Append("sockets on ").Append(weapon.name).Append(": ").Append(sockets == null ? 0 : sockets.Length).Append('\n');
                    if (sockets != null)
                        for (int i = 0; i < sockets.Length; i++)
                        {
                            var s = sockets[i]; if (s == null) continue;
                            sb.Append("  [").Append(i).Append("] ").Append(Safe(() => s.ModParentName))
                              .Append("  compat='").Append(Safe(() => s.Mod_Compatibility)).Append("'")
                              .Append("  full='").Append(Safe(() => s.FullName)).Append("'")
                              .Append("  acceptNull=").Append(Safe(() => s.acceptNullCompatibility.ToString()))
                              .Append("  attached=").Append(Safe(() => s.AttachedMod == null ? "none" : s.AttachedMod.name))
                              .Append('\n');
                        }
                }
            }
            catch (Exception e) { sb.Append("socket probe err: ").Append(e).Append('\n'); }

            sb.Append("===== END PARTS PROBE =====");
            M107Mod.Log(sb.ToString());
        }

        static int IndexOfInfo(Il2CppReferenceArray<Il2Cpp.WeaponModInfo> infos, string name)
        {
            for (int i = 0; i < infos.Length; i++)
            {
                var inf = infos[i]; if (inf == null) continue;
                string n; try { n = inf.Name; } catch { continue; }
                if (string.Equals(n, name, StringComparison.Ordinal)) return i;
            }
            return -1;
        }

        static string SafeCompat(Il2Cpp.WeaponModInfo i) { try { return i.GetCompatibilityString(); } catch { return "<err>"; } }
        static string Safe(Func<string> f) { try { return f() ?? "null"; } catch { return "<err>"; } }

        internal static bool RegisterAll(Il2Cpp.LoadoutManager lm)
        {
            if (Registered) return true;
            if (lm == null || !BarrettArt.Ready) return false;

            Il2CppReferenceArray<Il2Cpp.WeaponModInfo> oldInfos = null;
            Il2CppStringArray oldCache = null;
            Il2CppReferenceArray<UnityEngine.AddressableAssets.AssetReferenceGameObject> oldStripped = null;

            try
            {
                oldInfos = lm.WeaponModInfo;
                oldCache = lm.WeaponModCompatibilityCache;
                oldStripped = lm.AllStrippedWeaponMods;
                if (oldInfos == null || oldCache == null || oldStripped == null)
                { M107Mod.Log("parts: a registry array is null, standing down."); return false; }

                int compatIdx = -1;
                for (int i = 0; i < oldCache.Length; i++)
                    if (string.Equals(oldCache[i], Compat, StringComparison.Ordinal)) { compatIdx = i; break; }
                if (compatIdx < 0)
                {
                    var nc = new Il2CppStringArray(oldCache.Length + 1);
                    for (int i = 0; i < oldCache.Length; i++) nc[i] = oldCache[i];
                    nc[oldCache.Length] = Compat;
                    compatIdx = oldCache.Length;
                    lm.WeaponModCompatibilityCache = nc;
                }
                M107Mod.Log("parts: compatibility '" + Compat + "' at cache index " + compatIdx);

                var holder = new GameObject("M107_PartsHolder");
                UnityEngine.Object.DontDestroyOnLoad(holder);
                holder.SetActive(false);

                var built = new List<KeyValuePair<Def, GameObject>>();
                var donorIdx = new List<int>();
                foreach (var d in Defs)
                {
                    int di = IndexOfInfo(oldInfos, d.Donor);
                    if (di < 0) { M107Mod.Log("parts: donor '" + d.Donor + "' not in registry, '" + d.Name + "' skipped"); continue; }

                    GameObject donorPrefab = null;

                    try { donorPrefab = lm.GetWeaponMod(di); } catch (Exception e) { M107Mod.Log("parts: GetWeaponMod(" + di + ") threw: " + e.Message); }
                    if (donorPrefab == null) { M107Mod.Log("parts: donor prefab for '" + d.Donor + "' is null (id " + di + "), '" + d.Name + "' skipped"); continue; }

                    var go = BuildPart(d, donorPrefab, holder.transform);
                    if (go == null) continue;
                    built.Add(new KeyValuePair<Def, GameObject>(d, go));
                    donorIdx.Add(di);
                }
                if (built.Count == 0) { M107Mod.Log("parts: nothing built, standing down."); return false; }

                int baseId = oldStripped.Length;
                int add = built.Count;

                var newInfos = new Il2CppReferenceArray<Il2Cpp.WeaponModInfo>(oldInfos.Length + add);
                for (int i = 0; i < oldInfos.Length; i++) newInfos[i] = oldInfos[i];
                var newStripped = new Il2CppReferenceArray<UnityEngine.AddressableAssets.AssetReferenceGameObject>(oldStripped.Length + add);
                for (int i = 0; i < oldStripped.Length; i++) newStripped[i] = oldStripped[i];

                for (int k = 0; k < add; k++)
                {
                    var d = built[k].Key;
                    var go = built[k].Value;
                    int id = baseId + k;
                    var donorInfo = oldInfos[donorIdx[k]];

                    var info = new Il2Cpp.WeaponModInfo();
                    info.Name = d.Name;
                    info.DisplayName = d.Display;

                    int donorCompat = compatIdx;
                    try { donorCompat = donorInfo.Compatibility; } catch { }
                    info.Compatibility = d.Exclusive ? compatIdx : donorCompat;
                    try { info.AttachmentType = donorInfo.AttachmentType; } catch { }
                    try { info.UiIcon = donorInfo.UiIcon; } catch { }

                    if (d.IconMeshes != null)
                        IconQueue.Enqueue(new IconJob
                        {
                            Id = id,
                            Meshes = d.IconMeshes,
                            View = d.IconView,
                            Tint = d.IconTint,
                            Name = "M107_icon_" + d.Donor,
                        });

                    newInfos[oldInfos.Length + k] = info;

                    newStripped[oldStripped.Length + k] = oldStripped.Length > 0 ? oldStripped[0] : null;

                    var wm = go.GetComponent<Il2Cpp.WeaponMod>();
                    if (wm == null) wm = go.GetComponentInChildren<Il2Cpp.WeaponMod>(true);
                    if (wm != null)
                    {

                        try { wm.SetAssetReferenceIndex(id); } catch { try { wm.AssetReferenceIndex = id; } catch { } }

                        try { wm.DisplayNameOverride = d.Display; } catch { }

                        try { wm._assetRefInit = true; } catch { }
                    }
                    else M107Mod.Log("parts: WARNING '" + d.Name + "' has no WeaponMod component");

                    go.name = d.Name;
                    ById[id] = go;
                    IdByName[d.Name] = id;
                    NameById[id] = d.Name;
                    IdMap[donorIdx[k]] = id;
                    try
                    {
                        Offers.Add(new Offer { Id = id, Type = info.AttachmentType, Exclusive = d.Exclusive, Name = d.Name });
                        if (info.AttachmentType == Il2Cpp.WeaponMod.AttachmentType.Ammunition)
                        { AmmoId = id; AmmoOurName = d.Name; AmmoDonorName = d.Donor; }
                    }
                    catch { }
                    if (d.InBaseBuild) NameMap[d.Donor] = d.Name;

                    M107Mod.Log("parts: [" + id + "] '" + d.Name + "' from donor '" + d.Donor +
                                "' type=" + Safe(() => info.AttachmentType.ToString()) +
                                " meshes=" + go.GetComponentsInChildren<MeshFilter>(true).Length);
                }

                lm.WeaponModInfo = newInfos;
                lm.AllStrippedWeaponMods = newStripped;
                BuildPathMap();

                foreach (var kv in built)
                    if (kv.Key.Name.EndsWith("12.7x99mm NATO", StringComparison.Ordinal))
                    { BuildRoundVisuals(holder.transform, kv.Value); break; }

                int needed = 0; foreach (var d in Defs) if (d.InBaseBuild) needed++;
                FullSet = NameMap.Count >= needed;
                Registered = true;

                M107Mod.Log("parts: registered " + add + " part(s), ids " + baseId + ".." + (baseId + add - 1) +
                            "; base-build coverage " + NameMap.Count + "/" + needed +
                            (FullSet ? "  (complete - the rifle can be built entirely from its own parts)"
                                     : "  (INCOMPLETE - staying on the donor's build)"));
                return true;
            }
            catch (Exception e)
            {
                M107Mod.Log("parts: registration FAILED, rolling the registry back: " + e);
                try { if (oldInfos != null) lm.WeaponModInfo = oldInfos; } catch { }
                try { if (oldCache != null) lm.WeaponModCompatibilityCache = oldCache; } catch { }
                try { if (oldStripped != null) lm.AllStrippedWeaponMods = oldStripped; } catch { }
                ById.Clear(); IdByName.Clear(); NameMap.Clear();
                Registered = false; FullSet = false;
                return false;
            }
        }

        static GameObject BuildPart(Def d, GameObject donorPrefab, Transform holder)
        {
            try
            {
                var go = UnityEngine.Object.Instantiate(donorPrefab, holder).TryCast<GameObject>();
                if (go == null) { M107Mod.Log("parts: clone of '" + d.Donor + "' returned null"); return null; }
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;

                int off = 0;
                var rends = go.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < rends.Length; i++)
                {
                    if (rends[i] == null) continue;
                    try { rends[i].enabled = false; rends[i].forceRenderingOff = true; off++; } catch { }
                }

                var art = BarrettArt.Attach(go.transform, d.Name + "_art");
                if (art == null) { M107Mod.Log("parts: no art for '" + d.Name + "'"); UnityEngine.Object.Destroy(go); return null; }

                int kept = 0, skipped = 0;
                var all = art.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    var t = all[i]; if (t == null || t == art.transform) continue;
                    if (t.parent != art.transform) continue;
                    string n; try { n = t.name; } catch { continue; }
                    if (ClaimantOf(n) == d)
                    {
                        if (t.gameObject.activeSelf) kept++;
                        else skipped++;
                    }
                    else UnityEngine.Object.DestroyImmediate(t.gameObject);
                }
                if (skipped > 0) M107Mod.Log("parts: '" + d.Name + "' left " + skipped + " mesh(es) switched off, as shipped");

                if (kept == 0)
                {
                    M107Mod.Log("parts: '" + d.Name + "' matched no Barrett meshes, dropped");
                    UnityEngine.Object.Destroy(go);
                    return null;
                }
                M107Mod.Log("parts: '" + d.Name + "' donor renderers off=" + off + ", Barrett meshes kept=" + kept);

                FixMagazineSystem(go);
                return go;
            }
            catch (Exception e) { M107Mod.Log("parts: build '" + d.Name + "' err: " + e.Message); return null; }
        }

        internal static int FixMagazineSystem(GameObject go)
        {
            int n = 0;
            try
            {
                if (go == null) return 0;
                var mags = go.GetComponentsInChildren<Il2Cpp.Magazine>(true);
                if (mags == null) return 0;
                for (int i = 0; i < mags.Length; i++)
                {
                    var m = mags[i]; if (m == null) continue;

                    try
                    {
                        var cur = m.compatibleWeaponSystems;
                        if (cur != null && cur.Length > 0)
                        {
                            bool already = true;
                            string had = "";
                            for (int k = 0; k < cur.Length; k++)
                            {
                                had += (had.Length > 0 ? "," : "") + cur[k];
                                if (!string.Equals(cur[k], Compat, StringComparison.Ordinal)) already = false;
                            }
                            if (already) continue;
                            for (int k = 0; k < cur.Length; k++) cur[k] = Compat;
                            n++;
                            M107Mod.Log("mag: '" + go.name + "' fits [" + had + "] -> now fits [" + Compat + "] (in place)");
                        }
                        else
                        {

                            var arr = new Il2CppStringArray(1);
                            arr[0] = Compat;
                            m.compatibleWeaponSystems = arr;
                            n++;
                            M107Mod.Log("mag: '" + go.name + "' had no compatibility array, given [" + Compat + "]");
                        }
                    }
                    catch (Exception e) { M107Mod.Log("mag: could not set compatibleWeaponSystems: " + e.Message); }
                }
            }
            catch { }
            return n;
        }

        internal static string BuildBaseJson(string donorJson)
        {
            try
            {
                var use = new List<Def>();
                foreach (var d in Defs)
                    if (d.InBaseBuild && d.SocketPath != null && IdByName.ContainsKey(d.Name)) use.Add(d);
                if (use.Count == 0) return null;

                use.Sort((a, b) => a.SocketPath.Length.CompareTo(b.SocketPath.Length));

                string eye = "-0.011091679334640503";
                try
                {
                    const string tag = "\"EyeOffset\":";
                    int i = donorJson == null ? -1 : donorJson.IndexOf(tag, StringComparison.Ordinal);
                    if (i >= 0)
                    {
                        int s = i + tag.Length, e = s;
                        while (e < donorJson.Length && donorJson[e] != '}' && donorJson[e] != ',') e++;
                        var v = donorJson.Substring(s, e - s).Trim();
                        if (v.Length > 0) eye = v;
                    }
                }
                catch { }

                var names = new StringBuilder();
                var dirs = new StringBuilder();
                var pos = new StringBuilder();
                for (int i = 0; i < use.Count; i++)
                {
                    if (i > 0) { names.Append(','); dirs.Append(','); pos.Append(','); }
                    names.Append('"').Append(use[i].Name).Append('"');
                    dirs.Append("{\"path\":[");
                    for (int k = 0; k < use[i].SocketPath.Length; k++)
                    {
                        if (k > 0) dirs.Append(',');
                        dirs.Append('"').Append(use[i].SocketPath[k]).Append('"');
                    }
                    dirs.Append("]}");
                    pos.Append("{\"x\":0.0,\"y\":0.0,\"z\":0.0}");
                }

                string json = "{\"WeaponModName\":[" + names + "],\"ModParentDirectory\":[" + dirs +
                              "],\"WeaponModPosition\":[" + pos + "],\"EyeOffset\":" + eye + "}";
                M107Mod.Log("parts: wrote a base build of " + use.Count + " part(s), no optic (rail left bare)");
                return json;
            }
            catch (Exception e) { M107Mod.Log("parts: base build write err: " + e.Message); return null; }
        }

        static readonly string[] ClaimSockets =
        { "Magazine", "Barrel", "Upper", "Ammo", "Muzzle",
          "MOD_MAGAZINE", "MOD_Barrel", "MOD_Upper", "MOD_Ammo", "MOD_Muzzle" };
        static readonly string[] NeverClaim =
        { "Dovetail", "Ironsight", "IronSight", "Sight", "Mount", "Rail", "Optic", "ChargingHandle" };

        static readonly HashSet<int> _railSeen = new HashSet<int>();
        static readonly HashSet<int> _railRegistered = new HashSet<int>();

        internal static void ResetForScene() { _railSeen.Clear(); _railRegistered.Clear(); }

        internal static int MakeTopRail(GameObject weapon)
        {
            int n = 0;
            try
            {
                var sockets = weapon.GetComponentsInChildren<Il2Cpp.WeaponModParent>(true);
                if (sockets == null) return 0;
                for (int i = 0; i < sockets.Length; i++)
                {
                    var s = sockets[i]; if (s == null) continue;
                    int iid = 0; try { iid = s.GetInstanceID(); } catch { continue; }
                    if (_railSeen.Contains(iid))
                    {
                        if (_railRegistered.Contains(iid)) Alignment.RegisterRailSocket(s);
                        continue;
                    }
                    string nm; try { nm = s.ModParentName; } catch { continue; }
                    if (string.IsNullOrEmpty(nm)) continue;
                    bool isRail = nm.IndexOf("Top Rail", StringComparison.OrdinalIgnoreCase) >= 0
                               || nm.IndexOf("Dovetail", StringComparison.OrdinalIgnoreCase) >= 0;

                    bool isSight = string.Equals(nm, "Sight", StringComparison.OrdinalIgnoreCase);
                    if (!isRail && !isSight) continue;

                    if (!TakesAnOptic(s))
                    {
                        _railSeen.Add(iid);
                        M107Mod.Log("rail: '" + nm + "' takes lights and rail covers, not optics - left alone");
                        continue;
                    }

                    if (isRail)
                    {
                        try { s.ModParentName = "Piccatinny"; } catch { }
                        try { s.Mod_Compatibility = ""; } catch { }
                        try { s.acceptNullCompatibility = true; } catch { }

                        try { s.enableOffset = true; s.MinOffset = -0.20f; s.MaxOffset = 0.22f; } catch { }
                        M107Mod.Log("rail: '" + nm + "' is now the top Piccatinny (empty compat, accepts null, slides -0.20..0.22)");
                    }
                    else
                        M107Mod.Log("rail: 'Sight' socket registered for placement - this is the one the optic mounts on");

                    _railSeen.Add(iid);
                    _railRegistered.Add(iid);
                    Alignment.RegisterRailSocket(s);
                    n++;
                    DropDovetailOptions(s);
                }
            }
            catch (Exception e) { M107Mod.Log("rail: err " + e.Message); }
            return n;
        }

        static readonly string[] DovetailWords = { "Dovetail", "Side Rail", "SideRail", "AK Rail" };

        static bool TakesAnOptic(Il2Cpp.WeaponModParent s)
        {
            try
            {
                var types = s.attachmentType;
                if (types == null || types.Count == 0) return true;
                for (int i = 0; i < types.Count; i++)
                {
                    string t = types[i].ToString();
                    if (t.IndexOf("Sight", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    if (t.IndexOf("Dovetail", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    if (t.IndexOf("RMR", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    if (t.IndexOf("MicroSight", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
            }
            catch { return true; }
            return false;
        }

        static void DropDovetailOptions(Il2Cpp.WeaponModParent s)
        {
            try
            {
                var lm = Il2Cpp.LoadoutManager.singleton;
                var infos = lm == null ? null : lm.WeaponModInfo;
                var ids = s.CompatibleModIds;
                if (infos == null || ids == null) return;

                try
                {
                    var types = s.attachmentType;
                    var t = new StringBuilder();
                    if (types != null) for (int i = 0; i < types.Count; i++) t.Append(types[i]).Append(' ');
                    M107Mod.Log("rail: socket accepts types [" + t.ToString().Trim() + "], " + ids.Count + " part(s) offered");
                }
                catch { }

                var keep = new Il2CppSystem.Collections.Generic.List<int>();
                var dropped = new StringBuilder();
                var kept = new StringBuilder();
                for (int i = 0; i < ids.Count; i++)
                {
                    int id = ids[i];
                    string nm = null;
                    try { if (id >= 0 && id < infos.Length && infos[id] != null) nm = infos[id].Name; } catch { }
                    bool dove = false;
                    if (!string.IsNullOrEmpty(nm))
                        foreach (var w in DovetailWords)
                            if (nm.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) { dove = true; break; }
                    if (dove) { dropped.Append('\'').Append(nm).Append("' "); continue; }
                    keep.Add(id);
                    if (kept.Length < 400) kept.Append('\'').Append(nm ?? ("id" + id)).Append("' ");
                }

                if (keep.Count != ids.Count)
                {
                    s.CompatibleModIds = keep;
                    M107Mod.Log("rail: dropped " + (ids.Count - keep.Count) + " dovetail option(s): " + dropped);
                }
                M107Mod.Log("rail: now offering " + keep.Count + " part(s): " + kept);
            }
            catch (Exception e) { M107Mod.Log("rail: option filter err: " + e.Message); }
        }

        internal static int RetagSockets(GameObject weapon)
        {
            int n = 0, left = 0;
            try
            {
                var sockets = weapon.GetComponentsInChildren<Il2Cpp.WeaponModParent>(true);
                if (sockets == null) return 0;
                for (int i = 0; i < sockets.Length; i++)
                {
                    var s = sockets[i]; if (s == null) continue;
                    string nm = null, gn = null;
                    try { nm = s.ModParentName; } catch { }
                    try { gn = s.name; } catch { }
                    string probe = (nm ?? "") + "" + (gn ?? "");
                    if (probe.Length <= 1) continue;
                    if (string.IsNullOrEmpty(nm)) nm = gn;

                    bool blocked = false;
                    foreach (var k in NeverClaim)
                        if (probe.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) { blocked = true; break; }
                    if (blocked) { left++; continue; }

                    bool claim = false;
                    foreach (var k in ClaimSockets)
                        if (probe.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) { claim = true; break; }
                    if (!claim) { left++; M107Mod.Log("parts: socket '" + nm + "' matched nothing, left as-is"); continue; }

                    try
                    {
                        s.Mod_Compatibility = Compat;
                        s.BuildCompatibleModIdList();
                        n++;
                        M107Mod.Log("parts: socket '" + nm + "' claimed for M107");
                    }
                    catch (Exception e) { M107Mod.Log("parts: retag '" + nm + "' failed: " + e.Message); }
                }
            }
            catch (Exception e) { M107Mod.Log("parts: retag err: " + e.Message); }
            M107Mod.Log("parts: " + n + " socket(s) claimed, " + left + " left open for the game's sights and mounts.");
            return n;
        }

        internal static GameObject ShellMesh, BulletMesh;

        static Transform _roundHolder;
        internal static void EnsureRoundVisuals(Il2Cpp.BulletShell live)
        {
            try
            {
                if (ShellMesh != null || live == null) return;
                if (_roundHolder == null)
                {
                    var h = new GameObject("M107_RoundHolder");
                    UnityEngine.Object.DontDestroyOnLoad(h);
                    h.SetActive(false);
                    _roundHolder = h.transform;
                }
                GameObject dShell = null, dBullet = null;
                try { dShell = live.Mesh_Shell; } catch { }
                try { dBullet = live.Mesh_Bullet; } catch { }
                if (dShell == null && dBullet == null) return;

                ShellMesh = Reskin(dShell, "ammo_case", "M107_Case_127", _roundHolder, EjectedCaseScale);
                BulletMesh = Reskin(dBullet, "ammo_bullet", "M107_Bullet_127", _roundHolder, EjectedCaseScale);
                M107Mod.Log("round: built from a live round - 12.7mm case=" + (ShellMesh == null ? "FAILED" : "ready") +
                            "  projectile=" + (BulletMesh == null ? "FAILED" : "ready"));
            }
            catch (Exception e) { M107Mod.Log("round: live build err: " + e.Message); }
        }

        internal static void BuildRoundVisuals(Transform holder, GameObject ammoPart)
        {
            try
            {
                Il2Cpp.BulletShell bs = null;
                try { bs = ammoPart.GetComponent<Il2Cpp.BulletShell>(); } catch { }
                if (bs == null) { try { bs = ammoPart.GetComponentInChildren<Il2Cpp.BulletShell>(true); } catch { } }
                if (bs == null) { M107Mod.Log("round: no BulletShell on the ammo part, brass stays 7.62"); return; }

                GameObject dShell = null, dBullet = null;
                try { dShell = bs.Mesh_Shell; } catch { }
                try { dBullet = bs.Mesh_Bullet; } catch { }

                ShellMesh = Reskin(dShell, "ammo_case", "M107_Case_127", holder);
                BulletMesh = Reskin(dBullet, "ammo_bullet", "M107_Bullet_127", holder);
                M107Mod.Log("round: 12.7mm case=" + (ShellMesh == null ? "FAILED" : "ready") +
                            "  projectile=" + (BulletMesh == null ? "FAILED" : "ready"));
            }
            catch (Exception e) { M107Mod.Log("round: build err: " + e.Message); }
        }

        internal const float EjectedCaseScale = 1.8f;

        static GameObject Reskin(GameObject donor, string meshKey, string name, Transform holder, float scale = 1f)
        {
            try
            {
                if (donor == null) return null;
                if (!BarrettArt.FindMesh(meshKey, out var mesh, out var mat) || mesh == null)
                { M107Mod.Log("round: no Barrett mesh matching '" + meshKey + "'"); return null; }

                var clone = UnityEngine.Object.Instantiate(donor, holder).TryCast<GameObject>();
                if (clone == null) return null;
                clone.name = name;
                clone.transform.localPosition = Vector3.zero;
                clone.transform.localRotation = Quaternion.identity;
                if (scale != 1f) clone.transform.localScale = clone.transform.localScale * scale;

                int swapped = 0;
                var mfs = clone.GetComponentsInChildren<MeshFilter>(true);
                for (int i = 0; i < mfs.Length; i++)
                {
                    var mf = mfs[i]; if (mf == null) continue;
                    try { mf.sharedMesh = mesh; swapped++; } catch { }
                    try
                    {
                        var r = mf.gameObject.GetComponent<Renderer>();
                        if (r != null && mat != null) r.sharedMaterial = mat;
                    }
                    catch { }
                }
                if (swapped == 0) { M107Mod.Log("round: '" + name + "' had no MeshFilter to swap"); }
                return clone;
            }
            catch (Exception e) { M107Mod.Log("round: reskin '" + name + "' err: " + e.Message); return null; }
        }

        internal static string RemapIds(string json, out int changed)
        {
            changed = 0;
            try
            {
                if (string.IsNullOrEmpty(json) || IdMap.Count == 0) return json;
                const string tag = "\"ModIDs\":[";
                int s = json.IndexOf(tag, StringComparison.Ordinal);
                if (s < 0) return json;
                int open = s + tag.Length;
                int close = json.IndexOf(']', open);
                if (close < 0) return json;

                var body = json.Substring(open, close - open);
                if (body.Trim().Length == 0) return json;

                var parts = body.Split(',');
                var sb = new StringBuilder();
                for (int i = 0; i < parts.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    if (int.TryParse(parts[i].Trim(), out int id) && IdMap.TryGetValue(id, out int mine))
                    { sb.Append(mine); changed++; }
                    else sb.Append(parts[i].Trim());
                }
                if (changed == 0) return json;
                return json.Substring(0, open) + sb + json.Substring(close);
            }
            catch { return json; }
        }

        internal static bool TryGetPrefab(int id, out GameObject go) => ById.TryGetValue(id, out go);
        internal static bool TryGetId(string name, out int id) => IdByName.TryGetValue(name, out id);
    }
}
