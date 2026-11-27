
namespace MineDigibyteMobile.Services;

public sealed class MiningSessionRuntime
{
    private static readonly Lazy<MiningSessionRuntime> instance =
        new(() => new MiningSessionRuntime());

    private const float ThermalLimit50 = 45f;
    private const float ThermalLimit25 = 50f;
    private const float ThermalPauseTemperature = 55f;
    private const int ThermalPauseMinutes = 2;

    private readonly object sync = new();

    private Func<Task>? stopSession;
    private string status = "DESCONECTADO";

    private bool active;
    private bool backgroundServiceActive;
    private bool manualPaused;
    private bool thermalPaused;
    private bool thermalProtectionActive;

    private bool hasValidTemperature;
    private float lastBatteryTemperature;

    private CancellationTokenSource? thermalPauseCancellation;

    public StratumMiner Miner { get; } = new();

    private bool gpuInitialized;
    private IGpuHashBackend? gpuBackend;
    private readonly SemaphoreSlim gpuInitializationGate = new(1, 1);

    /// <summary>Resultado da inicialização da GPU, em texto para o painel e o log.</summary>
    public string GpuStatus { get; private set; } = "GPU ainda não inicializada";

    public bool GpuActive
    {
        get { lock (sync) return gpuBackend != null; }
    }

    /// <summary>Liga ou desliga a GPU sem permitir duas inicializações nativas simultâneas.</summary>
    public void SetGpuEnabled(bool enabled)
    {
        gpuInitializationGate.Wait();
        try
        {
            MiningPreferences.GpuEnabled = enabled;

            if (enabled)
            {
                lock (sync)
                {
                    if (gpuBackend != null)
                        return;

                    gpuInitialized = false;
                }

                InitializeGpuCore();
                return;
            }

            IGpuHashBackend? old;
            lock (sync)
            {
                old = gpuBackend;
                gpuBackend = null;
                gpuInitialized = true;
                GpuStatus = "GPU desativada nas configurações; usando CPU";
            }

            Miner.SetGpuBackend(null);
            old?.Dispose();
        }
        finally
        {
            gpuInitializationGate.Release();
        }
    }

    /// <summary>
    /// Inicializa a GPU fora da thread da interface. O semaphore também
    /// serializa esta operação com a alternância da GPU nas configurações.
    /// </summary>
    public Task InitializeGpuAsync(CancellationToken cancellationToken = default) =>
        Task.Run(InitializeGpu, cancellationToken);

    /// <summary>Alterna a GPU em segundo plano para não travar a tela de configurações.</summary>
    public Task SetGpuEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => SetGpuEnabled(enabled), cancellationToken);

    public string GpuDeviceName
    {
        get { lock (sync) return gpuBackend?.DeviceName ?? ""; }
    }

    public string GpuApiName
    {
        get { lock (sync) return gpuBackend?.ApiName ?? ""; }
    }

    /// <summary>Tenta Vulkan e, se não der, OpenGL ES 3.1; se nenhum funcionar, a mineração segue no CPU.</summary>
    public void InitializeGpu()
    {
        gpuInitializationGate.Wait();
        try
        {
            InitializeGpuCore();
        }
        finally
        {
            gpuInitializationGate.Release();
        }
    }

    private void InitializeGpuCore()
    {
        lock (sync)
        {
            if (gpuInitialized)
                return;

            gpuInitialized = true;
        }

        if (!MiningPreferences.GpuEnabled)
        {
            GpuStatus = "GPU desativada nas configurações; usando CPU";
            return;
        }

#if ANDROID
        IGpuHashBackend? backend = VulkanHashBackend.TryCreate(out string message);

        if (backend == null)
        {
            string vulkanMessage = message;
            backend = GlesHashBackend.TryCreate(out string glesMessage);
            message = backend != null
                ? glesMessage
                : $"{vulkanMessage} · {glesMessage} · usando CPU";
        }

        GpuStatus = message;

        if (backend != null)
        {
            lock (sync)
                gpuBackend = backend;

            Miner.SetGpuBackend(backend);
            ApplyEffectiveIntensity();
        }
#else
        GpuStatus = "GPU só é suportada no Android; usando CPU";
#endif
    }

    public void SetGpuIntensity(int value)
    {
        MiningPreferences.GpuIntensityPercent = value;
        ApplyEffectiveIntensity();
    }

    public MiningSessionMetrics SessionMetrics { get; } = new();

    public event Action<string>? StatusChanged;
    public event Action<bool>? BackgroundServiceChanged;

    public static MiningSessionRuntime Current => instance.Value;

    public bool IsActive
    {
        get { lock (sync) return active; }
    }

    public bool IsBackgroundServiceActive
    {
        get { lock (sync) return backgroundServiceActive; }
    }

    public string Status
    {
        get { lock (sync) return status; }
    }

    public int IntensityPercent => MiningPreferences.IntensityPercent;

    public int CpuWorkerCount => Miner.CpuWorkerCount;

    public bool IsManualPaused
    {
        get { lock (sync) return manualPaused; }
    }

    public bool IsThermalPaused
    {
        get { lock (sync) return thermalPaused; }
    }

    public bool IsThermalProtectionActive
    {
        get { lock (sync) return thermalProtectionActive; }
    }

    public bool IsPaused => Miner.IsPaused;

    public void Attach(Func<Task> stop)
    {
        ArgumentNullException.ThrowIfNull(stop);

        lock (sync)
            stopSession = stop;

        ApplyEffectiveIntensity();
    }

    public void SetStatus(string value)
    {
        lock (sync)
            status = value;

        StatusChanged?.Invoke(value);
    }

