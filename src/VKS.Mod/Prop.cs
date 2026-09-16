// ODG Operations - VKS
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Globalization;
using System.IO;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace VKS
{

    internal static class Prop
    {
        internal const string ObjectName = "WeaponSpawn VKS";
        const string RackRoot = "Weapons";
        const string RackPath = "OPFOR";
        const int InteractableLayer = 9;

        static readonly Vector3 OutOfSight = new Vector3(0f, -100f, 0f);

        internal static bool Built { get; private set; }
        static bool _placed;
        static GameObject _hook, _display;
        static BoxCollider _box;
        static Vector3 _barrel = Vector3.forward;
        static string _barrelSource = "unmeasured";

        static float _builtAt, _nextPoll, _stableSince;
        static int _lastRenderers = -1;
        const float PollEvery = 0.25f;
        const float StableFor = 0.75f;
        const float MinWait = 0.5f;
        const float MaxWait = 8f;

        internal static void Reset()
        {
            Built = false; _placed = false;
            _hook = null; _display = null; _box = null;
            _lastRenderers = -1; _builtAt = _nextPoll = _stableSince = 0f;
            Pose.Reset();
        }

        internal static bool TryBuild(GameObject template)
        {
            if (Built && _hook != null) return true;
            if (template == null) return false;

            var rack = FindRack();
            if (rack == null) return false;

            try
            {

                var existing = rack.Find(ObjectName);
                if (existing != null)
                {
                    _hook = existing.gameObject;
                    _display = _hook.transform.childCount > 0 ? _hook.transform.GetChild(0).gameObject : null;
                    _box = _hook.GetComponent<BoxCollider>();
                    Built = true; _placed = true;
                    return true;
                }

                var go = new GameObject(ObjectName);
                go.transform.SetParent(rack, false);
                go.transform.position = OutOfSight;
                go.layer = InteractableLayer;

                var ni = go.AddComponent(Il2CppType.Of<Il2CppMirror.NetworkIdentity>())
                           .TryCast<Il2CppMirror.NetworkIdentity>();
                if (ni == null) VKSMod.Log("prop: NetworkIdentity add returned null (continuing)");

                var spawner = go.AddComponent(Il2CppType.Of<Il2Cpp.N_StaticWeaponSpawner>())
                                .TryCast<Il2Cpp.N_StaticWeaponSpawner>();
                if (spawner == null) { VKSMod.Log("prop: AddComponent<N_StaticWeaponSpawner> was null"); return false; }
                spawner.WeaponReference = template;
                spawner.DisableWhenPickedUp = false;

                try
                {
                    var ic = go.AddComponent(Il2CppType.Of<Il2Cpp.InteractableComponent>())
                               .TryCast<Il2Cpp.InteractableComponent>();
                    if (ic != null) ic.InteractableName = "VKS";
                }
                catch (Exception ie) { VKSMod.Log("prop: InteractableComponent add failed: " + ie.Message); }

                _display = Art.Build(template, go.transform);
                if (_display == null) { VKSMod.Log("prop: no display, hook abandoned"); UnityEngine.Object.Destroy(go); return false; }

                _box = go.AddComponent(Il2CppType.Of<BoxCollider>()).TryCast<BoxCollider>();
                if (_box != null) { _box.isTrigger = true; _box.enabled = false; }

                _hook = go;
                Built = true;
                _builtAt = Time.unscaledTime;
                _nextPoll = _builtAt + PollEvery;
                _stableSince = _builtAt;
                _lastRenderers = Art.CountRenderers(_display);

                Pose.Load(DefaultPos());
                VKSMod.Log("prop: hook built under " + Art.Path(rack) + ", WeaponRef=" +
                           (spawner.WeaponReference == null ? "null" : spawner.WeaponReference.name) +
                           ". Waiting for the parts to stream in (" + _lastRenderers + " renderers so far).");
                return true;
            }
            catch (Exception e) { VKSMod.Log("prop build failed: " + e); return false; }
        }

        internal static void Tick()
        {
            if (!Built || _hook == null) return;
            try
            {
                if (!_placed) { WatchStreaming(); return; }
                if (Pose.Refresh()) { Apply(true); VKSMod.Log("prop: pose reloaded -> " + Describe()); }
            }
            catch { }
        }

        static void WatchStreaming()
        {
            float now = Time.unscaledTime;
            if (now < _nextPoll) return;
            _nextPoll = now + PollEvery;

            int n = Art.CountRenderers(_display);
            if (n != _lastRenderers) { _lastRenderers = n; _stableSince = now; }

            bool settled = (now - _stableSince) >= StableFor && (now - _builtAt) >= MinWait;
            bool timedOut = (now - _builtAt) >= MaxWait;
            if (!settled && !timedOut) return;

            if (timedOut && !settled) VKSMod.Log("prop: parts still changing after " + MaxWait + "s, placing anyway");
            Place();
        }

        static void Place()
        {
            try
            {
                Art.Neutralise(_display, true);
                _barrel = Art.MeasureBarrel(_display, out _barrelSource);
                VKSMod.Log("art: " + Art.Parts(_display));
                if (Art.Bounds(_display, out var b))
                    VKSMod.Log("art: " + Art.CountRenderers(_display) + " renderer(s), size " + Art.F(b.size) +
                               ", barrel axis (local) " + Art.F(_barrel) + " from " + _barrelSource);

                Apply(true);
                if (_box != null) _box.enabled = true;
                _placed = true;

                VKSMod.Log("PROP PLACED -> " + Describe());
                VKSMod.Log("prop: edit " + Pose.FilePath + " while the game runs; it is re-read twice a second.");
            }
            catch (Exception e) { VKSMod.Log("prop place failed: " + e); _placed = true; }
        }

        static void Apply(bool verbose)
        {
            if (_hook == null || _display == null) return;
            try
            {

                if (_box != null) _box.enabled = false;

                var p = Pose.Current;

                Vector3 desired = Quaternion.AngleAxis(p.Yaw, Vector3.up) *
                                  (Quaternion.AngleAxis(p.Lean, Vector3.right) * Vector3.up);

                Quaternion q = Quaternion.FromToRotation(_barrel, desired);

                Vector3 room = -Vector3.ProjectOnPlane(desired, Vector3.up);
                if (room.sqrMagnitude > 1e-4f)
                {
                    Vector3 upNow = Vector3.ProjectOnPlane(q * Vector3.up, desired);
                    Vector3 upWant = Vector3.ProjectOnPlane(room.normalized, desired);
                    if (upNow.sqrMagnitude > 1e-6f && upWant.sqrMagnitude > 1e-6f)
                    {
                        float fix = Mathf.Atan2(Vector3.Dot(Vector3.Cross(upNow, upWant), desired),
                                                Vector3.Dot(upNow, upWant)) * Mathf.Rad2Deg;
                        q = Quaternion.AngleAxis(fix, desired) * q;
                    }
                }
                q = Quaternion.AngleAxis(p.Roll, desired) * q;

                _hook.transform.rotation = q;
                _hook.transform.position = p.Pos;

                _display.transform.localPosition = Vector3.zero;
                _display.transform.localRotation = Quaternion.identity;
                _display.transform.localScale = Vector3.one * p.Scale;

                if (p.Seat) Seat(p);
                if (p.Wall) SnapToWall(verbose);
                FitCollider();
            }
            catch (Exception e) { VKSMod.Log("prop pose err: " + e.Message); }
            finally { if (_box != null) _box.enabled = true; }
        }

        static void Seat(Pose.Values p)
        {
            if (!Art.Bounds(_display, out var b)) return;

            Vector3 barrelW = (_hook.transform.rotation * _barrel).normalized;
            float half = Mathf.Abs(b.extents.x * barrelW.x)
                       + Mathf.Abs(b.extents.y * barrelW.y)
                       + Mathf.Abs(b.extents.z * barrelW.z);
            Vector3 butt = b.center - barrelW * half;

            Vector3 delta = p.Pos - butt;
            delta.y = p.Pos.y - b.min.y;
            _display.transform.position += delta;
        }

        static void SnapToWall(bool verbose)
        {
            const float back = 0.40f;
            const float reach = 1.20f;
            const float contact = 0.012f;
            const int samples = 7;

            if (!Art.Bounds(_display, out var b)) return;
            var corners = Art.Corners(_display);
            if (corners.Count == 0) return;

            Vector3 barrelW = (_hook.transform.rotation * _barrel).normalized;
            Vector3 h = Vector3.ProjectOnPlane(barrelW, Vector3.up);
            if (h.sqrMagnitude < 1e-4f) { if (verbose) VKSMod.Log("prop: lean is 0, nothing to lean on"); return; }
            h.Normalize();

            float half = Mathf.Abs(b.extents.x * barrelW.x)
                       + Mathf.Abs(b.extents.y * barrelW.y)
                       + Mathf.Abs(b.extents.z * barrelW.z);
            Vector3 butt = b.center - barrelW * half;
            Vector3 tip = b.center + barrelW * half;

            try
            {
                float bestMove = float.NegativeInfinity;
                Vector3 bestNormal = -h;
                string bestName = null;
                int hits = 0;

                for (int i = 0; i < samples; i++)
                {

                    float t = (i + 0.5f) / samples;
                    Vector3 s = Vector3.Lerp(butt, tip, t);

                    RaycastHit hit;
                    if (!Physics.Raycast(s - h * back, h, out hit, back + reach)) continue;

                    Vector3 n = Vector3.ProjectOnPlane(hit.normal, Vector3.up);
                    if (n.sqrMagnitude < 1e-4f) continue;
                    n.Normalize();
                    hits++;

                    float deepest = float.PositiveInfinity;
                    for (int c = 0; c < corners.Count; c++)
                    {
                        float d = Vector3.Dot(corners[c] - hit.point, n);
                        if (d < deepest) deepest = d;
                    }

                    float move = contact - deepest;
                    if (move > bestMove) { bestMove = move; bestNormal = n; bestName = hit.collider.gameObject.name; }
                }

                if (hits == 0)
                {
                    if (verbose) VKSMod.Log("prop: nothing within " + reach.ToString("F2") + "m behind the rifle, left where it is");
                    return;
                }
                if (Mathf.Abs(bestMove) > 0.60f)
                {
                    if (verbose) VKSMod.Log("prop: wall probe wanted to move it " + bestMove.ToString("F3") +
                                            "m off '" + bestName + "' - refused, that is not the wall");
                    return;
                }

                _hook.transform.position += bestNormal * bestMove;
                if (verbose) VKSMod.Log("prop: resting on '" + bestName + "', moved " + bestMove.ToString("F3") +
                                        "m " + (bestMove >= 0f ? "out of it" : "towards it") +
                                        " (" + hits + " surface(s) checked)");
            }
            catch (Exception e) { if (verbose) VKSMod.Log("prop: wall probe failed: " + e.Message); }
        }

        static void FitCollider()
        {
            if (_box == null) return;
            if (!Art.LocalBounds(_hook.transform, out var lb)) return;
            var size = lb.size;
            size.x = Mathf.Max(size.x + 0.06f, 0.16f);
            size.y = Mathf.Max(size.y + 0.06f, 0.16f);
            size.z = Mathf.Max(size.z + 0.06f, 0.16f);
            _box.center = lb.center;
            _box.size = size;
        }

        static string Describe()
        {
            try
            {
                var s = "pos " + Art.F(_hook.transform.position) + " euler " + Art.F(_hook.transform.eulerAngles);
                if (Art.Bounds(_display, out var b)) s += " bounds " + Art.F(b.size) + " floor y=" + b.min.y.ToString("F3");
                return s;
            }
            catch { return "?"; }
        }

        static Vector3 DefaultPos()
        {
            float x = -13.55f, z = -15.20f, y = 0f;
            try
            {
                RaycastHit hit;
                if (Physics.Raycast(new Vector3(x, 2f, z), Vector3.down, out hit, 6f))
                {
                    y = hit.point.y;
                    VKSMod.Log("prop: floor probe hit '" + hit.collider.gameObject.name + "' at y=" + y.ToString("F3"));
                }
                else VKSMod.Log("prop: floor probe found nothing, using y=0");
            }
            catch (Exception e) { VKSMod.Log("prop: floor probe failed (" + e.Message + "), using y=0"); }
            return new Vector3(x, y, z);
        }

        static Transform FindRack()
        {
            try
            {
                for (int s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++)
                {
                    var sc = UnityEngine.SceneManagement.SceneManager.GetSceneAt(s);
                    if (!sc.isLoaded) continue;
                    var roots = sc.GetRootGameObjects();
                    for (int r = 0; r < roots.Length; r++)
                    {
                        if (roots[r].name != RackRoot) continue;
                        var t = roots[r].transform.Find(RackPath);
                        if (t != null) return t;
                    }
                }
            }
            catch { }
            return null;
        }
    }

    internal static class Pose
    {
        internal struct Values
        {
            internal Vector3 Pos;
            internal float Yaw, Lean, Roll, Scale;
            internal bool Seat, Wall;
        }

        internal static Values Current;
        static DateTime _stamp;
        static float _nextCheck;
        static bool _loaded;

        internal static string FilePath
        {
            get
            {
                try { return Path.Combine(VKSMod.GameRoot, "UserData", "VKS_prop.txt"); }
                catch { return "VKS_prop.txt"; }
            }
        }

        internal static void Reset() { _loaded = false; _nextCheck = 0f; _stamp = default; }

        internal static void Load(Vector3 fallbackPos)
        {
            if (!_loaded)
            {
                Current = new Values { Pos = fallbackPos, Yaw = 180f, Lean = 22f, Roll = 0f, Scale = 1f, Seat = true, Wall = true };
                _loaded = true;
            }
            try
            {
                if (!File.Exists(FilePath)) { Write(); return; }

                var text = File.ReadAllText(FilePath);
                Parse(text);
                _stamp = File.GetLastWriteTimeUtc(FilePath);

                if (text.IndexOf("wall", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    VKSMod.Log("pose: file predates the wall option, rewriting it with your values kept");
                    Write();
                }
            }
            catch (Exception e) { VKSMod.Log("pose: load failed (" + e.Message + "), using defaults"); }
        }

        internal static bool Refresh()
        {
            if (Time.unscaledTime < _nextCheck) return false;
            _nextCheck = Time.unscaledTime + 0.5f;
            try
            {
                if (!File.Exists(FilePath)) return false;
                var t = File.GetLastWriteTimeUtc(FilePath);
                if (t == _stamp) return false;
                _stamp = t;
                Parse(File.ReadAllText(FilePath));
                return true;
            }
            catch { return false; }
        }

        static void Parse(string text)
        {
            try
            {
                foreach (var raw in text.Replace("\r", "").Split('\n'))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    var key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    var val = line.Substring(eq + 1).Trim();

                    switch (key)
                    {
                        case "pos":
                            var parts = val.Split(',');
                            if (parts.Length >= 3)
                            {
                                Current.Pos = new Vector3(Num(parts[0], Current.Pos.x),
                                                          Num(parts[1], Current.Pos.y),
                                                          Num(parts[2], Current.Pos.z));
                            }
                            break;
                        case "yaw": Current.Yaw = Num(val, Current.Yaw); break;
                        case "lean": Current.Lean = Num(val, Current.Lean); break;
                        case "roll": Current.Roll = Num(val, Current.Roll); break;
                        case "scale": Current.Scale = Mathf.Max(0.01f, Num(val, Current.Scale)); break;
                        case "seat": Current.Seat = Num(val, Current.Seat ? 1f : 0f) != 0f; break;
                        case "wall": Current.Wall = Num(val, Current.Wall ? 1f : 0f) != 0f; break;
                    }
                }
            }
            catch (Exception e) { VKSMod.Log("pose: parse failed: " + e.Message); }
        }

        static float Num(string s, float fallback)
        {
            float f;
            return float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out f) ? f : fallback;
        }

        static void Write()
        {
            try
            {
                var p = Current;
                var text =
                    "# VKS prop placement. Written by the mod, re-read twice a second while the game runs.\n" +
                    "# Save the file and the rifle moves. No rebuild, no restart.\n" +
                    "#\n" +
                    "#   pos    world point where the buttstock touches the floor, x, y, z\n" +
                    "#   yaw    degrees about world Y. 180 leans it back against the OPFOR wall\n" +
                    "#   lean   degrees the muzzle tips away from vertical. 0 stands it straight up\n" +
                    "#   roll   degrees about the barrel. 0 sights facing the room, 90 on its side,\n" +
                    "#          180 sights to the wall\n" +
                    "#   scale  display scale. 1 is actual size\n" +
                    "#   seat   1 drops it so the stock sits on pos, 0 leaves the art where it lands\n" +
                    "#   wall   1 slides it back until the muzzle touches the wall behind it, 0 leaves\n" +
                    "#          the gap. With wall on, pos only has to be roughly right\n" +
                    "#\n" +
                    "# If it stands on its muzzle, the barrel axis was guessed rather than measured\n" +
                    "# (the log says which): add 180 to lean.\n" +
                    "\n" +
                    "pos   = " + p.Pos.x.ToString("F3", CultureInfo.InvariantCulture) + ", "
                               + p.Pos.y.ToString("F3", CultureInfo.InvariantCulture) + ", "
                               + p.Pos.z.ToString("F3", CultureInfo.InvariantCulture) + "\n" +
                    "yaw   = " + p.Yaw.ToString("F1", CultureInfo.InvariantCulture) + "\n" +
                    "lean  = " + p.Lean.ToString("F1", CultureInfo.InvariantCulture) + "\n" +
                    "roll  = " + p.Roll.ToString("F1", CultureInfo.InvariantCulture) + "\n" +
                    "scale = " + p.Scale.ToString("F3", CultureInfo.InvariantCulture) + "\n" +
                    "seat  = " + (p.Seat ? "1" : "0") + "\n" +
                    "wall  = " + (p.Wall ? "1" : "0") + "\n";

                var dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(FilePath, text);
                _stamp = File.GetLastWriteTimeUtc(FilePath);
                VKSMod.Log("pose: wrote " + FilePath);
            }
            catch (Exception e) { VKSMod.Log("pose: could not write " + FilePath + ": " + e.Message); }
        }
    }
}
