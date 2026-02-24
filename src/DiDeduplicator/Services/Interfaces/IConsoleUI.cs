using DiDeduplicator.Models;

namespace DiDeduplicator.Services.Interfaces;

public interface IConsoleUI
{
    string PromptDirectory(string prompt);
    Task RunWithProgressAsync(string description, int totalItems, Func<Action<int, int>, Task> work);
    void WriteInfo(string message);
    void WriteWarning(string message);
    void WriteError(string message);
    void DisplaySummaryReport(SummaryReport report);
}
