using System.Diagnostics;
using DiDeduplicator.Data;
using DiDeduplicator.Data.Entities;
using DiDeduplicator.Enums;
using DiDeduplicator.Models;
using DiDeduplicator.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DiDeduplicator.Services;

public class ComparisonService : IComparisonService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IHashService _hashService;
    private readonly ISafeTransferService _transferService;
    private readonly ILogger<ComparisonService> _logger;

    public ComparisonService(
        IDbContextFactory<AppDbContext> dbFactory,
        IHashService hashService,
        ISafeTransferService transferService,
        ILogger<ComparisonService> logger)
    {
        _dbFactory = dbFactory;
        _hashService = hashService;
        _transferService = transferService;
        _logger = logger;
    }

    public async Task<ComparisonStatistics> CompareSlaveVsMasterAsync(
        string masterDir,
        string slaveDir,
        Action<int, int> progressCallback,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        int duplicatesFound = 0;
        int movedToQuarantine = 0;
        int mergedToMaster = 0;
        int byteMismatches = 0;
        int errors = 0;

        // Get all slave files that need processing (status = Hashed)
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var slaveFiles = await db.Files
            .Where(f => f.Source == FileSource.Slave && f.Status == FileStatus.Hashed)
            .OrderBy(f => f.FullPath)
            .ToListAsync(ct);

        int total = slaveFiles.Count;
        int processed = 0;
        progressCallback(0, total);

        var quarantineDir = Path.Combine(
            Path.GetDirectoryName(slaveDir.TrimEnd(Path.DirectorySeparatorChar))!,
            "slave_quarantine");

        foreach (var slave in slaveFiles)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                // Find master candidates matching on FileSize + HashSha256
                var masterCandidates = await db.Files
                    .Where(f => f.Source == FileSource.Master
                                && f.Status != FileStatus.Skipped
                                && f.FileSize == slave.FileSize
                                && f.HashSha256 == slave.HashSha256)
                    .ToListAsync(ct);

                if (masterCandidates.Count > 0)
                {
                    // Case A: Match found — byte-verify
                    duplicatesFound++;
                    bool foundMatch = false;
                    FileRecord? lastCandidate = null;

                    foreach (var master in masterCandidates)
                    {
                        lastCandidate = master;
                        if (!File.Exists(slave.FullPath) || !File.Exists(master.FullPath))
                            continue;

                        if (await _hashService.AreFilesIdenticalAsync(
                                slave.FullPath, master.FullPath, ct))
                        {
                            foundMatch = true;

                            // Transfer slave to quarantine
                            var relativePath = Path.GetRelativePath(slaveDir, slave.FullPath);
                            var result = await _transferService.TransferAsync(
                                slave.FileId, quarantineDir, relativePath,
                                FileStatus.MovedToSlaveQuarantine, ct);

                            if (result.Success)
                                movedToQuarantine++;
                            else
                            {
                                errors++;
                                _logger.LogError("Failed to quarantine slave {Path}: {Error}",
                                    slave.FullPath, result.ErrorMessage);
                            }
                            break;
                        }
                    }

                    if (!foundMatch && lastCandidate != null)
                    {
                        // No byte match with any candidate
                        byteMismatches++;
                        slave.Status = FileStatus.DifferentContent;
                        slave.ByteMismatchFileId = lastCandidate.FileId;
                        slave.ProcessedAt = DateTime.UtcNow;
                        await db.SaveChangesAsync(ct);

                        _logger.LogWarning(
                            "Byte mismatch: slave {SlavePath} hash matches master {MasterPath} but content differs",
                            slave.FullPath, lastCandidate.FullPath);
                    }
                }
                else
                {
                    // Case B: No match — merge to master
                    var relativePath = Path.GetRelativePath(slaveDir, slave.FullPath);
                    var result = await _transferService.TransferAsync(
                        slave.FileId, masterDir, relativePath,
                        FileStatus.MergedToMaster, ct);

                    if (result.Success)
                    {
                        // Create new master record with actual file metadata
                        var transferredInfo = new FileInfo(result.FinalPath!);
                        var transferredHash = await _hashService.ComputeHashAsync(result.FinalPath!, ct);

                        var masterRecord = new FileRecord
                        {
                            FileId = Guid.NewGuid(),
                            Source = FileSource.Master,
                            FullPath = result.FinalPath!,
                            FileSize = transferredInfo.Length,
                            LastWriteTimeUtc = transferredInfo.LastWriteTimeUtc,
                            CreationTimeUtc = transferredInfo.CreationTimeUtc,
                            HashSha256 = transferredHash,
                            Status = FileStatus.Hashed,
                            ProcessedAt = DateTime.UtcNow
                        };

                        db.Files.Add(masterRecord);

                        // Update slave record with link to master
                        slave.MergedToMasterFileId = masterRecord.FileId;
                        await db.SaveChangesAsync(ct);

                        mergedToMaster++;
                    }
                    else
                    {
                        errors++;
                        _logger.LogError("Failed to merge slave {Path} to master: {Error}",
                            slave.FullPath, result.ErrorMessage);
                    }
                }
            }
            catch (Exception ex)
            {
                errors++;
                _logger.LogError(ex, "Error processing slave file {Path}", slave.FullPath);
            }

            processed++;
            progressCallback(processed, total);
        }

        sw.Stop();

        return new ComparisonStatistics
        {
            DuplicatesFound = duplicatesFound,
            MovedToSlaveQuarantine = movedToQuarantine,
            MergedToMaster = mergedToMaster,
            ByteMismatchWarnings = byteMismatches,
            Errors = errors,
            Duration = sw.Elapsed
        };
    }
}
