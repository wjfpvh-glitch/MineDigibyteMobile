using Microsoft.Maui.Graphics;
using MineDigibyteMobile.Localization;

namespace MineDigibyteMobile.Services;

/// <summary>Desenha o histórico de hashrate recebido do motor real de mineração.</summary>
public sealed class HashrateChartDrawable : IDrawable
{
    private const int MaxSamples = 60;
    private readonly object sync = new();
    private readonly Queue<double> samples = new();

    public void Reset()
    {
        lock (sync)
            samples.Clear();
    }

    public void AddSample(double hashesPerSecond)
    {
        if (!double.IsFinite(hashesPerSecond) || hashesPerSecond < 0)
            return;

        lock (sync)
        {
            samples.Enqueue(hashesPerSecond);
            while (samples.Count > MaxSamples)
                samples.Dequeue();
        }
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        double[] values;
        lock (sync)
            values = samples.ToArray();

        float left = 38;
        float right = Math.Max(left + 1, dirtyRect.Width - 8);
        float top = 12;
        float bottom = Math.Max(top + 1, dirtyRect.Height - 24);
        float width = right - left;
        float height = bottom - top;

        canvas.StrokeColor = Color.FromArgb("#354252");
        canvas.StrokeSize = 1;
        for (int i = 0; i <= 3; i++)
        {
            float y = top + height * i / 3f;
            canvas.DrawLine(left, y, right, y);
        }

        double maximum = values.Length == 0 ? 1 : values.Max();
        if (maximum <= 0)
            maximum = 1;

        canvas.FontColor = Color.FromArgb("#AAB4C2");
        canvas.FontSize = 9;
        canvas.DrawString(FormatRate(maximum), 0, top - 5, left - 5, 14,
            HorizontalAlignment.Right, VerticalAlignment.Center);
        canvas.DrawString("0 H/s", 0, bottom - 7, left - 5, 14,
            HorizontalAlignment.Right, VerticalAlignment.Center);

        if (values.Length == 0)
        {
            canvas.FontSize = 12;
            canvas.DrawString(LocalizationService.Instance.GetString("ChartHashrateWaiting"), left, top,
                width, height, HorizontalAlignment.Center, VerticalAlignment.Center);
            return;
        }

        if (values.Length == 1)
        {
            float y = bottom - (float)(values[0] / maximum) * height;
            canvas.FillColor = Color.FromArgb("#1E90FF");
            canvas.FillCircle(right, y, 3.5f);
            return;
        }

        var path = new PathF();
        for (int i = 0; i < values.Length; i++)
        {
            float x = left + width * i / (values.Length - 1f);
            float y = bottom - (float)(values[i] / maximum) * height;
            if (i == 0)
                path.MoveTo(x, y);
            else
                path.LineTo(x, y);
        }

        canvas.StrokeColor = Color.FromArgb("#1E90FF");
        canvas.StrokeSize = 2.5f;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeLineJoin = LineJoin.Round;
        canvas.DrawPath(path);
    }

    private static string FormatRate(double value)
    {
        if (value >= 1_000_000_000) return $"{value / 1_000_000_000:0.##} GH/s";
        if (value >= 1_000_000) return $"{value / 1_000_000:0.##} MH/s";
        if (value >= 1_000) return $"{value / 1_000:0.##} KH/s";
        return $"{value:0.##} H/s";
    }
}
