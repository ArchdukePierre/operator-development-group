// ODG Operations - FireteamRoles
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using HarmonyLib;
using UnityEngine;

namespace FireteamRoles
{

    internal static class Hooks
    {
        static int _attempted, _bound;
        static readonly System.Collections.Generic.List<string> _missed = new System.Collections.Generic.List<string>();
        static readonly System.Collections.Generic.List<string> _failed = new System.Collections.Generic.List<string>();

        internal static string Summary()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(_bound).Append('/').Append(_attempted).Append(" hooks bound");
            if (_missed.Count > 0) sb.Append("   MISSING: ").Append(string.Join(", ", _missed.ToArray()));
            if (_failed.Count > 0) sb.Append("   FAILED: ").Append(string.Join(", ", _failed.ToArray()));
            return sb.ToString();
        }

        internal static bool AllBound { get { return _missed.Count == 0 && _failed.Count == 0; } }

        internal static void Install(HarmonyLib.Harmony h)
        {

            Patch(h, typeof(Il2Cpp.Health), "TakeDamage",
                new Type[] { typeof(float), typeof(Il2Cpp.HealthBodyPart), typeof(Il2Cpp.HealthBodyPart), typeof(int), typeof(Vector3) },
                nameof(Pre_TakeDamage), null);

            Patch(h, typeof(Il2Cpp.Health), "Die",
                new Type[] { typeof(Il2Cpp.HealthBodyPart), typeof(int) }, nameof(Pre_Die), null);

            Patch(h, typeof(Il2Cpp.MedicalSystem), "AddItemsToInventory", null, null, nameof(Post_AddItems));
            Patch(h, typeof(Il2Cpp.MedicalSystem), "UseItem",
                new Type[] { typeof(Il2Cpp.InventoryItem), typeof(Il2Cpp.HealthBodyPart) },
                nameof(Pre_UseItem), null);
            Patch(h, typeof(Il2Cpp.MedicalSystem), "ProcessMedicalMenuInput", null, nameof(Pre_MedMenu), null);

            Patch(h, typeof(Il2Cpp.MedicalSystem), "Button_OpenHealthInteractive", null, null, nameof(Post_HealthMenuOpen));

            Patch(h, typeof(Il2Cpp.PlayerNetworking), "ShoulderTapL",
                new Type[] { typeof(Il2Cpp.PlayerNetworking) }, nameof(Pre_ShoulderTapL), null);
            Patch(h, typeof(Il2Cpp.PlayerNetworking), "ShoulderTapR",
                new Type[] { typeof(Il2Cpp.PlayerNetworking) }, nameof(Pre_ShoulderTapR), null);

            Patch(h, typeof(Il2Cpp.WeaponV3), "CanFire", null, null, nameof(Post_CanFire));

            Patch(h, typeof(Il2Cpp.InputLayer), "GetKey",
                new Type[] { typeof(Il2Cpp.InputKey), typeof(bool) }, nameof(Pre_GetKey), null);
            Patch(h, typeof(Il2Cpp.InputLayer), "CheckMouseInputSuppressed", null, null, nameof(Post_MouseSuppressed));

            Patch(h, typeof(Il2Cpp.ISR), "SERVER_Firemissile", null, nameof(Pre_FireMissile), nameof(Post_FireMissile));
            Patch(h, typeof(Il2Cpp.PlayerNetworking), "UserCode_CMD_PushFireButton", null, nameof(Pre_PushFire), null);

            Patch(h, typeof(Il2Cpp.ISR), "Update", null, null, nameof(Post_IsrUpdate));
            Patch(h, typeof(Il2Cpp.MagManagerUI), "AddGrenadeToPouch",
                new Type[] { typeof(string) }, nameof(Pre_AddGrenadeToPouch), null);
            Patch(h, typeof(Il2Cpp.PlayerMaster), "TryAddGrenadeToPreset",
                new Type[] { typeof(string) }, nameof(Pre_TryAddPreset), null);
            Patch(h, typeof(Il2Cpp.MagManagerUI), "InitializeMagManager", null, nameof(Pre_InitMagManager), nameof(Post_InitMagManager));
            Patch(h, typeof(Il2Cpp.MagManagerUI), "ClearAllGrenades", null, nameof(Pre_ClearAllGrenades), null);
            Patch(h, typeof(Il2Cpp.GrenadeV2), "CMDPickupGrenade",
                new Type[] { typeof(Il2CppMirror.NetworkIdentity), typeof(Il2Cpp.PlayerNetworking), typeof(bool) },
                nameof(Pre_ClientPickup), null);
            Patch(h, typeof(Il2Cpp.GrenadeV2), "UserCode_CMD_PickupGrenade__NetworkIdentity__PlayerNetworking__Boolean",
                new Type[] { typeof(Il2CppMirror.NetworkIdentity), typeof(Il2Cpp.PlayerNetworking), typeof(bool) },
                nameof(Pre_ServerPickup), null);

            Patch(h, typeof(Il2Cpp.playerBoardItem), "UpdateSubText", null, null, nameof(Post_BoardSubText));

            Patch(h, typeof(Il2Cpp.SlapChargeExplosive), "UserCode_CMD_Explode__Vector3",
                new Type[] { typeof(UnityEngine.Vector3) }, null, nameof(Post_SlapCharge));

            Patch(h, typeof(Il2Cpp.GameManagerNetwork), "CompleteOperation", null, null, nameof(Post_OpEnd));
            Patch(h, typeof(Il2Cpp.GameManagerNetwork), "SERVER_FailOperation", null, null, nameof(Post_OpEnd));
            Patch(h, typeof(Il2Cpp.GameManagerNetwork), "UserCode_SERVER_RespawnALL", null, null, nameof(Post_Respawn));
        }

