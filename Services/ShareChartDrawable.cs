using Microsoft.Maui.Graphics;
using MineDigibyteMobile.Localization;

namespace MineDigibyteMobile.Services;

/// <summary>Compara shares aceitas e rejeitadas segundo as respostas do pool.</summary>
public sealed class ShareChartDrawable : IDrawable
{
    private readonly object sync = new();
    private long accepted;
    private long rejected;

    public void SetValues(long acceptedCount, long rejectedCount)
    {
        lock (sync)
        {
            accepted = Math.Max(0, acceptedCount);
            rejected = Math.Max(0, rejectedCount);
        }
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        long ok;
        long bad;
        lock (sync)
        {
            ok = accepted;
            bad = rejected;
        }

        long total = ok + bad;
        float left = 8;
        float width = Math.Max(1, dirtyRect.Width - 16);
        float barHeight = 20;
        float y = Math.Max(8, (dirtyRect.Height - barHeight) / 2);

        canvas.FontSize = 11;
        canvas.FontColor = Color.FromArgb("#CBD2DC");
        if (total == 0)
        {
            canvas.DrawString(LocalizationService.Instance.GetString("ChartShareWaiting"), left, y - 18,
                width, 56, HorizontalAlignment.Center, VerticalAlignment.Center);
            canvas.FillColor = Color.FromArgb("#354252");
            canvas.FillRoundedRectangle(left, y, width, barHeight, 7);
            return;
        }

        float acceptedWidth = width * (float)((double)ok / total);
        float rejectedWidth = width - acceptedWidth;
        canvas.FillColor = Color.FromArgb("#35B779");
        if (acceptedWidth > 0)
            canvas.FillRoundedRectangle(left, y, acceptedWidth, barHeight, 7);
        canvas.FillColor = Color.FromArgb("#E05D5D");
        if (rejectedWidth > 0)
            canvas.FillRoundedRectangle(left + acceptedWidth, y, rejectedWidth, barHeight, 7);

        canvas.DrawString($"{LocalizationService.Instance.GetString("ChartSharesAccepted")}{ok:N0}", left, y + 27, width / 2, 20,
            HorizontalAlignment.Left, VerticalAlignment.Center);
        canvas.DrawString($"{LocalizationService.Instance.GetString("ChartSharesRejected")}{bad:N0}", left + width / 2, y + 27,
            width / 2, 20, HorizontalAlignment.Right, VerticalAlignment.Center);
    }
}
