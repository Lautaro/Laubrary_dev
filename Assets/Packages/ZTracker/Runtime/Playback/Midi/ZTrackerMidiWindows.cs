using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Laubrary.ZTracker.Midi
{
    /// <summary>
    /// MIDI input through the Windows multimedia API (winmm), present on every Windows version with no driver or
    /// package to install. Messages arrive on a Windows-owned thread and go straight to the router; nothing is queued on
    /// the main thread. One device is open at a time. The device is always closed before scripts reload, when play mode
    /// changes the domain, and when the editor or player quits.
    /// </summary>
    public sealed class ZTrackerMidiWindows : IZTrackerMidiSource
    {
        const uint CallbackFunction = 0x30000;
        const uint MimData = 0x3C3, MimMoreData = 0x3CC;
        const int MmsyserrAllocated = 4, MmsyserrBadDeviceId = 2, MmsyserrNoMem = 7;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct MidiInCaps { public ushort wMid, wPid; public uint vDriverVersion; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname; public uint dwSupport; }
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void MidiInProc(IntPtr handle, uint message, IntPtr instance, IntPtr param1, IntPtr param2);

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [DllImport("winmm.dll")] static extern uint midiInGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)] static extern int midiInGetDevCapsW(UIntPtr id, ref MidiInCaps caps, uint size);
        [DllImport("winmm.dll")] static extern int midiInOpen(out IntPtr handle, uint deviceId, MidiInProc callback, IntPtr instance, uint flags);
        [DllImport("winmm.dll")] static extern int midiInStart(IntPtr handle);
        [DllImport("winmm.dll")] static extern int midiInStop(IntPtr handle);
        [DllImport("winmm.dll")] static extern int midiInReset(IntPtr handle);
        [DllImport("winmm.dll")] static extern int midiInClose(IntPtr handle);
        public static bool Supported => true;
#else
        static uint midiInGetNumDevs() => 0;
        static int midiInGetDevCapsW(UIntPtr id, ref MidiInCaps caps, uint size) => MmsyserrBadDeviceId;
        static int midiInOpen(out IntPtr handle, uint deviceId, MidiInProc callback, IntPtr instance, uint flags) { handle = IntPtr.Zero; return MmsyserrBadDeviceId; }
        static int midiInStart(IntPtr handle) => 0;
        static int midiInStop(IntPtr handle) => 0;
        static int midiInReset(IntPtr handle) => 0;
        static int midiInClose(IntPtr handle) => 0;
        public static bool Supported => false;
