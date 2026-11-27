namespace MineDigibyteMobile.Services;

/// <summary>
/// Proteção térmica baseada na temperatura reportada pela bateria.
/// Essa leitura não substitui sensores de CPU/GPU e não garante ausência
/// de superaquecimento. Usa histerese para evitar alternância rápida.
/// </summary>
public sealed class ThermalProtectionService
{
    public const double ThrottleAtCelsius = 42.0;
    public const double PauseAtCelsius = 45.0;
    public const double ResumeAtCelsius = 40.0;
    public const int ThrottledIntensityPercent = 25;

    private bool throttleActive;
    private bool thermalPauseActive;

    public bool IsThrottleActive => throttleActive;
    public bool IsThermalPauseActive => thermalPauseActive;

    public ThermalProtectionResult Evaluate(
        double? batteryTemperatureCelsius,
        bool miningActive,
        bool minerPaused,
        int configuredIntensity)
    {
        if (batteryTemperatureCelsius is null ||
            double.IsNaN(batteryTemperatureCelsius.Value) ||
            double.IsInfinity(batteryTemperatureCelsius.Value))
        {
            return new ThermalProtectionResult(
                "Temperatura indisponível; proteção automática sem leitura",
                null, false, false);
        }

        double temperature = batteryTemperatureCelsius.Value;

        if (temperature >= PauseAtCelsius && miningActive && !minerPaused)
        {
            thermalPauseActive = true;
            throttleActive = true;
            return new ThermalProtectionResult(
                $"Pausa térmica: bateria a {temperature:F1} °C (retoma abaixo de {ResumeAtCelsius:F0} °C)",
                null, true, false);
        }

        if (temperature <= ResumeAtCelsius)
        {
            bool resume = thermalPauseActive && miningActive && minerPaused;
            thermalPauseActive = false;
            throttleActive = false;
            return new ThermalProtectionResult(
                $"Temperatura segura: {temperature:F1} °C",
                configuredIntensity, false, resume);
        }

        if (temperature >= ThrottleAtCelsius)
        {
            throttleActive = true;
            int reduced = Math.Min(configuredIntensity, ThrottledIntensityPercent);
            return new ThermalProtectionResult(
                $"Intensidade reduzida: bateria a {temperature:F1} °C",
                reduced, false, false);
        }

        if (throttleActive)
        {
            return new ThermalProtectionResult(
                $"Proteção térmica ativa: {temperature:F1} °C; aguardando {ResumeAtCelsius:F0} °C para restaurar",
                ThrottledIntensityPercent, false, false);
        }

        return new ThermalProtectionResult(
            $"Temperatura normal: {temperature:F1} °C",
            configuredIntensity, false, false);
    }
}

public sealed record ThermalProtectionResult(
    string Status,
    int? IntensityPercent,
    bool PauseMining,
    bool ResumeMining);
