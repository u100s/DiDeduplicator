namespace DiDeduplicator.Models;

public record CleanupStatistics
{
    public int EmptyDirectoriesRemoved { get; init; }
    public TimeSpan Duration { get; init; }
}
