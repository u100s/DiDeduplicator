namespace DiDeduplicator.Models;

public record TransferResult
{
    public bool Success { get; init; }
    public string? FinalPath { get; init; }
    public string? ErrorMessage { get; init; }
}
