
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using MineDigibyteMobile.Models;

namespace MineDigibyteMobile.Services;

public sealed class StratumClient : IAsyncDisposable
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private Task? _readerTask;

    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private int _nextId = 1;
    private int _authorizeId = -1;

    public event Action<string>? Log;
    public event Action<StratumJob>? JobReceived;
    public event Action<bool>? AuthorizationChanged;
    public event Action<double>? DifficultyChanged;

    public bool IsConnected =>
        _client?.Connected == true && _stream != null;

    public async Task StartAsync(
        string host,
        int port,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (IsConnected)
            throw new InvalidOperationException(
                "Já existe uma conexão ativa.");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);

        _client = new TcpClient();

        using var timeout = CancellationTokenSource
            .CreateLinkedTokenSource(_cts.Token);

        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        Log?.Invoke($"Conectando em {host}:{port}");

        await _client.ConnectAsync(host, port, timeout.Token);

        _stream = _client.GetStream();

        Log?.Invoke("TCP conectado com sucesso.");

        _readerTask = ReadLoopAsync(_cts.Token);

        await SendRequestAsync(
            "mining.subscribe",
            new object[] { "MineDigibyteMobile/1.0" },
            _cts.Token);

        _authorizeId = _nextId;

        await SendRequestAsync(
            "mining.authorize",
            new object[] { username, password },
            _cts.Token);
    }

    private async Task SendRequestAsync(
        string method,
        object[] parameters,
        CancellationToken token)
    {
        if (_stream == null)
            throw new InvalidOperationException(
                "Conexão não estabelecida.");

        int id = _nextId++;

        var request = new
        {
            id,
            method,
            @params = parameters
        };

        string json = JsonSerializer.Serialize(request) + "\n";

        byte[] bytes = Encoding.UTF8.GetBytes(json);

        await _sendLock.WaitAsync(token);

        try
        {
            await _stream.WriteAsync(bytes, token);
            await _stream.FlushAsync(token);
        }
        finally
        {
            _sendLock.Release();
        }

        Log?.Invoke("→ " + json.Trim());
    }

    private async Task ReadLoopAsync(CancellationToken token)
    {
        try
        {
            if (_stream == null)
                return;

            using var reader = new StreamReader(
                _stream,
                Encoding.UTF8,
                false,
                1024,
                true);

            while (!token.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(token);

                if (line == null)
                {
                    Log?.Invoke("Pool encerrou a conexão.");
                    break;
                }

                if (!string.IsNullOrWhiteSpace(line))
                    ProcessMessage(line);
            }
        }
        catch (OperationCanceledException)
        {
            Log?.Invoke("Leitura cancelada.");
        }
        catch (Exception ex)
        {
            Log?.Invoke("Erro na leitura: " + ex.Message);
        }
    }

    private void ProcessMessage(string message)
    {
        Log?.Invoke("← " + message);

        try
        {
            using JsonDocument document =
                JsonDocument.Parse(message);

            JsonElement root = document.RootElement;

            if (root.TryGetProperty("method", out var methodElement))
            {
                string method = methodElement.GetString() ?? "";

                if (!root.TryGetProperty("params", out var parameters))
                    return;

                switch (method)
                {
                    case "mining.notify":
                        StratumJob job = StratumJob.FromJson(parameters);
                        JobReceived?.Invoke(job);
                        Log?.Invoke("Novo trabalho recebido.");
                        break;

                    case "mining.set_difficulty":
                        double difficulty = parameters[0].GetDouble();
                        DifficultyChanged?.Invoke(difficulty);
                        Log?.Invoke($"Dificuldade: {difficulty}");
                        break;

                    case "mining.set_extranonce":
                        Log?.Invoke("Extranonce atualizado.");
                        break;

                    case "mining.set_version_mask":
                        Log?.Invoke("Version mask recebido.");
                        break;
                }

                return;
            }

            if (!root.TryGetProperty("id", out var idElement))
                return;

            if (idElement.ValueKind == JsonValueKind.Null)
                return;

            int responseId = idElement.GetInt32();

            if (root.TryGetProperty("error", out var error) &&
                error.ValueKind != JsonValueKind.Null)
            {
                Log?.Invoke("Erro Stratum: " + error);

                if (responseId == _authorizeId)
                    AuthorizationChanged?.Invoke(false);

                return;
            }

            if (responseId == _authorizeId &&
                root.TryGetProperty("result", out var result))
            {
                bool authorized =
                    result.ValueKind == JsonValueKind.True;

                AuthorizationChanged?.Invoke(authorized);

                Log?.Invoke(authorized
                    ? "Autorização aceita."
                    : "Autorização recusada.");
            }
        }
        catch (Exception ex)
        {
            Log?.Invoke("Erro ao interpretar mensagem: " + ex.Message);
        }
    }

    public async Task StopAsync()
    {
        if (_cts == null)
            return;

        await _cts.CancelAsync();

        _stream?.Close();
        _client?.Close();

        if (_readerTask != null)
        {
            try
            {
                await _readerTask;
            }
            catch
            {
            }
        }

        _stream = null;
        _client = null;

        _cts.Dispose();
        _cts = null;

        _readerTask = null;

        Log?.Invoke("Conexão Stratum encerrada.");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _sendLock.Dispose();
    }
}
