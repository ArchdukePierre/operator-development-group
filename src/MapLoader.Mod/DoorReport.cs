// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Text;
using MelonLoader;
using UnityEngine;
using NetworkIdentity = Il2CppMirror.NetworkIdentity;

namespace MapLoader
{

    public static class DoorReport
    {
        static MelonLogger.Instance _log;
        public static void Init(MelonLogger.Instance log) { _log = log; }

        public static void Dump(GameObject go, string why)
        {
            if (go == null) { _log?.Msg("[doorreport] " + why + ": <null>"); return; }
            try
            {
                var sb = new StringBuilder();
                sb.Append("\n===== DOOR REPORT (").Append(why).Append(") =====\n");

                sb.Append("name        : ").Append(go.name).Append('\n');
                sb.Append("scene       : ").Append(SceneOf(go)).Append('\n');
                sb.Append("activeSelf  : ").Append(go.activeSelf)
                  .Append("   activeInHierarchy: ").Append(go.activeInHierarchy).Append('\n');

                sb.Append("ancestors   : ");
                var t = go.transform.parent;
                if (t == null) sb.Append("<none, it is a scene root>");
                int guard = 0;
                while (t != null && guard++ < 10)
                {
                    sb.Append(t.gameObject.activeSelf ? "on " : "OFF ").Append(t.name).Append(" / ");
                    t = t.parent;
                }
                sb.Append('\n');

                sb.Append("position    : ").Append(go.transform.position).Append('\n');
                sb.Append("lossyScale  : ").Append(go.transform.lossyScale).Append('\n');
                sb.Append("rotation    : ").Append(go.transform.rotation.eulerAngles).Append('\n');
                sb.Append("children    : ").Append(go.transform.childCount).Append('\n');

                var rends = go.GetComponentsInChildren<Renderer>(true);
                sb.Append("renderers   : ").Append(rends == null ? 0 : rends.Length).Append('\n');
                if (rends != null)
                {
                    int shown = 0;
                    var total = new Bounds(go.transform.position, Vector3.zero);
                    bool anyBounds = false;
                    for (int i = 0; i < rends.Length; i++)
                    {
                        var r = rends[i];
                        if (r == null) continue;
                        if (!anyBounds) { total = r.bounds; anyBounds = true; }
                        else total.Encapsulate(r.bounds);
                        if (shown++ < 6)
                        {
                            string mat = "?";
                            try
                            {
                                var m = r.sharedMaterial;
                                mat = m == null ? "NULL MATERIAL"
                                    : (m.shader != null ? m.shader.name : "no shader");
                            }
                            catch { }
                            sb.Append("   [").Append(i).Append("] ").Append(r.gameObject.name)
                              .Append("  enabled=").Append(r.enabled)
                              .Append(" forcedOff=").Append(Forced(r))
                              .Append(" goActive=").Append(r.gameObject.activeInHierarchy)
                              .Append(" size=").Append(r.bounds.size)
                              .Append(" mat=").Append(mat).Append('\n');
                        }
                    }
                    if (anyBounds)
                        sb.Append("   total bounds centre=").Append(total.center)
                          .Append(" size=").Append(total.size).Append('\n');
                }

                var cols = go.GetComponentsInChildren<Collider>(true);
                int colOn = 0;
                if (cols != null)
                    for (int i = 0; i < cols.Length; i++)
                        if (cols[i] != null && cols[i].enabled) colOn++;
                sb.Append("colliders   : ").Append(cols == null ? 0 : cols.Length)
                  .Append(" (").Append(colOn).Append(" enabled)\n");

                var d = go.GetComponent<Il2Cpp.DoorV2>();
                if (d == null) sb.Append("DoorV2      : ABSENT ON THE ROOT\n");
                else
                {
                    sb.Append("DoorV2      : enabled=").Append(Safe(() => d.enabled))
                      .Append(" locked=").Append(Safe(() => d.CurrentlyLocked))
                      .Append(" inUse=").Append(Safe(() => d.inUse))
                      .Append(" isDead=").Append(Safe(() => d.isDead)).Append('\n');
                    sb.Append("  pivot=").Append(Ref(() => d.PivotTransform))
                      .Append(" hitbox=").Append(Ref(() => d.DoorHitBox))
                      .Append(" modelParent=").Append(Ref(() => d.DoorModelParent))
                      .Append(" rb=").Append(Ref(() => d.rb)).Append('\n');
                    sb.Append("  handleFront=").Append(Ref(() => d.HandleFront))
                      .Append(" handleBack=").Append(Ref(() => d.HandleBack)).Append('\n');

                    sb.Append("  currentRotation=").Append(Safe(() => d.currentRotation))
                      .Append(" predicted=").Append(Safe(() => d.PredictedRotation))
                      .Append(" velocity=").Append(Safe(() => d.Velocity))
                      .Append(" canLatch=").Append(Safe(() => d.canLatch))
                      .Append(" isLatched=").Append(Safe(() => d.IsLatched))
                      .Append(" maxRotationY=").Append(Safe(() => d.maxRotationY))
                      .Append(" invert=").Append(Safe(() => d.Invert)).Append('\n');
                    sb.Append("  pivotLocalEuler=")
                      .Append(Safe(() => d.PivotTransform.localRotation.eulerAngles.ToString("F1")))
                      .Append('\n');
                }

                var handles = go.GetComponentsInChildren<Il2Cpp.DoorHandleV2>(true);
                sb.Append("handles     : ").Append(handles == null ? 0 : handles.Length).Append('\n');
                if (handles != null)
                    for (int i = 0; i < handles.Length && i < 4; i++)
                    {
                        var h = handles[i];
                        if (h == null) continue;
                        sb.Append("   [").Append(i).Append("] enabled=").Append(Safe(() => h.enabled))
                          .Append(" MyLocalPlayer=").Append(Ref(() => h.MyLocalPlayer))
                          .Append(" IntSystem=").Append(Ref(() => h.PlayerInteractionSystem))
                          .Append(" raycastTf=").Append(Ref(() => h.raycastTransform))
                          .Append(" handleDist=").Append(Safe(() => h.distanceHandleToPlayer))
                          .Append(" centerDist=").Append(Safe(() => h.distanceCenterToPlayer))
                          .Append('\n');
                    }

                var ni = go.GetComponent<NetworkIdentity>();
                sb.Append("network     : ");
                if (ni == null) sb.Append("NO NetworkIdentity\n");
                else sb.Append("netId=").Append(Safe(() => ni.netId))
                       .Append(" assetId=").Append(Safe(() => ni.assetId))
                       .Append(" isServer=").Append(Safe(() => ni.isServer))
                       .Append(" isClient=").Append(Safe(() => ni.isClient)).Append('\n');

                sb.Append("hierarchy   :\n");
                Tree(sb, go.transform, "   ", 0, 2);

                sb.Append("=====================================");
                _log?.Msg(sb.ToString());
            }
            catch (Exception e) { _log?.Error("[doorreport] " + e.Message); }
        }

        static void Tree(StringBuilder sb, Transform t, string pad, int depth, int max)
        {
            if (t == null || depth > max) return;
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                if (c == null) continue;
                var r = c.GetComponent<Renderer>();
                sb.Append(pad).Append(c.gameObject.activeSelf ? "on  " : "OFF ")
                  .Append(c.name);
                if (r != null)
                    sb.Append("   [Renderer enabled=").Append(r.enabled)
                      .Append(" forcedOff=").Append(Forced(r)).Append(']');
                sb.Append('\n');
                Tree(sb, c, pad + "   ", depth + 1, max);
            }
        }

        static string Forced(Renderer r)
        { try { return r.forceRenderingOff.ToString(); } catch { return "?"; } }

        static string SceneOf(GameObject go)
        { try { return go.scene.IsValid() ? go.scene.name : "<prefab asset>"; } catch { return "?"; } }

        static string Safe<T>(Func<T> f)
        { try { var v = f(); return v == null ? "null" : v.ToString(); } catch { return "<threw>"; } }

        static string Ref<T>(Func<T> f) where T : class
        { try { return f() != null ? "ok" : "NULL"; } catch { return "<threw>"; } }
    }
}
