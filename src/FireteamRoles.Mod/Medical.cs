// ODG Operations - FireteamRoles
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using UnityEngine;

namespace FireteamRoles
{
    internal static class Medical
    {
        internal static readonly Il2Cpp.InventoryItem.ItemType[] AllTypes =
        {
            Il2Cpp.InventoryItem.ItemType.Tourniquet,
            Il2Cpp.InventoryItem.ItemType.Bandage,
            Il2Cpp.InventoryItem.ItemType.Morphine,
            Il2Cpp.InventoryItem.ItemType.Splint,
            Il2Cpp.InventoryItem.ItemType.HemostaticInjector,
            Il2Cpp.InventoryItem.ItemType.PressureDressing,
            Il2Cpp.InventoryItem.ItemType.AntiBiotics,
            Il2Cpp.InventoryItem.ItemType.Blood
        };

        internal static string Name(Il2Cpp.InventoryItem.ItemType t)
        {
            switch (t)
            {
                case Il2Cpp.InventoryItem.ItemType.Tourniquet: return "Tourniquet";
                case Il2Cpp.InventoryItem.ItemType.Bandage: return "Bandage";
                case Il2Cpp.InventoryItem.ItemType.Morphine: return "Morphine";
                case Il2Cpp.InventoryItem.ItemType.Splint: return "Splint";
                case Il2Cpp.InventoryItem.ItemType.HemostaticInjector: return "Hemostatic";
                case Il2Cpp.InventoryItem.ItemType.PressureDressing: return "Pressure Dressing";
                case Il2Cpp.InventoryItem.ItemType.AntiBiotics: return "Antibiotics";
                case Il2Cpp.InventoryItem.ItemType.Blood: return "Blood";
                default: return t.ToString();
            }
        }

        static Il2Cpp.MedicalSystem _cachedLocal;
        static float _nextOwnedScan;

        internal static void Forget()
        {
            _cachedLocal = null;
            _nextOwnedScan = 0f;
        }

        internal static Il2Cpp.MedicalSystem Local()
        {
            var me = State.Me;
            if (me != null && me.Med != null) return me.Med;

            try { if (_cachedLocal != null && _cachedLocal.gameObject != null) return _cachedLocal; }
            catch { _cachedLocal = null; }

            if (UnityEngine.Time.realtimeSinceStartup < _nextOwnedScan) return null;
            _nextOwnedScan = UnityEngine.Time.realtimeSinceStartup + 2f;

            try
            {
                var all = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.MedicalSystem>();
                if (all == null) return null;
                for (int i = 0; i < all.Length; i++)
                {
                    var m = all[i];
                    if (m == null) continue;
                    try { if (!m.gameObject.scene.IsValid()) continue; } catch { continue; }
                    try { if (!m.isOwned) continue; } catch { continue; }
                    _cachedLocal = m;
                    Log.Msg("local MedicalSystem found by ownership on '" + m.gameObject.name + "'");
                    return m;
                }
            }
            catch (Exception e) { Log.Detail("owned medical scan failed: " + e.Message); }
            return null;
        }

        static bool Alive(Il2Cpp.InventoryItem it)
        {
            try { return it != null && it.gameObject != null; } catch { return false; }
        }

        internal static List<Il2Cpp.InventoryItem> Live(Il2Cpp.MedicalSystem med)
        {
            var outp = new List<Il2Cpp.InventoryItem>(12);
            if (med == null) return outp;
            try
            {
                var slots = med.inventorySlots;
                if (slots == null) return outp;
                for (int i = 0; i < slots.Count; i++)
                {
                    var s = slots[i]; if (s == null) continue;
                    Il2Cpp.InventoryItem it = null;
                    try { it = s.currentItem; } catch { }
                    if (Alive(it)) outp.Add(it);
                }
            }
            catch { }
            return outp;
        }

        internal static Il2Cpp.InventoryItem Find(Il2Cpp.MedicalSystem med, Il2Cpp.InventoryItem.ItemType t)
        {
            var live = Live(med);
            for (int i = 0; i < live.Count; i++)
            {
                var it = live[i];
                if (Epi.IsEpi(it)) continue;
                try { if (it.itemType == t) return it; } catch { }
            }
            return null;
        }

