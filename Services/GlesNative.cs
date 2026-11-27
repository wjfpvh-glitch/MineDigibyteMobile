using System.Runtime.InteropServices;

namespace MineDigibyteMobile.Services;

/// <summary>
/// Ponte P/Invoke para libglesminer.so (código em Native/gles_miner).
/// Se a biblioteca não estiver no APK, as chamadas lançam DllNotFoundException.
/// </summary>
internal static class GlesNative
{
    private const string Lib = "glesminer";

    [DllImport(Lib, EntryPoint = "glm_init")]
    public static extern int Init();

    [DllImport(Lib, EntryPoint = "glm_shutdown")]
    public static extern void Shutdown();

    [DllImport(Lib, EntryPoint = "glm_device_name")]
    public static extern IntPtr DeviceName();

    [DllImport(Lib, EntryPoint = "glm_last_error")]
    public static extern IntPtr LastError();

    [DllImport(Lib, EntryPoint = "glm_self_test")]
    public static extern int SelfTest();

    [DllImport(Lib, EntryPoint = "glm_scan")]
    public static extern int Scan(
        byte[] header80,
        uint startNonce,
        uint count,
        uint targetTop,
        uint[] outNonces,
        uint maxOut,
        out uint outFound);
}
