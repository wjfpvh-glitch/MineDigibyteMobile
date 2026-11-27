namespace MineDigibyteMobile.Services;

public sealed record MiningMonitorTotals(
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

public interface IMiningMonitorStore
{
    MiningMonitorTotals Load();
    void Save(MiningMonitorTotals totals);
}

public sealed class MiningMonitorState
{
    private readonly object sync = new();
    private readonly IMiningMonitorStore store;
    private MiningMonitorTotals totals;

    public MiningMonitorState(IMiningMonitorStore store)
    {
        this.store = store;
        totals = store.Load();
    }

    public MiningMonitorTotals Snapshot
    {
        get
        {
            lock (sync)
                return totals;
        }
    }

    public double AcceptancePercentage
    {
        get
        {
            lock (sync)
            {
                double answered =
                    (double)totals.SharesAccepted + totals.SharesRejected;
                return answered == 0
                    ? 0
                    : totals.SharesAccepted * 100d / answered;
            }
        }
    }

    public void AddHashes(long count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        Update(current => current with
        {
            TotalHashes = checked(current.TotalHashes + count)
        });
    }

    public void RecordJob(bool cleanJobs)
    {
        Update(current => current with
        {
            JobsReceived = checked(current.JobsReceived + 1),
            CleanJobsReceived = cleanJobs
                ? checked(current.CleanJobsReceived + 1)
                : current.CleanJobsReceived
        });
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

    public void RecordBestDifficulty(double difficulty)
    {
        if (double.IsNaN(difficulty) || difficulty < 0)
            throw new ArgumentOutOfRangeException(nameof(difficulty));

        lock (sync)
        {
            if (difficulty <= totals.BestDifficulty)
                return;

            MiningMonitorTotals updated =
                totals with { BestDifficulty = difficulty };
            store.Save(updated);
            totals = updated;
        }
    }

    private void Update(Func<MiningMonitorTotals, MiningMonitorTotals> update)
    {
        lock (sync)
        {
            MiningMonitorTotals updated = update(totals);
            store.Save(updated);
            totals = updated;
        }
    }
}
