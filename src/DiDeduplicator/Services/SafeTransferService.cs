using DiDeduplicator.Data;
using DiDeduplicator.Enums;
using DiDeduplicator.Models;
using DiDeduplicator.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DiDeduplicator.Services;

public class SafeTransferService : ISafeTransferService
{
    private const int BufferSize = 81_920;

    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IHashService _hashService;
    private readonly ILogger<SafeTransferService> _logger;

    public SafeTransferService(
        IDbContextFactory<AppDbContext> dbFactory,
        IHashService hashService,
        ILogger<SafeTransferService> logger)
    {
        _dbFactory = dbFactory;
        _hashService = hashService;
        _logger = logger;
    }

    public async Task<TransferResult> TransferAsync(
        Guid fileId,
        string targetDir,
        string relativePath,
        FileStatus finalStatus,
        CancellationToken ct = default)
    {
        string? tempFilePath = null;

        try
        {
            // Step 1: Re-read record from DB — verify not already transferred
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var record = await db.Files.FirstOrDefaultAsync(f => f.FileId == fileId, ct);

            if (record == null)
                return Fail("File record not found in database");

            if (record.Status is FileStatus.MovedToSlaveQuarantine
                or FileStatus.MergedToMaster
                or FileStatus.MovedToMasterQuarantine
                or FileStatus.Error)
            {
                return Fail($"File already processed with status {record.Status}");
            }

            var sourcePath = record.FullPath;

            if (!File.Exists(sourcePath))
            {
                record.Status = FileStatus.Error;
                record.ProcessedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                return Fail($"Source file does not exist: {sourcePath}");
            }

            // Step 2: Verify metadata matches DB
            var fileInfo = new FileInfo(sourcePath);
            bool metadataChanged = fileInfo.Length != record.FileSize
                                   || fileInfo.LastWriteTimeUtc != record.LastWriteTimeUtc;

            // Step 3: If metadata differs, rehash and verify
            if (metadataChanged)
            {
                _logger.LogWarning(
                    "File metadata changed for {Path}, rehashing", sourcePath);

                var currentHash = await _hashService.ComputeHashAsync(sourcePath, ct);
                if (currentHash != record.HashSha256)
                {
                    _logger.LogError(
                        "Hash mismatch after metadata change for {Path}: DB={DbHash}, Current={CurrentHash}",
                        sourcePath, record.HashSha256, currentHash);

                    record.Status = FileStatus.Error;
                    record.ProcessedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                    return Fail($"File hash changed: {sourcePath}");
                }
            }

            // Step 4: Metadata matches — trust DB hash, continue transfer

            // Step 5: Create target directory
            var targetFilePath = Path.Combine(targetDir, relativePath);
            var targetDirPath = Path.GetDirectoryName(targetFilePath)!;
            Directory.CreateDirectory(targetDirPath);

            // Step 6: Copy to temp file
            tempFilePath = Path.Combine(targetDirPath, $".tmp_{Guid.NewGuid()}");
            await using (var sourceStream = new FileStream(
                             sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                             BufferSize, FileOptions.SequentialScan | FileOptions.Asynchronous))
            await using (var targetStream = new FileStream(
                             tempFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             BufferSize, FileOptions.Asynchronous))
            {
                await sourceStream.CopyToAsync(targetStream, BufferSize, ct);
            }

            // Step 7: Atomic rename with conflict resolution
            var finalPath = ResolveConflict(targetFilePath);
            File.Move(tempFilePath, finalPath);
            tempFilePath = null; // Successfully moved, no cleanup needed

            // Step 8: Update DB
            record.Status = finalStatus;
            record.QuarantinePath = finalPath;
            record.ProcessedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            // Step 9: Delete source file
            File.Delete(sourcePath);

            _logger.LogInformation(
                "Transferred {Source} -> {Target} (status: {Status})",
                sourcePath, finalPath, finalStatus);

            return new TransferResult { Success = true, FinalPath = finalPath };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transfer failed for file {FileId}", fileId);

            // Step 10: Cleanup temp file on error
            if (tempFilePath != null)
            {
                try { File.Delete(tempFilePath); }
                catch (Exception cleanupEx)
                {
                    _logger.LogWarning(cleanupEx, "Failed to cleanup temp file {TempPath}", tempFilePath);
                }
            }

            // Mark as error in DB
            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync(ct);
                var record = await db.Files.FirstOrDefaultAsync(f => f.FileId == fileId, ct);
                if (record != null && record.Status != FileStatus.Error)
                {
                    record.Status = FileStatus.Error;
                    record.ProcessedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                }
            }
            catch (Exception dbEx)
            {
                _logger.LogError(dbEx, "Failed to update error status for file {FileId}", fileId);
            }

            return Fail(ex.Message);
        }
    }

    private static string ResolveConflict(string targetPath)
    {
        if (!File.Exists(targetPath))
            return targetPath;

        var dir = Path.GetDirectoryName(targetPath)!;
        var nameWithoutExt = Path.GetFileNameWithoutExtension(targetPath);
        var ext = Path.GetExtension(targetPath);

        for (int i = 1; ; i++)
        {
            var candidate = Path.Combine(dir, $"{nameWithoutExt}_{i}{ext}");
            if (!File.Exists(candidate))
                return candidate;
        }
    }

    private static TransferResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}
