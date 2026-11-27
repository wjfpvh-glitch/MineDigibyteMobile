using System.Runtime.InteropServices;

namespace MineDigibyteMobile.Services;

/// <summary>
/// Varre lotes de nonces na GPU via Vulkan Compute. A GPU só aplica um filtro
/// pelos 32 bits mais significativos do hash; quem chama deve confirmar cada
/// candidato com o alvo completo (o StratumMiner já faz isso no CPU).
/// </summary>
public sealed class VulkanHashBackend : IGpuHashBackend
{
    private const int MaxCandidates = 32;

    private readonly object sync = new();
    private bool disposed;

    public string ApiName => "Vulkan";

    public string DeviceName { get; }

    private VulkanHashBackend(string deviceName) => DeviceName = deviceName;

    /// <summary>
    /// Inicializa o Vulkan e roda o auto-teste (vetor de teste SHA-256d) na GPU.
    /// Só devolve o backend se o resultado da GPU for idêntico ao esperado.
    /// </summary>
    public static VulkanHashBackend? TryCreate(out string message)
    {
        try
        {
            int rc = VulkanNative.Init();
            if (rc != 0)
            {
                message = "Vulkan indisponível: " + ReadString(VulkanNative.LastError());
                return null;
            }

            string device = ReadString(VulkanNative.DeviceName());

            rc = VulkanNative.SelfTest();
            if (rc != 0)
            {
                string error = ReadString(VulkanNative.LastError());
                VulkanNative.Shutdown();
                message = $"Auto-teste Vulkan ({device}) falhou: {error}";
                return null;
            }

            message = $"GPU ativa via Vulkan: {device} (auto-teste OK)";
            return new VulkanHashBackend(device);
        }
        catch (DllNotFoundException)
        {
            message = "libvulkanminer.so não está no app (veja Native/vulkan_miner)";
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            message = "libvulkanminer.so incompatível com esta versão do app";
            return null;
        }
        catch (Exception ex)
        {
            message = "Falha ao iniciar o Vulkan: " + ex.Message;
            return null;
        }
    }

    /// <summary>
    /// Varre <paramref name="count"/> nonces a partir de <paramref name="startNonce"/>.
    /// Devolve os candidatos cujos 32 bits de topo do hash são &lt;= targetTop.
    /// </summary>
    public IReadOnlyList<uint> Scan(byte[] header, uint startNonce, uint count, uint targetTop)
    {
        if (header.Length < 80)
            throw new ArgumentException("O cabeçalho precisa ter 80 bytes.", nameof(header));

        var found = new uint[MaxCandidates];
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            int rc = VulkanNative.Scan(
                header, startNonce, count, targetTop, found, MaxCandidates, out uint n);

            if (rc != 0)
                throw new InvalidOperationException(
                    "Erro na GPU: " + ReadString(VulkanNative.LastError()));

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
            try { VulkanNative.Shutdown(); } catch { /* best-effort */ }
        }
    }

    private static string ReadString(IntPtr ptr) =>
        ptr == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(ptr) ?? "";
}
