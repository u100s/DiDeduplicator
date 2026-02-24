using DiDeduplicator.Models;
using DiDeduplicator.Services.Interfaces;
using Spectre.Console;

namespace DiDeduplicator.Services;

public class ConsoleUI : IConsoleUI
{
    public string PromptDirectory(string prompt)
    {
        return AnsiConsole.Prompt(
            new TextPrompt<string>(prompt)
                .Validate(path =>
                    Directory.Exists(path)
                        ? ValidationResult.Success()
                        : ValidationResult.Error("[red]Directory does not exist[/]")));
    }

    public async Task RunWithProgressAsync(
        string description,
        int totalItems,
        Func<Action<int, int>, Task> work)
    {
        await AnsiConsole.Progress()
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn())
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask(description, maxValue: totalItems);

                void ProgressCallback(int processed, int total)
                {
                    task.MaxValue = total;
                    task.Value = processed;
                }

                await work(ProgressCallback);

                task.Value = task.MaxValue;
            });
    }

    public void WriteInfo(string message)
    {
        AnsiConsole.MarkupLine($"[blue]INFO:[/] {Markup.Escape(message)}");
    }

    public void WriteWarning(string message)
    {
        AnsiConsole.MarkupLine($"[yellow]WARNING:[/] {Markup.Escape(message)}");
    }

    public void WriteError(string message)
    {
        AnsiConsole.MarkupLine($"[red]ERROR:[/] {Markup.Escape(message)}");
    }

    public void DisplaySummaryReport(SummaryReport report)
    {
        var table = new Table()
            .Border(TableBorder.Double)
            .Title("[bold]DiDeduplicator — Summary Report[/]")
            .AddColumn(new TableColumn("[bold]Metric[/]").LeftAligned())
            .AddColumn(new TableColumn("[bold]Value[/]").RightAligned());

        if (report.Scan is { } scan)
        {
            table.AddRow("[bold]Phase 1: Scanning[/]", "");
            table.AddRow("  Master files scanned", scan.MasterFilesScanned.ToString("N0"));
            table.AddRow("  Slave files scanned", scan.SlaveFilesScanned.ToString("N0"));
            table.AddRow("  Skipped (symlinks/empty)", scan.SkippedFiles.ToString("N0"));
            table.AddRow("  Scan duration", FormatDuration(scan.Duration));
            table.AddEmptyRow();
        }

        if (report.Comparison is { } comp)
        {
            table.AddRow("[bold]Phase 2: Slave vs Master comparison[/]", "");
            table.AddRow("  Duplicates found (slave)", comp.DuplicatesFound.ToString("N0"));
            table.AddRow("  Moved to slave_quarantine", comp.MovedToSlaveQuarantine.ToString("N0"));
            table.AddRow("  Merged to master", comp.MergedToMaster.ToString("N0"));
            table.AddRow("  Byte mismatch warnings", comp.ByteMismatchWarnings.ToString("N0"));
            table.AddRow("  Errors", comp.Errors.ToString("N0"));
            table.AddRow("  Phase duration", FormatDuration(comp.Duration));
            table.AddEmptyRow();
        }

        if (report.Deduplication is { } dedup)
        {
            table.AddRow("[bold]Phase 3: Master deduplication[/]", "");
            table.AddRow("  Duplicate pairs found", dedup.DuplicatePairsFound.ToString("N0"));
            table.AddRow("  Moved to master_quarantine", dedup.MovedToMasterQuarantine.ToString("N0"));
            table.AddRow("  Byte mismatch warnings", dedup.ByteMismatchWarnings.ToString("N0"));
            table.AddRow("  Errors", dedup.Errors.ToString("N0"));
            table.AddRow("  Phase duration", FormatDuration(dedup.Duration));
            table.AddEmptyRow();
        }

        if (report.Cleanup is { } cleanup)
        {
            table.AddRow("[bold]Phase 4: Cleanup[/]", "");
            table.AddRow("  Empty directories removed", cleanup.EmptyDirectoriesRemoved.ToString("N0"));
            table.AddRow("  Phase duration", FormatDuration(cleanup.Duration));
            table.AddEmptyRow();
        }

        table.AddRow("[bold]Total duration[/]", $"[bold]{FormatDuration(report.TotalDuration)}[/]");

        AnsiConsole.WriteLine();
        AnsiConsole.Write(table);
    }

    private static string FormatDuration(TimeSpan ts)
        => ts.ToString(@"hh\:mm\:ss");
}
