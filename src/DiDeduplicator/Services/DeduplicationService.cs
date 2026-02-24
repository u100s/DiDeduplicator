using System.Diagnostics;
using DiDeduplicator.Data;
using DiDeduplicator.Enums;
using DiDeduplicator.Models;
using DiDeduplicator.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DiDeduplicator.Services;

public class DeduplicationService : IDeduplicationService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IHashService _hashService;
    private readonly ISafeTransferService _transferService;
    private readonly ILogger<DeduplicationService> _logger;

    public DeduplicationService(
        IDbContextFactory<AppDbContext> dbFactory,
        IHashService hashService,
        ISafeTransferService transferService,
        ILogger<DeduplicationService> logger)
    {
        _dbFactory = dbFactory;
        _hashService = hashService;
        _transferService = transferService;
        _logger = logger;
    }

    public async Task<DeduplicationStatistics> DeduplicateMasterAsync(
        string masterDir,
        Action<int, int> progressCallback,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        int movedToQuarantine = 0;
        int byteMismatches = 0;
        int errors = 0;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Self-JOIN: find duplicate pairs among master files
        // OriginalId < DuplicateId (GUID stored as string — lexicographic order)
        // Both must be Hashed status and not Skipped
        var duplicatePairs = await (
            from original in db.Files
            join duplicate in db.Files
                on original.HashSha256 equals duplicate.HashSha256
            where original.Source == FileSource.Master
                  && duplicate.Source == FileSource.Master
                  && original.Status == FileStatus.Hashed
                  && duplicate.Status == FileStatus.Hashed
                  && original.FullPath != duplicate.FullPath
                  && string.Compare(original.FileId.ToString(), duplicate.FileId.ToString()) < 0
            orderby original.FullPath, duplicate.FullPath
            select new
            {
                OriginalId = original.FileId,
                OriginalPath = original.FullPath,
                DuplicateId = duplicate.FileId,
                DuplicatePath = duplicate.FullPath
            }
        ).ToListAsync(ct);

        int totalPairs = duplicatePairs.Count;
        int processed = 0;
        progressCallback(0, totalPairs);

        var quarantineDir = Path.Combine(
            Path.GetDirectoryName(masterDir.TrimEnd(Path.DirectorySeparatorChar))!,
            "master_quarantine");

        foreach (var pair in duplicatePairs)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                // Re-check original hasn't been moved
                var original = await db.Files.FirstOrDefaultAsync(
                    f => f.FileId == pair.OriginalId, ct);

                if (original == null || original.Status != FileStatus.Hashed)
                {
                    _logger.LogInformation(
                        "Skipping pair: original {OriginalPath} already processed",
                        pair.OriginalPath);
                    processed++;
                    progressCallback(processed, totalPairs);
                    continue;
                }

                // Re-check duplicate hasn't been moved
                var duplicate = await db.Files.FirstOrDefaultAsync(
                    f => f.FileId == pair.DuplicateId, ct);

                if (duplicate == null || duplicate.Status != FileStatus.Hashed)
                {
                    processed++;
                    progressCallback(processed, totalPairs);
                    continue;
                }

                if (!File.Exists(original.FullPath) || !File.Exists(duplicate.FullPath))
                {
                    _logger.LogWarning(
                        "Skipping pair: file(s) missing. Original={Original}, Duplicate={Duplicate}",
                        original.FullPath, duplicate.FullPath);
                    processed++;
                    progressCallback(processed, totalPairs);
                    continue;
                }

                // Byte-compare
                if (await _hashService.AreFilesIdenticalAsync(
                        original.FullPath, duplicate.FullPath, ct))
                {
                    var relativePath = Path.GetRelativePath(masterDir, duplicate.FullPath);
                    var result = await _transferService.TransferAsync(
                        duplicate.FileId, quarantineDir, relativePath,
                        FileStatus.MovedToMasterQuarantine, ct);

                    if (result.Success)
                        movedToQuarantine++;
                    else
                    {
                        errors++;
                        _logger.LogError(
                            "Failed to quarantine master duplicate {Path}: {Error}",
                            duplicate.FullPath, result.ErrorMessage);
                    }
                }
                else
                {
                    byteMismatches++;
                    duplicate.Status = FileStatus.DifferentContent;
                    duplicate.ByteMismatchFileId = original.FileId;
                    duplicate.ProcessedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);

                    _logger.LogWarning(
                        "Byte mismatch in master: {DuplicatePath} hash matches {OriginalPath} but content differs",
                        duplicate.FullPath, original.FullPath);
                }
            }
            catch (Exception ex)
            {
                errors++;
                _logger.LogError(ex,
                    "Error processing duplicate pair: {OriginalPath} / {DuplicatePath}",
                    pair.OriginalPath, pair.DuplicatePath);
            }

            processed++;
            progressCallback(processed, totalPairs);
        }

        sw.Stop();

        return new DeduplicationStatistics
        {
            DuplicatePairsFound = totalPairs,
            MovedToMasterQuarantine = movedToQuarantine,
            ByteMismatchWarnings = byteMismatches,
            Errors = errors,
            Duration = sw.Elapsed
        };
    }
}