        static void Patch(HarmonyLib.Harmony h, Type type, string method, Type[] args, string prefix, string postfix)
        {
            try
            {
                _attempted++;
                var mi = args == null ? AccessTools.Method(type, method) : AccessTools.Method(type, method, args);
                if (mi == null)
                {
                    _missed.Add(type.Name + "." + method);
                    Log.Warn("HOOK MISS  " + type.Name + "." + method + "  (that feature is now dead)");
                    return;
                }
                h.Patch(mi,
                    prefix == null ? null : new HarmonyMethod(typeof(Hooks), prefix),
                    postfix == null ? null : new HarmonyMethod(typeof(Hooks), postfix));
                _bound++;
                Log.Msg("hooked " + type.Name + "." + method);
            }
            catch (Exception e)
            {
                _failed.Add(type.Name + "." + method + ": " + e.Message);
                Log.Err("HOOK FAIL  " + type.Name + "." + method + ": " + e.Message);
            }
        }

        static bool Owned(Il2CppMirror.NetworkBehaviour nb)
        {
            try { return nb != null && nb.isOwned; }
            catch { return false; }
        }

        static bool Pre_TakeDamage(Il2Cpp.Health __instance, float _damage, Il2Cpp.HealthBodyPart _bodyPart,
                                   Il2Cpp.HealthBodyPart crossedDamage, int shooterID, Vector3 DamageSourcePosition)
        {
            Log.Fired("Health.TakeDamage(5)");
            if (!State.IsHost) return true;
            try { return !Downed.OnHostDamage(__instance, _damage, _bodyPart, crossedDamage, shooterID); }
            catch (Exception e) { Err.Note("hook.takedamage", e); return true; }
        }

        static bool Pre_Die(Il2Cpp.Health __instance, Il2Cpp.HealthBodyPart causeOfDeath, int shooterID)
        {
            Log.Fired("Health.Die");
            if (!State.IsHost) return true;
            try { return !Downed.OnHostDie(__instance, causeOfDeath, shooterID); }
            catch (Exception e) { Err.Note("hook.die", e); return true; }
        }

        static void Post_AddItems(Il2Cpp.MedicalSystem __instance)
        {
            Log.Fired("MedicalSystem.AddItemsToInventory");
            Mod.KitDirty = true;
            Mod.KitCandidate = __instance;
        }

