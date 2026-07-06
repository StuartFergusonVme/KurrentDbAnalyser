using System.Globalization;

namespace ESAnalyser.Offline;

internal static class ChunkProgressFormatter
{
    public static string FormatDiscoveryMessage(string dataPath, int totalChunkFiles)
    {
        return totalChunkFiles == 0
            ? $"No chunk files found in '{dataPath}'."
            : $"Found {totalChunkFiles.ToString("N0", CultureInfo.InvariantCulture)} chunk files in '{dataPath}'.";
    }

    public static string FormatProgressMessage(
        int completedChunkFiles,
        int totalChunkFiles,
        TimeSpan elapsed,
        string currentChunkFile,
        DateTime startedAtUtc,
        DateTime completedAtUtc)
    {
        var elapsedSeconds = Math.Max(elapsed.TotalSeconds, 0.001);
        var chunkFilesPerSecond = completedChunkFiles / elapsedSeconds;
        var percentComplete = totalChunkFiles <= 0 ? 100d : completedChunkFiles * 100d / totalChunkFiles;
        var duration = completedAtUtc >= startedAtUtc ? completedAtUtc - startedAtUtc : TimeSpan.Zero;

        return
            $"Chunk trace: {completedChunkFiles.ToString("N0", CultureInfo.InvariantCulture)}/{totalChunkFiles.ToString("N0", CultureInfo.InvariantCulture)} " +
            $"({percentComplete.ToString("0.0", CultureInfo.InvariantCulture)}%) | " +
            $"{chunkFilesPerSecond.ToString("0.0", CultureInfo.InvariantCulture)} chunk files/sec | {currentChunkFile} | " +
            $"start={startedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)} | " +
            $"end={completedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)} | " +
            $"duration={duration.ToString("c", CultureInfo.InvariantCulture)}";
    }
}
