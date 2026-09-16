// ODG Operations - NewSlots
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace NewSlots
{

    internal static class Catalog
    {
        internal static bool Registered;
        internal static int StockCount = -1;

        internal static int StreamAllowFor;

        internal static readonly Dictionary<int, Il2Cpp.CharacterMod> ById = new Dictionary<int, Il2Cpp.CharacterMod>();
        internal static readonly Dictionary<string, int> IdByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        static readonly List<Def> _live = new List<Def>();
        static bool _failedHard;

        internal static bool IsOurs(int index) { return index >= 0 && ById.ContainsKey(index); }

        internal static IEnumerable<Def> Live { get { return _live; } }

        static int _cursor;
        static int _stock;
        static bool _started;
        static Il2CppReferenceArray<UnityEngine.AddressableAssets.AssetReferenceGameObject> _oldStripped;
        static Il2CppReferenceArray<Il2Cpp.CharacterModInfo> _oldInfos;
        static readonly List<KeyValuePair<Def, Il2Cpp.CharacterMod>> _built = new List<KeyValuePair<Def, Il2Cpp.CharacterMod>>();
        static readonly List<Il2Cpp.CharacterModInfo> _newInfos = new List<Il2Cpp.CharacterModInfo>();

        internal static int Progress { get { return _cursor; } }

        internal static bool Started { get { return _started; } }

        internal static bool EnsureRegistered()
        {
            if (Registered) return true;
            if (_failedHard) return false;
            return Pump(0f);
        }

        internal static void Tick(float budgetMs)
        {
            if (Registered || _failedHard) return;
            Pump(budgetMs);
        }

        static bool Pump(float budgetMs)
        {
            Il2Cpp.LoadoutManager lm = null;
            try { lm = Il2Cpp.LoadoutManager.singleton; } catch { }
            if (lm == null) return false;

            try
            {
                if (!Begin(lm)) return false;

                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (_cursor < Defs.All.Count)
                {
                    BuildOne(lm, Defs.All[_cursor]);
                    _cursor++;
                    if (budgetMs > 0f && sw.Elapsed.TotalMilliseconds >= budgetMs)
                        return false;
                }
                var _ok = Commit(lm);
                BuildPerf.Report(delegate (string m) { NewSlotsMod.Log(m); });
                return _ok;
            }
            catch (Exception e)
            {
                NewSlotsMod.Log("REGISTER THREW: " + e.GetType().Name + ": " + e.Message);
                _failedHard = true;
                return false;
            }
        }

        static bool Begin(Il2Cpp.LoadoutManager lm)
        {
            if (_started) return true;

            Il2CppReferenceArray<UnityEngine.AddressableAssets.AssetReferenceGameObject> oldStripped = null;
            Il2CppReferenceArray<Il2Cpp.CharacterModInfo> oldInfos = null;
            try { oldStripped = lm.AllStrippedCharacterMods; } catch { }
            try { oldInfos = lm.CharacterModInfo; } catch { }

            if (oldStripped == null || oldInfos == null) return false;
            if (oldStripped.Length == 0 || oldInfos.Length == 0) return false;

            if (oldStripped.Length != oldInfos.Length)
            {
                NewSlotsMod.Log("STOCK ARRAYS ARE UNEVEN (stripped=" + oldStripped.Length + " infos=" + oldInfos.Length +
                                "). Standing down rather than making it worse.");
                _failedHard = true;
                return false;
            }

            _oldStripped = oldStripped;
            _oldInfos = oldInfos;
            _stock = oldStripped.Length;
            _cursor = 0;
            _built.Clear();
            _newInfos.Clear();
            _started = true;
            NewSlotsMod.Log("building " + Defs.All.Count + " item(s) incrementally from stock=" + _stock);
            return true;
        }

        static void BuildOne(Il2Cpp.LoadoutManager lm, Def d)
        {
            long _pFind = BuildPerf.Begin();
            int donorIdx = ItemFactory.FindDonorIndex(_oldInfos, d.DonorContains);
            BuildPerf.End("FindDonorIndex", _pFind);
            if (donorIdx < 0) { NewSlotsMod.Log("skip " + ItemFactory.Fmt(d.ModName) + ": no donor matching " + ItemFactory.Fmt(d.DonorContains)); return; }

            var donorInfo = _oldInfos[donorIdx];

            Il2Cpp.CharacterMod donor = null;
            long _pGet = BuildPerf.Begin();
            try { donor = lm.GetCharacterMod(donorIdx); }
            catch (Exception e) { NewSlotsMod.Log("skip " + ItemFactory.Fmt(d.ModName) + ": GetCharacterMod(" + donorIdx + ") threw: " + e.Message); }
            BuildPerf.End("lm.GetCharacterMod", _pGet);
            if (donor == null) { NewSlotsMod.Log("skip " + ItemFactory.Fmt(d.ModName) + ": donor prefab null at " + donorIdx); return; }

            if (NameExists(_oldInfos, d.ModName))
            {
                NewSlotsMod.Log("skip " + ItemFactory.Fmt(d.ModName) + ": that ModName already exists in the catalog");
                return;
            }

            int ownIndex = _stock + _built.Count;
            long _pBuild = BuildPerf.Begin();
            var mod = ItemFactory.Build(d, donor, donorInfo, ownIndex, donorIdx);
            BuildPerf.End("ItemFactory.Build", _pBuild);
            if (mod == null) return;

            _built.Add(new KeyValuePair<Def, Il2Cpp.CharacterMod>(d, mod));
            _newInfos.Add(ItemFactory.BuildInfo(d, donorInfo, d.Tex));

            Art.QueueSeal(d.Tex); Art.QueueSeal(d.TexNormal); Art.QueueSeal(d.TexMask);
            Art.QueueSeal(d.TexOpacity); Art.QueueSeal(d.TexIcon);
            Art.QueueSeal(d.Tex2); Art.QueueSeal(d.TexNormal2); Art.QueueSeal(d.TexMask2);
        }

        static bool Commit(Il2Cpp.LoadoutManager lm)
        {
            var built = _built;
            var infos = _newInfos;
            int stock = _stock;
            var oldStripped = _oldStripped;
            var oldInfos = _oldInfos;

            if (built.Count == 0)
            {
                NewSlotsMod.Log("nothing built, nothing registered.");
                _failedHard = true;
                return false;
            }
            if (built.Count < Defs.All.Count)
                NewSlotsMod.Log("NOTE: " + (Defs.All.Count - built.Count) + " of " + Defs.All.Count +
                                " manifest item(s) did not build - see the skip lines above.");

            try
            {
                if (lm.AllStrippedCharacterMods.Length != stock || lm.CharacterModInfo.Length != stock)
                {
                    NewSlotsMod.Log("catalog changed mid-build (now " + lm.AllStrippedCharacterMods.Length +
                                    "), restarting the build.");
                    _started = false; _cursor = 0; _built.Clear(); _newInfos.Clear();
                    return false;
                }
            }
            catch { }

            try
            {
                int add = built.Count;

                var newInfos = new Il2CppReferenceArray<Il2Cpp.CharacterModInfo>(oldInfos.Length + add);
                for (int i = 0; i < oldInfos.Length; i++) newInfos[i] = oldInfos[i];

                var newStripped = new Il2CppReferenceArray<UnityEngine.AddressableAssets.AssetReferenceGameObject>(oldStripped.Length + add);
                for (int i = 0; i < oldStripped.Length; i++) newStripped[i] = oldStripped[i];

                for (int k = 0; k < add; k++)
                {
                    newInfos[stock + k] = infos[k];

                    newStripped[stock + k] = oldStripped[0];
                }

                lm.CharacterModInfo = newInfos;
                lm.AllStrippedCharacterMods = newStripped;

                ById.Clear();
                IdByName.Clear();
                _live.Clear();
                for (int k = 0; k < add; k++)
                {
                    int id = stock + k;
                    var d = built[k].Key;
                    ById[id] = built[k].Value;
                    IdByName[d.ModName] = id;
                    _live.Add(d);
                }

                StockCount = stock;
                Registered = true;

                NewSlotsMod.Log("REGISTERED " + add + " item(s). stock=" + stock + " -> " + newInfos.Length +
                                "  count=" + Il2Cpp.LoadoutManager.GetNumCharacterMods());

                foreach (var d in _live) NewSlotsMod.Detail("   [" + d.Index + "] " + ItemFactory.Fmt(d.ModName) + " (" + d.Type + ")");
                NewSlotsMod.FlushLog();
                return true;
            }
            catch (Exception e)
            {
                NewSlotsMod.Log("APPEND FAILED, rolling back: " + e.GetType().Name + ": " + e.Message);
                try { lm.CharacterModInfo = oldInfos; lm.AllStrippedCharacterMods = oldStripped; } catch { }
                ById.Clear(); IdByName.Clear(); _live.Clear();
                Registered = false;
                _failedHard = true;
                return false;
            }
        }

        static bool NameExists(Il2CppReferenceArray<Il2Cpp.CharacterModInfo> infos, string name)
        {
            for (int i = 0; i < infos.Length; i++)
            {
                var r = infos[i]; if (r == null) continue;
                string nm = null; try { nm = r.ModName; } catch { }
                if (nm != null && string.Equals(nm, name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        internal static void Verify()
        {
            if (!Registered) return;
            try
            {
                var lm = Il2Cpp.LoadoutManager.singleton;
                if (lm == null) return;
                var infos = lm.CharacterModInfo;
                if (infos == null) return;

                foreach (var d in _live)
                {
                    if (d.Index < 0 || d.Index >= infos.Length) { Reset("catalog shrank below our index"); return; }
                    var row = infos[d.Index];
                    string nm = null; try { nm = row == null ? null : row.ModName; } catch { }
                    if (!string.Equals(nm, d.ModName, StringComparison.Ordinal)) { Reset("our row at " + d.Index + " now reads " + ItemFactory.Fmt(nm)); return; }
                }
            }
            catch { }
        }

        static void Reset(string why)
        {
            NewSlotsMod.Log("catalog was replaced under us (" + why + ") - re-registering.");
            Registered = false;
            ById.Clear();
            IdByName.Clear();
            _live.Clear();
            foreach (var d in Defs.All) d.Index = -1;
            EnsureRegistered();
        }
    }
}
