// ODG Operations - FireteamRoles
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Text;
using UnityEngine;

namespace FireteamRoles
{

    internal static class Diag
    {
        internal static void Dump()
        {

            Err.Guard("breach.survey", Breach.Survey);

            var sb = new StringBuilder();
            sb.Append("\n==================== DUMP ").Append(DateTime.Now).Append(" ====================\n");
            sb.Append("host=").Append(State.IsHost)
              .Append("  lobby=").Append(Net.Lobby)
              .Append("  self=").Append(Net.Self)
              .Append("  role=").Append(RoleInfo.Name(State.MyRole))
              .Append("  pvp=").Append(State.IsPvp)
              .Append("  inMission=").Append(State.InMission)
              .Append("  dragging=").Append(Drag.Victim)
              .Append("  treating=").Append(Treat.TargetId).Append('\n');

            foreach (var kv in State.Peers)
            {
                var p = kv.Value;
                sb.Append("\nPEER ").Append(p.Id).Append("  '").Append(p.Name).Append("'\n");
                sb.Append("  role=").Append(RoleInfo.Name(p.Role))
                  .Append("  hasMod=").Append(p.HasMod)
                  .Append("  downed=").Append(p.Downed)
                  .Append("  remaining=").Append(p.Remaining.ToString("0.0"))
                  .Append("  revives=").Append(p.Revives)
                  .Append("  dragger=").Append(p.Dragger).Append('\n');
                sb.Append("  live=").Append(p.Live)
                  .Append("  pn=").Append(p.Pn != null)
                  .Append("  pm=").Append(p.Pm != null)
                  .Append("  health=").Append(p.Health != null)
                  .Append("  med=").Append(p.Med != null)
                  .Append("  pos=").Append(p.Position).Append('\n');

                if (p.Health != null)
                {
                    try
                    {
                        sb.Append("  isDead=").Append(p.Health.isDead).Append("  isBot=").Append(p.Health.isBot).Append('\n');
                        var entries = Wounds.Read(p.Health);
                        for (int i = 0; i < entries.Count; i++)
                        {
                            var e = entries[i];
                            sb.Append("    ").Append(Wounds.Label(e.Part).PadRight(10))
                              .Append(" hp=").Append(e.HP.ToString("0.0").PadRight(7))
                              .Append(" bleed=").Append(e.Rate.ToString("0.00").PadRight(7))
                              .Append(" fractured=").Append(e.Fractured).Append('\n');
                        }
                        sb.Append("    clean=").Append(Wounds.IsClean(p.Health))
                          .Append("  open=[").Append(Wounds.OpenSummary(p.Health)).Append("]\n");
                    }
                    catch (Exception e) { sb.Append("    health read failed: ").Append(e.Message).Append('\n'); }
                }

                if (p.Med != null)
                {
                    try
                    {
                        sb.Append("  hr=").Append(p.Med.HeartRate.ToString("0"))
                          .Append(" target=").Append(p.Med.targetHeartRate.ToString("0"))
                          .Append(" pain=").Append(p.Med.PainLevel.ToString("0.00"))
                          .Append(" morphine=").Append(p.Med.currentMorphineInUse)
                          .Append(" effects=").Append(p.Med.activeEffects).Append('\n');
                        sb.Append("  tq: LArm=").Append(p.Med.Tourniquet_LArm)
                          .Append(" RArm=").Append(p.Med.Tourniquet_RArm)
                          .Append(" LLeg=").Append(p.Med.Tourniquet_LLeg)
                          .Append(" RLeg=").Append(p.Med.Tourniquet_RLeg)
                          .Append(" Head=").Append(p.Med.Tourniquet_Head)
                          .Append("  tqTimer=").Append(p.Med.tourniquetDamageTimer.ToString("0.0"))
                          .Append('/').Append(p.Med.tourniquetDamageTime.ToString("0.0")).Append('\n');

                        var items = p.Med.inventoryItems;
                        sb.Append("  items=").Append(items != null ? items.Count : -1)
                          .Append("  slots=").Append(p.Med.inventorySlots != null ? p.Med.inventorySlots.Count : -1).Append('\n');
                        if (items != null)
                        {
                            for (int i = 0; i < items.Count; i++)
                            {
                                var it = items[i];
                                if (it == null) { sb.Append("    [").Append(i).Append("] null\n"); continue; }
                                sb.Append("    [").Append(i).Append("] ")
                                  .Append((it.ItemName ?? "?").PadRight(20))
                                  .Append(" type=").Append(it.itemType.ToString().PadRight(20))
                                  .Append(" uses=").Append(it.currentUses).Append('/').Append(it.maxUses)
                                  .Append(" useTime=").Append(it.UseTime.ToString("0.0"))
                                  .Append(" slot=").Append(it.inventorySlot != null)
                                  .Append(" epi=").Append(Epi.IsEpi(it)).Append('\n');
                            }
                        }
                    }
                    catch (Exception e) { sb.Append("    medical read failed: ").Append(e.Message).Append('\n'); }
                    try
                    {
                        var live = Medical.Live(p.Med);
                        sb.Append("  live in slots=").Append(live.Count).Append('\n');
                        for (int i = 0; i < live.Count; i++)
                        {
                            var it = live[i];
                            sb.Append("    slot[").Append(i).Append("] ")
                              .Append((it.ItemName ?? "?").PadRight(20))
                              .Append(" uses=").Append(it.currentUses).Append('/').Append(it.maxUses)
                              .Append(" epi=").Append(Epi.IsEpi(it)).Append('\n');
                        }
                    }
                    catch (Exception e) { sb.Append("    live read failed: ").Append(e.Message).Append('\n'); }
                }

                if (p.Pn != null)
                {
                    try
                    {
                        var gl = p.Pn.GrenadesList;
                        sb.Append("  grenades=").Append(gl != null ? gl.Count : -1);
                        sb.Append("  leftShoulder=").Append(p.Pn.leftShoulder != null)
                          .Append(" rightShoulder=").Append(p.Pn.rightShoulder != null)
                          .Append(" maxShoulderDist=").Append(p.Pn.maxShoulderDistance.ToString("0.0")).Append('\n');
                    }
                    catch (Exception e) { sb.Append("    pn read failed: ").Append(e.Message).Append('\n'); }
                }

                try
                {
                    var sys = p.Root != null ? p.Root.GetComponentInChildren<Il2CppRootMotion.FinalIK.InteractionSystem>(true) : null;
                    sb.Append("  interactionSystem=").Append(sys != null).Append('\n');
                }
                catch { }
            }

            sb.Append("==================== END DUMP ====================\n");
            Log.Detail(sb.ToString());
            Log.Flush();
            Log.Msg("dump written to " + Log.LogPath);
            Ui.Toast("diagnostic dump written");
        }
    }
}
