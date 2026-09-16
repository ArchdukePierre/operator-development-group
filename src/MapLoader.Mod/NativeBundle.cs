// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace MapLoader
{

    public static unsafe class NativeBundle
    {
        [StructLayout(LayoutKind.Sequential)]
        struct ManagedSpanWrapper
        {
            public void* Begin;
            public int Length;
        }

        delegate IntPtr LoadFromFileInjectedDelegate(
            ref ManagedSpanWrapper path, uint crc, ulong offset);

        static bool _probed;
        static LoadFromFileInjectedDelegate _icall;

        public static AssetBundle LoadSync(string path, out string how)
        {
            how = "";
            if (string.IsNullOrEmpty(path)) { how = "empty path"; return null; }

            if (!_probed)
            {
                _probed = true;
                try
                {
                    _icall = IL2CPP.ResolveICall<LoadFromFileInjectedDelegate>(
                        "UnityEngine.AssetBundle::LoadFromFile_Internal_Injected");
                }
                catch (Exception e) { how = "icall probe threw: " + e.Message; }
            }
            if (_icall == null)
            {
                how = "LoadFromFile_Internal_Injected is not registered on this build";
                return null;
            }

            try
            {
                fixed (char* p = path)
                {
                    var span = new ManagedSpanWrapper { Begin = p, Length = path.Length };
                    IntPtr handle = _icall(ref span, 0u, 0uL);
                    if (handle == IntPtr.Zero)
                    {
                        how = "injected icall returned a null GCHandle";
                        return null;
                    }
                    IntPtr obj = IL2CPP.il2cpp_gchandle_get_target(handle);
                    if (obj == IntPtr.Zero)
                    {
                        how = "injected icall returned a GCHandle with a null target";
                        return null;
                    }
                    how = "loaded through LoadFromFile_Internal_Injected";
                    return new AssetBundle(obj);
                }
            }
            catch (Exception e)
            {
                how = e.GetType().Name + ": " + e.Message;
                return null;
            }
        }
    }
}
