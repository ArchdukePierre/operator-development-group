// ODG Operations - NewSlots
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace NewSlots
{

    internal static class ItemFactory
    {
        static GameObject _holder;

        static Transform Holder()
        {
            if (_holder != null) return _holder.transform;
            _holder = new GameObject("NewSlots_Templates");
            UnityEngine.Object.DontDestroyOnLoad(_holder);
            _holder.SetActive(false);
            return _holder.transform;
        }

        internal static int FindDonorIndex(Il2CppReferenceArray<Il2Cpp.CharacterModInfo> infos, string want)
        {
            if (infos == null || string.IsNullOrEmpty(want)) return -1;

            for (int i = 0; i < infos.Length; i++)
            {
                var row = infos[i]; if (row == null) continue;
                string nm = null;
                try { nm = row.ModName; } catch { }
                if (nm != null && string.Equals(nm.Trim(), want.Trim(), StringComparison.OrdinalIgnoreCase)) return i;
            }

            int hit = -1, hits = 0;
            for (int i = 0; i < infos.Length; i++)
            {
                var row = infos[i]; if (row == null) continue;
                string nm = null;
                try { nm = row.ModName; } catch { }
                if (nm != null && nm.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    hits++;
                    if (hit < 0) hit = i;
                }
            }
            if (hits > 1) NewSlotsMod.Detail("donor " + Fmt(want) + " matched " + hits + " rows, taking the first");
            return hit;
        }

        internal static Il2Cpp.CharacterMod Build(Def d, Il2Cpp.CharacterMod donor, Il2Cpp.CharacterModInfo donorInfo, int ownIndex, int donorIndex)
        {
            d.DonorIndex = donorIndex;
            if (donor == null) { NewSlotsMod.Detail("build " + d.ModName + ": donor is null"); return null; }

            GameObject clone = null;
            try
            {
                long _pInst = BuildPerf.Begin();
                clone = UnityEngine.Object.Instantiate(donor.gameObject, Holder());
                BuildPerf.End("Instantiate", _pInst);

                clone.name = d.ModName;

                var mod = clone.GetComponent<Il2Cpp.CharacterMod>();
                if (mod == null) { NewSlotsMod.Detail("build " + d.ModName + ": clone has no CharacterMod"); Kill(clone); return null; }

                try { mod.SetAssetReferenceIndex(ownIndex); } catch (Exception e) { NewSlotsMod.Detail("build " + d.ModName + ": SetAssetReferenceIndex threw: " + e.Message); }
                try { mod.ModInfoIndex = ownIndex; } catch { }

                if (d.KeepHead)
                {
                    try
                    {
                        mod.RemoveHead = false;
                        NewSlotsMod.Log("keepHead " + Fmt(d.ModName) + ": RemoveHead cleared");
                    }
                    catch (Exception e) { NewSlotsMod.Log("keepHead " + d.ModName + ": " + e.Message); }
                }

                try { mod._assetRefInit = true; } catch { }

                if (d.Type != Kind.Garment) NeutralizeMaterialLink(clone);

                long _pTex = BuildPerf.Begin();
                var tex = (d.Type == Kind.Garment && string.IsNullOrEmpty(d.Art)) ? null : Art.Texture(d);
                BuildPerf.End("Art.Texture", _pTex);
                d.Tex = tex;
                int painted = 0;
                var rends = clone.GetComponentsInChildren<Renderer>(true);

                if (d.Type == Kind.Camo)
                {

                    try
                    {
                        mod.baseColour = tex;
                        mod.modifyColour = true;
                        mod.textureModifer = true;
                        if (string.IsNullOrEmpty(mod.materialBaseColourReference))
                            mod.materialBaseColourReference = "_BaseColorMap";
                        painted = 1;
                        NewSlotsMod.Detail("build " + Fmt(d.ModName) + ": camo payload set  baseColourRef=" +
                                        Fmt(mod.materialBaseColourReference) + " materialIndex=" + mod.materialIndex +
                                        " textureModifer=" + mod.textureModifer);
                    }
                    catch (Exception e) { NewSlotsMod.Detail("build " + d.ModName + ": camo payload threw: " + e.Message); }
                }
                else if (d.Type == Kind.Garment)
                {
                    d.Clone = clone;
                    d.TexNormal = Art.Optional(d.ArtNormal, d.ModName + "_n", true);
                    d.TexMask = Art.Optional(d.ArtMask, d.ModName + "_m", true);
                    d.TexOpacity = Art.Optional(d.ArtOpacity, d.ModName + "_o", true);

                    d.Tex2 = Art.Optional(d.Art2, d.ModName + "_s1", false);
                    d.TexNormal2 = Art.Optional(d.ArtNormal2, d.ModName + "_s1n", true);
                    d.TexMask2 = Art.Optional(d.ArtMask2, d.ModName + "_s1m", true);

                    if (!string.IsNullOrEmpty(d.MeshFileName))
                    {
                        var swapped = MeshFile.Load(d.MeshFileName, d.ModName);
                        if (swapped != null)
                        {
                            int n = 0, skipped = 0;
                            try
                            {
                                var smrs = clone.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                                for (int i = 0; i < smrs.Length; i++)
                                {
                                    if (smrs[i] == null) continue;

                                    if (!string.IsNullOrEmpty(d.MeshTarget) && !TargetsMesh(smrs[i], d.MeshTarget))
                                    { skipped++; continue; }

                                    smrs[i].sharedMesh = swapped;

                                    try { smrs[i].localBounds = swapped.bounds; } catch { }
                                    n++;
                                }
                            }
                            catch (Exception e) { NewSlotsMod.Log("mesh swap " + d.ModName + ": " + e.Message); }
                            NewSlotsMod.Log("mesh swap " + Fmt(d.ModName) + ": " + n + " skinned renderer(s)" +
                                            (string.IsNullOrEmpty(d.MeshTarget) ? "" :
                                             "  target=" + Fmt(d.MeshTarget) + " left " + skipped + " alone"));

                            if (n == 0)
                                NewSlotsMod.Log("mesh swap " + Fmt(d.ModName) +
                                                ": TARGET MATCHED NO RENDERER, garment keeps donor geometry");
                        }
                    }

                    painted = 0;

                    try
                    {
                        Catalog.StreamAllowFor = mod.GetInstanceID();
                        mod._assetRefInit = false;
                        mod.TryInitAssetRef();
                    }
                    catch (Exception e) { NewSlotsMod.Detail("build " + d.ModName + ": stream kick threw: " + e.Message); }
                    finally { Catalog.StreamAllowFor = 0; }

                    try
                    {
                        var links = clone.GetComponentsInChildren<Il2Cpp.ModMaterialLink>(true);
                        int started = 0;
                        if (links != null)
                            for (int li = 0; li < links.Length; li++)
                            {
                                var link = links[li]; if (link == null) continue;
                                var refs = link.AllMaterialReferences;
                                if (refs == null) continue;
                                for (int ri = 0; ri < refs.Length; ri++)
                                {
                                    var mr = refs[ri]; if (mr == null) continue;
                                    try { mr.Initialize(); started++; } catch (Exception e2)
                                    { NewSlotsMod.Detail("build " + d.ModName + ": MaterialReference.Initialize threw: " + e2.Message); }
                                }
                            }
                        NewSlotsMod.Detail("build " + d.ModName + ": asked the game to load " + started + " donor material(s)");
                    }
                    catch (Exception e) { NewSlotsMod.Detail("build " + d.ModName + ": material load kick threw: " + e.Message); }
                }
                else if (rends != null)
                {

                    DonorBox(clone, out d.BoxSize, out d.BoxCentre, out d.WA, out d.HA, out d.TA);

                    for (int i = 0; i < rends.Length; i++)
                    {
                        var r = rends[i]; if (r == null) continue;

                        Material donorMat = null;
                        try { donorMat = r.sharedMaterial; } catch { }
                        var m = Art.Material(donorMat, tex, d.ModName);
                        if (m == null) continue;

                        if (d.ClipPatch) SetupClip(m, d.ModName);

                        try
                        {
                            var arr = new Il2CppReferenceArray<Material>(1);
                            arr[0] = m;
                            r.sharedMaterials = arr;
                            painted++;
                        }
                        catch (Exception e) { NewSlotsMod.Detail("build " + d.ModName + ": material assign threw: " + e.Message); }
                    }
                }

                PinMeshes(clone);

                d.Index = ownIndex;
                NewSlotsMod.Detail("build " + Fmt(d.ModName) + ": ok  index=" + ownIndex +
                                " renderers=" + (rends == null ? 0 : rends.Length) + " painted=" + painted +
                                " donor=" + Fmt(SafeName(donor)));
                return mod;
            }
            catch (Exception e)
            {
                NewSlotsMod.Detail("build " + d.ModName + " THREW: " + e.GetType().Name + ": " + e.Message);
                Kill(clone);
                return null;
            }
        }

        internal static Il2Cpp.CharacterModInfo BuildInfo(Def d, Il2Cpp.CharacterModInfo donorInfo, Texture2D iconSource)
        {
            var info = new Il2Cpp.CharacterModInfo();
            info.ModName = d.ModName;
            info.DisplayName = string.IsNullOrEmpty(d.Display) ? d.ModName : d.Display;
            try
            {
                if (donorInfo != null)
                {
                    info.ModSlot = donorInfo.ModSlot;
                    info.CharacterSlot = donorInfo.CharacterSlot;
                    info.Compatibility = donorInfo.Compatibility;
                }
            }
            catch (Exception e) { NewSlotsMod.Detail("info " + d.ModName + ": donor copy partial: " + e.Message); }

            try
            {
                d.TexIcon = Art.Optional(d.Icon, d.ModName + "_icon", false);
                var icon = Art.Icon(d.TexIcon ?? iconSource, d.ModName);
                if (icon != null) info.UiIcon = icon;
                else if (donorInfo != null) info.UiIcon = donorInfo.UiIcon;
            }
            catch { }

            info.isLocked = false;
            return info;
        }

        static int Repaint(Def d, GameObject clone, Material template = null)
        {
            long _pRep = BuildPerf.Begin();
            try { return RepaintInner(d, clone, template); }
            finally { BuildPerf.End("Repaint", _pRep); }
        }

        static int RepaintInner(Def d, GameObject clone, Material template = null)
        {
            int painted = 0;
            try
            {
                var rends = clone.GetComponentsInChildren<Renderer>(true);
                if (rends == null) return 0;
                for (int i = 0; i < rends.Length; i++)
                {
                    var r = rends[i]; if (r == null) continue;
                    Material src = template;
                    if (src == null) { try { src = r.sharedMaterial; } catch { } }

                    var m = Art.Material(src, d.Tex, d.ModName);
                    if (m == null) continue;
                    Set(m, "_NormalMap", d.TexNormal);
                    Set(m, "_BumpMap", d.TexNormal);
                    Set(m, "_MaskMap", d.TexMask);
                    Set(m, "_OpacityMap", d.TexOpacity);
                    try
                    {
                        var arr = new Il2CppReferenceArray<Material>(1);
                        arr[0] = m;
                        r.sharedMaterials = arr;
                        painted++;
                    }
                    catch (Exception e) { NewSlotsMod.Detail("repaint " + d.ModName + ": assign threw: " + e.Message); }
                }
            }
            catch (Exception e) { NewSlotsMod.Detail("repaint " + d.ModName + " threw: " + e.Message); }
            return painted;
        }

        static void Set(Material m, string prop, Texture t)
        {
            if (t == null) return;
            try { if (m.HasProperty(prop)) m.SetTexture(prop, t); } catch { }
        }

        internal static void SetupClip(Material m, string name)
        {
            if (m == null) return;
            bool viaApi = false;
            try
            {
                UnityEngine.Rendering.HighDefinition.HDMaterial.SetAlphaClipping(m, true);
                UnityEngine.Rendering.HighDefinition.HDMaterial.SetAlphaCutoff(m, 0.45f);
                viaApi = true;
            }
            catch (Exception e) { NewSlotsMod.Detail("clip: HDMaterial setters unavailable (" + e.GetType().Name + ")"); }

            try
            {
                m.SetFloat("_AlphaCutoffEnable", 1f);
                m.SetFloat("_AlphaCutoff", 0.45f);
                m.SetFloat("_UseShadowThreshold", 1f);
                m.EnableKeyword("_ALPHATEST_ON");

                m.DisableKeyword("_DOUBLESIDED_ON");
                m.SetFloat("_DoubleSidedEnable", 0f);
                m.SetFloat("_CullMode", 2f);
                m.SetFloat("_CullModeForward", 2f);
                m.SetFloat("_TransparentCullMode", 2f);
            }
            catch { }

            bool validated = false;
            try { validated = UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(m); }
            catch (Exception e) { NewSlotsMod.Detail("clip: ValidateMaterial threw (" + e.GetType().Name + ")"); }

            try { m.renderQueue = 2450; } catch { }
            NewSlotsMod.Detail("clip " + Fmt(name) + ": api=" + viaApi + " validated=" + validated +
                               " cutoff=" + (m.HasProperty("_AlphaCutoff") ? m.GetFloat("_AlphaCutoff") : -1f) +
                               " enable=" + (m.HasProperty("_AlphaCutoffEnable") ? m.GetFloat("_AlphaCutoffEnable") : -1f) +
                               " q=" + m.renderQueue);
        }

        static void DonorBox(GameObject clone, out Vector3 size, out Vector3 centre,
                             out int wAxis, out int hAxis, out int tAxis)
        {
            size = new Vector3(0.085f, 0.06f, 0.002f);
            centre = Vector3.zero;
            wAxis = 0; hAxis = 1; tAxis = 2;
            try
            {
                var mf = clone.GetComponentInChildren<MeshFilter>(true);
                if (mf == null || mf.sharedMesh == null) return;
                var b = mf.sharedMesh.bounds;
                size = b.size;
                centre = b.center;

                int[] order = { 0, 1, 2 };

                for (int i = 0; i < 2; i++)
                    for (int j = i + 1; j < 3; j++)
                        if (size[order[j]] > size[order[i]])
                        { int t = order[i]; order[i] = order[j]; order[j] = t; }
                wAxis = order[0]; hAxis = order[1]; tAxis = order[2];
            }
            catch { }
        }

        static readonly string[] TexProps =
        {
            "_BaseColorMap", "_NormalMap", "_MaskMap", "_OpacityMap", "_MainTex", "_BumpMap",
            "_EmissiveColorMap", "_DetailMap", "_SpecularColorMap", "_CoatMaskMap",
        };

        static readonly System.Collections.Generic.List<UnityEngine.Object> _held =
            new System.Collections.Generic.List<UnityEngine.Object>();

        internal static void UpgradeMaterials(Il2Cpp.LoadoutManager lm, bool lastChance)
        {
            foreach (var d in Catalog.Live)
            {
                if (d.Type != Kind.Garment || d.MaterialFinal) continue;
                if (d.Clone == null) { d.MaterialFinal = true; continue; }

                Material live = null;
                try
                {
                    var r = d.Clone.GetComponentInChildren<Renderer>(true);
                    if (r != null) live = r.sharedMaterial;
                }
                catch { continue; }

                bool streamed = !Art.IsPlaceholder(live) &&
                                !(live != null && live.name != null && live.name.StartsWith("ODG_"));

                if (streamed && (d.Tex2 != null || d.TexNormal2 != null || d.TexMask2 != null))
                {
                    try
                    {
                        var r0 = d.Clone.GetComponentInChildren<Renderer>(true);
                        var sm = r0 != null ? r0.sharedMaterials : null;
                        if (sm != null && sm.Length >= 2)
                        {
                            var s1 = sm[1];
                            if (Art.IsPlaceholder(s1) || (s1 != null && s1.name != null && s1.name.StartsWith("ODG_")))
                                streamed = false;
                        }
                    }
                    catch { }
                }

                if (!streamed && !lastChance) continue;

                Material own = null;
                int n = 0;
                Material[] perRenderer = null;
                Material[][] perSlots = null;
                string[] shNames = null;
                try
                {
                    var rends = d.Clone.GetComponentsInChildren<Renderer>(true);
                    perRenderer = new Material[rends.Length];
                    perSlots = new Material[rends.Length][];
                    shNames = new string[rends.Length];
                    for (int i = 0; i < rends.Length; i++)
                    {
                        var r = rends[i]; if (r == null) continue;
                        Material m = null;
                        try { m = r.material; } catch (Exception e) { NewSlotsMod.Log("finalize " + d.ModName + ": .material threw: " + e.Message); }
                        if (m == null) continue;

                        try { m.name = "ODG_" + d.ModName; } catch { }
                        SetTex(m, d.Tex, "_BaseColorMap", "_MainTex", "_BaseMap");
                        SetTex(m, d.TexNormal, "_NormalMap", "_BumpMap");
                        SetTex(m, d.TexMask, "_MaskMap");
                        SetTex(m, d.TexOpacity, "_OpacityMap");

                        Art.Pin(m);
                        _held.Add(m);
                        try { if (m.shader != null) { Art.Pin(m.shader); _held.Add(m.shader); } } catch { }
                        for (int k = 0; k < TexProps.Length; k++)
                        {
                            try
                            {
                                if (!m.HasProperty(TexProps[k])) continue;
                                var t = m.GetTexture(TexProps[k]);
                                if (t != null) { Art.Pin(t); _held.Add(t); }
                            }
                            catch { }
                        }

                        if (d.Tex2 != null || d.TexNormal2 != null || d.TexMask2 != null)
                        {
                            try
                            {
                                var all = r.materials;
                                if (all != null && all.Length >= 2 && all[1] != null)
                                {

                                    var s0 = all[0];
                                    if (s0 != null && s0.GetInstanceID() != m.GetInstanceID())
                                    {
                                        NewSlotsMod.Log("finalize " + Fmt(d.ModName) + ": r.materials re-instanced slot 0, repainting it");
                                        try { s0.name = "ODG_" + d.ModName; } catch { }
                                        SetTex(s0, d.Tex, "_BaseColorMap", "_MainTex", "_BaseMap");
                                        SetTex(s0, d.TexNormal, "_NormalMap", "_BumpMap");
                                        SetTex(s0, d.TexMask, "_MaskMap");
                                        SetTex(s0, d.TexOpacity, "_OpacityMap");
                                        Art.Pin(s0); _held.Add(s0);
                                        m = s0;
                                    }
                                    var m2 = all[1];
                                    try { m2.name = "ODG_" + d.ModName + "_s1"; } catch { }
                                    SetTex(m2, d.Tex2, "_BaseColorMap", "_MainTex", "_BaseMap");
                                    SetTex(m2, d.TexNormal2, "_NormalMap", "_BumpMap");
                                    SetTex(m2, d.TexMask2, "_MaskMap");
                                    Art.Pin(m2); _held.Add(m2);
                                    try { if (m2.shader != null) { Art.Pin(m2.shader); _held.Add(m2.shader); } } catch { }
                                    for (int k = 0; k < TexProps.Length; k++)
                                    {
                                        try
                                        {
                                            if (!m2.HasProperty(TexProps[k])) continue;
                                            var t2 = m2.GetTexture(TexProps[k]);
                                            if (t2 != null) { Art.Pin(t2); _held.Add(t2); }
                                        }
                                        catch { }
                                    }
                                    var slots = new Material[all.Length];
                                    for (int si = 0; si < all.Length; si++) slots[si] = all[si];
                                    perSlots[i] = slots;
                                    NewSlotsMod.Log("finalize " + Fmt(d.ModName) + ": renderer " + i + " has " + all.Length +
                                                    " material slots, slot 1 painted with its own maps");
                                }
                                else
                                    NewSlotsMod.Log("finalize " + Fmt(d.ModName) + ": slot-1 art shipped but renderer " + i +
                                                    " has " + (all == null ? 0 : all.Length) + " slot(s) - second maps unused");
                            }
                            catch (Exception e) { NewSlotsMod.Log("finalize " + d.ModName + ": slot 1: " + e.Message); }
                        }

                        perRenderer[i] = m;
                        try { shNames[i] = m.shader != null ? m.shader.name : null; } catch { }
                        own = m;
                        n++;
                    }
                }
                catch (Exception e) { NewSlotsMod.Log("finalize " + d.ModName + ": " + e.Message); }

                PinDonorMaterials(d);

                NeutralizeMaterialLink(d.Clone);

                d.OwnMat = own;
                d.OwnMats = perRenderer;
                d.OwnSlots = perSlots;
                d.OwnShaderNames = shNames;
                d.MaterialFinal = true;
                NewSlotsMod.Log("garment finalized " + Fmt(d.ModName) + ": " + n + " renderer(s) on " +
                                (streamed ? "the streamed material" : "the placeholder (stream never arrived)") +
                                (own == null ? "  MATERIAL INSTANCE FAILED" : "") +
                                "  shader=" + (own == null || own.shader == null ? "NULL" : own.shader.name));
                Describe(own, d.ModName);
                NewSlotsMod.FlushLog();
            }
        }

        static readonly System.Collections.Generic.Dictionary<int, Vector3[]> _rest =
            new System.Collections.Generic.Dictionary<int, Vector3[]>();

        internal static void ApplyInstance(Def d, GameObject instance, float scale, float outMm)
        {
            if (instance == null) return;
            try
            {
                var t = instance.transform;
                int id = instance.GetInstanceID();

                Vector3[] rest;
                if (!_rest.TryGetValue(id, out rest))
                {
                    rest = new[] { t.localPosition, t.localScale };
                    _rest[id] = rest;
                    if (_rest.Count > 256) _rest.Clear();
                }

                t.localScale = new Vector3(rest[1].x * scale, rest[1].y * scale, rest[1].z * scale);

                var axis = Vector3.right;
                if (d.TA == 1) axis = Vector3.up; else if (d.TA == 2) axis = Vector3.forward;
                float dir = NewSlotsMod.PatchDepthFlip ? -1f : 1f;
                t.localPosition = rest[0] + axis * (dir * outMm / 1000f);
            }
            catch (Exception e) { NewSlotsMod.Detail("seat " + d.ModName + ": " + e.Message); }
        }

        internal static Mesh ApplyGeometry(Def d)
        {
            if (d.ArtAspect <= 0f) return d.GenMesh;
            const float thick = 0.0015f;
            float scale = NewSlotsMod.PatchScale * d.PerScale * (d.TallDonor ? 1.45f : 1f);

            float boxW = d.BoxSize[d.WA] * scale, boxH = d.BoxSize[d.HA] * scale;
            float w = boxW, h = boxW / d.ArtAspect;
            if (h > boxH) { h = boxH; w = boxH * d.ArtAspect; }

            var mesh = PatchMesh.BuildCard(w, h, thick, d.ModName, d.GenMesh);
            if (mesh == null) return d.GenMesh;

            float dir = NewSlotsMod.PatchDepthFlip ? -1f : 1f;
            var at = d.BoxCentre;
            at[d.TA] = d.BoxCentre[d.TA] + dir * (thick / 2f + d.PerOutMm / 1000f);
            PatchMesh.Remap(mesh, d.WA, d.HA, d.TA, at);
            d.GenMesh = mesh;
            return mesh;
        }

        static Material BuildOwnMaterial(Def d, Material template)
        {
            try
            {
                Material m;
                if (template != null)
                {

                    m = new Material(template);
                }
                else
                {
                    var sh = Shader.Find("HDRP/Lit");
                    if (sh == null) return null;
                    m = new Material(sh);
                    Art.Pin(sh);
                    _held.Add(sh);
                }

                m.name = "ODG_" + d.ModName;

                SetTex(m, d.Tex, "_BaseColorMap", "_MainTex", "_BaseMap");
                SetTex(m, d.TexNormal, "_NormalMap", "_BumpMap");
                SetTex(m, d.TexMask, "_MaskMap");
                SetTex(m, d.TexOpacity, "_OpacityMap");

                if (template != null)
                {

                    try
                    {
                        if (template.HasProperty("_AlphaCutoffEnable") &&
                            template.GetFloat("_AlphaCutoffEnable") > 0.5f)
                        {
                            m.SetFloat("_AlphaCutoffEnable", 1f);
                            if (template.HasProperty("_AlphaCutoff"))
                                m.SetFloat("_AlphaCutoff", template.GetFloat("_AlphaCutoff"));
                            m.EnableKeyword("_ALPHATEST_ON");
                            m.renderQueue = template.renderQueue;
                        }
                    }
                    catch { }
                }
                else
                {

                    try
                    {
                        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.25f);
                        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
                        if (m.HasProperty("_SmoothnessRemapMax")) m.SetFloat("_SmoothnessRemapMax", 0.35f);
                    }
                    catch { }
                }

                for (int i = 0; i < TexProps.Length; i++)
                {
                    try
                    {
                        if (!m.HasProperty(TexProps[i])) continue;
                        var t = m.GetTexture(TexProps[i]);
                        if (t != null) { Art.Pin(t); _held.Add(t); }
                    }
                    catch { }
                }

                Art.Pin(m);
                try { if (m.shader != null) { Art.Pin(m.shader); _held.Add(m.shader); } } catch { }
                _held.Add(m);
                return m;
            }
            catch (Exception e)
            {
                NewSlotsMod.Log("BuildOwnMaterial " + d.ModName + " threw: " + e.Message);
                return null;
            }
        }

        static bool TargetsMesh(SkinnedMeshRenderer smr, string want)
        {
            string have = null;
            try { if (smr.sharedMesh != null) have = smr.sharedMesh.name; } catch { }
            if (string.IsNullOrEmpty(have)) return false;
            if (string.Equals(have, want, StringComparison.OrdinalIgnoreCase)) return true;
            return have.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static void SetTex(Material m, Texture t, params string[] props)
        {
            if (t == null) return;
            for (int i = 0; i < props.Length; i++)
            {
                try { if (m.HasProperty(props[i])) m.SetTexture(props[i], t); } catch { }
            }
        }

        static void Describe(Material m, string name)
        {
            if (m == null) return;
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.Append("   mat ").Append(m.name);
                sb.Append(" shader=").Append(m.shader == null ? "null" : m.shader.name);

                try
                {
                    if (m.shader != null)
                    {
                        bool ok = m.shader.isSupported;
                        sb.Append(ok ? " SUPPORTED" : "  *** UNSUPPORTED SHADER - THIS RENDERS MAGENTA ***");
                        sb.Append(" passes=").Append(m.passCount);
                    }
                }
                catch { }

                sb.Append(" q=").Append(m.renderQueue);
                try { if (m.HasProperty("_AlphaCutoffEnable")) sb.Append(" clip=").Append(m.GetFloat("_AlphaCutoffEnable")); } catch { }
                foreach (var p in TexProps)
                {
                    try
                    {
                        if (!m.HasProperty(p)) continue;
                        var t = m.GetTexture(p);
                        if (t != null) sb.Append(' ').Append(p).Append('=').Append(t.name);
                    }
                    catch { }
                }
                NewSlotsMod.Log(sb.ToString());
            }
            catch { }
        }

        static readonly System.Collections.Generic.HashSet<string> _pinnedKeys =
            new System.Collections.Generic.HashSet<string>();

        static void PinDonorMaterials(Def d)
        {
            try
            {
                if (d.Clone == null) return;
                var links = d.Clone.GetComponentsInChildren<Il2Cpp.ModMaterialLink>(true);
                if (links == null || links.Length == 0) return;
                var cache = Il2Cpp.ModMaterialLink._cache;
                if (cache == null) { NewSlotsMod.Log("pin " + Fmt(d.ModName) + ": material cache is null"); return; }

                for (int li = 0; li < links.Length; li++)
                {
                    var link = links[li]; if (link == null) continue;
                    var mrefs = link.AllMaterialReferences; if (mrefs == null) continue;
                    for (int ri = 0; ri < mrefs.Length; ri++)
                    {
                        var mr = mrefs[ri]; if (mr == null) continue;
                        var mats = mr.Materials; if (mats == null) continue;
                        for (int mi = 0; mi < mats.Length; mi++)
                        {
                            var ar = mats[mi]; if (ar == null) continue;
                            string key = null;
                            try { var rk = ar.RuntimeKey; if (rk != null) key = rk.ToString(); } catch { }
                            if (string.IsNullOrEmpty(key)) { try { key = ar.AssetGUID; } catch { } }
                            if (string.IsNullOrEmpty(key) || _pinnedKeys.Contains(key)) continue;

                            try
                            {
                                if (cache.ContainsKey(key))
                                {
                                    var entry = cache[key];
                                    if (entry == null) continue;
                                    int before = entry.RefCount;
                                    entry.RefCount = before + 1000;
                                    _pinnedKeys.Add(key);
                                    NewSlotsMod.Log("pinned donor material for " + Fmt(d.ModName) +
                                                    " key=" + key.Substring(0, Math.Min(12, key.Length)) +
                                                    ".. refcount " + before + " -> " + entry.RefCount);
                                }
                                else
                                {

                                    NewSlotsMod.Log("pin miss for " + Fmt(d.ModName) + " key=" + key +
                                                    " (no cache entry - material not loaded)");
                                }
                            }
                            catch (Exception e) { NewSlotsMod.Log("pin " + Fmt(d.ModName) + " threw: " + e.Message); }
                        }
                    }
                }
            }
            catch (Exception e) { NewSlotsMod.Log("PinDonorMaterials " + d.ModName + ": " + e.Message); }
        }

        static void NeutralizeMaterialLink(GameObject go)
        {
            try
            {
                var links = go.GetComponentsInChildren<Il2Cpp.ModMaterialLink>(true);
                if (links == null) return;
                for (int i = 0; i < links.Length; i++)
                {
                    var l = links[i]; if (l == null) continue;
                    UnityEngine.Object.DestroyImmediate(l);
                }
            }
            catch (Exception e) { NewSlotsMod.Log("material link neutralize: " + e.Message); }
        }

        static void PinMeshes(GameObject go)
        {
            try
            {
                var mfs = go.GetComponentsInChildren<MeshFilter>(true);
                if (mfs != null)
                    for (int i = 0; i < mfs.Length; i++)
                        if (mfs[i] != null) Art.Pin(mfs[i].sharedMesh);

                var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (smrs != null)
                    for (int i = 0; i < smrs.Length; i++)
                        if (smrs[i] != null) Art.Pin(smrs[i].sharedMesh);
            }
            catch { }
        }

        static void Kill(GameObject go)
        {
            try { if (go != null) UnityEngine.Object.Destroy(go); } catch { }
        }

        static string SafeName(Il2Cpp.CharacterMod m)
        {
            try { return m == null ? "null" : m.gameObject.name; } catch { return "(unreadable)"; }
        }

        internal static string Fmt(string s) { return s == null ? "null" : "\"" + s + "\""; }
    }
}