public void SetActive(bool value)
{
    bool releasePause = false;
    bool startThermalPause = false;
    CancellationTokenSource? oldCancellation = null;

    lock (sync)
    {
        active = value;

        if (!value)
        {
            releasePause = manualPaused || thermalPaused;

            manualPaused = false;
            thermalPaused = false;
            thermalProtectionActive = false;

            oldCancellation = thermalPauseCancellation;
            thermalPauseCancellation = null;
        }
        else if (hasValidTemperature &&
                 lastBatteryTemperature >= ThermalPauseTemperature)
        {
            thermalProtectionActive = true;
            startThermalPause = !thermalPaused;
        }
    }

    oldCancellation?.Cancel();

    ApplyEffectiveIntensity();

    if (releasePause)
        Miner.SetPaused(false);

    if (startThermalPause)
        StartThermalPause();
}


    public void SetBackgroundServiceActive(bool value)
    {
        bool changed;

        lock (sync)
        {
            changed = backgroundServiceActive != value;
            backgroundServiceActive = value;
        }

        if (changed)
            BackgroundServiceChanged?.Invoke(value);
    }

    public void SetIntensity(int value)
    {
        if (value is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(value));

        MiningPreferences.IntensityPercent = value;

        ApplyEffectiveIntensity();
    }

    public void SetCpuWorkerCount(int value)
    {
        Miner.SetCpuWorkerCount(value);
    }

    // Recalcula a intensidade efetiva sem modificar
    // a intensidade configurada pelo usuário.
    public void RefreshEffectiveIntensity()
    {
        ApplyEffectiveIntensity();
    }

    private void ApplyEffectiveIntensity()
    {
        int configured = MiningPreferences.IntensityPercent;
        int limit = 100;

        lock (sync)
        {
            if (thermalProtectionActive)
            {
                if (lastBatteryTemperature >= ThermalLimit25)
                    limit = 25;
                else if (lastBatteryTemperature >= ThermalLimit50)
                    limit = 50;
            }
        }

        int effectiveIntensity = Math.Min(configured, limit);

        Miner.SetIntensityPercent(effectiveIntensity);

        // A GPU tem intensidade própria; o limite térmico vale para as duas.
        Miner.SetGpuIntensityPercent(
            Math.Min(MiningPreferences.GpuIntensityPercent, limit));
    }

    public void UpdateBatteryTemperature(float? temperatureC)
    {
        if (!temperatureC.HasValue ||
            float.IsNaN(temperatureC.Value) ||
            float.IsInfinity(temperatureC.Value) ||
            temperatureC.Value < 0 ||
            temperatureC.Value > 100)
        {
            // Leitura inválida: mantém o estado de segurança anterior.
            return;
        }

        float temperature = temperatureC.Value;

        bool pauseRequired = false;
        bool cancelThermalPause = false;

        CancellationTokenSource? oldCancellation = null;

        lock (sync)
        {
            hasValidTemperature = true;
            lastBatteryTemperature = temperature;

            if (temperature >= ThermalPauseTemperature)
            {
                thermalProtectionActive = true;

                if (active && !thermalPaused)
                    pauseRequired = true;
            }
            else
            {
                if (thermalPaused)
                {
                    // A temperatura baixou: podemos cancelar
                    // a pausa térmica antes do fim dos 2 minutos.
                    cancelThermalPause = true;

                    oldCancellation = thermalPauseCancellation;
                    thermalPauseCancellation = null;
                }

                if (temperature >= ThermalLimit50)
                    thermalProtectionActive = true;
                else
                    thermalProtectionActive = false;
            }
        }

        // O monitor térmico é responsável por descartar seu próprio CTS.
        oldCancellation?.Cancel();

        ApplyEffectiveIntensity();

        if (pauseRequired)
            StartThermalPause();

        if (cancelThermalPause)
            SetThermalPaused(false);
    }

    private void StartThermalPause()
    {
        CancellationTokenSource current;

        lock (sync)
        {
            if (!active ||
                thermalPaused ||
                !hasValidTemperature ||
                lastBatteryTemperature < ThermalPauseTemperature)
            {
                return;
            }

            if (thermalPauseCancellation != null)
                return;

            current = new CancellationTokenSource();

            thermalPauseCancellation = current;
        }

        SetThermalPaused(true);

        // Confere novamente a temperatura para evitar uma
        // pausa desnecessária se ela esfriou durante a operação.
        bool cancelImmediately = false;

        lock (sync)
        {
            if (ReferenceEquals(thermalPauseCancellation, current) &&
                (!active ||
                 (hasValidTemperature &&
                  lastBatteryTemperature < ThermalPauseTemperature)))
            {
                thermalPauseCancellation = null;
                cancelImmediately = true;
            }
        }

        if (cancelImmediately)
        {
            current.Cancel();
            SetThermalPaused(false);
            return;
        }

        _ = MonitorThermalPauseAsync(current);
    }

    private async Task MonitorThermalPauseAsync(
        CancellationTokenSource source)
    {
        try
        {
            while (true)
            {
                await Task.Delay(
                    TimeSpan.FromMinutes(ThermalPauseMinutes),
                    source.Token);

                bool shouldResume = false;
                bool shouldContinueWaiting = false;

                lock (sync)
                {
                    // Impede que um monitor antigo interfira
                    // em uma pausa térmica mais recente.
                    if (!ReferenceEquals(thermalPauseCancellation, source))
                        return;

                    if (!active)
                    {
                        thermalPauseCancellation = null;
                        shouldResume = true;
                    }
                    else if (hasValidTemperature &&
                             lastBatteryTemperature < ThermalPauseTemperature)
                    {
                        // Temperatura segura: libera a pausa térmica.
                        thermalPauseCancellation = null;
                        shouldResume = true;
                    }
                    else
                    {
                        // Continua quente ou a leitura está indisponível.
                        // Mantém a pausa e aguarda outros 2 minutos.
                        shouldContinueWaiting = true;
                    }
                }

                if (shouldResume)
                {
                    SetThermalPaused(false);
                    return;
                }

                if (shouldContinueWaiting)
                {
                    // O loop inicia automaticamente outro ciclo de 2 minutos.
                    continue;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelamento quando a temperatura diminui
            // ou quando a sessão é encerrada.
        }
        finally
        {
            lock (sync)
            {
                if (ReferenceEquals(thermalPauseCancellation, source))
                    thermalPauseCancellation = null;
            }

            source.Dispose();
        }
    }

    public void TogglePause()
    {
        SetManualPaused(!IsManualPaused);
    }

    public void SetManualPaused(bool value)
    {
        AtualizarMotivoPausa(false, value);
    }

    public void SetThermalPaused(bool value)
    {
        AtualizarMotivoPausa(true, value);
    }

    // Compatibilidade com chamadas antigas.
    public void SetPaused(bool value)
    {
        SetManualPaused(value);
    }

    private void AtualizarMotivoPausa(bool thermal, bool value)
    {
        bool shouldPause;
        bool changed;
        bool isActive;

        lock (sync)
        {
            bool wasPaused = manualPaused || thermalPaused;

            if (thermal)
                thermalPaused = value;
            else
                manualPaused = value;

            shouldPause = manualPaused || thermalPaused;
            changed = wasPaused != shouldPause;

            isActive = active;

            if (!changed)
                return;
        }

        if (shouldPause)
        {
            SetStatus("PAUSADO");
            Miner.SetPaused(true);
            return;
        }

        SetStatus(isActive
            ? Miner.IsRunning ? "MINERANDO" : "AUTORIZADO"
            : "DESCONECTADO");

        Miner.SetPaused(false);
    }

    public async Task StopAsync()
    {
        Func<Task>? stop;

        lock (sync)
            stop = stopSession;

        if (stop != null)
        {
            if (MainThread.IsMainThread)
                await stop();
            else
                await MainThread.InvokeOnMainThreadAsync(stop);

            return;
        }

        await Miner.StopAsync();
    }
}
