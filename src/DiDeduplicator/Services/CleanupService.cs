using DiDeduplicator.Models;
using DiDeduplicator.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace DiDeduplicator.Services;

public class CleanupService : ICleanupService
{
    private readonly ILogger<CleanupService> _logger;

    public CleanupService(ILogger<CleanupService> logger)
    {
        _logger = logger;
    }

    public Task<CleanupStatistics> RemoveEmptyDirectoriesAsync(
        string slaveDir, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int removed = 0;

        RemoveEmptyDirsRecursive(slaveDir, slaveDir, ref removed);

        sw.Stop();
        return Task.FromResult(new CleanupStatistics
        {
            EmptyDirectoriesRemoved = removed,
            Duration = sw.Elapsed
        });
    }

    private void RemoveEmptyDirsRecursive(string rootDir, string currentDir, ref int removed)
    {
        try
        {
            foreach (var subDir in Directory.GetDirectories(currentDir))
            {
                RemoveEmptyDirsRecursive(rootDir, subDir, ref removed);
            }

            // Never remove the root slave directory itself
            if (string.Equals(Path.GetFullPath(currentDir), Path.GetFullPath(rootDir),
                    StringComparison.OrdinalIgnoreCase))
                return;

            if (!Directory.EnumerateFileSystemEntries(currentDir).Any())
            {
                Directory.Delete(currentDir);
                removed++;
                _logger.LogInformation("Removed empty directory: {Directory}", currentDir);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process directory {Directory}", currentDir);
        }
    }
}
