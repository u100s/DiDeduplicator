using DiDeduplicator.Models;

namespace DiDeduplicator.Services.Interfaces;

public interface IComparisonService
{
    Task<ComparisonStatistics> CompareSlaveVsMasterAsync(
        string masterDir,
        string slaveDir,
        Action<int, int> progressCallback,
        CancellationToken ct = default);
}
