
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.Maui.Devices;
using MineDigibyteMobile.Models;
using MineDigibyteMobile.Services;
using MineDigibyteMobile.Localization;
using QRCoder;
using MineDigibyteMobile.Platforms.Android;

namespace MineDigibyteMobile;

public partial class MainPage : ContentPage
{
    private static readonly TimeSpan SubscribeTimeout =
        TimeSpan.FromSeconds(15);
    private static readonly TimeSpan AuthorizeTimeout =
        TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ShareTimeout =
        TimeSpan.FromSeconds(30);

    private bool minerando;
    private bool autorizado;

    private DateTime? ultimoJobRecebido;
    private string jobAtual = "—";
    private string dificuldadeAtual = "N/D";
    private string ultimaShareEnviada = "Nenhuma";
    private string ultimaRespostaPool = "Nenhuma resposta";
    private bool mineracaoPausada;
    private bool conexaoAnterior;
    private double hashrateAtual;
    private double hashrateMedio;
    private TimeSpan uptimeMineracao;
    private long ultimoTotalHashesSessao;
    private readonly object estatisticasSync = new();

    private TcpClient? cliente;
    private NetworkStream? stream;
    private CancellationTokenSource? cancelamento;
    private Task? tarefaLeitura;

    private readonly SemaphoreSlim bloqueioEnvio = new(1, 1);
    private readonly StratumMiner miner = MiningSessionRuntime.Current.Miner;

    // Pool e porta que já vêm preenchidas para quem instala o app pela primeira vez.
    private const string DefaultPool = "stratum.bcmonster.com";
    private const string DefaultPort = "9996";

    // Carteira pública para receber doações em DigiByte (DGB) Mainnet.
    // Endereço DGB (bech32) do desenvolvedor.
    private const string DonationDigiByteAddress =
        "dgb1q269vr89kzrfg3vfa9upae32fqkfh2uvf7d0jg2";

    private readonly ShareRequestTracker shares = new();
    private readonly MiningMonitorState monitor =
        new(new PreferencesMiningMonitorStore());

    private double? ultimaTemperaturaBateriaCelsius;
    private BatteryTemperatureService.BatteryInfo? ultimaInfoBateria;
    private string? ultimoStatusTermicoSistema;
    private bool falhaConsultaBateria;
    private HardwareCapabilities? hardwareCapabilities;
    private string estadoTermicoKey = "ThermalProtectionWaiting";
    private string? erroProtecaoTermicaOriginal;
    private string ultimoEstadoTermico =
        "Proteção térmica aguardando leitura";

    private readonly ObservableCollection<string> historicoAtividade = new();
    private readonly List<(string Timestamp, string Message)> activitySourceHistory = new();
    private readonly List<(string Timestamp, string Message)> logSourceHistory = new();
    private string emptyLogKey = "WaitingStart";
    private readonly HashrateChartDrawable hashrateChartDrawable = new();
    private readonly TemperatureChartDrawable temperatureChartDrawable = new();
    private readonly ShareChartDrawable shareChartDrawable = new();

    private int idRequisicao;
    private int? idSubscribe;
    private int? idAuthorize;

    private string usuarioPool = "";

    private static string T(string key) =>
        LocalizationService.Instance.GetString(key);

    private static string TM(string? text) =>
        LocalizationService.Instance.TranslateText(text);

    public MainPage()
    {
        InitializeComponent();
        ConfigurarDoacao();
        AtualizarPainelHardware();

        HashrateChart.Drawable = hashrateChartDrawable;
        TempChart.Drawable = temperatureChartDrawable;
        AtualizarLimitesTermicosNoPainel();
        ShareChart.Drawable = shareChartDrawable;

        CarregarConfiguracao();

        BindableLayout.SetItemsSource(
            EventHistoryLayout,
            historicoAtividade);

        BindableLayout.SetItemTemplate(
            EventHistoryLayout,
            new DataTemplate(() =>
            {
                var label = new Label
                {
                    FontSize = 12,
                    TextColor = Color.FromArgb("#D7DEE8"),
                    LineBreakMode = LineBreakMode.WordWrap
                };

                label.SetBinding(
                    Label.TextProperty,
                    static (string item) => item);

                return label;
            }));

        miner.Log += mensagem => AdicionarLog(mensagem);
        miner.StatisticsUpdated += AtualizarEstatisticasMotor;
        miner.ShareFound += RegistrarShareEncontrada;
        miner.WorkStarted += RegistrarInicioProcessamento;
        miner.PauseChanged += Miner_PauseChanged;

        MiningPreferences.IntensityChanged += Miner_IntensityChanged;
        MiningPreferences.BackgroundMiningChanged += BackgroundMiningChanged;

        MiningSessionRuntime.Current.Attach(
            () => PararMineracaoAsync());

        // Vulkan/GLES initialization performs native setup and a GPU self-test.
        // Run it in the background so the first screen can appear immediately.
        _ = InicializarGpuEmSegundoPlanoAsync();

        AtualizarInformacoesBateria();

        Dispatcher.StartTimer(
            TimeSpan.FromSeconds(1),
            () =>
            {
                AtualizarInformacoesBateria();
                return AtualizarPainel();
            });
    }

    private async Task InicializarGpuEmSegundoPlanoAsync()
    {
        MiningSessionRuntime runtime = MiningSessionRuntime.Current;
        try
        {
            await runtime.InitializeGpuAsync();
            AdicionarLog(runtime.GpuStatus);
            MainThread.BeginInvokeOnMainThread(AtualizarPainelHardware);
        }
        catch (Exception ex)
        {
            AdicionarLog("Falha ao inicializar GPU: " + ex.Message);
            MainThread.BeginInvokeOnMainThread(AtualizarPainelHardware);
        }
    }

