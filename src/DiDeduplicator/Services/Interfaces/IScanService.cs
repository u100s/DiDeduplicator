using DiDeduplicator.Enums;
using DiDeduplicator.Models;

namespace DiDeduplicator.Services.Interfaces;

public interface IScanService
{
    Task<ScanStatistics> ScanDirectoryAsync(
        string directory,
        FileSource source,
        int parallelism,
        Action<int, int> progressCallback,
        CancellationToken ct = default);

    Task<bool> HasExistingRecordsAsync(CancellationToken ct = default);
}
