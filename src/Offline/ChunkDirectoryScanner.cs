namespace ESAnalyser.Offline;

public static class ChunkDirectoryScanner
{
    public static IEnumerable<string> EnumerateChunkFiles(string directory) =>
        Directory.EnumerateFiles(directory, "chunk-*", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
}
