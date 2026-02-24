namespace DiDeduplicator.Models;

public record SummaryReport
{
    public ScanStatistics? Scan { get; init; }
    public ComparisonStatistics? Comparison { get; init; }
    public DeduplicationStatistics? Deduplication { get; init; }
    public CleanupStatistics? Cleanup { get; init; }
    public TimeSpan TotalDuration { get; init; }
}
