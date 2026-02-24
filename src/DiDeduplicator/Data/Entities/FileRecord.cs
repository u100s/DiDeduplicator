using DiDeduplicator.Enums;

namespace DiDeduplicator.Data.Entities;

public class FileRecord
{
    public Guid FileId { get; set; }
    public FileSource Source { get; set; }
    public string FullPath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime LastWriteTimeUtc { get; set; }
    public DateTime CreationTimeUtc { get; set; }
    public string HashSha256 { get; set; } = string.Empty;
    public FileStatus Status { get; set; }
    public string? QuarantinePath { get; set; }
    public Guid? MergedToMasterFileId { get; set; }
    public Guid? ByteMismatchFileId { get; set; }
    public DateTime? ProcessedAt { get; set; }
}
