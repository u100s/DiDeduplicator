namespace DiDeduplicator.Enums;

public enum FileStatus
{
    Discovered,
    Hashed,
    CandidateMatched,
    ByteVerified,
    MovedToSlaveQuarantine,
    MergedToMaster,
    MovedToMasterQuarantine,
    DifferentContent,
    Error,
    Skipped
}
