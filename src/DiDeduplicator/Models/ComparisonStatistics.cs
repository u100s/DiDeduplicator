namespace DiDeduplicator.Models;

public record ComparisonStatistics
{
    public int DuplicatesFound { get; init; }
    public int MovedToSlaveQuarantine { get; init; }
    public int MergedToMaster { get; init; }
    public int ByteMismatchWarnings { get; init; }
    public int Errors { get; init; }
    public TimeSpan Duration { get; init; }
}