        static Il2Cpp.InventoryItem Source(Il2Cpp.MedicalSystem med, Il2Cpp.InventoryItem.ItemType t)
        {
            try
            {
                var items = med != null ? med.inventoryItems : null;
                if (items != null)
                    for (int i = 0; i < items.Count; i++)
                    {
                        var it = items[i];
                        if (it == null || Epi.IsEpi(it)) continue;
                        try { if (it.itemType == t) return it; } catch { }
                    }
            }
            catch { }
            try
            {
                var lm = Il2Cpp.LoadoutManager.singleton;
                return lm != null ? lm.GetMedicalItem(t) : null;
            }
            catch { return null; }
        }

        internal static int Uses(Il2Cpp.MedicalSystem med, Il2Cpp.InventoryItem.ItemType t)
        {
            var it = Find(med, t);
            try { return it != null ? it.currentUses : 0; }
            catch { return 0; }
        }

        internal static readonly int[] Counts = new int[8];
        internal static int EpiCount;
        static float _nextCount;

        internal static void RefreshCounts(bool force)
        {
            if (!force && UnityEngine.Time.realtimeSinceStartup < _nextCount) return;
            _nextCount = UnityEngine.Time.realtimeSinceStartup + 0.25f;

            for (int i = 0; i < Counts.Length; i++) Counts[i] = 0;
            EpiCount = 0;

            var med = Local();
            if (med == null) return;
            try
            {
                var live = Live(med);
                for (int i = 0; i < live.Count; i++)
                {
                    var it = live[i];
                    if (Epi.IsEpi(it)) { EpiCount += it.currentUses; continue; }
                    var t = it.itemType;
                    for (int k = 0; k < AllTypes.Length; k++)
                        if (AllTypes[k] == t) { Counts[k] += it.currentUses; break; }
                }
                Census(med, live);
            }
            catch { }

            RefreshLabels(med);
        }

        internal static int CachedUses(int typeIndex)
        {
            if (typeIndex < 0 || typeIndex >= Counts.Length) return 0;
            return Counts[typeIndex];
        }

        static readonly HashSet<IntPtr> _censused = new HashSet<IntPtr>();

        static void Census(Il2Cpp.MedicalSystem med, List<Il2Cpp.InventoryItem> live)
        {
            if (live.Count == 0) return;
            IntPtr key; try { key = med.Pointer; } catch { return; }
            if (!_censused.Add(key)) return;
            var sb = new System.Text.StringBuilder("kit: live in slots [");
            for (int i = 0; i < live.Count; i++)
            {
                var it = live[i];
                try { sb.Append(Epi.IsEpi(it) ? Epi.ItemName : Name(it.itemType)).Append(' ').Append(it.currentUses).Append('/').Append(it.maxUses).Append(i + 1 < live.Count ? ", " : ""); } catch { }
            }
            sb.Append("]  sources [");
            try
            {
                var items = med.inventoryItems;
                if (items != null)
                    for (int i = 0; i < items.Count; i++)
                    {
                        var it = items[i]; if (it == null) continue;
                        try { sb.Append(Epi.IsEpi(it) ? Epi.ItemName : Name(it.itemType)).Append(' ').Append(it.currentUses).Append('/').Append(it.maxUses).Append(i + 1 < items.Count ? ", " : ""); } catch { }
                    }
            }
            catch { }
            sb.Append(']');
            Log.Msg(sb.ToString());
        }

        internal static bool Spend(Il2Cpp.MedicalSystem med, Il2Cpp.InventoryItem.ItemType t)
        {
            var it = Find(med, t);
            try
            {
                if (it == null || it.currentUses <= 0) return false;

                it.OnItemUsed();
                return true;
            }
            catch (Exception e)
            {
                Log.Detail("spend via OnItemUsed failed (" + e.Message + "), decrementing directly");
                try { it.currentUses = Mathf.Max(0, it.currentUses - 1); RefreshCount(it); return true; }
                catch { return false; }
            }
        }

        internal static void Refund(Il2Cpp.MedicalSystem med, Il2Cpp.InventoryItem.ItemType t)
        {
            if (!Grant(med, t, 1)) Log.Detail("refund of " + Name(t) + " found nowhere to go");
        }

