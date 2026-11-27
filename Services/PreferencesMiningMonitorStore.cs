using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Maui.Storage;

namespace MineDigibyteMobile.Services;

public sealed class PreferencesMiningMonitorStore : IMiningMonitorStore
{
    private const string StorageKey = "mining.monitor.v1";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    private sealed record StoredTotals(
        long TotalHashes,
        long JobsReceived,
        long CleanJobsReceived,
        long SharesFound,
        long SharesSent,
        long SharesAccepted,
        long SharesRejected,
        long SharesExpired,
        double BestDifficulty,
        string ValidBlocks);

    public MiningMonitorTotals Load()
    {
        string json = Preferences.Default.Get(StorageKey, "");

        if (string.IsNullOrEmpty(json))
        {
            return new MiningMonitorTotals(
                0, 0, 0, 0, 0, 0, 0, 0, 0, "0");
        }

        StoredTotals stored = JsonSerializer.Deserialize<StoredTotals>(
                json,
                SerializerOptions)
            ?? throw new InvalidDataException(
                "Os dados persistidos do monitor estão vazios.");

        if (!System.Numerics.BigInteger.TryParse(
                stored.ValidBlocks,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out System.Numerics.BigInteger validBlocks) ||
            validBlocks < System.Numerics.BigInteger.Zero)
        {
            throw new InvalidDataException(
                "O contador de blocos persistido está inválido.");
        }

        return new MiningMonitorTotals(
            stored.TotalHashes,
            stored.JobsReceived,
            stored.CleanJobsReceived,
            stored.SharesFound,
            stored.SharesSent,
            stored.SharesAccepted,
            stored.SharesRejected,
            stored.SharesExpired,
            stored.BestDifficulty,
            validBlocks.ToString(CultureInfo.InvariantCulture));
    }

    public void Save(MiningMonitorTotals totals)
    {
        var stored = new StoredTotals(
            totals.TotalHashes,
            totals.JobsReceived,
            totals.CleanJobsReceived,
            totals.SharesFound,
            totals.SharesSent,
            totals.SharesAccepted,
            totals.SharesRejected,
            totals.SharesExpired,
            totals.BestDifficulty,
            totals.ValidBlocks);

        Preferences.Default.Set(
            StorageKey,
            JsonSerializer.Serialize(stored, SerializerOptions));
    }
}
