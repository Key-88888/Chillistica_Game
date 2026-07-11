namespace Chillistica_game.Service;

public sealed class AppStrategyCandidate
{
    public string StrategyId { get; init; } =
        string.Empty;

    public string Description { get; init; } =
        string.Empty;

    public string ArgumentsFragment { get; init; } =
        string.Empty;

    // Process-wide --wf-tcp/--wf-udp WinDivert filter ports; composer unions these across all selected strategies (not per --new chain).
    public string TcpPorts { get; init; } =
        string.Empty;

    public string UdpPorts { get; init; } =
        string.Empty;

    public bool UsesUdp { get; init; }

    public string Confidence { get; init; } =
        "BestEffort";

    public IReadOnlyList<EngineFileHash> FileHashes { get; init; } =
        Array.Empty<EngineFileHash>();
}
