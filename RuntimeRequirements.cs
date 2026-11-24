using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

internal sealed partial class Launcher
{
    private const uint Avx2InstructionsAvailable = 40;
    private const int Direct3DFeatureLevel12 = 0xC000;
    private const int ShaderModelFeature = 7;
    private const uint RequiredShaderModel = 0x66;
    private const int InvalidArgument = unchecked((int)0x80070057);
    private const int AdapterNotFound = unchecked((int)0x887A0002);
    private const uint SoftwareAdapter = 2;
    private static readonly Guid FactoryId = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
    private static readonly Guid DeviceId = new Guid("189819f1-1db6-4b57-be54-1821339b85f7");

    // Slots in the native COM interfaces, including the three IUnknown methods.
    private const int EnumAdapters1Slot = 12;
    private const int GetDesc1Slot = 10;
    private const int CheckFeatureSupportSlot = 13;

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsProcessorFeaturePresent(uint feature);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(ref Guid interfaceId, out IntPtr factory);

    [DllImport("d3d12.dll", ExactSpelling = true)]
    private static extern int D3D12CreateDevice(IntPtr adapter, int featureLevel, ref Guid interfaceId, out IntPtr device);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapters1(IntPtr factory, uint index, out IntPtr adapter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDesc1(IntPtr adapter, out AdapterDescription description);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CheckFeatureSupport(IntPtr device, int feature, ref uint data, uint size);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDescription
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Name;
        public uint VendorId, DeviceId, SubsystemId, Revision;
        public UIntPtr DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint AdapterLuidLow;
        public int AdapterLuidHigh;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile;
        public ulong TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }

    private static string ProbePlayRequirements(out string details)
    {
        details = null;
        string runtimeProblem = ProbeRuntime();
        if (runtimeProblem != null) return runtimeProblem;
        if (!IsProcessorFeaturePresent(Avx2InstructionsAvailable))
            return "Windows does not expose AVX2. VIctory requires a CPU and Windows installation with AVX2 support.";

        try
        {
            string graphics;
            string graphicsProblem = CheckGraphics(out graphics);
            if (graphicsProblem != null) return graphicsProblem;
            details = "Runtime checks passed for Visual C++ x64, VIctory, AVX2 and " + graphics + ".";
            var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf(typeof(MemoryStatus)) };
            if (GlobalMemoryStatusEx(ref memory))
                details += string.Format(" {0:F1} GiB of memory commit available.", memory.AvailablePageFile / 1073741824.0);
            return null;
        }
        catch (DllNotFoundException) { return "Direct3D 12 is unavailable. Check Windows and the manufacturer’s graphics driver."; }
        catch (EntryPointNotFoundException) { return "Direct3D 12 is unavailable. Check Windows and the manufacturer’s graphics driver."; }
    }

    private static string CheckGraphics(out string graphics)
    {
        graphics = null;
        var reports = new List<string>();
        IntPtr factory;
        Guid factoryId = FactoryId;
        int result = CreateDXGIFactory1(ref factoryId, out factory);
        if (result < 0)
            return "Windows could not enumerate graphics adapters (" + FormatHResult(result) + "). Check the graphics driver.";

        try
        {
            var enumerate = (EnumAdapters1)Marshal.GetDelegateForFunctionPointer(
                ComMethod(factory, EnumAdapters1Slot), typeof(EnumAdapters1));
            for (uint index = 0; ; index++)
            {
                IntPtr adapter;
                result = enumerate(factory, index, out adapter);
                if (result == AdapterNotFound) break;
                if (result < 0)
                    return "Windows could not enumerate graphics adapters (" + FormatHResult(result) + "). Check the graphics driver.";

                try
                {
                    var describe = (GetDesc1)Marshal.GetDelegateForFunctionPointer(
                        ComMethod(adapter, GetDesc1Slot), typeof(GetDesc1));
                    AdapterDescription description;
                    if (describe(adapter, out description) < 0 || (description.Flags & SoftwareAdapter) != 0)
                        continue;

                    string adapterReport;
                    if (CheckShaderModel(adapter, out adapterReport))
                    {
                        graphics = description.Name + " (" + adapterReport + ")";
                        return null;
                    }
                    reports.Add(description.Name + " (" + adapterReport + ")");
                }
                finally { Marshal.Release(adapter); }
            }
        }
        finally { Marshal.Release(factory); }

        string adapters = reports.Count == 0 ? "Windows did not report a hardware adapter." : string.Join(", ", reports) + ".";
        return "VIctory requires Direct3D 12 with Shader Model 6.6. " + adapters +
            " Check Windows and the manufacturer’s graphics driver.";
    }

    private static bool CheckShaderModel(IntPtr adapter, out string report)
    {
        IntPtr device;
        Guid deviceId = DeviceId;
        int result = D3D12CreateDevice(adapter, Direct3DFeatureLevel12, ref deviceId, out device);
        if (result < 0)
        {
            report = "Direct3D 12 device creation failed " + FormatHResult(result);
            return false;
        }

        try
        {
            var check = (CheckFeatureSupport)Marshal.GetDelegateForFunctionPointer(
                ComMethod(device, CheckFeatureSupportSlot), typeof(CheckFeatureSupport));
            uint model = RequiredShaderModel;
            result = check(device, ShaderModelFeature, ref model, sizeof(uint));
            // Older Direct3D runtimes reject an unknown requested shader model.
            for (uint requested = RequiredShaderModel - 1; result == InvalidArgument && requested >= 0x60; requested--)
            {
                model = requested;
                result = check(device, ShaderModelFeature, ref model, sizeof(uint));
            }
            report = result < 0 ? "Shader Model check failed " + FormatHResult(result) :
                "Shader Model " + (model >> 4) + "." + (model & 0xF);
            return result >= 0 && model >= RequiredShaderModel;
        }
        finally { Marshal.Release(device); }
    }

    private static IntPtr ComMethod(IntPtr instance, int slot)
    {
        return Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size);
    }

    private static string FormatHResult(int result)
    {
        return "0x" + result.ToString("X8");
    }
}
