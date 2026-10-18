using Godot;
using System.Runtime.InteropServices;

namespace MarioStudy;

internal static class DeviceControls
{
    // A touchscreen is also present on HarmonyOS PCs. Godot's OpenHarmony
    // backend reports desktop/touchscreen for every host, so query the OS.
    [DllImport("libdeviceinfo_ndk.z.so", EntryPoint = "OH_GetDeviceType", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint GetOpenHarmonyDeviceType();

    public static string DetectDeviceType()
    {
        string platform = OS.GetName();
        if (platform == "OpenHarmony")
        {
            try { return (Marshal.PtrToStringUTF8(GetOpenHarmonyDeviceType()) ?? "unknown").Trim().ToLowerInvariant(); }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            { GD.PushWarning("Device type unavailable: " + ex.Message); return "unknown"; }
        }
        if (platform == "iOS") return OS.GetModelName().StartsWith("iPhone", StringComparison.OrdinalIgnoreCase) ? "phone" : "tablet";
        return platform.ToLowerInvariant();
    }

    // Modern deviceInfo also has a generic "default" type. Do not guess that
    // an unknown/default device is a phone, or a touchscreen PC could opt in.
    public static bool IsPhone(string deviceType) => deviceType.Trim().Equals("phone", StringComparison.OrdinalIgnoreCase);
}
