namespace MineDigibyteMobile.Services;

public sealed record MiningSessionSnapshot(
    long TotalHashes,
    long SharesFound,
    long SharesSent,
    long SharesAccepted,
    long SharesRejected,
    long SharesExpired);

public sealed class MiningSessionMetrics
{
    private readonly object sync = new();
    private MiningSessionSnapshot snapshot = new(0, 0, 0, 0, 0, 0);

    public MiningSessionSnapshot Snapshot
    {
        get
        {
            lock (sync)
                return snapshot;
        }
    }

    public double AcceptancePercentage
    {
        get
        {
            lock (sync)
            {
                double responses =
                    (double)snapshot.SharesAccepted + snapshot.SharesRejected;
                return responses == 0
                    ? 0
                    : snapshot.SharesAccepted * 100d / responses;
            }
        }
    }

    public void Reset()
    {
        lock (sync)
            snapshot = new MiningSessionSnapshot(0, 0, 0, 0, 0, 0);
    }

    public void SetTotalHashes(long totalHashes)
    {
        if (totalHashes < 0)
            throw new ArgumentOutOfRangeException(nameof(totalHashes));

        Update(current => current with { TotalHashes = totalHashes });
    }

    public void RecordShareFound() =>
        Update(current => current with
        {
            SharesFound = checked(current.SharesFound + 1)
        });

    public void RecordShareSent() =>
        Update(current => current with
        {
            SharesSent = checked(current.SharesSent + 1)
        });

    public void RecordShareAccepted() =>
        Update(current => current with
        {
            SharesAccepted = checked(current.SharesAccepted + 1)
        });

    public void RecordShareRejected() =>
        Update(current => current with
        {
            SharesRejected = checked(current.SharesRejected + 1)
        });

    public void RecordShareExpired() =>
        Update(current => current with
        {
            SharesExpired = checked(current.SharesExpired + 1)
        });

    private void Update(Func<MiningSessionSnapshot, MiningSessionSnapshot> update)
    {
        lock (sync)
            snapshot = update(snapshot);
    }
}
