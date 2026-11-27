namespace MineDigibyteMobile.Services;

/// <summary>
/// Capacidades declaradas pelo Android. A detecção não significa que exista
/// um backend de mineração GPU implementado ou validado.
/// </summary>
public sealed record HardwareCapabilities(
    string Manufacturer,
    string Model,
    string OperatingSystem,
    int CpuLogicalCores,
    bool VulkanAvailable,
    bool VulkanComputeAvailable,
    bool OpenGlesAvailable,
    string MiningMode,
    string GpuMiningStatus)
{
    public string CpuDescription =>
        $"{CpuLogicalCores} núcleos lógicos disponíveis";

    public string GraphicsDescription =>
        VulkanComputeAvailable
            ? "Vulkan Compute anunciado pelo Android"
            : VulkanAvailable
                ? "Vulkan disponível; Compute não confirmado"
                : OpenGlesAvailable
                    ? "OpenGL ES detectável; não habilita mineração GPU"
                    : "API de computação gráfica não detectada";
}

public static class HardwareCapabilitiesService
{
    public static HardwareCapabilities Detect()
    {
        int cores = Math.Max(1, Environment.ProcessorCount);
        string manufacturer = "Desconhecido";
        string model = Microsoft.Maui.Devices.DeviceInfo.Model;
        string os = $"{Microsoft.Maui.Devices.DeviceInfo.Platform} {Microsoft.Maui.Devices.DeviceInfo.VersionString}";
        bool vulkan = false;
        bool vulkanCompute = false;
        bool openGles = false;

#if ANDROID
        try
        {
            manufacturer = Android.OS.Build.Manufacturer ?? "Desconhecido";
            model = Android.OS.Build.Model ?? model;
            os = $"Android {Android.OS.Build.VERSION.Release} (API {(int)Android.OS.Build.VERSION.SdkInt})";

            var packageManager = Android.App.Application.Context.PackageManager;
            if (packageManager != null)
            {
                // Feature flags padronizadas pelo Android PackageManager.
                vulkan = packageManager.HasSystemFeature("android.hardware.vulkan");
                vulkanCompute = packageManager.HasSystemFeature(
                    "android.hardware.vulkan.compute");

                // A presença de OpenGL ES é verificada pela versão declarada
                // pelo sistema; isto não implica capacidade de compute.
                openGles = packageManager.HasSystemFeature(
                    "android.hardware.opengles.aep");
            }
        }
        catch
        {
            // A detecção é best-effort; a mineração CPU permanece disponível.
        }
#endif

        // Até existir um backend GPU nativo compilado e validado, CPU é
        // sempre o único motor de mineração habilitado.
        string gpuStatus = vulkanCompute
            ? "GPU compatível detectada; backend de mineração ainda não integrado"
            : vulkan
                ? "Vulkan detectado, mas Compute não confirmado; usando CPU"
                : "GPU de computação não confirmada; usando CPU";

        return new HardwareCapabilities(
            manufacturer,
            model,
            os,
            cores,
            vulkan,
            vulkanCompute,
            openGles,
            "CPU",
            gpuStatus);
    }
}
