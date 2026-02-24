namespace DiDeduplicator.Models;

public record DeduplicationStatistics
{
    public int DuplicatePairsFound { get; init; }
    public int MovedToMasterQuarantine { get; init; }
    public int ByteMismatchWarnings { get; init; }
    public int Errors { get; init; }
    public TimeSpan Duration { get; init; }
}
