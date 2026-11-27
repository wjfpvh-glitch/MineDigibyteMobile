namespace MineDigibyteMobile.Services;

/// <summary>
/// Backend de hash na GPU (Vulkan ou OpenGL ES). A GPU só filtra pelos 32 bits
/// mais significativos do hash; quem chama confirma cada candidato no CPU.
/// </summary>
public interface IGpuHashBackend : IDisposable
{
    /// <summary>"Vulkan" ou "OpenGL ES".</summary>
    string ApiName { get; }

    string DeviceName { get; }

    IReadOnlyList<uint> Scan(byte[] header, uint startNonce, uint count, uint targetTop);
}
