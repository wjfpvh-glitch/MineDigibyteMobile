using System.Collections.Concurrent;

namespace MineDigibyteMobile.Services;

internal sealed record PendingShareInfo(string JobId, string Nonce);

internal sealed class ShareRequestTracker
{
    private sealed class PendingRequest : IDisposable
    {
        private readonly CancellationTokenSource timeoutCancellation;
        private int disposed;

        public PendingRequest(
            PendingShareInfo info,
            CancellationToken sessionToken)
        {
            Info = info;
            timeoutCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(sessionToken);
            TimeoutToken = timeoutCancellation.Token;
        }

        public PendingShareInfo Info { get; }
        public CancellationToken TimeoutToken { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
                return;

            timeoutCancellation.Cancel();
            timeoutCancellation.Dispose();
        }
    }

    private readonly ConcurrentDictionary<int, PendingRequest> pending = new();

    private int accepted;
    private int rejected;
    private int expired;

    public int AcceptedCount => Volatile.Read(ref accepted);
    public int RejectedCount => Volatile.Read(ref rejected);
    public int ExpiredCount => Volatile.Read(ref expired);
    public int PendingCount => pending.Count;

    public bool TryAdd(
        int requestId,
        string jobId,
        string nonce,
        CancellationToken sessionToken,
        out CancellationToken timeoutToken)
    {
        var request = new PendingRequest(
            new PendingShareInfo(jobId, nonce),
            sessionToken);

        if (!pending.TryAdd(requestId, request))
        {
            request.Dispose();
            timeoutToken = default;
            return false;
        }

        timeoutToken = request.TimeoutToken;
        return true;
    }

    public bool TryResolve(
        int requestId,
        bool isAccepted,
        out PendingShareInfo? info)
    {
        if (!pending.TryRemove(requestId, out PendingRequest? request))
        {
            info = null;
            return false;
        }

        request.Dispose();
        info = request.Info;

        if (isAccepted)
            Interlocked.Increment(ref accepted);
        else
            Interlocked.Increment(ref rejected);

        return true;
    }

    public bool TryExpire(
        int requestId,
        out PendingShareInfo? info)
    {
        if (!pending.TryRemove(requestId, out PendingRequest? request))
        {
            info = null;
            return false;
        }

        request.Dispose();
        info = request.Info;
        Interlocked.Increment(ref expired);
        return true;
    }

    public bool TryAbandon(int requestId)
    {
        if (!pending.TryRemove(requestId, out PendingRequest? request))
            return false;

        request.Dispose();
        return true;
    }

    public void ClearPending()
    {
        foreach (KeyValuePair<int, PendingRequest> item in pending)
        {
            if (pending.TryRemove(item.Key, out PendingRequest? request))
                request.Dispose();
        }
    }

    public void Reset()
    {
        ClearPending();
        Interlocked.Exchange(ref accepted, 0);
        Interlocked.Exchange(ref rejected, 0);
        Interlocked.Exchange(ref expired, 0);
    }
}
