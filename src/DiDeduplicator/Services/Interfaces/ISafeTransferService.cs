using DiDeduplicator.Enums;
using DiDeduplicator.Models;

namespace DiDeduplicator.Services.Interfaces;

public interface ISafeTransferService
{
    Task<TransferResult> TransferAsync(
        Guid fileId,
        string targetDir,
        string relativePath,
        FileStatus finalStatus,
        CancellationToken ct = default);
}
