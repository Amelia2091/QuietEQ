using System.Runtime.InteropServices;
using Microsoft.Win32;
using QuietEQ.Core;

namespace QuietEQ.App;

internal static class DeviceService
{
    public static string? ConfigDirectory()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\EqualizerAPO");
        return key?.GetValue("ConfigPath") as string;
    }
    public static string? InstallDirectory()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\EqualizerAPO");
        return key?.GetValue("InstallPath") as string;
    }

    public static List<AudioDevice> Enumerate()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        IMMDeviceCollection? collection = null;
        IMMDevice? defaultDevice = null;
        try
        {
            string? defaultId = null;
            if (enumerator.GetDefaultAudioEndpoint(0, 1, out defaultDevice) == 0)
            { Marshal.ThrowExceptionForHR(defaultDevice.GetId(out defaultId)); }
            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(0, 15, out collection));
            Marshal.ThrowExceptionForHR(collection.GetCount(out var count));
            var result = new List<AudioDevice>();
            for (uint i = 0; i < count; i++)
            {
                Marshal.ThrowExceptionForHR(collection.Item(i, out var device));
                try
                {
                    Marshal.ThrowExceptionForHR(device.GetId(out var id));
                    Marshal.ThrowExceptionForHR(device.GetState(out var state));
                    var guid = ConfigCompiler.EndpointGuid(id);
                    using var properties = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render\" + guid + @"\Properties");
                    string Get(string name) => properties?.GetValue(name) as string ?? "";
                    var name = Get("{a45c254e-df1c-4efd-8020-67d146a850e0},2");
                    var friendly = Get("{a45c254e-df1c-4efd-8020-67d146a850e0},14");
                    var adapter = Get("{b3f8fa53-0004-438e-9003-51a46e139bfc},6");
                    if (!string.IsNullOrWhiteSpace(friendly)) { name = friendly; adapter = ""; }
                    using var apo = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\EqualizerAPO\Child APOs\" + guid);
                    result.Add(new AudioDevice(id, string.IsNullOrWhiteSpace(name) ? guid : name, adapter, state, id == defaultId, apo is not null));
                }
                finally { Marshal.ReleaseComObject(device); }
            }
            return result.OrderByDescending(d => d.IsDefault).ThenBy(d => d.State != 1).ThenBy(d => d.DisplayName).ToList();
        }
        finally
        {
            if (defaultDevice is not null) Marshal.ReleaseComObject(defaultDevice);
            if (collection is not null) Marshal.ReleaseComObject(collection);
            Marshal.ReleaseComObject(enumerator);
        }
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class MMDeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint state, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, out IntPtr result);
        [PreserveSig] int OpenPropertyStore(uint mode, out IntPtr store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }
}
