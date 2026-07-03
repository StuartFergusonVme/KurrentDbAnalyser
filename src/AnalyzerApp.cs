using ESAnalyser.Analysis;
using ESAnalyser.Live;
using ESAnalyser.Offline;
using ESAnalyser.Output;
using System.Globalization;

namespace ESAnalyser;

public static class AnalyzerApp
{
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

                await WriteOfflineReportAsync(path, Console.Out, CancellationToken.None, outputOptions);
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

    public static ReportEnvelope AnalyzeOffline(string dataPath)
    {
        var generatedAtUtc = DateTime.UtcNow;
        var records = OfflineDataReader.Read(dataPath);
        return BuildReport("offline", records, generatedAtUtc);
    }

    public static async Task<ReportEnvelope> AnalyzeLiveAsync(string connectionString, CancellationToken cancellationToken)
    {
        var generatedAtUtc = DateTime.UtcNow;
        var records = await LiveDataReader.ReadAsync(connectionString, cancellationToken);
        return BuildReport("live", records, generatedAtUtc);
    }

    public static async Task WriteOfflineReportAsync(string dataPath, TextWriter writer, CancellationToken cancellationToken)
    {
        var report = AnalyzeOffline(dataPath);
        await JsonReportWriter.WriteAsync(report, writer, cancellationToken, new ReportOutputOptions(false, true, false));
    }

    public static async Task WriteOfflineReportAsync(string dataPath, TextWriter writer, CancellationToken cancellationToken, ReportOutputOptions outputOptions)
    {
        var report = AnalyzeOffline(dataPath);
        await JsonReportWriter.WriteAsync(report, writer, cancellationToken, outputOptions);
    }

    public static async Task WriteOfflineReportAsync(string dataPath, string outputPath, CancellationToken cancellationToken, ReportOutputOptions? outputOptions = null)
    {
        var generatedAtUtc = DateTime.UtcNow;
        Console.WriteLine("Scanning chunk files...");
        var chunkFiles = ChunkDirectoryScanner.EnumerateChunkFiles(dataPath).ToArray();
        var aggregator = new EventAggregator();
        var chunkFileSummaries = new List<ChunkFileSummary>(chunkFiles.Length);

        if (chunkFiles.Length == 0)
        {
            await JsonReportWriter.WriteAsync(CreateReport("offline", Array.Empty<EventGroup>(), null, null, Array.Empty<ChunkFileSummary>(), generatedAtUtc), outputPath, cancellationToken, outputOptions ?? new ReportOutputOptions(false, true, false));
            return;
        }

        var currentChunk = string.Empty;
        DateTime? lastEventTimestampUtc = null;

        foreach (var chunkFile in chunkFiles)
        {
            Console.WriteLine($"Processing chunk: {Path.GetFileName(chunkFile)}");
            currentChunk = Path.GetFileName(chunkFile);
            var chunkEventPayloadBytes = 0L;
            var chunkEventRecordBytes = 0L;
            foreach (var record in OfflineDataReader.ReadLogicalRecordsWithTimestamps(new[] { chunkFile }))
            {
                aggregator.Add(record.Record);
                lastEventTimestampUtc = record.TimestampUtc;
                chunkEventPayloadBytes += record.Record.PayloadSize;
                chunkEventRecordBytes += record.Record.RecordSize;
            }

            var chunkSizeBytes = new FileInfo(chunkFile).Length;
            chunkFileSummaries.Add(new ChunkFileSummary(
                currentChunk,
                chunkSizeBytes,
                chunkSizeBytes.ToString("N0", CultureInfo.InvariantCulture),
                chunkSizeBytes / 1024d / 1024d,
                chunkEventPayloadBytes,
                chunkEventPayloadBytes.ToString("N0", CultureInfo.InvariantCulture),
                chunkEventPayloadBytes / 1024d / 1024d,
                chunkEventRecordBytes,
                chunkEventRecordBytes.ToString("N0", CultureInfo.InvariantCulture),
                chunkEventRecordBytes / 1024d / 1024d));

            var report = CreateReport("offline", aggregator.Snapshot(), currentChunk, lastEventTimestampUtc, chunkFileSummaries, generatedAtUtc);
            await JsonReportWriter.WriteAsync(report, outputPath, cancellationToken, outputOptions ?? new ReportOutputOptions(false, true, false));
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

        return CreateReport(source, aggregator.Snapshot(), null, null, Array.Empty<ChunkFileSummary>(), generatedAtUtc);
    }

    private static ReportEnvelope CreateReport(string source, IReadOnlyList<EventGroup> groups, string? currentChunk, DateTime? lastEventTimestampUtc, IReadOnlyList<ChunkFileSummary> chunkFiles, DateTime generatedAtUtc)
    {
        var totalCount = groups.Sum(group => group.TotalCount);
        var totalPayloadSize = groups.Sum(group => group.TotalPayloadSize);
        var totalRecordSize = groups.Sum(group => group.TotalRecordSize);
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
            currentChunk,
            lastEventTimestampUtc,
            chunkFiles,
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
