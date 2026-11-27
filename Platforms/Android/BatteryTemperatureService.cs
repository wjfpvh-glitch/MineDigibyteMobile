
using Android.Content;
using Android.OS;

namespace MineDigibyteMobile.Platforms.Android
{
    public static class BatteryTemperatureService
    {
        public sealed record BatteryInfo(
            int? LevelPercent,
            string State,
            double? TemperatureCelsius
        );

        public static BatteryInfo? GetBatteryInfo()
        {
            try
            {
                var context = global::Android.App.Application.Context;

                using var intent = context.RegisterReceiver(
                    null,
                    new IntentFilter(Intent.ActionBatteryChanged)
                );

                if (intent == null)
                    return null;

                // Nível da bateria
                int level = intent.GetIntExtra(
                    BatteryManager.ExtraLevel, -1
                );

                int scale = intent.GetIntExtra(
                    BatteryManager.ExtraScale, -1
                );

                int? percentage = null;

                if (level >= 0 && scale > 0)
                {
                    percentage = Math.Clamp(
                        (int)Math.Round(level * 100.0 / scale),
                        0,
                        100
                    );
                }

                // Estado da bateria
                int status = intent.GetIntExtra(
                    BatteryManager.ExtraStatus, -1
                );

                string state = status switch
                {
                    2 => "carregando",
                    3 => "descarregando",
                    4 => "não carregando",
                    5 => "completa",
                    _ => "estado indisponível"
                };

                // Temperatura em décimos de grau Celsius
                int temperature = intent.GetIntExtra(
                    BatteryManager.ExtraTemperature, -1
                );

                double? temperatureCelsius =
                    temperature >= 0
                        ? temperature / 10.0
                        : null;

                return new BatteryInfo(
                    percentage,
                    state,
                    temperatureCelsius
                );
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Status térmico do sistema (API 29+). É um sinal do próprio Android
        /// sobre o aparelho como um todo, não só da bateria. Retorna null em
        /// versões antigas ou se o sistema não informar.
        /// </summary>
        public static string? GetSystemThermalStatus()
        {
            try
            {
                if (!OperatingSystem.IsAndroidVersionAtLeast(29))
                    return null;

                var context = global::Android.App.Application.Context;
                var power = context.GetSystemService(Context.PowerService)
                    as PowerManager;

                if (power == null)
                    return null;

                return (int)power.CurrentThermalStatus switch
                {
                    0 => "Normal",
                    1 => "Leve",
                    2 => "Moderado",
                    3 => "Severo",
                    4 => "Crítico",
                    5 => "Emergência",
                    6 => "Desligamento",
                    _ => null
                };
            }
            catch
            {
                return null;
            }
        }

        // Mantém compatibilidade com chamadas antigas.
        public static float? GetBatteryTemperature()
        {
            return GetBatteryInfo()?.TemperatureCelsius is double temp
                ? (float)temp
                : null;
        }
    }
}
