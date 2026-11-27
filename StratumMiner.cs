
using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Globalization;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using MineDigibyteMobile.Services;

namespace MineDigibyteMobile;

public sealed class StratumShare
{
    public string JobId { get; init; } = "";
    public string Extranonce2 { get; init; } = "";
    public string NTime { get; init; } = "";
    public string Nonce { get; init; } = "";
    public long CleanJobsGeneration { get; init; }
}

public sealed record MiningStatisticsSnapshot(
    long TotalHashes,
    double Hashrate,
    double AverageHashrate,
    double BestDifficulty,
    TimeSpan Uptime);

public sealed class StratumMiner : IDisposable
{
    private static readonly TimeSpan IntensityCycle =
        TimeSpan.FromMilliseconds(100);

    private sealed class MiningJob
    {
        public string JobId = "";
        public string PrevHash = "";
        public string Coinb1 = "";
        public string Coinb2 = "";
        public string[] Branches = Array.Empty<string>();
        public string Version = "";
        public string NBits = "";
        public string NTime = "";
        public bool CleanJobs;
        public long CleanJobsGeneration;
        public BigInteger DifficultyNumerator = BigInteger.One;
        public BigInteger DifficultyDenominator = BigInteger.One;
    }

    private readonly object sync = new();
    private readonly SemaphoreSlim shareSubmissionGate = new(1, 1);

    private CancellationTokenSource? cancellation;
    private Task? miningTask;

    private MiningJob? currentJob;

    private string extranonce1 = "";
    private int extranonce2Size;

    private BigInteger pendingDifficultyNumerator = BigInteger.One;
    private BigInteger pendingDifficultyDenominator = BigInteger.One;

    private int revision;
    private long cleanJobsGeneration;
    private bool paused;
    private int intensityPercent = 50;
    private int cpuWorkerCount = Math.Max(1, Environment.ProcessorCount);

    private Func<StratumShare, Task>? submitCallback;

    // Backend GPU (Vulkan ou OpenGL ES). Quando nulo, a mineração usa só os workers de CPU.
    private IGpuHashBackend? gpuBackend;

    // Despachos curtos deixam a GPU livre para desenhar a tela. O tamanho do lote
    // se ajusta sozinho para cada despacho durar perto de GpuTargetMs.
    private const int GpuBatchMin = 1 << 12;
    private const int GpuBatchMax = 1 << 20;
    private const double GpuTargetMs = 12.0;
    private int gpuBatchSize = 1 << 16;
    private int gpuIntensityPercent = 70;

    public event Action<string>? Log;
    public event Action<double>? HashrateUpdated;
    public event Action<MiningStatisticsSnapshot>? StatisticsUpdated;
    public event Action<StratumShare>? ShareFound;
    public event Action<string>? WorkStarted;
    public event Action<bool>? PauseChanged;

    public long CleanJobsGeneration
    {
        get
        {
            lock (sync)
                return cleanJobsGeneration;
        }
    }

    public bool IsPaused
    {
        get
        {
            lock (sync)
                return paused;
        }
    }

    public int IntensityPercent => Volatile.Read(ref intensityPercent);

    public int CpuWorkerCount => Volatile.Read(ref cpuWorkerCount);

    public bool UsingGpu => Volatile.Read(ref gpuBackend) != null;

    public void SetGpuBackend(IGpuHashBackend? backend) =>
        Interlocked.Exchange(ref gpuBackend, backend);

    public int GpuIntensityPercent => Volatile.Read(ref gpuIntensityPercent);

    /// <summary>Intensidade da GPU (1–100%): fatia do tempo em que ela trabalha.</summary>
    public void SetGpuIntensityPercent(int value) =>
        Volatile.Write(ref gpuIntensityPercent, Math.Clamp(value, 1, 100));

    private void AdaptGpuBatch(TimeSpan scanTime)
    {
        double ms = scanTime.TotalMilliseconds;
        int size = Volatile.Read(ref gpuBatchSize);

        if (ms > GpuTargetMs * 1.5 && size > GpuBatchMin)
            size >>= 1;
        else if (ms < GpuTargetMs * 0.5 && size < GpuBatchMax)
            size <<= 1;

        Volatile.Write(ref gpuBatchSize, size);
    }