        internal static bool Grant(Il2Cpp.MedicalSystem med, Il2Cpp.InventoryItem.ItemType t, int n)
        {
            if (med == null || n <= 0) return false;
            var it = Find(med, t);
            try
            {
                if (it != null)
                {
                    int max = Mathf.Max(1, it.maxUses);
                    int want = Mathf.Min(max, it.currentUses + n);
                    if (want == it.currentUses) return false;
                    it.currentUses = want;
                    RefreshCount(it);
                    return true;
                }
            }
            catch { }

            var src = Source(med, t);
            if (src == null) { Log.Detail("no source for " + Name(t) + " to clone"); return false; }
            int keepC = 0, keepM = 0; bool have = false;
            try { keepC = src.currentUses; keepM = src.maxUses; have = true; } catch { }
            try
            {
                if (have) src.currentUses = Mathf.Min(Mathf.Max(1, keepM), n);
                med.AddToInventory(src);
            }
            catch (Exception e) { Log.Detail("AddToInventory failed for " + Name(t) + ": " + e.Message); }
            finally { try { if (have) { src.currentUses = keepC; src.maxUses = keepM; } } catch { } }

            var fresh = Find(med, t);
            if (fresh == null) { Log.Detail("no free slot for " + Name(t)); return false; }
            try { RefreshCount(fresh); } catch { }
            Log.Detail(Name(t) + " restocked as a fresh item with " + n);
            return true;
        }

        internal static bool ApplyingExternal;

        internal static bool ApplyToSelf(Il2Cpp.MedicalSystem med, Il2Cpp.InventoryItem.ItemType t, Il2Cpp.HealthBodyPart limb)
        {
            if (med == null) return false;

            try
            {
                ApplyingExternal = true;

                bool ok = Direct(med, t, limb);
                Enforce(med, t, limb);
                if (!ok) ok = Cleared(med, t, limb);
                return ok;
            }
            catch (Exception e)
            {
                Err.Note("medical.apply", e);
                return false;
            }
            finally { ApplyingExternal = false; }
        }

        static bool Cleared(Il2Cpp.MedicalSystem med, Il2Cpp.InventoryItem.ItemType t, Il2Cpp.HealthBodyPart limb)
        {
            Il2Cpp.Health h = null;
            try { h = med != null ? med.health : null; } catch { }
            if (h == null) return false;

            switch (t)
            {
                case Il2Cpp.InventoryItem.ItemType.Splint:
                    return !Wounds.Fractured(h, limb);
                case Il2Cpp.InventoryItem.ItemType.Bandage:
                case Il2Cpp.InventoryItem.ItemType.HemostaticInjector:
                case Il2Cpp.InventoryItem.ItemType.PressureDressing:
                case Il2Cpp.InventoryItem.ItemType.Tourniquet:
                    return Wounds.Bleed(h, limb) <= 0.001f;
            }
            return false;
        }

        static void Enforce(Il2Cpp.MedicalSystem med, Il2Cpp.InventoryItem.ItemType t, Il2Cpp.HealthBodyPart limb)
        {
            Il2Cpp.Health h = null;
            try { h = med.health; } catch { }
            if (h == null) return;

            try
            {
                switch (t)
                {
                    case Il2Cpp.InventoryItem.ItemType.Splint:
                        h.CMD_FixFracture(limb);
                        Log.Fired("enforce-fracture");
                        break;

                    case Il2Cpp.InventoryItem.ItemType.Bandage:
                    case Il2Cpp.InventoryItem.ItemType.HemostaticInjector:
                    case Il2Cpp.InventoryItem.ItemType.PressureDressing:
                    case Il2Cpp.InventoryItem.ItemType.Tourniquet:
                        h.CMD_StopBleeding(limb);
                        Log.Fired("enforce-bleed");
                        break;
                }
            }
            catch (Exception e) { Log.Detail("enforce failed for " + Name(t) + ": " + e.Message); }
        }

        internal static bool Direct(Il2Cpp.MedicalSystem med, Il2Cpp.InventoryItem.ItemType t, Il2Cpp.HealthBodyPart limb)
        {
            if (med == null) return false;
            try
            {
                switch (t)
                {
                    case Il2Cpp.InventoryItem.ItemType.Bandage: med.ProcessBandage(limb); break;
                    case Il2Cpp.InventoryItem.ItemType.Splint: med.ProcessSplint(limb); break;
                    case Il2Cpp.InventoryItem.ItemType.HemostaticInjector: med.ProcessHemostaticInjector(limb); break;
                    case Il2Cpp.InventoryItem.ItemType.Tourniquet: med.ProcessAddTourniquet(limb); break;
                    case Il2Cpp.InventoryItem.ItemType.PressureDressing: med.ProcessPressureDressing(); break;
                    case Il2Cpp.InventoryItem.ItemType.AntiBiotics: med.ProcessAntiBiotics(); break;
                    case Il2Cpp.InventoryItem.ItemType.Blood: med.ProcessBlood(); break;
                    case Il2Cpp.InventoryItem.ItemType.Morphine: med.ProcessMorphine(); break;
                    default: return false;
                }
                Log.Fired("Process*-direct");
                return true;
            }
            catch (Exception e) { Log.Detail("direct effect failed for " + Name(t) + ": " + e.Message); return false; }
        }

