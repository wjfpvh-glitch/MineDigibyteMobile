using Microsoft.Maui.Graphics;
using MineDigibyteMobile.Localization;

namespace MineDigibyteMobile.Services;

/// <summary>
/// Desenha o histórico recente da temperatura da bateria, com as linhas de
/// limite da proteção térmica (valores de ThermalProtectionService) e a cor da linha por faixa.
/// </summary>
public sealed class TemperatureChartDrawable : IDrawable
{
    private const int MaxSamples = 120; // ~2 minutos com leitura a cada 1 s

    /// <summary>Início da escala do medidor e do gráfico.</summary>
    public const double ScaleMin = 20.0;

    /// <summary>
    /// Fim da escala: 5 °C acima do limite de pausa, arredondado para múltiplo de 5.
    /// Acompanha ThermalProtectionService, então mudar o limite lá ajusta o painel.
    /// </summary>
    public static double ScaleMax { get; } = Math.Max(
        ScaleMin + 10.0,
        Math.Ceiling((ThermalProtectionService.PauseAtCelsius + 5.0) / 5.0) * 5.0);

    private readonly object sync = new();
    private readonly Queue<double> samples = new();

    public double? Minimum { get; private set; }
    public double? Maximum { get; private set; }
    public double? Average { get; private set; }

    public void Reset()
    {
        lock (sync)
        {
            samples.Clear();
            Minimum = Maximum = Average = null;
        }
    }

    public void AddSample(double celsius)
    {
        if (!double.IsFinite(celsius))
            return;

        lock (sync)
        {
            samples.Enqueue(celsius);
            while (samples.Count > MaxSamples)
                samples.Dequeue();

            Minimum = samples.Min();
            Maximum = samples.Max();
            Average = samples.Average();
        }
    }

    /// <summary>Cor da faixa térmica, igual à usada pelo medidor.</summary>
    public static Color ColorFor(double? celsius) => celsius switch
    {
        null => Color.FromArgb("#AAB4C2"),
        >= ThermalProtectionService.PauseAtCelsius => Color.FromArgb("#E74C3C"),
        >= ThermalProtectionService.ThrottleAtCelsius => Color.FromArgb("#F39C12"),
        >= ThermalProtectionService.ResumeAtCelsius => Color.FromArgb("#F7DC6F"),
        _ => Color.FromArgb("#82E0AA")
    };

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        double[] values;
        lock (sync)
            values = samples.ToArray();

        float left = 34;
        float right = Math.Max(left + 1, dirtyRect.Width - 6);
        float top = 8;
        float bottom = Math.Max(top + 1, dirtyRect.Height - 8);
        float width = right - left;
        float height = bottom - top;

        float YFor(double c) =>
            bottom - (float)((Math.Clamp(c, ScaleMin, ScaleMax) - ScaleMin)
                              / (ScaleMax - ScaleMin)) * height;

        // Linhas de limite da proteção térmica.
        (double Value, string Color)[] limits =
        {
            (ThermalProtectionService.ResumeAtCelsius, "#F7DC6F"),
            (ThermalProtectionService.ThrottleAtCelsius, "#F39C12"),
            (ThermalProtectionService.PauseAtCelsius, "#E74C3C"),
        };

        canvas.FontSize = 9;
        foreach (var (value, color) in limits)
        {
            float y = YFor(value);
            canvas.StrokeColor = Color.FromArgb(color).WithAlpha(0.45f);
            canvas.StrokeSize = 1;
            canvas.StrokeDashPattern = new float[] { 4, 4 };
            canvas.DrawLine(left, y, right, y);
            canvas.StrokeDashPattern = null;

            canvas.FontColor = Color.FromArgb("#AAB4C2");
            canvas.DrawString($"{value:0}°", 0, y - 7, left - 4, 14,
                HorizontalAlignment.Right, VerticalAlignment.Center);
        }

        if (values.Length == 0)
        {
            canvas.FontSize = 12;
            canvas.FontColor = Color.FromArgb("#AAB4C2");
            canvas.DrawString(LocalizationService.Instance.GetString("ChartTemperatureWaiting"), left, top,
                width, height, HorizontalAlignment.Center, VerticalAlignment.Center);
            return;
        }

        if (values.Length == 1)
        {
            canvas.FillColor = ColorFor(values[0]);
            canvas.FillCircle(right, YFor(values[0]), 3.5f);
            return;
        }

        // Um segmento por par de amostras, colorido pela faixa do ponto final.
        canvas.StrokeSize = 2.5f;
        canvas.StrokeLineCap = LineCap.Round;
        for (int i = 1; i < values.Length; i++)
        {
            float x1 = left + width * (i - 1) / (values.Length - 1f);
            float x2 = left + width * i / (values.Length - 1f);
            canvas.StrokeColor = ColorFor(values[i]);
            canvas.DrawLine(x1, YFor(values[i - 1]), x2, YFor(values[i]));
        }
    }
}
