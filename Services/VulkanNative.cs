using System.Runtime.InteropServices;

namespace MineDigibyteMobile.Services;

/// <summary>
/// Ponte P/Invoke para libvulkanminer.so (código em Native/vulkan_miner).
/// Se a biblioteca não estiver no APK, as chamadas lançam
/// DllNotFoundException e o app continua minerando só com CPU.
/// </summary>
internal static class VulkanNative
{
    private const string Lib = "vulkanminer";

    [DllImport(Lib, EntryPoint = "vkm_init")]
    public static extern int Init();

    [DllImport(Lib, EntryPoint = "vkm_shutdown")]
    public static extern void Shutdown();

    [DllImport(Lib, EntryPoint = "vkm_device_name")]
    public static extern IntPtr DeviceName();

    [DllImport(Lib, EntryPoint = "vkm_last_error")]
    public static extern IntPtr LastError();

    [DllImport(Lib, EntryPoint = "vkm_self_test")]
    public static extern int SelfTest();

    [DllImport(Lib, EntryPoint = "vkm_scan")]
    public static extern int Scan(
        byte[] header80,
        uint startNonce,
        uint count,
        uint targetTop,
        uint[] outNonces,
        uint maxOut,
        out uint outFound);
}