    private void ConfigurarDoacao()
    {
        try
        {
            string endereco = DonationDigiByteAddress.Trim();

            if (string.IsNullOrWhiteSpace(endereco) ||
                endereco == "COLOQUE_SEU_ENDERECO_DIGIBYTE_AQUI")
            {
                DonationAddressLabel.Text = T("DonationUnconfigured");

                DonationQrImage.IsVisible = false;
                return;
            }

            // Gera QR Code compatível com carteiras DigiByte.
            string pagamento = $"digibyte:{endereco}";

            using var generator = new QRCodeGenerator();

            using QRCodeData qrData = generator.CreateQrCode(
                pagamento,
                QRCodeGenerator.ECCLevel.Q);

            var qrCode = new PngByteQRCode(qrData);

            byte[] imagem = qrCode.GetGraphic(10);

            DonationQrImage.Source = ImageSource.FromStream(
                () => new MemoryStream(imagem));

            DonationQrImage.IsVisible = true;

            DonationAddressLabel.Text = endereco;

            AdicionarLog("Carteira de doação configurada.");
        }
        catch (Exception ex)
        {
            DonationQrImage.IsVisible = false;

            DonationAddressLabel.Text = T("DonationQrError");

            AdicionarLog(
                "Erro ao configurar doação: " + ex.Message);
        }
    }

    private async void CopyDonationAddress_Clicked(
        object? sender,
        EventArgs e)
    {
        try
        {
            string endereco = DonationDigiByteAddress.Trim();

            if (string.IsNullOrWhiteSpace(endereco) ||
                endereco == "COLOQUE_SEU_ENDERECO_DIGIBYTE_AQUI")
            {
                await DisplayAlertAsync(
                    T("Donation"),
                    T("DonationNotConfiguredAlert"),
                    T("Ok"));

                return;
            }

            await Clipboard.Default.SetTextAsync(endereco);

            await DisplayAlertAsync(
                T("DonationDigiByte"),
                T("AddressCopied"),
                T("Ok"));
        }
        catch (Exception ex)
        {
            AdicionarLog("Não foi possível copiar o conteúdo. " + ex.GetType().Name);
            await DisplayAlertAsync(
                T("Error"),
                T("ClipboardCopyFailed"),
                T("Ok"));
        }
    }

    private void AtualizarPainelHardware()
    {
        // Detect once and reuse the result when the page only needs a
        // language refresh. Hardware probing is not required on every return
        // to the main page or every language change.
        HardwareCapabilities capabilities =
            hardwareCapabilities ??= HardwareCapabilitiesService.Detect();

        HardwareDeviceLabel.Text = LocalizationService.Instance.Format(
            "HardwareDeviceFormat",
            capabilities.Manufacturer,
            capabilities.Model,
            capabilities.OperatingSystem);

        HardwareCpuLabel.Text = LocalizationService.Instance.Format(
            "CpuInfoFormat", TM(capabilities.CpuDescription));

        HardwareGpuLabel.Text = LocalizationService.Instance.Format(
            "GpuInfoFormat", TM(capabilities.GraphicsDescription));

        int cpuWorkers =
            MiningSessionRuntime.Current.Miner.CpuWorkerCount;

        MiningSessionRuntime runtime = MiningSessionRuntime.Current;

        MiningBackendLabel.Text = runtime.GpuActive
            ? LocalizationService.Instance.Format(
                "GpuEngineFormat", runtime.GpuApiName, runtime.GpuDeviceName, cpuWorkers)
            : LocalizationService.Instance.Format(
                "CpuEngineFormat", TM(runtime.GpuStatus), cpuWorkers);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        LocalizationService.Instance.LanguageChanged += Localization_LanguageChanged;
        SincronizarEstadoRuntime();
        ApplyDynamicLocalization();
    }

    protected override void OnDisappearing()
    {
        LocalizationService.Instance.LanguageChanged -= Localization_LanguageChanged;
        base.OnDisappearing();
    }

    private void Localization_LanguageChanged(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(ApplyDynamicLocalization);
    }

    private void ApplyDynamicLocalization()
    {
        DefinirStatus(MiningSessionRuntime.Current.Status);
        StartButton.Text = minerando ? T("Stop") : T("Start");
        PauseButton.Text = mineracaoPausada ? T("Resume") : T("Pause");
        PauseButton.IsVisible = autorizado;

        AtualizarPainelHardware();
        AtualizarLimitesTermicosNoPainel();

        // Re-render cached readings in the new language. Do not query Android
        // sensors or run thermal-protection logic just to change UI language.
        AtualizarTextoBateria();
        AtualizarPainelTemperatura(
            ultimaTemperaturaBateriaCelsius,
            registrarAmostra: false);
        AtualizarTextoEstadoTermico();
        AtualizarPainelValores();
        HashrateChart.Invalidate();
        ShareChart.Invalidate();
        TempChart.Invalidate();

        if (!DonationQrImage.IsVisible)
            DonationAddressLabel.Text = T("DonationNotConfigured");

        ConfigStatusLabel.Text = string.Empty;
        RenderLog();

        historicoAtividade.Clear();
        foreach (var item in activitySourceHistory)
            historicoAtividade.Add($"[{item.Timestamp}] {TM(item.Message)}");
    }

    private async void SettingsButton_Clicked(
        object? sender,
        EventArgs e)
    {
        await Navigation.PushModalAsync(new SettingsPage());
    }


private void Miner_IntensityChanged(int value)
{
    MiningSessionRuntime.Current.RefreshEffectiveIntensity();
}

    private void BackgroundMiningChanged(bool enabled)
    {
        if (!minerando)
            return;

        if (enabled)
            MiningBackgroundServiceControl.Start();
        else
            MiningBackgroundServiceControl.Stop();
    }

    private void Miner_PauseChanged(bool paused)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            mineracaoPausada = paused;

            PauseButton.Text = paused
                ? T("Resume")
                : T("Pause");

