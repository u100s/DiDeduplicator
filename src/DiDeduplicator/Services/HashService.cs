using System.Security.Cryptography;
using DiDeduplicator.Services.Interfaces;

namespace DiDeduplicator.Services;

public class HashService : IHashService
{
    private const int BufferSize = 81_920; // 80 KB — matches .NET Stream.CopyTo default

    public async Task<string> ComputeHashAsync(string filePath, CancellationToken ct = default)
    {
        var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BufferSize];

        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.SequentialScan | FileOptions.Asynchronous);

        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer, ct)) > 0)
        {
            hash.AppendData(buffer, 0, bytesRead);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    public async Task<bool> AreFilesIdenticalAsync(string path1, string path2, CancellationToken ct = default)
    {
        var info1 = new FileInfo(path1);
        var info2 = new FileInfo(path2);

        if (info1.Length != info2.Length)
            return false;

        var buffer1 = new byte[BufferSize];
        var buffer2 = new byte[BufferSize];

        await using var stream1 = new FileStream(
            path1, FileMode.Open, FileAccess.Read, FileShare.Read,
            BufferSize, FileOptions.SequentialScan | FileOptions.Asynchronous);

        await using var stream2 = new FileStream(
            path2, FileMode.Open, FileAccess.Read, FileShare.Read,
            BufferSize, FileOptions.SequentialScan | FileOptions.Asynchronous);

        while (true)
        {
            var bytesRead1 = await ReadFullBufferAsync(stream1, buffer1, ct);
            var bytesRead2 = await ReadFullBufferAsync(stream2, buffer2, ct);

            if (bytesRead1 != bytesRead2)
                return false;

            if (bytesRead1 == 0)
                return true;

            if (!buffer1.AsSpan(0, bytesRead1).SequenceEqual(buffer2.AsSpan(0, bytesRead2)))
                return false;
        }
    }

    private static async Task<int> ReadFullBufferAsync(
        Stream stream, byte[] buffer, CancellationToken ct)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var bytesRead = await stream.ReadAsync(
                buffer.AsMemory(totalRead, buffer.Length - totalRead), ct);
            if (bytesRead == 0)
                break;
            totalRead += bytesRead;
        }
        return totalRead;
    }
}
