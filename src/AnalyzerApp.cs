using ESAnalyser.Analysis;
using ESAnalyser.Live;
using ESAnalyser.Offline;
using ESAnalyser.Output;
using System.Globalization;

namespace ESAnalyser;

public static class AnalyzerApp
{
    private static readonly object ConsoleStatusLock = new();

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintUsage();
            return 0;
        }

        var command = args[0].ToLowerInvariant();
        var options = ParseOptions(args.Skip(1));
        var outputOptions = BuildOutputOptions(options);

        switch (command)
        {
            case "offline":
            {
                if (!options.TryGetValue("path", out var path) || string.IsNullOrWhiteSpace(path))
                {
                    Console.Error.WriteLine("Missing required --path for offline mode.");
                    return 1;
                }

                var configuration = AppConfiguration.Load();
                var maxConcurrentChunkFiles = AppConfiguration.GetIntValue(configuration, "OfflineReport:MaxConcurrentChunkFiles", 1);

                await WriteOfflineReportAsync(path, Console.Out, CancellationToken.None, outputOptions, maxConcurrentChunkFiles, Console.Error);
                return 0;
            }

            case "live":
            {
                if (!TryBuildConnectionString(options, out var connectionString, out var error))
                {
                    Console.Error.WriteLine(error);
                    return 1;
                }

                await WriteLiveReportAsync(connectionString, Console.Out, CancellationToken.None, outputOptions);
                return 0;
            }

            case "selftest":
            {
                SelfTest.Run();
                return 0;
            }

            default:
                Console.Error.WriteLine($"Unknown command '{args[0]}'.");
                PrintUsage();
                return 1;
        }
    }

    public static ReportEnvelope AnalyzeOffline(string dataPath) =>
        AnalyzeOffline(dataPath, 1, CancellationToken.None);

    public static ReportEnvelope AnalyzeOffline(string dataPath, int maxConcurrentChunkFiles) =>
        AnalyzeOffline(dataPath, maxConcurrentChunkFiles, CancellationToken.None);

    public static ReportEnvelope AnalyzeOffline(string dataPath, int maxConcurrentChunkFiles, CancellationToken cancellationToken, Action<ChunkAnalysisResult>? chunkCompleted = null, TextWriter? statusWriter = null)
    {
        var generatedAtUtc = DateTime.UtcNow;
        var chunkFiles = ChunkDirectoryScanner.EnumerateChunkFiles(dataPath).ToArray();
        WriteChunkDiscoveryStatus(statusWriter, dataPath, chunkFiles.Length);

        if (chunkFiles.Length == 0)
        {
            return CreateReport("offline", Array.Empty<EventGroup>(), null, null, 0, generatedAtUtc);
        }

        var progressTimer = System.Diagnostics.Stopwatch.StartNew();
        var completedChunkCount = 0;
        Action<ChunkAnalysisResult>? chunkCompletedWithProgress = chunkResult =>
        {
            chunkCompleted?.Invoke(chunkResult);
            if (statusWriter is not null)
            {
                var completed = Interlocked.Increment(ref completedChunkCount);
                WriteChunkProgressStatus(
                    statusWriter,
                    completed,
                    chunkFiles.Length,
                    progressTimer.Elapsed,
                    chunkResult.ChunkFileSummary.ChunkFile,
                    chunkResult.StartedAtUtc,
                    chunkResult.CompletedAtUtc);
            }
        };

        var chunkResults = AnalyzeChunkFiles(chunkFiles, maxConcurrentChunkFiles, cancellationToken, chunkCompletedWithProgress);
        var aggregator = new EventAggregator();
        string? currentChunk = null;
        DateTime? lastEventTimestampUtc = null;
        long totalEmptySpaceBytes = 0;

        foreach (var result in chunkResults)
        {
            currentChunk = result.ChunkFileSummary.ChunkFile;
            totalEmptySpaceBytes += result.ChunkFileSummary.EmptySpaceBytes;

            if (result.LastEventTimestampUtc.HasValue)
            {
                lastEventTimestampUtc = lastEventTimestampUtc.HasValue
                    ? (lastEventTimestampUtc.Value > result.LastEventTimestampUtc.Value ? lastEventTimestampUtc : result.LastEventTimestampUtc)
                    : result.LastEventTimestampUtc;
            }

            foreach (var group in result.Groups)
            {
                aggregator.AddGroup(group);
            }
        }

        return CreateReport("offline", aggregator.Snapshot(), currentChunk, lastEventTimestampUtc, totalEmptySpaceBytes, generatedAtUtc);
    }

    public static async Task<ReportEnvelope> AnalyzeLiveAsync(string connectionString, CancellationToken cancellationToken)
    {
        var generatedAtUtc = DateTime.UtcNow;
        var records = await LiveDataReader.ReadAsync(connectionString, cancellationToken);
        return BuildReport("live", records, generatedAtUtc);
    }

    public static async Task WriteOfflineReportAsync(string dataPath, TextWriter writer, CancellationToken cancellationToken)
    {
        await WriteOfflineReportAsync(dataPath, writer, cancellationToken, new ReportOutputOptions(false, true, false), 1);
    }

    public static async Task WriteOfflineReportAsync(string dataPath, TextWriter writer, CancellationToken cancellationToken, ReportOutputOptions outputOptions)
    {
        await WriteOfflineReportAsync(dataPath, writer, cancellationToken, outputOptions, 1);
    }

    public static async Task WriteOfflineReportAsync(string dataPath, TextWriter writer, CancellationToken cancellationToken, ReportOutputOptions outputOptions, int maxConcurrentChunkFiles, TextWriter? statusWriter = null)
    {
        var report = AnalyzeOffline(dataPath, maxConcurrentChunkFiles, cancellationToken, null, statusWriter);
        await JsonReportWriter.WriteAsync(report, writer, cancellationToken, outputOptions);
    }

    public static async Task WriteOfflineReportAsync(string dataPath, string outputPath, CancellationToken cancellationToken, ReportOutputOptions? outputOptions = null)
    {
        await WriteOfflineReportAsync(dataPath, outputPath, cancellationToken, outputOptions, 1);
    }

    public static async Task WriteOfflineReportAsync(string dataPath, string outputPath, CancellationToken cancellationToken, ReportOutputOptions? outputOptions, int maxConcurrentChunkFiles, TextWriter? statusWriter = null)
    {
        var actualOutputOptions = outputOptions ?? new ReportOutputOptions(false, true, false);
        var statePath = OfflineReportStateStore.GetStatePath(outputPath);
        var report = AnalyzeOffline(
            dataPath,
            maxConcurrentChunkFiles,
            cancellationToken,
            chunkResult => OfflineReportStateStore.MergeChunkAndPublish(statePath, outputPath, chunkResult, cancellationToken, actualOutputOptions),
            statusWriter);
        var finalReport = OfflineReportStateStore.LoadSnapshot(statePath) ?? report;
        await JsonReportWriter.WriteAsync(finalReport, outputPath, cancellationToken, actualOutputOptions);
    }

    internal static ChunkAnalysisResult[] AnalyzeChunkFiles(
        string[] chunkFiles,
        int maxConcurrentChunkFiles,
        CancellationToken cancellationToken,
        Action<ChunkAnalysisResult>? chunkCompleted = null,
        Func<string, CancellationToken, ChunkAnalysisResult>? analyzeChunk = null)
    {
        maxConcurrentChunkFiles = Math.Max(1, maxConcurrentChunkFiles);
        var results = new ChunkAnalysisResult[chunkFiles.Length];
        var completedResults = new ChunkAnalysisResult?[chunkFiles.Length];
        var analyzeChunkCore = analyzeChunk ?? AnalyzeChunkWithCancellation;

        if (chunkFiles.Length == 0)
        {
            return results;
        }

        if (maxConcurrentChunkFiles == 1 || chunkFiles.Length == 1)
        {
            for (var i = 0; i < chunkFiles.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                results[i] = analyzeChunkCore(chunkFiles[i], cancellationToken);
                chunkCompleted?.Invoke(results[i]);
            }

            return results;
        }

        // Analyze each chunk independently, then emit completions in the original file order.
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxConcurrentChunkFiles,
            CancellationToken = cancellationToken
        };
        var nextCompletedIndex = 0;
        var completionLock = new object();

        Parallel.ForEach(Enumerable.Range(0, chunkFiles.Length), parallelOptions, index =>
        {
            var result = analyzeChunkCore(chunkFiles[index], cancellationToken);

            // Preserve the original chunk-file order for observable completions, even though
            // the analysis itself still runs in parallel.
            lock (completionLock)
            {
                results[index] = result;
                completedResults[index] = result;

                while (nextCompletedIndex < completedResults.Length && completedResults[nextCompletedIndex] is not null)
                {
                    chunkCompleted?.Invoke(completedResults[nextCompletedIndex]!);
                    nextCompletedIndex++;
                }
            }
        });

        return results;
    }

    private static ChunkAnalysisResult AnalyzeChunkWithCancellation(string chunkFile, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return OfflineDataReader.AnalyzeChunk(chunkFile);
    }

    private static void WriteChunkDiscoveryStatus(TextWriter? statusWriter, string dataPath, int totalChunkFiles)
    {
        if (statusWriter is null)
        {
            return;
        }

        WriteStatusLine(statusWriter, ChunkProgressFormatter.FormatDiscoveryMessage(dataPath, totalChunkFiles));
    }

    private static void WriteChunkProgressStatus(
        TextWriter? statusWriter,
        int completedChunkFiles,
        int totalChunkFiles,
        TimeSpan elapsed,
        string currentChunkFile,
        DateTime startedAtUtc,
        DateTime completedAtUtc)
    {
        if (statusWriter is null)
        {
            return;
        }

        WriteStatusLine(statusWriter, ChunkProgressFormatter.FormatProgressMessage(completedChunkFiles, totalChunkFiles, elapsed, currentChunkFile, startedAtUtc, completedAtUtc));
    }

    private static void WriteStatusLine(TextWriter statusWriter, string message)
    {
        lock (ConsoleStatusLock)
        {
            try
            {
                statusWriter.WriteLine(message);
                statusWriter.Flush();
            }
            catch
            {
                // Progress output is advisory only and must never stop the scan.
            }
        }
    }

    public static async Task WriteLiveReportAsync(string connectionString, TextWriter writer, CancellationToken cancellationToken, ReportOutputOptions outputOptions)
    {
        var report = await AnalyzeLiveAsync(connectionString, cancellationToken);
        await JsonReportWriter.WriteAsync(report, writer, cancellationToken, outputOptions);
    }

    private static ReportEnvelope BuildReport(string source, IReadOnlyList<EventRecord> records, DateTime generatedAtUtc)
    {
        var aggregator = new EventAggregator();
        foreach (var record in records)
        {
            aggregator.Add(record);
        }

        return CreateReport(source, aggregator.Snapshot(), null, null, 0, generatedAtUtc);
    }

    private static ReportEnvelope CreateReport(string source, IReadOnlyList<EventGroup> groups, string? currentChunk, DateTime? lastEventTimestampUtc, long totalEmptySpaceBytes, DateTime generatedAtUtc)
    {
        var totalCount = groups.Sum(group => group.TotalCount);
        var totalPayloadSize = groups.Sum(group => group.TotalPayloadSize);
        var totalRecordSize = groups.Sum(group => group.TotalRecordSize);
        var totalEmptySpaceMb = totalEmptySpaceBytes / 1024d / 1024d;
        var completedAtUtc = DateTime.UtcNow;
        return new ReportEnvelope(
            generatedAtUtc,
            completedAtUtc,
            source,
            totalCount,
            totalPayloadSize,
            totalPayloadSize.ToString("N0", CultureInfo.InvariantCulture),
            totalPayloadSize / 1024d / 1024d,
            totalRecordSize,
            totalRecordSize.ToString("N0", CultureInfo.InvariantCulture),
            totalRecordSize / 1024d / 1024d,
            totalEmptySpaceBytes,
            totalEmptySpaceBytes.ToString("N0", CultureInfo.InvariantCulture),
            totalEmptySpaceMb,
            currentChunk,
            lastEventTimestampUtc,
            groups);
    }

    private static bool TryBuildConnectionString(IReadOnlyDictionary<string, string> options, out string connectionString, out string error)
    {
        if (options.TryGetValue("connection-string", out connectionString!) && !string.IsNullOrWhiteSpace(connectionString))
        {
            error = string.Empty;
            return true;
        }

        if (!options.TryGetValue("url", out var url) || string.IsNullOrWhiteSpace(url))
        {
            connectionString = string.Empty;
            error = "Missing required --connection-string or --url for live mode.";
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            connectionString = string.Empty;
            error = $"Invalid --url value '{url}'.";
            return false;
        }

        var tls = uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? "true" : "false";
        var host = uri.Host;
        var port = uri.IsDefaultPort ? 2113 : uri.Port;
        var userInfo = uri.UserInfo;
        var credentials = string.Empty;

        if (!string.IsNullOrWhiteSpace(userInfo))
        {
            var pieces = userInfo.Split(':', 2);
            credentials = pieces.Length == 2
                ? $"{Uri.EscapeDataString(pieces[0])}:{Uri.EscapeDataString(pieces[1])}@"
                : $"{Uri.EscapeDataString(userInfo)}@";
        }
        else if (options.TryGetValue("username", out var username) && options.TryGetValue("password", out var password) && !string.IsNullOrWhiteSpace(username))
        {
            credentials = $"{Uri.EscapeDataString(username)}:{Uri.EscapeDataString(password)}@";
        }

        connectionString = $"kurrentdb://{credentials}{host}:{port}?tls={tls}";
        error = string.Empty;
        return true;
    }

    private static Dictionary<string, string> ParseOptions(IEnumerable<string> args)
    {
        var tokens = args as string[] ?? args.ToArray();
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = token[2..];
            if (i + 1 >= tokens.Length)
            {
                result[key] = "true";
                break;
            }

            var value = tokens[i + 1];
            if (value.StartsWith("--", StringComparison.Ordinal))
            {
                result[key] = "true";
                continue;
            }

            result[key] = value;
            i++;
        }

        return result;
    }

    private static ReportOutputOptions BuildOutputOptions(IReadOnlyDictionary<string, string> options) =>
        new(
            IncludePayloadValues: options.ContainsKey("include-payload-values"),
            IncludeEventGroups: !options.ContainsKey("no-event-groups"),
            IncludeChunkFiles: options.ContainsKey("include-chunk-files"));

    private static bool IsHelp(string token) =>
        token is "-h" or "--help" or "/?";

    private static void PrintUsage()
    {
        Console.WriteLine("""
Usage:
  ESAnalyser offline --path <kurrent-data-folder> [--include-payload-values] [--include-event-groups] [--no-event-groups] [--include-chunk-files]
  ESAnalyser live --url <http-or-https-url> [--username <u> --password <p>] [--include-payload-values] [--include-event-groups] [--no-event-groups] [--include-chunk-files]
  ESAnalyser live --connection-string <kurrentdb-connection-string> [--include-payload-values] [--include-event-groups] [--no-event-groups] [--include-chunk-files]
  ESAnalyser selftest
""");
    }
}
