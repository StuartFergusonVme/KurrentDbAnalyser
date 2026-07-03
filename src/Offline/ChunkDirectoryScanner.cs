namespace ESAnalyser.Offline;

public static class ChunkDirectoryScanner
{
    public static IEnumerable<string> EnumerateChunkFiles(string directory) =>
        Directory.EnumerateFiles(directory, "chunk-*", SearchOption.TopDirectoryOnly)
            .OrderBy(GetChunkNumber, Comparer<int>.Default)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase);

    private static int GetChunkNumber(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        if (!fileName.StartsWith("chunk-", StringComparison.OrdinalIgnoreCase))
        {
            return int.MaxValue;
        }

        var chunkNumberText = fileName["chunk-".Length..];
        return int.TryParse(chunkNumberText, out var chunkNumber) ? chunkNumber : int.MaxValue;
    }
}
