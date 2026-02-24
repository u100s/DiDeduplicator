using DiDeduplicator.Models;

namespace DiDeduplicator.Services.Interfaces;

public interface IDeduplicationService
{
    Task<DeduplicationStatistics> DeduplicateMasterAsync(
        string masterDir,
        Action<int, int> progressCallback,
        CancellationToken ct = default);
}