        static bool Pre_UseItem(Il2Cpp.MedicalSystem __instance, Il2Cpp.InventoryItem item, Il2Cpp.HealthBodyPart bodyPart)
        {

            try
            {
                if (Owned(__instance) && Downed.AmDowned && !Medical.ApplyingExternal)
                {
                    if (!Downed.SelfHealsLeft) { Ui.Toast("you need a medic for the rest"); return false; }
                    Downed.SelfHealsUsed++;
                    Log.Fired("downed-self-heal");
                    Ui.Toast("that cost you " + Cfg.DownedSelfHealTimeCost.Value.ToString("0") + "s and " +
                             Cfg.DownedSelfHealHPCost.Value.ToString("0") + " health");
                }
            }
            catch { }

            try
            {
                if (!Epi.IsEpi(item)) return true;
                Log.Fired("MedicalSystem.UseItem(epi)");
                Epi.Apply(__instance);
                try { if (item.currentUses > 0) item.currentUses = item.currentUses - 1; } catch { }
                Medical.RefreshCount(item);
                return false;
            }
            catch (Exception e) { Err.Note("hook.useitem", e); return true; }
        }

        static bool Pre_MedMenu(Il2Cpp.MedicalSystem __instance)
        {
            try
            {
                if (!Owned(__instance)) return true;
                if (!Downed.AmDowned) return true;
                if (Downed.SelfHealsLeft) return true;
                return false;
            }
            catch { return true; }
        }

        static void Post_HealthMenuOpen(Il2Cpp.MedicalSystem __instance)
        {
            Log.Fired("MedicalSystem.Button_OpenHealthInteractive");
            try
            {
                if (!Owned(__instance)) return;
                Medical.Watchdog();

                Medical.RefreshLabels(__instance);
            }
            catch (Exception e) { Err.Note("hook.menuopen", e); }
        }

        static bool Pre_ShoulderTapL(Il2Cpp.PlayerNetworking __instance, Il2Cpp.PlayerNetworking _playerNetworking)
        {
            Log.Fired("PlayerNetworking.ShoulderTapL");
            return !TargetIsDown(_playerNetworking);
        }

        static bool Pre_ShoulderTapR(Il2Cpp.PlayerNetworking __instance, Il2Cpp.PlayerNetworking _playerNetworking)
        {
            Log.Fired("PlayerNetworking.ShoulderTapR");
            return !TargetIsDown(_playerNetworking);
        }

        static bool TargetIsDown(Il2Cpp.PlayerNetworking pn)
        {
            if (pn == null) return false;
            try
            {
                foreach (var kv in State.Peers)
                {
                    var p = kv.Value;
                    if (p.Pn != null && p.Pn.Pointer == pn.Pointer) return p.Downed;
                }
            }
            catch { }
            return false;
        }

        static void Post_CanFire(Il2Cpp.WeaponV3 __instance, ref bool __result)
        {
            if (!__result) return;
            try
            {
                if (!Owned(__instance)) return;
                if (Downed.AmDowned || Drag.Dragging || Treat.Open) __result = false;
            }
            catch { }
        }

        static void Post_SlapCharge(Il2Cpp.SlapChargeExplosive __instance, UnityEngine.Vector3 explosionPos)
        {
            Log.Fired("SlapChargeExplosive.UserCode_CMD_Explode");
            try { Breach.OnCharge(__instance, explosionPos); }
            catch (Exception e) { Err.Note("hook.breach", e); }
        }

        static bool Pre_AddGrenadeToPouch(string nadeName)
        {
            Log.Fired("MagManagerUI.AddGrenadeToPouch");
            try { return Perks.AddGrenadeToPouch(nadeName); }
            catch (Exception e) { Err.Note("hook.nade.add", e); return true; }
        }

        static bool Pre_TryAddPreset(Il2Cpp.PlayerMaster __instance, string grenadeName)
        {
            Log.Fired("PlayerMaster.TryAddGrenadeToPreset");
            try { return Perks.TryAddGrenadeToPreset(__instance, grenadeName); }
            catch (Exception e) { Err.Note("hook.nade.preset", e); return true; }
        }

        static void Pre_InitMagManager()
        {
            Log.Fired("MagManagerUI.InitializeMagManager");
            Err.Guard("hook.maginit", Perks.PreInit);
        }

        static void Post_InitMagManager() { Perks.PostInit(); }

        static bool Pre_ClearAllGrenades()
        {
            try { return Perks.AllowClearAllGrenades(); }
            catch (Exception e) { Err.Note("hook.nade.clear", e); return true; }
        }

        static bool Pre_ClientPickup(Il2Cpp.GrenadeV2 __instance, Il2CppMirror.NetworkIdentity netid,
                                     Il2Cpp.PlayerNetworking playerNetworking, bool spawnEquip)
        {
            try { return Perks.ClientPickup(__instance, netid, playerNetworking, spawnEquip); }
            catch (Exception e) { Err.Note("hook.nade.pickup", e); return true; }
        }

