using System.Diagnostics;
using DiDeduplicator.Data;
using DiDeduplicator.Data.Entities;
using DiDeduplicator.Enums;
using DiDeduplicator.Models;
using DiDeduplicator.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DiDeduplicator.Services;

public class ScanService : IScanService
{
    private const int BatchSize = 100;

    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IHashService _hashService;
    private readonly ILogger<ScanService> _logger;

    public ScanService(
        IDbContextFactory<AppDbContext> dbFactory,
        IHashService hashService,
        ILogger<ScanService> logger)
    {
        _dbFactory = dbFactory;
        _hashService = hashService;
        _logger = logger;
    }

    public async Task<bool> HasExistingRecordsAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Files.AnyAsync(ct);
    }

    public async Task<ScanStatistics> ScanDirectoryAsync(
        string directory,
        FileSource source,
        int parallelism,
        Action<int, int> progressCallback,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        int skipped = 0;
        int processed = 0;

        // Enumerate all files
        var allFiles = new List<string>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                allFiles.Add(file);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enumerating files in {Directory}", directory);
            throw;
        }

        int totalFiles = allFiles.Count;
        progressCallback(0, totalFiles);

        // Thread-safe counters
        var batchLock = new SemaphoreSlim(1, 1);
        var pendingRecords = new List<FileRecord>();

        async Task FlushBatchAsync(List<FileRecord> records)
        {
            if (records.Count == 0) return;
            var toInsert = new List<FileRecord>(records);
            records.Clear();

            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            db.Files.AddRange(toInsert);
            await db.SaveChangesAsync(ct);
        }

        await Parallel.ForEachAsync(allFiles, new ParallelOptions
        {
            MaxDegreeOfParallelism = parallelism,
            CancellationToken = ct
        }, async (filePath, token) =>
        {
            FileRecord record;
            try
            {
                var fileInfo = new FileInfo(filePath);

                // Skip symlinks / reparse points
                if (fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    _logger.LogWarning("Skipping symlink: {Path}", filePath);
                    record = new FileRecord
                    {
                        FileId = Guid.NewGuid(),
                        Source = source,
                        FullPath = filePath,
                        FileSize = 0,
                        LastWriteTimeUtc = fileInfo.LastWriteTimeUtc,
                        CreationTimeUtc = fileInfo.CreationTimeUtc,
                        Status = FileStatus.Skipped
                    };
                    Interlocked.Increment(ref skipped);
                    await AddToBatch(record);
                    return;
                }

                // Skip 0-byte files
                if (fileInfo.Length == 0)
                {
                    _logger.LogWarning("Skipping 0-byte file: {Path}", filePath);
                    record = new FileRecord
                    {
                        FileId = Guid.NewGuid(),
                        Source = source,
                        FullPath = filePath,
                        FileSize = 0,
                        LastWriteTimeUtc = fileInfo.LastWriteTimeUtc,
                        CreationTimeUtc = fileInfo.CreationTimeUtc,
                        Status = FileStatus.Skipped
                    };
                    Interlocked.Increment(ref skipped);
                    await AddToBatch(record);
                    return;
                }

                // Compute hash
                var hash = await _hashService.ComputeHashAsync(filePath, token);

                record = new FileRecord
                {
                    FileId = Guid.NewGuid(),
                    Source = source,
                    FullPath = filePath,
                    FileSize = fileInfo.Length,
                    LastWriteTimeUtc = fileInfo.LastWriteTimeUtc,
                    CreationTimeUtc = fileInfo.CreationTimeUtc,
                    HashSha256 = hash,
                    Status = FileStatus.Hashed
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipping inaccessible file: {Path}", filePath);
                record = new FileRecord
                {
                    FileId = Guid.NewGuid(),
                    Source = source,
                    FullPath = filePath,
                    Status = FileStatus.Skipped
                };
                Interlocked.Increment(ref skipped);
            }

            await AddToBatch(record);
        });

        // Flush remaining records
        await batchLock.WaitAsync(ct);
        try
        {
            await FlushBatchAsync(pendingRecords);
        }
        finally
        {
            batchLock.Release();
        }

        sw.Stop();

        return new ScanStatistics
        {
            MasterFilesScanned = source == FileSource.Master ? totalFiles - skipped : 0,
            SlaveFilesScanned = source == FileSource.Slave ? totalFiles - skipped : 0,
            SkippedFiles = skipped,
            Duration = sw.Elapsed
        };

        async Task AddToBatch(FileRecord record)
        {
            List<FileRecord>? batchToFlush = null;

            await batchLock.WaitAsync(ct);
            try
            {
                pendingRecords.Add(record);
                int current = Interlocked.Increment(ref processed);
                progressCallback(current, totalFiles);

                if (pendingRecords.Count >= BatchSize)
                {
                    batchToFlush = new List<FileRecord>(pendingRecords);
                    pendingRecords.Clear();
                }
            }
            finally
            {
                batchLock.Release();
            }

            if (batchToFlush != null)
            {
                await using var db = await _dbFactory.CreateDbContextAsync(ct);
                db.Files.AddRange(batchToFlush);
                await db.SaveChangesAsync(ct);
            }
        }
    }
}
