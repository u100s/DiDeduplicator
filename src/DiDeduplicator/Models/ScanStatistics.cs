namespace DiDeduplicator.Models;

public record ScanStatistics
{
    public int MasterFilesScanned { get; init; }
    public int SlaveFilesScanned { get; init; }
    public int SkippedFiles { get; init; }
    public TimeSpan Duration { get; init; }
}