        static bool Pre_ServerPickup(Il2Cpp.GrenadeV2 __instance, Il2CppMirror.NetworkIdentity netid,
                                     Il2Cpp.PlayerNetworking playerNetworking, bool spawnEquip)
        {
            Log.Fired("GrenadeV2.UserCode_CMD_PickupGrenade");
            try { return Perks.ServerPickup(__instance, netid, playerNetworking, spawnEquip); }
            catch (Exception e) { Err.Note("hook.nade.serverpickup", e); return true; }
        }

        static bool _blockLogged;
        static bool _downBlockLogged;

        static bool Pre_GetKey(Il2Cpp.InputKey key, bool ActivatedThisFrame, ref bool __result)
        {
            if (Ui.BlocksKeys)
            {
                if (!_blockLogged) { _blockLogged = true; Log.Fired("InputLayer.GetKey suppression"); }
                __result = false;
                return false;
            }

            if (Downed.AmDowned && !DownedAllowed(key))
            {
                if (!_downBlockLogged) { _downBlockLogged = true; Log.Fired("downed input lock"); }
                __result = false;
                return false;
            }

            return true;
        }

        static bool DownedAllowed(Il2Cpp.InputKey key)
        {
            switch (key)
            {
                case Il2Cpp.InputKey.WalkForward:
                case Il2Cpp.InputKey.WalkBackward:
                case Il2Cpp.InputKey.WalkLeft:
                case Il2Cpp.InputKey.WalkRight:
                case Il2Cpp.InputKey.Voicechat:
                case Il2Cpp.InputKey.RadioChat:
                case Il2Cpp.InputKey.HealthMenu:
                case Il2Cpp.InputKey.Freelook:
                case Il2Cpp.InputKey.Console:
                    return true;
                default:
                    return false;
            }
        }

        static void Post_MouseSuppressed(ref bool __result)
        {
            if (Ui.BlocksMouse) __result = true;
        }

        static void Post_BoardSubText(Il2Cpp.playerBoardItem __instance)
        {
            Log.Fired("playerBoardItem.UpdateSubText");
            if (!State.Enabled) return;
            try
            {
                var label = __instance.subText;
                if (label == null) return;

                var peer = State.Find(__instance.Networkulong_cSteamID);
                if (peer == null || !peer.HasMod) return;

                string tag = peer.Downed ? "DOWN" : RoleInfo.Short(peer.Role);
                var cur = label.text ?? "";
                if (cur.EndsWith("]")) return;
                label.text = cur.Length > 0 ? cur + "   [" + tag + "]" : "[" + tag + "]";
            }
            catch { }
        }

        static void Pre_FireMissile(Il2Cpp.ISR __instance)
        {
            try { Jtac.BeforeFire(__instance); } catch (Exception e) { Err.Note("hook.fire.pre", e); }
        }

        static void Post_FireMissile(Il2Cpp.ISR __instance)
        {
            try { Jtac.AfterFire(__instance); } catch (Exception e) { Err.Note("hook.fire.post", e); }
        }

        static void Pre_PushFire(Il2Cpp.PlayerNetworking __instance)
        {
            try { Jtac.OnFireRequest(__instance); } catch (Exception e) { Err.Note("hook.fire.request", e); }
        }

        static void Post_IsrUpdate(Il2Cpp.ISR __instance)
        {
            try { if (Drone.BorrowingIsr) Drone.AfterIsrUpdate(__instance); } catch (Exception e) { Err.Note("hook.isr.update", e); }
        }

        static void Post_OpEnd()
        {
            Log.Fired("operation end");
            Downed.HostClearAll("operation ended");
            Perks.Reset("operation ended");
            Pouch.Reset("operation ended");
            Breach.Reset("operation ended");
            Ladder.Reset("operation ended");
            Drone.Reset("operation ended");
            Jtac.Reset("operation ended");
            Radar.Reset("operation ended");
            State.RolesLocked = false;
        }

        static void Post_Respawn()
        {
            Log.Fired("SERVER_RespawnALL");
            Downed.HostClearAll("respawn all");
            Perks.Reset("respawn all");
            Pouch.Reset("respawn all");
            Drone.Reset("respawn all");
            Jtac.Reset("respawn all");
        }
    }
}