#endif

        // A static method behind a delegate kept alive for the process: required by IL2CPP and safe under Mono.
        static readonly MidiInProc callback = OnMessage;
        static volatile ZTrackerMidiRouter receiver;
        static readonly List<ZTrackerMidiWindows> open = new List<ZTrackerMidiWindows>();
        static bool hooked;

        readonly List<string> devices = new List<string>();
        IntPtr handle;
        public string SourceName => "Windows MIDI";
        public int DeviceCount => devices.Count;
        public string DeviceName(int index) => devices[index];
        public bool IsOpen => handle != IntPtr.Zero;
        public string OpenDevice { get; private set; }

        [AOT.MonoPInvokeCallback(typeof(MidiInProc))]
        static void OnMessage(IntPtr h, uint message, IntPtr instance, IntPtr param1, IntPtr param2)
        {
            if (message != MimData && message != MimMoreData) return;
            var r = receiver;
            if (r != null) r.Receive((uint)param1.ToInt64(), System.Diagnostics.Stopwatch.GetTimestamp());
        }

        public ZTrackerMidiWindows() { Hook(); RefreshDevices(); }

        public bool RefreshDevices()
        {
            uint count = midiInGetNumDevs();
            var names = new List<string>((int)count);
            for (uint i = 0; i < count; i++)
            {
                var caps = new MidiInCaps();
                names.Add(midiInGetDevCapsW((UIntPtr)i, ref caps, (uint)Marshal.SizeOf<MidiInCaps>()) == 0 ? caps.szPname : "Device " + (i + 1));
            }
            bool changed = names.Count != devices.Count;
            for (int i = 0; !changed && i < names.Count; i++) changed = names[i] != devices[i];
            if (changed) { devices.Clear(); devices.AddRange(names); }
            return changed;
        }

        public bool Open(string device, ZTrackerMidiRouter router, out string error)
        {
            Close(); error = null;
            if (!Supported) { error = "MIDI input is available on Windows only"; return false; }
            RefreshDevices();
            int index = devices.IndexOf(device);
            if (index < 0) { error = "Not found"; return false; }
            int result = midiInOpen(out var h, (uint)index, callback, IntPtr.Zero, CallbackFunction);
            if (result != 0)
            {
                error = result == MmsyserrAllocated ? "In use by another program" : result == MmsyserrBadDeviceId ? "Not found" : result == MmsyserrNoMem ? "Out of memory" : "Could not open (error " + result + ")";
                return false;
            }
            receiver = router; handle = h; OpenDevice = device;
            lock (open) open.Add(this);
            result = midiInStart(h);
            if (result != 0) { Close(); error = "Could not start (error " + result + ")"; return false; }
            return true;
        }

        public void Close()
        {
            if (handle == IntPtr.Zero) return;
            var h = handle; handle = IntPtr.Zero;
            midiInStop(h); midiInReset(h); midiInClose(h);
            // After midiInClose returns, Windows delivers no further messages for this handle.
            receiver = null; OpenDevice = null;
            lock (open) open.Remove(this);
        }

        public void Dispose() => Close();

        static void CloseAll()
        {
            ZTrackerMidiWindows[] all; lock (open) all = open.ToArray();
            foreach (var source in all) source.Close();
            receiver = null;
        }

        static void Hook()
        {
            if (hooked) return; hooked = true;
            Application.quitting += CloseAll;
#if UNITY_EDITOR
            AssemblyReloadEvents.beforeAssemblyReload += CloseAll;
            EditorApplication.quitting += CloseAll;
#endif
        }
    }

    /// <summary>A stand-in keyboard for checks and for trying the window without hardware. Messages are delivered on the
    /// calling thread through the same router path a real device uses.</summary>
    public sealed class ZTrackerMidiSimulated : IZTrackerMidiSource
    {
        public const string Device = "Simulated keyboard";
        /// <summary>The one simulated keyboard the editor lists beside real devices; checks drive it.</summary>
        public static readonly ZTrackerMidiSimulated Shared = new ZTrackerMidiSimulated();
        ZTrackerMidiRouter router;
        /// <summary>Makes the next Open fail as if another program held the device.</summary>
        public bool SimulateInUse;
        /// <summary>Removes the device from the list, as if it had been unplugged.</summary>
        public bool Unplugged;
        public string SourceName => "Simulated";
        public bool RefreshDevices() { bool gone = Unplugged && router != null; if (gone) Close(); return gone; }
        public int DeviceCount => Unplugged ? 0 : 1;
        public string DeviceName(int index) => Device;
        public bool IsOpen => router != null;
        public string OpenDevice => router != null ? Device : null;
        public bool Open(string device, ZTrackerMidiRouter target, out string error)
        {
            Close(); error = null;
            if (Unplugged || device != Device) { error = "Not found"; return false; }
            if (SimulateInUse) { error = "In use by another program"; return false; }
            router = target; return true;
        }
        public void Close() => router = null;
        public void Dispose() => Close();
        public bool Send(int status, int data1, int data2)
        {
            var r = router; if (r == null) return false;
            r.Receive((uint)(status & 0xFF) | (uint)(data1 & 0x7F) << 8 | (uint)(data2 & 0x7F) << 16, System.Diagnostics.Stopwatch.GetTimestamp());
            return true;
        }
        public bool NoteOn(int note, int velocity = 100, int channel = 0) => Send(0x90 | channel, note, velocity);
        public bool NoteOff(int note, int channel = 0) => Send(0x80 | channel, note, 0);
        public bool Controller(int number, int value, int channel = 0) => Send(0xB0 | channel, number, value);
        public bool Bend(int value14, int channel = 0) => Send(0xE0 | channel, value14 & 0x7F, value14 >> 7);
    }
}
