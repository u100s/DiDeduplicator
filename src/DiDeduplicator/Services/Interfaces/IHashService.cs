namespace DiDeduplicator.Services.Interfaces;

public interface IHashService
{
    Task<string> ComputeHashAsync(string filePath, CancellationToken ct = default);
    Task<bool> AreFilesIdenticalAsync(string path1, string path2, CancellationToken ct = default);
}
