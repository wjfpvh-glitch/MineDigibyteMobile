using System.Runtime.InteropServices;

namespace MineDigibyteMobile.Services;

/// <summary>
/// Backend de GPU via OpenGL ES 3.1 (compute shader), usado quando o aparelho
/// não tem Vulkan. Exige OpenGL ES 3.1; aparelhos só com 3.0 ficam na CPU.
/// </summary>
public sealed class GlesHashBackend : IGpuHashBackend
{
    private const int MaxCandidates = 32;

    private readonly object sync = new();
    private bool disposed;

    public string ApiName => "OpenGL ES";

    public string DeviceName { get; }

    private GlesHashBackend(string deviceName) => DeviceName = deviceName;

    /// <summary>
    /// Inicializa o OpenGL ES e roda o auto-teste (vetor de teste SHA-256d).
    /// Só devolve o backend se o resultado da GPU for idêntico ao esperado.
    /// </summary>
    public static GlesHashBackend? TryCreate(out string message)
    {
        try
        {
            int rc = GlesNative.Init();
            if (rc != 0)
            {
                message = "OpenGL ES indisponível: " + ReadString(GlesNative.LastError());
                return null;
            }

            string device = ReadString(GlesNative.DeviceName());

            rc = GlesNative.SelfTest();
            if (rc != 0)
            {
                string error = ReadString(GlesNative.LastError());
                GlesNative.Shutdown();
                message = $"Auto-teste OpenGL ES ({device}) falhou: {error}";
                return null;
            }

            message = $"GPU ativa via OpenGL ES: {device} (auto-teste OK)";
            return new GlesHashBackend(device);
        }
        catch (DllNotFoundException)
        {
            message = "libglesminer.so não está no app (veja Native/gles_miner)";
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            message = "libglesminer.so incompatível com esta versão do app";
            return null;
        }
        catch (Exception ex)
        {
            message = "Falha ao iniciar o OpenGL ES: " + ex.Message;
            return null;
        }
    }

    public IReadOnlyList<uint> Scan(byte[] header, uint startNonce, uint count, uint targetTop)
    {
        if (header.Length < 80)
            throw new ArgumentException("O cabeçalho precisa ter 80 bytes.", nameof(header));

        var found = new uint[MaxCandidates];
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            int rc = GlesNative.Scan(
                header, startNonce, count, targetTop, found, MaxCandidates, out uint n);

            if (rc != 0)
                throw new InvalidOperationException(
                    "Erro na GPU: " + ReadString(GlesNative.LastError()));

            return found.Take((int)Math.Min(n, MaxCandidates)).ToArray();
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
                return;

            disposed = true;
            try { GlesNative.Shutdown(); } catch { /* best-effort */ }
        }
    }

    private static string ReadString(IntPtr ptr) =>
        ptr == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(ptr) ?? "";
}