    private TimeSpan GetGpuRest(TimeSpan scanTime)
    {
        int percent = GpuIntensityPercent;
        if (percent >= 100)
            return TimeSpan.Zero;

        double restMs = scanTime.TotalMilliseconds * (100 - percent) / percent;
        return TimeSpan.FromMilliseconds(Math.Min(restMs, 500.0));
    }

    public void SetCpuWorkerCount(int value)
    {
        int maximum = Math.Max(1, Environment.ProcessorCount);
        if (value < 1 || value > maximum)
            throw new ArgumentOutOfRangeException(
                nameof(value),
                $"A quantidade de workers deve estar entre 1 e {maximum}.");

        Volatile.Write(ref cpuWorkerCount, value);
        WriteLog($"Workers CPU configurados: {value}.");
    }

    public bool IsRunning
    {
        get
        {
            lock (sync)
                return miningTask != null && !miningTask.IsCompleted;
        }
    }

    public void SetIntensityPercent(int value)
    {
        if (value is < 1 or > 100)
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "A intensidade deve estar entre 1% e 100%.");

        Volatile.Write(ref intensityPercent, value);
    }

    public void SetPaused(bool value)
    {
        lock (sync)
        {
            if (paused == value)
                return;

            paused = value;
            revision++;
        }

        WriteLog(value
            ? "Mineração pausada."
            : "Mineração retomada.");
        PauseChanged?.Invoke(value);
    }

    public bool IsShareCurrent(StratumShare share)
    {
        lock (sync)
            return share.CleanJobsGeneration == cleanJobsGeneration;
    }

    private void WriteLog(string message)
    {
        Log?.Invoke(message);
    }

    public void SetSubscription(
        string extraNonce1,
        int extraNonce2Size)
    {
        if (string.IsNullOrWhiteSpace(extraNonce1))
            throw new ArgumentException("Extranonce1 inválido.");

        DecodeHex(extraNonce1, null, "extranonce1");

        if (extraNonce2Size < 1 || extraNonce2Size > 32)
            throw new ArgumentOutOfRangeException(
                nameof(extraNonce2Size));

        lock (sync)
        {
            if (string.Equals(
                    extranonce1,
                    extraNonce1,
                    StringComparison.OrdinalIgnoreCase) &&
                this.extranonce2Size == extraNonce2Size)
            {
                return;
            }

            extranonce1 = extraNonce1;
            this.extranonce2Size = extraNonce2Size;
            revision++;
        }

        WriteLog("Configuração Extranonce recebida.");
    }

    public void SetDifficulty(string newDifficulty)
    {
        if (!TryParseDifficulty(
                newDifficulty,
                out BigInteger numerator,
                out BigInteger denominator))
        {
            WriteLog("Dificuldade inválida.");
            return;
        }

        lock (sync)
        {
            pendingDifficultyNumerator = numerator;
            pendingDifficultyDenominator = denominator;
        }

        WriteLog(
            $"Dificuldade recebida para o próximo trabalho: {newDifficulty}");
    }

    public void SetJob(
        string jobId,
        string prevHash,
        string coinb1,
        string coinb2,
        string[] branches,
        string version,
        string nBits,
        string nTime,
        bool cleanJobs)
    {
        if (cleanJobs)
            InvalidateForCleanJobsState();

        if (string.IsNullOrWhiteSpace(jobId))
            throw new FormatException("Job ID inválido.");

        DecodeHex(prevHash, 32, "prevHash");
        DecodeHex(coinb1, null, "coinb1");
        DecodeHex(coinb2, null, "coinb2");
        DecodeHex(version, 4, "version");
        DecodeHex(nBits, 4, "nBits");
        DecodeHex(nTime, 4, "nTime");

        if (branches == null)
            throw new FormatException("Lista de Merkle Branches inválida.");

        foreach (string branch in branches)
            DecodeHex(branch, 32, "Merkle Branch");

        var job = new MiningJob
        {
            JobId = jobId,
            PrevHash = prevHash,
            Coinb1 = coinb1,
            Coinb2 = coinb2,
            Branches = branches.ToArray(),
            Version = version,
            NBits = nBits,
            NTime = nTime,
            CleanJobs = cleanJobs
        };

        if (cleanJobs)
            shareSubmissionGate.Wait();

        try
        {
            lock (sync)
            {
                job.CleanJobsGeneration = cleanJobsGeneration;
                job.DifficultyNumerator = pendingDifficultyNumerator;
                job.DifficultyDenominator = pendingDifficultyDenominator;
                currentJob = job;

                revision++;
            }
        }
        finally
        {
            if (cleanJobs)
                shareSubmissionGate.Release();
        }

        WriteLog(
            $"Trabalho {jobId} preparado para processamento.");
    }

    public void InvalidateForCleanJobs()
    {
        InvalidateForCleanJobsState();

        shareSubmissionGate.Wait();
        shareSubmissionGate.Release();

        WriteLog("Trabalho anterior invalidado pela pool.");
    }

    public async Task InvalidateForCleanJobsAsync(
        CancellationToken cancellationToken)
    {
        InvalidateForCleanJobsState();

        await shareSubmissionGate.WaitAsync(cancellationToken);
        shareSubmissionGate.Release();

        WriteLog("Trabalho anterior invalidado pela pool.");
    }

    public async Task<bool> TrySubmitShareAsync(
        StratumShare share,
        Func<Task<bool>> sendAsync,
        CancellationToken cancellationToken)
    {
        await shareSubmissionGate.WaitAsync(cancellationToken);

        try
        {
            if (!IsShareCurrent(share))
                return false;

            return await sendAsync();
        }
        finally
        {
            shareSubmissionGate.Release();
        }
    }

    private void InvalidateForCleanJobsState()
    {
        lock (sync)
        {
            currentJob = null;
            cleanJobsGeneration++;
            revision++;
        }
    }

    public void Start(
        Func<StratumShare, Task> submit)
    {
        lock (sync)
        {
            if (miningTask != null &&
                !miningTask.IsCompleted)
            {
                return;
            }

            cancellation?.Dispose();

            cancellation = new CancellationTokenSource();

            submitCallback = submit;

            CancellationToken token = cancellation.Token;

            miningTask = Task.Run(
                () => MiningLoopAsync(token));
        }

        WriteLog("Motor SHA-256d iniciado.");
    }

    public async Task StopAsync()
    {
        Task? task;

        lock (sync)
        {
            cancellation?.Cancel();
            task = miningTask;
        }

        if (task != null)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
            }
        }

        lock (sync)
        {
            miningTask = null;
            currentJob = null;
            extranonce1 = "";
            extranonce2Size = 0;
            paused = false;
            cleanJobsGeneration++;
            revision++;

            cancellation?.Dispose();
            cancellation = null;
            submitCallback = null;
        }

        HashrateUpdated?.Invoke(0);

        WriteLog("Motor SHA-256d parado.");
    }

    private async Task MiningLoopAsync(
        CancellationToken token)
    {
        int observedRevision = -1;

        byte[]? header = null;
        byte[] extranonce2 = Array.Empty<byte>();

        BigInteger target = BigInteger.Zero;

        string currentJobId = "";
        string currentNTime = "";

        MiningJob? previousJob = null;
        string previousExtra1 = "";
        int previousExtraSize = 0;

        uint nonce = 0;
        bool workExhausted = false;

        long totalHashes = 0;
        long windowHashes = 0;

        double bestDifficulty = 0;
        byte[]? bestHash = null;
        Stopwatch rateTimer = new();
        Stopwatch miningTimer = new();
        Stopwatch intensityTimer = Stopwatch.StartNew();

        while (!token.IsCancellationRequested)
        {
            MiningJob? job;
            string extra1;
            int extraSize;
            int currentRevision;
            bool isPaused;

            lock (sync)
            {
                job = currentJob;
                extra1 = extranonce1;
                extraSize = extranonce2Size;
                currentRevision = revision;
                isPaused = paused;
            }

            if (job == null || string.IsNullOrEmpty(extra1) || isPaused)
            {
                if (miningTimer.IsRunning)
                {
                    miningTimer.Stop();
                    TimeSpan uptime = miningTimer.Elapsed;
                    HashrateUpdated?.Invoke(0);
                    StatisticsUpdated?.Invoke(new MiningStatisticsSnapshot(
                        totalHashes,
                        0,
                        CalculateAverageHashrate(totalHashes, uptime),
                        bestDifficulty,
                        uptime));
                }

                await Task.Delay(100, token);
                continue;
            }

            try
            {
                if (IntensityPercent < 100 &&
                    Volatile.Read(ref gpuBackend) == null)
                {
                    TimeSpan initialRest = GetIntensityRest(
                        intensityTimer.Elapsed,
                        IntensityPercent);

                    if (initialRest > TimeSpan.Zero)
                    {
                        await Task.Delay(initialRest, token);
                        intensityTimer.Restart();
                        continue;
                    }

                    if (intensityTimer.Elapsed >= IntensityCycle)
                        intensityTimer.Restart();
                }

                if (!miningTimer.IsRunning)
                {
                    miningTimer.Start();
                    rateTimer.Restart();
                }

                if (observedRevision != currentRevision)
                {
                    bool sameWork =
                        !job.CleanJobs &&
                        header != null &&
                        previousJob != null &&
                        HasSameWork(previousJob, job) &&
                        string.Equals(
                            previousExtra1,
                            extra1,
                            StringComparison.OrdinalIgnoreCase) &&
                        previousExtraSize == extraSize;

                    observedRevision = currentRevision;

                    currentJobId = job.JobId;
                    currentNTime = job.NTime;

                    if (!sameWork)
                    {
                        extranonce2 = new byte[extraSize];
                        nonce = 0;
                        header = BuildHeader(job, extra1, extranonce2);
                    }

                    target = CalculateTarget(
                        job.DifficultyNumerator,
                        job.DifficultyDenominator);

                    previousJob = job;
                    previousExtra1 = extra1;
                    previousExtraSize = extraSize;

                    if (!sameWork)
                        workExhausted = false;

                    WriteLog(
                        $"Processando trabalho {job.JobId}.");

                    if (!sameWork)
                        WorkStarted?.Invoke(job.JobId);
                }

                if (header == null)
                {
                    await Task.Delay(10, token);
                    continue;
                }

                if (workExhausted)
                {
                    await Task.Delay(100, token);
                    continue;
                }

                // Cada lote divide nonces distintos entre workers CPU. O cabeçalho
                // permanece imutável durante o lote; um novo trabalho só é aplicado
                // depois que o lote atual termina ou detecta mudança de revisão.
                IGpuHashBackend? gpu = Volatile.Read(ref gpuBackend);
                long batchLimit = gpu != null
                    ? Volatile.Read(ref gpuBatchSize)
                    : 8192L;
                TimeSpan gpuRest = TimeSpan.Zero;

                uint batchStartNonce = nonce;
                int batchCount = (int)Math.Min(
                    batchLimit,
                    (long)uint.MaxValue - batchStartNonce + 1L);

                var foundShares = new ConcurrentBag<StratumShare>();
                object bestHashLock = new();
                var parallelOptions = new ParallelOptions
                {
                    CancellationToken = token,
                    MaxDegreeOfParallelism = CpuWorkerCount
                };

                if (gpu != null)
                {
                    try
                    {
                        // A GPU filtra pelos 32 bits de topo do hash; o CPU confirma
                        // cada candidato contra o alvo completo antes de gerar a share.
                        BigInteger topBig = target >> 224;
                        uint targetTop = topBig > uint.MaxValue
                            ? uint.MaxValue
                            : (uint)topBig;
                        long scanStart = Stopwatch.GetTimestamp();
                        IReadOnlyList<uint> candidates = gpu.Scan(
                            header, batchStartNonce, (uint)batchCount, targetTop);
                        TimeSpan scanTime = Stopwatch.GetElapsedTime(scanStart);
                        AdaptGpuBatch(scanTime);
                        gpuRest = GetGpuRest(scanTime);

                        Interlocked.Add(ref totalHashes, batchCount);
                        Interlocked.Add(ref windowHashes, batchCount);

                        byte[] check = new byte[144];
                        foreach (uint candidateNonce in candidates)
                        {
                            Buffer.BlockCopy(header, 0, check, 0, 80);
                            BinaryPrimitives.WriteUInt32LittleEndian(
                                check.AsSpan(76, 4), candidateNonce);

                            Span<byte> firstHash = check.AsSpan(80, 32);
                            Span<byte> finalHash = check.AsSpan(112, 32);
                            SHA256.HashData(check.AsSpan(0, 80), firstHash);
                            SHA256.HashData(firstHash, finalHash);

                            if (bestHash == null ||
                                IsHashNumericallyLess(finalHash, bestHash))
                            {
                                bestHash = finalHash.ToArray();
                                bestDifficulty = CalculateHashDifficulty(finalHash);
                            }

                            if (HashMeetsTarget(finalHash, target))
                            {
                                foundShares.Add(new StratumShare
                                {
                                    JobId = currentJobId,
                                    Extranonce2 = Convert.ToHexString(extranonce2)
                                        .ToLowerInvariant(),
                                    NTime = currentNTime,
                                    Nonce = candidateNonce.ToString("x8"),
                                    CleanJobsGeneration = job.CleanJobsGeneration
                                });
                            }
                        }
                    }
                    catch (Exception gpuEx) when (gpuEx is not OperationCanceledException)
                    {
                        // Qualquer falha da GPU desliga o backend e continua no CPU.
                        // Só desliga se ainda for este backend (evita apagar um novo).
                        Interlocked.CompareExchange(ref gpuBackend, null, gpu);
                        if (gpuEx is not ObjectDisposedException)
                            WriteLog("GPU desativada após erro: " + gpuEx.Message +
                                     " — continuando com CPU.");
                    }
                }
                else
                Parallel.For<byte[]>(
                    0,
                    batchCount,
                    parallelOptions,
                    static () => new byte[144],
                    (index, loopState, buffers) =>
                    {
                        token.ThrowIfCancellationRequested();

                        lock (sync)
                        {
                            if (revision != observedRevision)
                            {
                                loopState.Stop();
                                return buffers;
                            }
                        }

                        uint candidateNonce = batchStartNonce + (uint)index;
                        // Cada worker possui sua própria cópia do cabeçalho: escrever
                        // o nonce no buffer compartilhado causaria uma corrida de dados.
                        Buffer.BlockCopy(header, 0, buffers, 0, 80);
                        BinaryPrimitives.WriteUInt32LittleEndian(
                            buffers.AsSpan(76, 4), candidateNonce);

                        Span<byte> firstHash = buffers.AsSpan(80, 32);
                        Span<byte> finalHash = buffers.AsSpan(112, 32);
                        SHA256.HashData(buffers.AsSpan(0, 80), firstHash);
                        SHA256.HashData(firstHash, finalHash);

                        Interlocked.Increment(ref totalHashes);
                        Interlocked.Increment(ref windowHashes);

                        lock (bestHashLock)
                        {
                            if (bestHash == null ||
                                IsHashNumericallyLess(finalHash, bestHash))
                            {
                                bestHash = finalHash.ToArray();
                                bestDifficulty = CalculateHashDifficulty(finalHash);
                            }
                        }

                        if (HashMeetsTarget(finalHash, target))
                        {
                            foundShares.Add(new StratumShare
                            {
                                JobId = currentJobId,
                                Extranonce2 = Convert.ToHexString(extranonce2)
                                    .ToLowerInvariant(),
                                NTime = currentNTime,
                                Nonce = candidateNonce.ToString("x8"),
                                CleanJobsGeneration = job.CleanJobsGeneration
                            });
                        }

                        return buffers;
                    },
                    static _ => { });

                // Avança o cursor somente depois do lote, evitando que dois workers
                // processem o mesmo nonce. A troca de extranonce mantém a sequência.
                bool revisionChanged;
                lock (sync)
                    revisionChanged = revision != observedRevision;

                if (!revisionChanged)
                {
                    ulong nextNonce = (ulong)batchStartNonce + (uint)batchCount;
                    if (nextNonce > uint.MaxValue)
                    {
                        if (IncrementExtranonce2(extranonce2))
                        {
                            nonce = 0;
                            header = BuildHeader(job, extra1, extranonce2);
                        }
                        else
                        {
                            workExhausted = true;
                            WriteLog(
                                "Espaço de nonce e extranonce2 esgotado para este trabalho.");
                        }
                    }
                    else
                    {
                        nonce = (uint)nextNonce;
                    }
                }

                foreach (StratumShare share in foundShares.OrderBy(
                    item => uint.Parse(item.Nonce, NumberStyles.HexNumber, CultureInfo.InvariantCulture)))
                {
                    if (submitCallback == null)
                        break;

                    bool currentShare;
                    lock (sync)
                    {
                        currentShare =
                            revision == observedRevision &&
                            share.CleanJobsGeneration == cleanJobsGeneration;
                    }

                    if (!currentShare)
                        break;

                    WriteLog($"Share encontrada! Nonce: {share.Nonce}");
                    ShareFound?.Invoke(share);
                    await submitCallback(share);
                }

                if (rateTimer.ElapsedMilliseconds >= 1000)
                {
                    double hashrate =
                        Interlocked.Read(ref windowHashes) /
                        rateTimer.Elapsed.TotalSeconds;
                    TimeSpan uptime = miningTimer.Elapsed;

                    HashrateUpdated?.Invoke(hashrate);
                    StatisticsUpdated?.Invoke(new MiningStatisticsSnapshot(
                        Interlocked.Read(ref totalHashes),
                        hashrate,
                        CalculateAverageHashrate(
                            Interlocked.Read(ref totalHashes), uptime),
                        bestDifficulty,
                        uptime));

                    Interlocked.Exchange(ref windowHashes, 0);
                    rateTimer.Restart();
                }

                if (gpuRest > TimeSpan.Zero)
                    await Task.Delay(gpuRest, token);

                await Task.Yield();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                WriteLog(
                    "Erro no motor SHA-256d: " + ex.Message);

                await Task.Delay(500, token);
            }
        }

        if (miningTimer.IsRunning)
            miningTimer.Stop();

        TimeSpan finalUptime = miningTimer.Elapsed;
        HashrateUpdated?.Invoke(0);
        StatisticsUpdated?.Invoke(new MiningStatisticsSnapshot(
            totalHashes,
            0,
            CalculateAverageHashrate(totalHashes, finalUptime),
            bestDifficulty,
            finalUptime));

        WriteLog(
            $"Processamento encerrado. Total de hashes: {totalHashes}");
    }

    private static double CalculateAverageHashrate(
        long totalHashes,
        TimeSpan uptime)
    {
        return uptime.TotalSeconds <= 0
            ? 0
            : totalHashes / uptime.TotalSeconds;
    }

    private static TimeSpan GetIntensityRest(
        TimeSpan elapsed,
        int percent)
    {
        if (percent is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(percent));

        if (percent == 100)
            return TimeSpan.Zero;

        long cycleTicks = IntensityCycle.Ticks;
        long activeTicks = cycleTicks * percent / 100;
        long phaseTicks = elapsed.Ticks % cycleTicks;

        return phaseTicks < activeTicks
            ? TimeSpan.Zero
            : TimeSpan.FromTicks(cycleTicks - phaseTicks);
    }

    private static bool IsHashNumericallyLess(
        ReadOnlySpan<byte> left,
        ReadOnlySpan<byte> right)
    {
        for (int index = left.Length - 1; index >= 0; index--)
        {
            if (left[index] != right[index])
                return left[index] < right[index];
        }

        return false;
    }

    private static double CalculateHashDifficulty(ReadOnlySpan<byte> hash)
    {
        var hashValue = new BigInteger(
            hash,
            isUnsigned: true,
            isBigEndian: false);

        if (hashValue.IsZero)
            return double.PositiveInfinity;

        BigInteger difficultyOneTarget = new(
            Convert.FromHexString(
                "00000000FFFF0000000000000000000000000000000000000000000000000000"),
            isUnsigned: true,
            isBigEndian: true);

        return (double)difficultyOneTarget / (double)hashValue;
    }

    private static byte[] BuildHeader(
        MiningJob job,
        string extra1,
        byte[] extra2)
    {
        byte[] coinbase = Combine(
            DecodeHex(job.Coinb1, null, "coinb1"),
            DecodeHex(extra1, null, "extranonce1"),
            extra2,
            DecodeHex(job.Coinb2, null, "coinb2"));

        byte[] merkleRoot = DoubleSha256(coinbase);

        foreach (string branch in job.Branches)
        {
            byte[] branchBytes = DecodeHex(
                branch,
                32,
                "Merkle Branch");

            merkleRoot = DoubleSha256(
                Combine(merkleRoot, branchBytes));
        }

        byte[] previousHash =
            SwapWords(DecodeHex(job.PrevHash, 32, "prevHash"));

        if (previousHash.Length != 32 ||
            merkleRoot.Length != 32 ||
            job.Version.Length != 8 ||
            job.NTime.Length != 8 ||
            job.NBits.Length != 8)
        {
            throw new FormatException(
                "Dados do cabeçalho do bloco inválidos.");
        }

        byte[] header = new byte[80];

        WriteHeaderUInt32(job.Version, header.AsSpan(0, 4));
        Buffer.BlockCopy(previousHash, 0, header, 4, 32);

        Buffer.BlockCopy(merkleRoot, 0, header, 36, 32);
        WriteHeaderUInt32(job.NTime, header.AsSpan(68, 4));
        WriteHeaderUInt32(job.NBits, header.AsSpan(72, 4));

        return header;
    }

    private static byte[] DecodeHex(
        string? value,
        int? expectedLength,
        string fieldName)
    {
        if (value == null)
            throw new FormatException($"{fieldName} ausente.");

        byte[] bytes;

        try
        {
            bytes = Convert.FromHexString(value);
        }
        catch (FormatException exception)
        {
            throw new FormatException(
                $"{fieldName} não contém hexadecimal válido.",
                exception);
        }

        if (expectedLength.HasValue &&
            bytes.Length != expectedLength.Value)
        {
            throw new FormatException(
                $"{fieldName} deve conter {expectedLength.Value} bytes.");
        }

        return bytes;
    }

    private static void WriteHeaderUInt32(
        string value,
        Span<byte> destination)
    {
        if (destination.Length != sizeof(uint) ||
            !uint.TryParse(
                value,
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out uint parsed))
        {
            throw new FormatException(
                "Campo numérico do cabeçalho do bloco inválido.");
        }

        BinaryPrimitives.WriteUInt32LittleEndian(
            destination,
            parsed);
    }

    private static void WriteNonce(byte[] header, uint nonce)
    {
        if (header.Length != 80)
            throw new ArgumentException(
                "O cabeçalho do bloco deve conter 80 bytes.",
                nameof(header));

        BinaryPrimitives.WriteUInt32LittleEndian(
            header.AsSpan(76, 4),
            nonce);
    }

    private static byte[] DoubleSha256(byte[] data)
    {
        byte[] first = SHA256.HashData(data);

        return SHA256.HashData(first);
    }

    private static byte[] Combine(params byte[][] arrays)
    {
        int length = arrays.Sum(a => a.Length);

        byte[] result = new byte[length];

        int offset = 0;

        foreach (byte[] array in arrays)
        {
            Buffer.BlockCopy(
                array, 0, result, offset, array.Length);

            offset += array.Length;
        }

        return result;
    }

    private static byte[] SwapWords(byte[] data)
    {
        if (data.Length % 4 != 0)
            throw new FormatException("Hash inválido.");

        byte[] result = new byte[data.Length];

        for (int i = 0; i < data.Length; i += 4)
        {
            result[i] = data[i + 3];
            result[i + 1] = data[i + 2];
            result[i + 2] = data[i + 1];
            result[i + 3] = data[i];
        }

        return result;
    }

    private static BigInteger CalculateTarget(
        BigInteger difficultyNumerator,
        BigInteger difficultyDenominator)
    {
        if (difficultyNumerator <= BigInteger.Zero ||
            difficultyDenominator <= BigInteger.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(difficultyNumerator));
        }

        byte[] diff1Bytes = Convert.FromHexString(
            "00000000FFFF0000000000000000000000000000000000000000000000000000");

        BigInteger diff1 = new BigInteger(
            diff1Bytes,
            isUnsigned: true,
            isBigEndian: true);

        BigInteger target =
            diff1 * difficultyDenominator / difficultyNumerator;
        BigInteger maximumHash =
            (BigInteger.One << 256) - BigInteger.One;

        return BigInteger.Min(target, maximumHash);
    }

    private static bool HashMeetsTarget(
        ReadOnlySpan<byte> hash,
        BigInteger target)
    {
        if (hash.Length != 32)
            throw new ArgumentException(
                "O hash deve conter exatamente 32 bytes.",
                nameof(hash));

        if (target < BigInteger.Zero)
            throw new ArgumentOutOfRangeException(nameof(target));

        BigInteger hashValue = new(
            hash,
            isUnsigned: true,
            isBigEndian: false);

        return hashValue <= target;
    }

    private static bool TryParseDifficulty(
        string? value,
        out BigInteger numerator,
        out BigInteger denominator)
    {
        numerator = BigInteger.Zero;
        denominator = BigInteger.One;

        if (string.IsNullOrWhiteSpace(value) || value.Length > 256)
            return false;

        int exponent = 0;
        int exponentIndex = value.IndexOfAny(new[] { 'e', 'E' });

        string mantissa = value;

        if (exponentIndex >= 0)
        {
            string exponentText = value[(exponentIndex + 1)..];

            if (value.IndexOfAny(
                    new[] { 'e', 'E' },
                    exponentIndex + 1) >= 0 ||
                !int.TryParse(
                    exponentText,
                    NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out exponent) ||
                Math.Abs((long)exponent) > 256)
            {
                return false;
            }

            mantissa = value[..exponentIndex];
        }

        if (mantissa.StartsWith("-", StringComparison.Ordinal) ||
            mantissa.StartsWith("+", StringComparison.Ordinal))
        {
            return false;
        }

        int decimalPoint = mantissa.IndexOf('.');

        if (decimalPoint >= 0 &&
            mantissa.IndexOf('.', decimalPoint + 1) >= 0)
        {
            return false;
        }

        string integerPart = decimalPoint < 0
            ? mantissa
            : mantissa[..decimalPoint];
        string fractionalPart = decimalPoint < 0
            ? ""
            : mantissa[(decimalPoint + 1)..];

        if (integerPart.Length == 0 ||
            (decimalPoint >= 0 && fractionalPart.Length == 0) ||
            integerPart.Any(character => character < '0' || character > '9') ||
            fractionalPart.Any(character => character < '0' || character > '9'))
        {
            return false;
        }

        string digits = integerPart + fractionalPart;

        if (!BigInteger.TryParse(
                digits,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out numerator) ||
            numerator <= BigInteger.Zero)
        {
            return false;
        }

        int scale = fractionalPart.Length - exponent;

        if (Math.Abs((long)scale) > 256)
            return false;

        if (scale > 0)
        {
            denominator = BigInteger.Pow(10, scale);
        }
        else if (scale < 0)
        {
            numerator *= BigInteger.Pow(10, -scale);
        }

        BigInteger divisor =
            BigInteger.GreatestCommonDivisor(numerator, denominator);

        numerator /= divisor;
        denominator /= divisor;

        return true;
    }

    private static bool HasSameWork(
        MiningJob left,
        MiningJob right)
    {
        return string.Equals(left.PrevHash, right.PrevHash, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(left.Coinb1, right.Coinb1, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(left.Coinb2, right.Coinb2, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(left.Version, right.Version, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(left.NBits, right.NBits, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(left.NTime, right.NTime, StringComparison.OrdinalIgnoreCase) &&
               left.Branches.SequenceEqual(
                   right.Branches,
                   StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryAdvanceNonce(
        ref uint nonce,
        byte[] extranonce2,
        out bool extranonceAdvanced)
    {
        extranonceAdvanced = false;

        if (nonce < uint.MaxValue)
        {
            nonce++;
            return true;
        }

        if (!IncrementExtranonce2(extranonce2))
            return false;

        nonce = 0;
        extranonceAdvanced = true;
        return true;
    }

    private static bool IncrementExtranonce2(byte[] value)
    {
        if (value.Length == 0 ||
            Array.TrueForAll(value, static item => item == byte.MaxValue))
        {
            return false;
        }

        for (int i = value.Length - 1; i >= 0; i--)
        {
            if (value[i] < byte.MaxValue)
            {
                value[i]++;
                return true;
            }

            value[i] = 0;
        }

        return false;
    }

    public void Dispose()
    {
        cancellation?.Cancel();
        cancellation?.Dispose();
    }
}