            DefinirStatus(paused
                ? "PAUSADO"
                : autorizado && jobAtual != "—"
                    ? "MINERANDO"
                    : autorizado
                        ? "AUTORIZADO"
                        : MiningSessionRuntime.Current.Status);
        });
    }

    private void CarregarConfiguracao()
    {
        // One-time migration: remove any wallet address carried over from an
        // earlier build. The user must enter and save their own address.
        const string walletMigrationKey = "wallet_prompt_migration_v1";

        if (!Preferences.Default.Get(walletMigrationKey, false))
        {
            Preferences.Default.Remove("wallet");
            Preferences.Default.Set(walletMigrationKey, true);
        }

        WalletEntry.Text = Preferences.Default.Get("wallet", "");
        PoolEntry.Text = Preferences.Default.Get("pool", DefaultPool);
        PortEntry.Text = Preferences.Default.Get("port", DefaultPort);

        WorkerEntry.Text = Preferences.Default.Get(
            "worker",
            "MineDigibyteMobile");
    }

    private void SaveConfig_Clicked(
        object? sender,
        EventArgs e)
    {
        Preferences.Default.Set("wallet", WalletEntry.Text ?? "");
        Preferences.Default.Set("pool", PoolEntry.Text ?? DefaultPool);
        Preferences.Default.Set("port", PortEntry.Text ?? DefaultPort);
        Preferences.Default.Set("worker", WorkerEntry.Text ?? "");

        ConfigStatusLabel.Text = T("ConfigSaved");
        ConfigStatusLabel.TextColor = Colors.LimeGreen;

        AdicionarLog("Configuração salva.");
    }

    private async void StartButton_Clicked(
        object? sender,
        EventArgs e)
    {
        MiningSessionRuntime runtime = MiningSessionRuntime.Current;

        if (minerando ||
            runtime.IsActive ||
            StatusRepresentaSessaoAtiva(runtime.Status))
        {
            SincronizarEstadoRuntime();
            await PararMineracaoAsync();
            return;
        }

        string carteira = WalletEntry.Text?.Trim() ?? "";
        string pool = PoolEntry.Text?.Trim() ?? "";
        string worker = WorkerEntry.Text?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(carteira) ||
            string.IsNullOrWhiteSpace(pool) ||
            string.IsNullOrWhiteSpace(worker))
        {
            ConfigStatusLabel.Text = T("ConfigRequired");

            ConfigStatusLabel.TextColor = Colors.Orange;
            return;
        }

        if (!int.TryParse(PortEntry.Text, out int porta) ||
            porta < 1 || porta > 65535)
        {
            ConfigStatusLabel.Text = T("InvalidPort");
            ConfigStatusLabel.TextColor = Colors.Orange;
            return;
        }

        try
        {
            StartButton.IsEnabled = false;

            cancelamento = new CancellationTokenSource();

            Interlocked.Exchange(ref idRequisicao, 0);
            idSubscribe = null;
            idAuthorize = null;

            autorizado = false;
            mineracaoPausada = false;
            jobAtual = "—";
            ultimoJobRecebido = null;
            dificuldadeAtual = "N/D";

            PauseButton.IsVisible = false;

            Interlocked.Exchange(ref ultimoTotalHashesSessao, 0);

            shares.Reset();
            runtime.SessionMetrics.Reset();

            lock (estatisticasSync)
            {
                hashrateAtual = 0;
                hashrateMedio = 0;
                uptimeMineracao = TimeSpan.Zero;
            }

            hashrateChartDrawable.Reset();
            HashrateChart.Invalidate();

            shareChartDrawable.SetValues(0, 0);
            ShareChart.Invalidate();

            usuarioPool = carteira + "." + worker;

            DefinirStatus("CONECTANDO");

            AdicionarLog($"Conectando em {pool}:{porta}");

            cliente = new TcpClient();

            using var timeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(10));

            using var combinado =
                CancellationTokenSource.CreateLinkedTokenSource(
                    timeout.Token,
                    cancelamento.Token);

            await cliente.ConnectAsync(
                pool,
                porta,
                combinado.Token);

            stream = cliente.GetStream();

            minerando = true;

            MiningSessionRuntime.Current.SetActive(true);

            DefinirStatus("CONECTADO");

            if (MiningPreferences.BackgroundMiningEnabled)
                MiningBackgroundServiceControl.Start();

            StartButton.Text = T("Stop");

            AdicionarLog("✓ TCP conectado.");

            RegistrarAtividade(conexaoAnterior
                ? "Reconexão com a pool estabelecida."
                : "Conexão com a pool estabelecida.");

            conexaoAnterior = true;

            tarefaLeitura = Task.Run(
                () => LerMensagensAsync(cancelamento.Token));

            int requisicaoSubscribe = ProximoIdRequisicao();
            idSubscribe = requisicaoSubscribe;

            await EnviarMensagemAsync(new
            {
                id = requisicaoSubscribe,
                method = "mining.subscribe",
                @params = new object[]
                {
                    "MineDigibyteMobile/1.0"
                }
            });

            _ = MonitorHandshakeTimeoutAsync(
                requisicaoSubscribe,
                SubscribeTimeout,
                isSubscribe: true,
                cancelamento.Token);

            AdicionarLog("Aguardando Subscribe...");
        }
        catch (Exception ex)
        {
            AdicionarLog("✗ Erro: " + ex.Message);
            DefinirStatus("ERRO");

            await PararMineracaoAsync(preservarErro: true);
        }
        finally
        {
            StartButton.IsEnabled = true;
        }
    }

    private async Task<bool> EnviarMensagemAsync(
        object mensagem,
        bool registrarLog = true,
        StratumShare? share = null)
    {
        if (stream == null)
            throw new InvalidOperationException(
                "Stream não conectado.");

        string json =
            JsonSerializer.Serialize(mensagem) + "\n";

        byte[] dados = Encoding.UTF8.GetBytes(json);

        await bloqueioEnvio.WaitAsync(
            cancelamento?.Token ?? CancellationToken.None);

        try
        {
            if (stream == null)
                throw new InvalidOperationException(
                    "Conexão encerrada.");

            if (share != null && !miner.IsShareCurrent(share))
                return false;

            await stream.WriteAsync(
                dados,
                cancelamento?.Token ?? CancellationToken.None);

            await stream.FlushAsync(
                cancelamento?.Token ?? CancellationToken.None);
        }
        finally
        {
            bloqueioEnvio.Release();
        }

        if (registrarLog)
            AdicionarLog("→ " + json.Trim());

        return true;
    }

    private async Task LerMensagensAsync(
        CancellationToken token)
    {
        try
        {
            if (stream == null)
                return;

            using StreamReader leitor = new(
                stream,
                Encoding.UTF8,
                false,
                1024,
                true);

            while (!token.IsCancellationRequested)
            {
                string? linha = await leitor.ReadLineAsync(token);

                if (linha == null)
                    break;

                if (string.IsNullOrWhiteSpace(linha))
                    continue;

                if (IsCleanJobsNotification(linha))
                    await miner.InvalidateForCleanJobsAsync(token);

                await MainThread.InvokeOnMainThreadAsync(
                    () => ProcessarMensagemAsync(linha));
            }

            if (!token.IsCancellationRequested)
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    AdicionarLog("Conexão encerrada pela pool.");
                    RegistrarAtividade("Conexão perdida.");

                    await PararMineracaoAsync();
                });
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                AdicionarLog("Erro de leitura: " + ex.Message);
                RegistrarAtividade("Erro de conexão: " + ex.Message);

                DefinirStatus("ERRO");

                await PararMineracaoAsync(preservarErro: true);
            });
        }
    }

    private async Task ProcessarMensagemAsync(string mensagem)
    {
        AdicionarLog("← " + mensagem);

        try
        {
            using JsonDocument documento =
                JsonDocument.Parse(mensagem);

            JsonElement raiz = documento.RootElement;

            if (raiz.TryGetProperty("id", out JsonElement idElemento) &&
                idElemento.ValueKind == JsonValueKind.Number)
            {
                int id = idElemento.GetInt32();

                bool temErro =
                    raiz.TryGetProperty("error", out JsonElement erro) &&
                    erro.ValueKind != JsonValueKind.Null;

                bool shareAccepted =
                    !temErro &&
                    raiz.TryGetProperty("result", out JsonElement shareResult) &&
                    shareResult.ValueKind == JsonValueKind.True;

                if (shares.TryResolve(
                    id,
                    shareAccepted,
                    out _))
                {
                    if (shareAccepted)
                    {
                        monitor.RecordShareAccepted();

                        MiningSessionRuntime.Current.SessionMetrics
                            .RecordShareAccepted();

                        ultimaRespostaPool =
                            $"{DateTime.Now:HH:mm:ss} · Share aceita";

                        AdicionarLog(
                            $"✓ SHARE ACEITA! Total: {shares.AcceptedCount}");

                        RegistrarAtividade(
                            $"Share aceita pela pool (ID {id}).");
                    }
                    else
                    {
                        monitor.RecordShareRejected();

                        MiningSessionRuntime.Current.SessionMetrics
                            .RecordShareRejected();

                        ultimaRespostaPool =
                            $"{DateTime.Now:HH:mm:ss} · Share rejeitada" +
                            (temErro ? $": {erro}" : "");

                        AdicionarLog(
                            $"✗ SHARE REJEITADA. Total: {shares.RejectedCount}");

                        if (temErro)
                            AdicionarLog("Motivo: " + erro);

                        RegistrarAtividade(
                            $"Share rejeitada pela pool (ID {id}).");
                    }

                    AtualizarPainel();
                    return;
                }

                if (temErro)
                {
                    ultimaRespostaPool =
                        $"{DateTime.Now:HH:mm:ss} · Erro da pool: {erro}";

                    AdicionarLog("✗ Erro da pool: " + erro);

                    if (idSubscribe == id)
                    {
                        idSubscribe = null;
                        RequestStop();
                        return;
                    }

                    if (id == idAuthorize)
                    {
                        idAuthorize = null;
                        autorizado = false;

                        DefinirStatus("ERRO");

                        RegistrarAtividade(
                            "Autorização recusada pela pool.");
                    }

                    return;
                }

                if (raiz.TryGetProperty(
                    "result",
                    out JsonElement resultado))
                {
                    if (idSubscribe == id)
                    {
                        idSubscribe = null;

                        if (resultado.ValueKind != JsonValueKind.Array ||
                            resultado.GetArrayLength() < 3)
                        {
                            AdicionarLog("Subscribe inválido.");
                            RequestStop();
                            return;
                        }

                        if (resultado[1].ValueKind != JsonValueKind.String)
                        {
                            AdicionarLog("Extranonce1 inválido.");
                            RequestStop();
                            return;
                        }

                        string extraNonce1 =
                            resultado[1].GetString() ?? "";

                        if (resultado[2].ValueKind != JsonValueKind.Number ||
                            !resultado[2].TryGetInt32(out int extraNonce2Size))
                        {
                            AdicionarLog("Tamanho Extranonce inválido.");
                            RequestStop();
                            return;
                        }

                        try
                        {
                            miner.SetSubscription(
                                extraNonce1,
                                extraNonce2Size);
                        }
                        catch (Exception ex)
                        {
                            AdicionarLog(
                                "Resposta Subscribe inválida: " + ex.Message);

                            RequestStop();
                            return;
                        }

                        AdicionarLog("✓ Subscribe configurado.");

                        ultimaRespostaPool =
                            $"{DateTime.Now:HH:mm:ss} · Subscribe aceito";

                        DefinirStatus("AUTORIZANDO");

                        idAuthorize = ProximoIdRequisicao();

                        await EnviarMensagemAsync(new
                        {
                            id = idAuthorize,
                            method = "mining.authorize",
                            @params = new object[]
                            {
                                usuarioPool,
                                "x"
                            }
                        });

                        _ = MonitorHandshakeTimeoutAsync(
                            idAuthorize.Value,
                            AuthorizeTimeout,
                            isSubscribe: false,
                            cancelamento!.Token);

                        AdicionarLog("Aguardando autorização...");
                    }
                    else if (idAuthorize == id)
                    {
                        idAuthorize = null;

                        if (resultado.ValueKind == JsonValueKind.True)
                        {
                            autorizado = true;

                            ultimaRespostaPool =
                                $"{DateTime.Now:HH:mm:ss} · Autorização aceita";

                            PauseButton.IsVisible = true;

                            RegistrarAtividade(
                                "Autorização concluída.");

                            AdicionarLog("✓ Carteira autorizada!");

                            mineracaoPausada = miner.IsPaused;

                            PauseButton.Text = mineracaoPausada
                                ? T("Resume")
                                : T("Pause");

                            DefinirStatus(mineracaoPausada
                                ? "PAUSADO"
                                : "AUTORIZADO");

                            miner.Start(EnviarShareAsync);

                            AdicionarLog(
                                $"Motor SHA-256d iniciado com {miner.CpuWorkerCount} workers CPU. Aguardando trabalho válido...");
                        }
                        else
                        {
                            autorizado = false;

                            ultimaRespostaPool =
                                $"{DateTime.Now:HH:mm:ss} · Autorização recusada";

                            DefinirStatus("ERRO");

                            RegistrarAtividade(
                                "Autorização recusada pela pool.");

                            AdicionarLog("Autorização recusada.");
                        }
                    }
                }
            }

            if (!raiz.TryGetProperty(
                "method",
                out JsonElement metodo))
                return;

            string nomeMetodo = metodo.GetString() ?? "";

            if (!raiz.TryGetProperty(
                "params",
                out JsonElement parametros) ||
                parametros.ValueKind != JsonValueKind.Array)
                return;

            switch (nomeMetodo)
            {
                case "mining.set_difficulty":
                    if (parametros.GetArrayLength() >= 1 &&
                        parametros[0].ValueKind == JsonValueKind.Number)
                    {
                        dificuldadeAtual = parametros[0].GetRawText();

                        miner.SetDifficulty(dificuldadeAtual);

                        ultimaRespostaPool =
                            $"{DateTime.Now:HH:mm:ss} · Dificuldade atualizada";

                        AtualizarPainel();
                    }
                    break;

                case "mining.set_extranonce":
                    if (parametros.GetArrayLength() >= 2 &&
                        parametros[0].ValueKind == JsonValueKind.String &&
                        parametros[1].ValueKind == JsonValueKind.Number)
                    {
                        miner.SetSubscription(
                            parametros[0].GetString()!,
                            parametros[1].GetInt32());

                        ultimaRespostaPool =
                            $"{DateTime.Now:HH:mm:ss} · Extranonce atualizado";
                    }
                    break;

                case "mining.notify":
                    if (parametros.GetArrayLength() < 9)
                    {
                        AdicionarLog("Trabalho incompleto recebido.");
                        break;
                    }

                    string jobId = parametros[0].GetString() ?? "";
                    string prevHash = parametros[1].GetString() ?? "";
                    string coinb1 = parametros[2].GetString() ?? "";
                    string coinb2 = parametros[3].GetString() ?? "";

                    string[] branches = parametros[4]
                        .EnumerateArray()
                        .Select(x => x.GetString() ?? "")
                        .ToArray();

                    string version = parametros[5].GetString() ?? "";
                    string nBits = parametros[6].GetString() ?? "";
                    string nTime = parametros[7].GetString() ?? "";

                    bool cleanJobs =
                        parametros[8].ValueKind == JsonValueKind.True;

                    miner.SetJob(
                        jobId,
                        prevHash,
                        coinb1,
                        coinb2,
                        branches,
                        version,
                        nBits,
                        nTime,
                        cleanJobs);

                    monitor.RecordJob(cleanJobs);

                    jobAtual = jobId;
                    ultimoJobRecebido = DateTime.Now;

                    ultimaRespostaPool =
                        $"{DateTime.Now:HH:mm:ss} · Job {jobId} recebido";

                    RegistrarAtividade(cleanJobs
                        ? $"Novo job {jobId}; cleanJobs=true, trabalho anterior invalidado."
                        : $"Novo job recebido: {jobId}.");

                    AdicionarLog(
                        "✓ Trabalho entregue ao motor SHA-256d.");

                    AtualizarPainel();
                    break;
            }
        }
        catch (JsonException)
        {
            AdicionarLog("Resposta JSON inválida.");
        }
        catch (Exception ex)
        {
            AdicionarLog("Erro de processamento: " + ex.Message);
        }
    }

    private async Task EnviarShareAsync(StratumShare share)
    {
        if (!minerando || !autorizado)
            return;

        if (!miner.IsShareCurrent(share))
        {
            AdicionarLog(
                $"Share obsoleta descartada: job {share.JobId}, nonce {share.Nonce}.");

            return;
        }

        int id = ProximoIdRequisicao();

        CancellationToken sessionToken =
            cancelamento?.Token ?? CancellationToken.None;

        if (!shares.TryAdd(
            id,
            share.JobId,
            share.Nonce,
            sessionToken,
            out CancellationToken timeoutToken))
        {
            throw new InvalidOperationException(
                "O identificador da share já está pendente.");
        }

        AtualizarPainel();

        try
        {
            bool enviada = await miner.TrySubmitShareAsync(
                share,
                () => EnviarMensagemAsync(new
                {
                    id,
                    method = "mining.submit",
                    @params = new object[]
                    {
                        usuarioPool,
                        share.JobId,
                        share.Extranonce2,
                        share.NTime,
                        share.Nonce
                    }
                }, false, share),
                sessionToken);

            if (!enviada)
            {
                shares.TryAbandon(id);

                AtualizarPainel();

                AdicionarLog(
                    $"Share obsoleta descartada antes do envio: job {share.JobId}, nonce {share.Nonce}.");

                return;
            }

            _ = MonitorShareTimeoutAsync(
                id,
                share.JobId,
                share.Nonce,
                timeoutToken);

            monitor.RecordShareSent();

            MiningSessionRuntime.Current.SessionMetrics
                .RecordShareSent();

            ultimaShareEnviada =
                $"{DateTime.Now:HH:mm:ss} · job {share.JobId} · nonce {share.Nonce}";

            AdicionarLog(
                $"→ Share enviada: {share.Nonce}");

            RegistrarAtividade(
                $"Share enviada à pool (job {share.JobId}).");

            AtualizarPainel();
        }
        catch
        {
            shares.TryAbandon(id);

            AtualizarPainel();
            throw;
        }
    }

    private int ProximoIdRequisicao()
    {
        return Interlocked.Increment(ref idRequisicao);
    }

    private async Task MonitorHandshakeTimeoutAsync(
        int requestId,
        TimeSpan timeout,
        bool isSubscribe,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(timeout, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            bool stillPending = isSubscribe
                ? idSubscribe == requestId
                : idAuthorize == requestId;

            if (!stillPending ||
                cancellationToken.IsCancellationRequested)
                return;

            if (isSubscribe)
                idSubscribe = null;
            else
                idAuthorize = null;

            AdicionarLog(isSubscribe
                ? "Timeout aguardando resposta de mining.subscribe."
                : "Timeout aguardando resposta de mining.authorize.");

            DefinirStatus("ERRO");

            await PararMineracaoAsync(preservarErro: true);
        });
    }

    private async Task MonitorShareTimeoutAsync(
        int requestId,
        string jobId,
        string nonce,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(
                ShareTimeout,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!shares.TryExpire(requestId, out _))
            return;

        monitor.RecordShareExpired();

        MiningSessionRuntime.Current.SessionMetrics
            .RecordShareExpired();

        MainThread.BeginInvokeOnMainThread(() =>
        {
            ultimaRespostaPool =
                $"{DateTime.Now:HH:mm:ss} · Share expirada sem resposta";

            AdicionarLog(
                $"Share expirada sem resposta da pool: job {jobId}, nonce {nonce}.");

            RegistrarAtividade(
                $"Share expirada sem resposta da pool (job {jobId}).");

            AtualizarPainel();
        });
    }

    private static bool IsCleanJobsNotification(string message)
    {
        try
        {
            using JsonDocument document =
                JsonDocument.Parse(message);

            JsonElement root = document.RootElement;

            return root.TryGetProperty(
                       "method",
                       out JsonElement method) &&
                   method.ValueKind == JsonValueKind.String &&
                   method.GetString() == "mining.notify" &&
                   root.TryGetProperty(
                       "params",
                       out JsonElement parameters) &&
                   parameters.ValueKind == JsonValueKind.Array &&
                   parameters.GetArrayLength() >= 9 &&
                   parameters[8].ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private void RequestStop()
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            DefinirStatus("ERRO");
            await PararMineracaoAsync(preservarErro: true);
        });
    }

    private async Task PararMineracaoAsync(
        bool preservarErro = false)
    {
        minerando = false;
        autorizado = false;
        mineracaoPausada = false;

        idSubscribe = null;
        idAuthorize = null;

        try
        {
            cancelamento?.Cancel();

            await miner.StopAsync();

            stream?.Close();
            cliente?.Close();

            if (tarefaLeitura != null)
            {
                await tarefaLeitura;
                tarefaLeitura = null;
            }
        }
        catch (Exception ex)
        {
            AdicionarLog(
                "Erro ao encerrar a mineração: " + ex.Message);

            preservarErro = true;
        }
        finally
        {
            MiningSessionRuntime.Current.SetActive(false);

            stream = null;
            cliente = null;

            cancelamento?.Dispose();
            cancelamento = null;

            shares.ClearPending();

            StartButton.Text = T("Start");
            PauseButton.Text = T("Pause");
            PauseButton.IsVisible = false;

            if (!preservarErro)
                DefinirStatus("DESCONECTADO");

            MiningBackgroundServiceControl.Stop();

            AdicionarLog("Conexão encerrada.");
            RegistrarAtividade("Conexão encerrada.");

            MiningMonitorTotals totals = monitor.Snapshot;

            AdicionarLog(
                $"Shares acumuladas — aceitas: {totals.SharesAccepted} | " +
                $"rejeitadas: {totals.SharesRejected} | expiradas: {totals.SharesExpired}");

            AtualizarPainel();
        }
    }

    private void PauseButton_Clicked(
        object? sender,
        EventArgs e)
    {
        if (!minerando || !autorizado)
            return;

        MiningSessionRuntime.Current.TogglePause();

        RegistrarAtividade(miner.IsPaused
            ? "Mineração pausada pelo usuário."
            : "Mineração retomada pelo usuário.");
    }

    private void DefinirStatus(string estado)
    {
        StatusLabel.Text = estado switch
        {
            "DESCONECTADO" => T("StatusDisconnected"),
            "CONECTANDO" => T("StatusConnecting"),
            "CONECTADO" => T("StatusConnected"),
            "AUTORIZANDO" => T("StatusAuthorizing"),
            "AUTORIZADO" => T("StatusAuthorized"),
            "MINERANDO" => T("StatusMining"),
            "PAUSADO" => T("StatusPaused"),
            "ERRO" => T("StatusError"),
            _ => TM(estado)
        };

        MiningSessionRuntime.Current.SetStatus(estado);

        StatusLabel.TextColor = estado switch
        {
            "CONECTANDO" => Color.FromArgb("#FFD27A"),
            "CONECTADO" => Color.FromArgb("#7DD3FC"),
            "AUTORIZANDO" => Color.FromArgb("#FFD27A"),
            "AUTORIZADO" => Color.FromArgb("#82E0AA"),
            "MINERANDO" => Color.FromArgb("#82E0AA"),
            "PAUSADO" => Color.FromArgb("#FFD27A"),
            "ERRO" => Color.FromArgb("#FF7777"),
            _ => Color.FromArgb("#CBD2DC")
        };
    }

    private void SincronizarEstadoRuntime()
    {
        MiningSessionRuntime runtime =
            MiningSessionRuntime.Current;

        string status = runtime.Status;

        minerando =
            runtime.IsActive ||
            StatusRepresentaSessaoAtiva(status);

        autorizado = status is
            "AUTORIZADO" or "MINERANDO" or "PAUSADO";

        mineracaoPausada =
            runtime.IsPaused ||
            status == "PAUSADO";

        StartButton.Text = minerando
            ? T("Stop")
            : T("Start");

        PauseButton.Text = mineracaoPausada
            ? T("Resume")
            : T("Pause");

        PauseButton.IsVisible = autorizado;

        DefinirStatus(status);
    }

    private static bool StatusRepresentaSessaoAtiva(string status) =>
        status is "CONECTANDO" or "CONECTADO" or "AUTORIZANDO" or
            "AUTORIZADO" or "MINERANDO" or "PAUSADO";

    private void RegistrarInicioProcessamento(string jobId)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (autorizado && !mineracaoPausada)
                DefinirStatus("MINERANDO");

            RegistrarAtividade(
                $"Cálculo SHA256d iniciado para o job {jobId}.");
        });
    }

    private void RegistrarShareEncontrada(StratumShare share)
    {
        monitor.RecordShareFound();

        MiningSessionRuntime.Current.SessionMetrics
            .RecordShareFound();

        RegistrarAtividade(
            $"Share encontrada (job {share.JobId}, nonce {share.Nonce}).");

        AtualizarPainel();
    }

    private void AtualizarEstatisticasMotor(
        MiningStatisticsSnapshot statistics)
    {
        long previous = Interlocked.Exchange(
            ref ultimoTotalHashesSessao,
            statistics.TotalHashes);

        if (statistics.TotalHashes > previous)
            monitor.AddHashes(
                statistics.TotalHashes - previous);

        MiningSessionRuntime.Current.SessionMetrics
            .SetTotalHashes(statistics.TotalHashes);

        monitor.RecordBestDifficulty(
            statistics.BestDifficulty);

        lock (estatisticasSync)
        {
            hashrateAtual = statistics.Hashrate;
            hashrateMedio = statistics.AverageHashrate;
            uptimeMineracao = statistics.Uptime;
        }

        hashrateChartDrawable.AddSample(
            statistics.Hashrate);

        MainThread.BeginInvokeOnMainThread(
            () => HashrateChart.Invalidate());
    }

    private void BateriaAlterada(
        object? sender,
        BatteryInfoChangedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(
            AtualizarInformacoesBateria);
    }



