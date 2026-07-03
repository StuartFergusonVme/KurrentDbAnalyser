using System.Text.Json;
using System.Text.Json.Serialization;
using ESAnalyser.Analysis;
using System.Globalization;
using System.Text;

namespace ESAnalyser.Output;

public static class JsonReportWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task WriteAsync(ReportEnvelope report, TextWriter writer, CancellationToken cancellationToken, ReportOutputOptions? outputOptions = null)
    {
        var json = JsonSerializer.Serialize(ToOutput(report, outputOptions ?? new ReportOutputOptions(false, true, false)), Options);
        await writer.WriteLineAsync(json);
        await writer.FlushAsync();
    }

    public static async Task WriteAsync(ReportEnvelope report, string path, CancellationToken cancellationToken, ReportOutputOptions? outputOptions = null)
    {
        var json = JsonSerializer.Serialize(ToOutput(report, outputOptions ?? new ReportOutputOptions(false, true, false)), Options);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, FileOptions.SequentialScan);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteAsync(json.AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
    }

    private static ReportEnvelopeOutput ToOutput(ReportEnvelope report, ReportOutputOptions outputOptions)
    {
        IReadOnlyList<ChunkFileSummaryOutput>? chunkFiles = null;
        if (outputOptions.IncludeChunkFiles)
        {
            chunkFiles = report.ChunkFiles.Select(chunkFile => new ChunkFileSummaryOutput(
                chunkFile.ChunkFile,
                chunkFile.SizeBytes.ToString("N0", CultureInfo.InvariantCulture),
                chunkFile.SizeBytes / 1024d / 1024d,
                outputOptions.IncludePayloadValues ? chunkFile.EventPayloadBytes.ToString("N0", CultureInfo.InvariantCulture) : null,
                outputOptions.IncludePayloadValues ? chunkFile.EventPayloadBytes / 1024d / 1024d : null,
                chunkFile.EventRecordBytes.ToString("N0", CultureInfo.InvariantCulture),
                chunkFile.EventRecordBytes / 1024d / 1024d)).ToList();
        }

        IReadOnlyList<EventGroupOutput>? groups = null;
        if (outputOptions.IncludeEventGroups)
        {
            groups = report.Groups.Select(group => new EventGroupOutput(
                group.EventType,
                group.SourceStream,
                group.IsLinkedEvent,
                group.TotalCount,
                group.TotalRecordSize.ToString("N0", CultureInfo.InvariantCulture),
                group.TotalRecordSize / 1024d / 1024d,
                outputOptions.IncludePayloadValues ? group.TotalPayloadSize.ToString("N0", CultureInfo.InvariantCulture) : null,
                outputOptions.IncludePayloadValues ? group.TotalPayloadSize / 1024d / 1024d : null,
                group.AvgRecordSizePerEvent,
                outputOptions.IncludePayloadValues ? group.AvgPayloadSizePerEvent : null)).ToList();
        }

        return new ReportEnvelopeOutput(
            report.GeneratedAtUtc,
            report.CompletedAtUtc,
            report.Source,
            report.TotalCount,
            report.TotalRecordSize.ToString("N0", CultureInfo.InvariantCulture),
            report.TotalRecordSize / 1024d / 1024d,
            outputOptions.IncludePayloadValues ? report.TotalPayloadSize.ToString("N0", CultureInfo.InvariantCulture) : null,
            outputOptions.IncludePayloadValues ? report.TotalPayloadSize / 1024d / 1024d : null,
            report.CurrentChunk,
            report.LastEventTimestampUtc,
            chunkFiles,
            groups);
    }

    private sealed record ReportEnvelopeOutput(
        [property: JsonPropertyName("generatedAtUtc")] DateTime GeneratedAtUtc,
        [property: JsonPropertyName("completedAtUtc")] DateTime CompletedAtUtc,
        [property: JsonPropertyName("source")] string Source,
        [property: JsonPropertyName("totalCount")] long TotalCount,
        [property: JsonPropertyName("totalRecordSizeDisplay")] string TotalRecordSizeDisplay,
        [property: JsonPropertyName("totalRecordMb")] double TotalRecordMb,
        [property: JsonPropertyName("totalPayloadSizeDisplay")] string? TotalPayloadSizeDisplay,
        [property: JsonPropertyName("totalPayloadMb")] double? TotalPayloadMb,
        [property: JsonPropertyName("currentChunk")] string? CurrentChunk,
        [property: JsonPropertyName("lastEventTimestampUtc")] DateTime? LastEventTimestampUtc,
        [property: JsonPropertyName("chunkFiles")] IReadOnlyList<ChunkFileSummaryOutput>? ChunkFiles,
        [property: JsonPropertyName("groups")] IReadOnlyList<EventGroupOutput>? Groups);

    private sealed record ChunkFileSummaryOutput(
        [property: JsonPropertyName("chunkFile")] string ChunkFile,
        [property: JsonPropertyName("sizeBytesDisplay")] string SizeBytesDisplay,
        [property: JsonPropertyName("sizeMb")] double SizeMb,
        [property: JsonPropertyName("eventPayloadBytesDisplay")] string? EventPayloadBytesDisplay,
        [property: JsonPropertyName("eventPayloadMb")] double? EventPayloadMb,
        [property: JsonPropertyName("eventRecordBytesDisplay")] string EventRecordBytesDisplay,
        [property: JsonPropertyName("eventRecordMb")] double EventRecordMb);

    private sealed record EventGroupOutput(
        [property: JsonPropertyName("EventType")] string EventType,
        [property: JsonPropertyName("sourceStream")] string SourceStream,
        [property: JsonPropertyName("isLinkedEvent")] bool IsLinkedEvent,
        [property: JsonPropertyName("totalCount")] long TotalCount,
        [property: JsonPropertyName("totalRecordSizeDisplay")] string TotalRecordSizeDisplay,
        [property: JsonPropertyName("totalRecordMb")] double TotalRecordMb,
        [property: JsonPropertyName("totalPayloadSizeDisplay")] string? TotalPayloadSizeDisplay,
        [property: JsonPropertyName("totalPayloadMb")] double? TotalPayloadMb,
        [property: JsonPropertyName("avgRecordSizePerEvent")] double AvgRecordSizePerEvent,
        [property: JsonPropertyName("avgPayloadSizePerEvent")] double? AvgPayloadSizePerEvent);
}
