// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using MelonLoader;
using UnityEngine.SceneManagement;

namespace MapLoader
{

    public static unsafe class SceneFactory
    {
        [StructLayout(LayoutKind.Sequential)]
        struct ManagedSpanWrapper
        {
            public void* Begin;
            public int Length;
        }

        delegate void CreateSceneInjectedDelegate(
            ref ManagedSpanWrapper name, ref CreateSceneParameters parameters, out Scene ret);

        static bool _probed;
        static CreateSceneInjectedDelegate _icall;
        static IntPtr _methodPtr;

        public static Scene TryCreate(string name, MelonLogger.Instance log)
        {
            Probe(log);

            if (_methodPtr != IntPtr.Zero)
            {
                try
                {
                    var pars = default(CreateSceneParameters);
                    IntPtr namePtr = IL2CPP.ManagedStringToIl2Cpp(name);
                    IntPtr exc = IntPtr.Zero;
                    var args = new void*[] { (void*)namePtr, &pars };
                    fixed (void** pArgs = args)
                    {
                        IntPtr ret = IL2CPP.il2cpp_runtime_invoke(
                            _methodPtr, IntPtr.Zero, pArgs, ref exc);
                        if (exc == IntPtr.Zero && ret != IntPtr.Zero)
                        {

                            int handle = *(int*)IL2CPP.il2cpp_object_unbox(ret);
                            var s = SceneFromHandle(handle);
                            if (s.IsValid())
                            {
                                log?.Msg("[scene] created via CreateScene method pointer");
                                return s;
                            }
                        }
                    }
                }
                catch (Exception e)
                { log?.Warning("[scene] method-pointer route failed: " + e.Message); }
            }

            if (_icall != null)
            {
                try
                {
                    var pars = default(CreateSceneParameters);
                    Scene ret;
                    fixed (char* p = name)
                    {
                        var span = new ManagedSpanWrapper { Begin = p, Length = name.Length };
                        _icall(ref span, ref pars, out ret);
                    }
                    if (ret.IsValid())
                    {
                        log?.Msg("[scene] created via CreateScene_Injected icall");
                        return ret;
                    }
                    log?.Warning("[scene] CreateScene_Injected returned an invalid scene");
                }
                catch (Exception e)
                { log?.Warning("[scene] icall route failed: " + e.Message); }
            }

            return default(Scene);
        }

        static void Probe(MelonLogger.Instance log)
        {
            if (_probed) return;
            _probed = true;

            try
            {
                var fields = typeof(SceneManager).GetFields(
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (var f in fields)
                {
                    if (!f.Name.StartsWith("NativeMethodInfoPtr_CreateScene",
                                           StringComparison.Ordinal)) continue;
                    if (f.GetValue(null) is IntPtr p && p != IntPtr.Zero)
                    {
                        _methodPtr = p;
                        log?.Msg("[scene] found " + f.Name);
                        break;
                    }
                }
            }
            catch (Exception e) { log?.Warning("[scene] pointer probe: " + e.Message); }

            try
            {
                _icall = IL2CPP.ResolveICall<CreateSceneInjectedDelegate>(
                    "UnityEngine.SceneManagement.SceneManager::CreateScene_Injected");
            }
            catch (Exception e) { log?.Warning("[scene] icall probe: " + e.Message); }

            log?.Msg("[scene] CreateScene availability: methodPtr="
                   + (_methodPtr != IntPtr.Zero) + " icall=" + (_icall != null));
        }

        static Scene SceneFromHandle(int handle)
        {
            var s = default(Scene);
            try
            {
                var f = typeof(Scene).GetField("m_Handle",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null)
                {
                    object boxed = s;
                    f.SetValue(boxed, handle);
                    s = (Scene)boxed;
                }
            }
            catch { }
            return s;
        }
    }
}
