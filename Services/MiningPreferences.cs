using Microsoft.Maui.Storage;

namespace MineDigibyteMobile.Services;

public static class MiningPreferences
{
    private const string IntensityKey = "mining.intensity.percent";
    private const string BackgroundKey = "mining.background.enabled";
    private const string GpuKey = "mining.gpu.enabled";
    private const string GpuIntensityKey = "mining.gpu.intensity.percent";

    public static event Action<int>? IntensityChanged;
    public static event Action<bool>? BackgroundMiningChanged;

    public static int IntensityPercent
    {
        get => Math.Clamp(
            Preferences.Default.Get(IntensityKey, 50),
            1,
            100);
        set
        {
            if (value is < 1 or > 100)
                throw new ArgumentOutOfRangeException(nameof(value));

            Preferences.Default.Set(IntensityKey, value);
            IntensityChanged?.Invoke(value);
        }
    }

    /// <summary>Intensidade da GPU, separada da intensidade geral (CPU).</summary>
    public static int GpuIntensityPercent
    {
        get => Math.Clamp(Preferences.Default.Get(GpuIntensityKey, 70), 1, 100);
        set => Preferences.Default.Set(GpuIntensityKey, Math.Clamp(value, 1, 100));
    }

    /// <summary>Usar a GPU (Vulkan ou OpenGL ES) quando disponível.</summary>
    public static bool GpuEnabled
    {
        get => Preferences.Default.Get(GpuKey, true);
        set => Preferences.Default.Set(GpuKey, value);
    }

    public static bool BackgroundMiningEnabled
    {
        get => Preferences.Default.Get(BackgroundKey, false);
        set
        {
            Preferences.Default.Set(BackgroundKey, value);
            BackgroundMiningChanged?.Invoke(value);
        }
    }
}