private void AtualizarInformacoesBateria()
{
    try
    {
        ultimaInfoBateria = BatteryTemperatureService.GetBatteryInfo();
        falhaConsultaBateria = false;
        ultimaTemperaturaBateriaCelsius =
            ultimaInfoBateria?.TemperatureCelsius;
        ultimoStatusTermicoSistema =
            BatteryTemperatureService.GetSystemThermalStatus();

        AtualizarTextoBateria();
        AtualizarPainelTemperatura(
            ultimaTemperaturaBateriaCelsius,
            registrarAmostra: true);

        AplicarProtecaoTermica();
    }
    catch (Exception ex)
    {
        ultimaInfoBateria = null;
        ultimoStatusTermicoSistema = null;
        falhaConsultaBateria = true;
        ultimaTemperaturaBateriaCelsius = null;

        BatteryLabel.Text = T("BatteryUnavailable");
        AtualizarPainelTemperatura(null, registrarAmostra: false);

        MiningSessionRuntime.Current.UpdateBatteryTemperature(null);
        AplicarProtecaoTermica();

        AdicionarLog("Erro ao consultar bateria: " + ex);
    }
}

private void AtualizarTextoBateria()
{
    if (falhaConsultaBateria)
    {
        BatteryLabel.Text = T("BatteryUnavailable");
        return;
    }

    string charge = ultimaInfoBateria?.LevelPercent is int level
        ? $"{level}%"
        : "N/D";
    string state = ultimaInfoBateria?.State ?? T("BatteryStateUnavailable");

    BatteryLabel.Text = LocalizationService.Instance.Format(
        "BatteryFormat", charge, TM(state));
}



    /// <summary>
    /// Escreve no painel os limites que a proteção térmica realmente usa
    /// (ThermalProtectionService), em vez de textos fixos.
    /// </summary>
    private void AtualizarLimitesTermicosNoPainel()
    {
        double resume = ThermalProtectionService.ResumeAtCelsius;
        double throttle = ThermalProtectionService.ThrottleAtCelsius;
        double pause = ThermalProtectionService.PauseAtCelsius;

        TempMarkerMinLabel.Text = $"{TemperatureChartDrawable.ScaleMin:0.#}°";
        TempMarkerResumeLabel.Text = LocalizationService.Instance.Format(
            "TemperatureResumeFormat", resume);
        TempMarkerThrottleLabel.Text = LocalizationService.Instance.Format(
            "TemperatureThrottleFormat", throttle);
        TempMarkerPauseLabel.Text = LocalizationService.Instance.Format(
            "TemperaturePauseFormat", pause);

        TempExplainLabel.Text = LocalizationService.Instance.Format(
            "TemperatureExplainFormat", throttle, pause, resume);

        // Acima de ~45 °C a bateria de íon de lítio se desgasta bem mais rápido
        // (e não deve ser carregada); o aviso só aparece se o limite for alto.
        bool limiteAlto = pause > 48.0;
        TempLimitWarningLabel.IsVisible = limiteAlto;
        TempLimitWarningLabel.Text = limiteAlto
            ? LocalizationService.Instance.Format("TemperatureWarningFormat", pause)
            : "";
    }

    private void AtualizarPainelTemperatura(
        double? celsius,
        bool registrarAmostra = true)
    {
        Color cor = TemperatureChartDrawable.ColorFor(celsius);

        if (celsius is double t)
        {
            if (registrarAmostra)
                temperatureChartDrawable.AddSample(t);

            TempBigLabel.Text = $"{t:F1} °C";
            TempGauge.Progress = Math.Clamp(
                (t - TemperatureChartDrawable.ScaleMin) /
                (TemperatureChartDrawable.ScaleMax - TemperatureChartDrawable.ScaleMin),
                0.0, 1.0);

            string zona =
                t >= ThermalProtectionService.PauseAtCelsius ? T("ThermalPauseZone")
                : t >= ThermalProtectionService.ThrottleAtCelsius ? T("ThermalLimitingZone")
                : t >= ThermalProtectionService.ResumeAtCelsius ? T("ThermalAttentionZone")
                : T("ThermalNormalZone");

            TempZoneLabel.Text = zona;
        }
        else
        {
            TempBigLabel.Text = "-- °C";
            TempGauge.Progress = 0;
            TempZoneLabel.Text = T("NoReading");
        }

        TempBigLabel.TextColor = cor;
        TempGauge.ProgressColor = cor;
        TempZoneLabel.TextColor = cor;
        TempZoneChip.Background = new SolidColorBrush(cor.WithAlpha(0.18f));

        BatteryTempLabel.Text =
            temperatureChartDrawable.Minimum is double min &&
            temperatureChartDrawable.Maximum is double max &&
            temperatureChartDrawable.Average is double avg
                ? LocalizationService.Instance.Format("ThermalHistory", min, avg, max)
                : T("ThermalHistoryEmpty");

        SystemThermalLabel.Text = ultimoStatusTermicoSistema is null
            ? T("AndroidThermalUnavailable")
            : LocalizationService.Instance.Format(
                "AndroidThermalFormat", TM(ultimoStatusTermicoSistema));

        TempChart.Invalidate();
    }

    private void AplicarProtecaoTermica()
    {
        try
        {
            MiningSessionRuntime runtime =
                MiningSessionRuntime.Current;

            float? temperature =
                ultimaTemperaturaBateriaCelsius is double temp
                    ? (float)temp
                    : null;

            // O Runtime é a única fonte de decisão térmica.
            // A pausa térmica não remove a pausa manual.
            runtime.UpdateBatteryTemperature(temperature);

            string statusKey;
            string sourceStatus;

            if (runtime.IsThermalPaused)
            {
                statusKey = "ThermalPaused";
                sourceStatus = "PROTEÇÃO TÉRMICA: MINERAÇÃO PAUSADA";
            }
            else if (runtime.IsThermalProtectionActive)
            {
                statusKey = "ThermalLimited";
                sourceStatus = "Proteção térmica ativa: intensidade limitada";
            }
            else if (!temperature.HasValue)
            {
                statusKey = "ThermalProtectionWaiting";
                sourceStatus = "Proteção térmica aguardando leitura";
            }
            else
            {
                statusKey = "ThermalSafe";
                sourceStatus = "Temperatura segura: operação normal";
            }

            bool statusChanged = !string.Equals(
                ultimoEstadoTermico,
                sourceStatus,
                StringComparison.Ordinal);

            ultimoEstadoTermico = sourceStatus;
            estadoTermicoKey = statusKey;
            erroProtecaoTermicaOriginal = null;
            AtualizarTextoEstadoTermico();

            if (statusChanged)
                AdicionarLog(sourceStatus);
        }
        catch (Exception ex)
        {
            ultimoEstadoTermico = "Falha na proteção térmica: " + ex.Message;
            erroProtecaoTermicaOriginal = ultimoEstadoTermico;
            AtualizarTextoEstadoTermico();

            AdicionarLog(ultimoEstadoTermico);
        }
    }

    private void AtualizarTextoEstadoTermico()
    {
        DeviceTempLabel.Text = erroProtecaoTermicaOriginal is null
            ? T(estadoTermicoKey)
            : TM(erroProtecaoTermicaOriginal);
    }

    private bool AtualizarPainel()
    {
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(
                AtualizarPainelValores);

            return true;
        }

        AtualizarPainelValores();
        return true;
    }

    private void AtualizarPainelValores()
    {
        MiningMonitorTotals totals = monitor.Snapshot;

        MiningSessionSnapshot session =
            MiningSessionRuntime.Current.SessionMetrics.Snapshot;

        double currentRate;
        double averageRate;
        TimeSpan uptime;

        lock (estatisticasSync)
        {
            currentRate = hashrateAtual;
            averageRate = hashrateMedio;
            uptime = uptimeMineracao;
        }

        HashrateLabel.Text =
            FormatarHashrate(currentRate);

        AverageHashrateLabel.Text =
            FormatarHashrate(averageRate);

        BestDifficultyLabel.Text =
            FormatarDificuldade(totals.BestDifficulty);

        ValidBlocksLabel.Text = LocalizationService.Instance.Format(
            "ValidBlocksUnavailable", totals.ValidBlocks);

        TotalHashesLabel.Text =
            session.TotalHashes.ToString(
                "N0",
                CultureInfo.CurrentCulture);

        TotalMHashesLabel.Text =
            $"{session.TotalHashes / 1_000_000d:N2} MH";

        TimeLabel.Text =
            $"{(int)uptime.TotalHours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2}";

        CurrentDifficultyLabel.Text = dificuldadeAtual;

        JobsLabel.Text =
            totals.JobsReceived.ToString(
                "N0",
                CultureInfo.CurrentCulture);

        CleanJobsLabel.Text =
            totals.CleanJobsReceived.ToString(
                "N0",
                CultureInfo.CurrentCulture);

        CurrentJobLabel.Text = jobAtual;

        LastJobLabel.Text =
            ultimoJobRecebido?.ToString("HH:mm:ss") ?? "—";

        SharesFoundLabel.Text =
            session.SharesFound.ToString(
                "N0",
                CultureInfo.CurrentCulture);

        SharesSentLabel.Text =
            session.SharesSent.ToString(
                "N0",
                CultureInfo.CurrentCulture);

        SharesAcceptedLabel.Text =
            $"{session.SharesAccepted:N0} / {session.SharesRejected:N0} / {session.SharesExpired:N0}";

        PendingSharesLabel.Text =
            shares.PendingCount.ToString(
                "N0",
                CultureInfo.CurrentCulture);

        AcceptanceLabel.Text =
            MiningSessionRuntime.Current.SessionMetrics
                .AcceptancePercentage.ToString(
                    "F1",
                    CultureInfo.CurrentCulture) + "%";

        LastShareLabel.Text = TM(ultimaShareEnviada);
        LastPoolResponseLabel.Text = TM(ultimaRespostaPool);

        shareChartDrawable.SetValues(
            session.SharesAccepted,
            session.SharesRejected);

        ShareChart.Invalidate();
    }

    private static string FormatarHashrate(double valor)
    {
        if (valor >= 1_000_000_000)
            return $"{valor / 1_000_000_000:F2} GH/s";

        if (valor >= 1_000_000)
            return $"{valor / 1_000_000:F2} MH/s";

        if (valor >= 1_000)
            return $"{valor / 1_000:F2} KH/s";

        return $"{valor:F0} H/s";
    }

    private static string FormatarDificuldade(double valor)
    {
        if (double.IsPositiveInfinity(valor))
            return "∞";

        return valor.ToString(
            "N3",
            CultureInfo.CurrentCulture);
    }

    private void RegistrarAtividade(string mensagem)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss");

        MainThread.BeginInvokeOnMainThread(() =>
        {
            activitySourceHistory.Insert(0, (timestamp, mensagem));
            while (activitySourceHistory.Count > 30)
                activitySourceHistory.RemoveAt(activitySourceHistory.Count - 1);

            historicoAtividade.Insert(0, $"[{timestamp}] {TM(mensagem)}");
            while (historicoAtividade.Count > 30)
                historicoAtividade.RemoveAt(historicoAtividade.Count - 1);
        });
    }

    private void AdicionarLog(string mensagem)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss");

        MainThread.BeginInvokeOnMainThread(() =>
        {
            emptyLogKey = "WaitingStart";
            logSourceHistory.Add((timestamp, mensagem));
            while (logSourceHistory.Count > 200)
                logSourceHistory.RemoveAt(0);
            RenderLog();
        });
    }

    private void RenderLog()
    {
        if (logSourceHistory.Count == 0)
        {
            LogLabel.Text = T(emptyLogKey);
            return;
        }

        LogLabel.Text = string.Join(
            Environment.NewLine,
            logSourceHistory.Select(item =>
                $"[{item.Timestamp}] {TM(item.Message)}"));
    }

    private async void CopyLog_Clicked(
        object? sender,
        EventArgs e)
    {
        try
        {
            await Clipboard.Default.SetTextAsync(
                LogLabel.Text ?? "");

            await DisplayAlertAsync(
                "MineDigibyteMobile",
                T("LogCopied"),
                T("Ok"));
        }
        catch (Exception ex)
        {
            AdicionarLog("Não foi possível copiar o conteúdo. " + ex.GetType().Name);
            await DisplayAlertAsync(
                T("Error"),
                T("ClipboardCopyFailed"),
                T("Ok"));
        }
    }

    private void ClearLog_Clicked(
        object? sender,
        EventArgs e)
    {
        logSourceHistory.Clear();
        emptyLogKey = "MonitorCleared";
        RenderLog();
    }
}
