namespace DiDeduplicator.Configuration;

public class AppSettings
{
    public string MasterDirectory { get; set; } = string.Empty;
    public string SlaveDirectory { get; set; } = string.Empty;
    public int ScanThreads { get; set; } = 4;
}
