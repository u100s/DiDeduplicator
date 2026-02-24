using System.Diagnostics;
using DiDeduplicator.Configuration;
using DiDeduplicator.Enums;
using DiDeduplicator.Models;
using DiDeduplicator.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DiDeduplicator.Pipeline;

public class PipelineOrchestrator
{
    private readonly AppSettings _settings;
    private readonly IScanService _scanService;
    private readonly IComparisonService _comparisonService;
    private readonly IDeduplicationService _deduplicationService;
    private readonly ICleanupService _cleanupService;
    private readonly IConsoleUI _consoleUI;
    private readonly ILogger<PipelineOrchestrator> _logger;

    public PipelineOrchestrator(
        IOptions<AppSettings> settings,
        IScanService scanService,
        IComparisonService comparisonService,
        IDeduplicationService deduplicationService,
        ICleanupService cleanupService,
        IConsoleUI consoleUI,
        ILogger<PipelineOrchestrator> logger)
    {
        _settings = settings.Value;
        _scanService = scanService;
        _comparisonService = comparisonService;
        _deduplicationService = deduplicationService;
        _cleanupService = cleanupService;
        _consoleUI = consoleUI;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var totalSw = Stopwatch.StartNew();

        // Resolve directories
        var masterDir = ResolveDirectory(_settings.MasterDirectory, "Enter master directory path:");
        var slaveDir = ResolveDirectory(_settings.SlaveDirectory, "Enter slave directory path:");

        // Normalize paths
        masterDir = Path.GetFullPath(masterDir);
        slaveDir = Path.GetFullPath(slaveDir);

        // Validate not nested
        if (IsSubdirectory(masterDir, slaveDir) || IsSubdirectory(slaveDir, masterDir))
        {
            _consoleUI.WriteError("Master and slave directories cannot be nested within each other.");
            _logger.LogError(
                "Nested directories detected: Master={Master}, Slave={Slave}",
                masterDir, slaveDir);
            return;
        }

        _consoleUI.WriteInfo($"Master: {masterDir}");
        _consoleUI.WriteInfo($"Slave: {slaveDir}");

        // Phase 1: Scanning
        ScanStatistics? scanStats = null;
        if (!await _scanService.HasExistingRecordsAsync(ct))
        {
            _consoleUI.WriteInfo("Phase 1: Scanning directories...");
            _logger.LogInformation("Phase 1: Scanning started");

            var masterTask = ScanWithProgress("Scanning master", masterDir, FileSource.Master, ct);
            var slaveTask = ScanWithProgress("Scanning slave", slaveDir, FileSource.Slave, ct);

            var results = await Task.WhenAll(masterTask, slaveTask);

            scanStats = new ScanStatistics
            {
                MasterFilesScanned = results[0].MasterFilesScanned,
                SlaveFilesScanned = results[1].SlaveFilesScanned,
                SkippedFiles = results[0].SkippedFiles + results[1].SkippedFiles,
                Duration = results[0].Duration > results[1].Duration
                    ? results[0].Duration
                    : results[1].Duration
            };

            _consoleUI.WriteInfo("Phase 1: Scanning complete.");
            _logger.LogInformation("Phase 1: Scanning completed");
        }
        else
        {
            _consoleUI.WriteInfo("Phase 1: Skipped (existing records found in database).");
            _logger.LogInformation("Phase 1: Skipped — existing records found");
        }

        // Phase 2: Comparison
        _consoleUI.WriteInfo("Phase 2: Comparing slave vs master...");
        _logger.LogInformation("Phase 2: Comparison started");

        ComparisonStatistics? compStats = null;
        await _consoleUI.RunWithProgressAsync("Comparing slave vs master", 0, async progress =>
        {
            compStats = await _comparisonService.CompareSlaveVsMasterAsync(
                masterDir, slaveDir, progress, ct);
        });

        _consoleUI.WriteInfo("Phase 2: Comparison complete.");
        _logger.LogInformation("Phase 2: Comparison completed");

        // Phase 3: Deduplication
        DeduplicationStatistics? dedupStats = null;
        if (_settings.EnableMasterDeduplication)
        {
            _consoleUI.WriteInfo("Phase 3: Deduplicating master...");
            _logger.LogInformation("Phase 3: Deduplication started");

            await _consoleUI.RunWithProgressAsync("Deduplicating master", 0, async progress =>
            {
                dedupStats = await _deduplicationService.DeduplicateMasterAsync(
                    masterDir, progress, ct);
            });

            _consoleUI.WriteInfo("Phase 3: Deduplication complete.");
            _logger.LogInformation("Phase 3: Deduplication completed");
        }
        else
        {
            _consoleUI.WriteInfo("Phase 3: Skipped (master deduplication disabled in settings).");
            _logger.LogInformation("Phase 3: Skipped — master deduplication disabled");
        }

        // Phase 4: Cleanup
        _consoleUI.WriteInfo("Phase 4: Cleaning up empty directories...");
        _logger.LogInformation("Phase 4: Cleanup started");

        var cleanupStats = await _cleanupService.RemoveEmptyDirectoriesAsync(slaveDir, ct);

        _consoleUI.WriteInfo("Phase 4: Cleanup complete.");
        _logger.LogInformation("Phase 4: Cleanup completed");

        totalSw.Stop();

        // Summary report
        var report = new SummaryReport
        {
            Scan = scanStats,
            Comparison = compStats,
            Deduplication = dedupStats,
            Cleanup = cleanupStats,
            TotalDuration = totalSw.Elapsed
        };

        _consoleUI.DisplaySummaryReport(report);
        _logger.LogInformation("Pipeline completed in {Duration}", totalSw.Elapsed);
    }

    private string ResolveDirectory(string configValue, string prompt)
    {
        if (!string.IsNullOrWhiteSpace(configValue) && Directory.Exists(configValue))
            return configValue;

        return _consoleUI.PromptDirectory(prompt);
    }

    private async Task<ScanStatistics> ScanWithProgress(
        string description, string directory, FileSource source, CancellationToken ct)
    {
        ScanStatistics? stats = null;
        await _consoleUI.RunWithProgressAsync(description, 0, async progress =>
        {
            stats = await _scanService.ScanDirectoryAsync(
                directory, source, _settings.ScanThreads, progress, ct);
        });
        return stats!;
    }

    private static bool IsSubdirectory(string parentPath, string childPath)
    {
        var parent = Path.GetFullPath(parentPath).TrimEnd(Path.DirectorySeparatorChar)
                     + Path.DirectorySeparatorChar;
        var child = Path.GetFullPath(childPath).TrimEnd(Path.DirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
        return child.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
    }
}
