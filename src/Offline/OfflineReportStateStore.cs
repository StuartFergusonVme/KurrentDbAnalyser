using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ESAnalyser.Analysis;
using ESAnalyser.Output;

namespace ESAnalyser.Offline;

internal static class OfflineReportStateStore
{
    private static readonly SemaphoreSlim StateLock = new(1, 1);
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string GetStatePath(string outputPath) =>
        Path.ChangeExtension(outputPath, ".state.json");

    public static ReportEnvelope? LoadSnapshot(string statePath) =>
        LoadState(statePath);

    public static ReportEnvelope MergeChunkAndPublish(
        string statePath,
        string outputPath,
        ChunkAnalysisResult chunkResult,
        CancellationToken cancellationToken,
        ReportOutputOptions outputOptions)
    {
        StateLock.Wait(cancellationToken);
        try
        {
            var existingState = LoadState(statePath);
            var generatedAtUtc = existingState?.GeneratedAtUtc ?? DateTime.UtcNow;
            var source = existingState?.Source ?? "offline";

            var aggregator = new EventAggregator();
            var totalEmptySpaceBytes = existingState?.TotalEmptySpaceBytes ?? 0L;
            var lastEventTimestampUtc = existingState?.LastEventTimestampUtc;

            if (existingState is not null)
            {
                foreach (var group in existingState.Groups)
                {
                    aggregator.AddGroup(group);
                }
            }

            foreach (var group in chunkResult.Groups)
            {
                aggregator.AddGroup(group);
            }

            totalEmptySpaceBytes += chunkResult.ChunkFileSummary.EmptySpaceBytes;
            lastEventTimestampUtc = MaxTimestamp(lastEventTimestampUtc, chunkResult.LastEventTimestampUtc);

            var snapshot = CreateReport(
                generatedAtUtc,
                source,
                chunkResult.ChunkFileSummary.ChunkFile,
                lastEventTimestampUtc,
                aggregator.Snapshot(),
                totalEmptySpaceBytes,
                DateTime.UtcNow);

            SaveState(statePath, snapshot);
            JsonReportWriter.WriteToFile(snapshot, outputPath, outputOptions);
            return snapshot;
        }
        finally
        {
            StateLock.Release();
        }
    }

    private static ReportEnvelope? LoadState(string statePath)
    {
        if (!File.Exists(statePath))
        {
            return null;
        }

        var json = File.ReadAllText(statePath, Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<ReportEnvelope>(json, Options);
    }

    private static void SaveState(string statePath, ReportEnvelope state)
    {
        var fullPath = Path.GetFullPath(statePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = fullPath + ".tmp";
        var json = JsonSerializer.Serialize(state, Options);
        File.WriteAllText(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(tempPath, fullPath, true);
    }

    private static ReportEnvelope CreateReport(
        DateTime generatedAtUtc,
        string source,
        string? currentChunk,
        DateTime? lastEventTimestampUtc,
        IReadOnlyList<EventGroup> groups,
        long totalEmptySpaceBytes,
        DateTime completedAtUtc)
    {
        var totalCount = groups.Sum(group => group.TotalCount);
        var totalPayloadSize = groups.Sum(group => group.TotalPayloadSize);
        var totalRecordSize = groups.Sum(group => group.TotalRecordSize);
        var totalEmptySpaceMb = totalEmptySpaceBytes / 1024d / 1024d;

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

    private static DateTime? MaxTimestamp(DateTime? left, DateTime? right)
    {
        return (left, right) switch
        {
            (null, null) => null,
            (null, DateTime value) => value,
            (DateTime value, null) => value,
            (DateTime a, DateTime b) => a > b ? a : b
        };
    }
}
