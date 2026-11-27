using System.Text.Json;
using System.Linq;
namespace MineDigibyteMobile.Models;
public sealed class StratumJob
{
    public string JobId { get; init; } = "";
    public string PreviousHash { get; init; } = "";
    public string CoinbasePart1 { get; init; } = "";
    public string CoinbasePart2 { get; init; } = "";
    public List<string> MerkleBranches { get; init; } = [];
    public string Version { get; init; } = "";
    public string NBits { get; init; } = "";
    public string NTime { get; init; } = "";
    public bool CleanJobs { get; init; }

    public static StratumJob FromJson(JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Array ||
            parameters.GetArrayLength() < 9)
        {
            throw new FormatException(
                "Parâmetros mining.notify incompletos.");
        }

        var branches = parameters[4]
            .EnumerateArray()
            .Select(item => item.GetString() ?? "")
            .ToList();

        return new StratumJob
        {
            JobId = parameters[0].GetString() ?? "",
            PreviousHash = parameters[1].GetString() ?? "",
            CoinbasePart1 = parameters[2].GetString() ?? "",
            CoinbasePart2 = parameters[3].GetString() ?? "",
            MerkleBranches = branches,
            Version = parameters[5].GetString() ?? "",
            NBits = parameters[6].GetString() ?? "",
            NTime = parameters[7].GetString() ?? "",
            CleanJobs = parameters[8].GetBoolean()
        };
    }
}
