using DiDeduplicator.Models;

namespace DiDeduplicator.Services.Interfaces;

public interface ICleanupService
{
    Task<CleanupStatistics> RemoveEmptyDirectoriesAsync(string slaveDir, CancellationToken ct = default);
}