        internal static bool ApplyMedicKit(Il2Cpp.MedicalSystem med)
        {
            if (med == null) { Log.Detail("medic kit: no MedicalSystem yet"); return false; }
            bool medic = State.MyRole == Role.Medic && State.Enabled;
            int n = Mathf.Max(1, Cfg.MedicItemCount.Value);

            try
            {
                var live = Live(med);
                if (live.Count == 0)
                {
                    Log.Detail("medic kit: no live items in the slots yet, will retry");
                    return false;
                }

                if (medic)
                {
                    int touched = 0, total = 0;
                    for (int i = 0; i < live.Count; i++)
                    {
                        var it = live[i];
                        if (Epi.IsEpi(it)) continue;
                        total++;
                        try
                        {
                            if (it.maxUses == n) continue;
                            it.maxUses = n;
                            it.currentUses = n;
                            RefreshCount(it);
                            touched++;
                        }
                        catch (Exception e) { Log.Detail("medic kit: " + (it.ItemName ?? "?") + " failed: " + e.Message); }
                    }

                    if (Cfg.MedicAddsMissingItems.Value)
                    {
                        for (int t = 0; t < AllTypes.Length; t++)
                        {
                            if (Find(med, AllTypes[t]) != null) continue;
                            if (!Grant(med, AllTypes[t], n)) continue;
                            var added = Find(med, AllTypes[t]);
                            try { if (added != null) { added.maxUses = n; added.currentUses = n; RefreshCount(added); } } catch { }
                            Log.Msg("medic kit: added " + Name(AllTypes[t]));
                        }
                    }

                    int labels = RefreshLabels(med);
                    if (touched > 0)
                    {
                        Log.Msg("medic kit applied: " + touched + " of " + total + " live items set to " + n + "/" + n +
                                "  (" + labels + " label(s) redrawn)");
                        Log.Fired("medic-kit");
                    }

                    for (int i = 0; i < live.Count; i++)
                    {
                        var it = live[i];
                        if (Epi.IsEpi(it)) continue;
                        try
                        {
                            if (it.maxUses != n) { Log.Warn("medic kit: " + (it.ItemName ?? "?") + " did not take the new size"); return false; }
                        }
                        catch { }
                    }
                }
                else Log.Detail("medic kit skipped, role is " + RoleInfo.Name(State.MyRole));

                Epi.Sync(med);
                return true;
            }
            catch (Exception e) { Log.Err("ApplyMedicKit failed: " + e.Message); return false; }
        }

        internal static void Watchdog()
        {
            if (State.MyRole != Role.Medic || !State.Enabled) return;
            var med = Local();
            if (med == null) return;
            int n = Mathf.Max(1, Cfg.MedicItemCount.Value);

            bool stock = false;
            try
            {
                var live = Live(med);
                if (live.Count == 0) return;
                for (int i = 0; i < live.Count; i++)
                {
                    var it = live[i];
                    if (Epi.IsEpi(it)) continue;
                    if (it.maxUses != n) { stock = true; break; }
                }
            }
            catch { return; }

            if (stock)
            {
                Log.Msg("medic kit: stock-sized items in the kit, reapplying");
                Mod.KitDirty = true;
            }

            Epi.Sync(med);
        }

        internal static int RefreshLabels(Il2Cpp.MedicalSystem med)
        {
            if (med == null) return 0;
            int n = 0;
            try
            {
                var slots = med.inventorySlots;
                if (slots == null) return 0;
                for (int i = 0; i < slots.Count; i++)
                {
                    var s = slots[i];
                    if (s == null) continue;
                    Il2Cpp.InventoryItem it = null;
                    try { it = s.currentItem; } catch { }
                    if (it == null) continue;
                    try
                    {
                        var label = s.itemUsesText;
                        if (label == null) continue;
                        string want = it.currentUses + "/" + it.maxUses;
                        if (label.text != want) { label.text = want; n++; }
                    }
                    catch { }
                }
            }
            catch { }
            return n;
        }

        internal static void RefreshCount(Il2Cpp.InventoryItem it)
        {
            try
            {
                var slot = it.inventorySlot;
                if (slot == null || slot.itemUsesText == null) return;
                slot.itemUsesText.text = it.currentUses + "/" + it.maxUses;
            }
            catch { }
        }
    }
}
