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

    public static Task WriteAsync(ReportEnvelope report, string path, CancellationToken cancellationToken, ReportOutputOptions? outputOptions = null)
    {
        WriteToFile(report, path, outputOptions);
        return Task.CompletedTask;
    }

    public static void WriteToFile(ReportEnvelope report, string path, ReportOutputOptions? outputOptions = null)
    {
        var json = JsonSerializer.Serialize(ToOutput(report, outputOptions ?? new ReportOutputOptions(false, true, false)), Options);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static ReportEnvelopeOutput ToOutput(ReportEnvelope report, ReportOutputOptions outputOptions)
    {
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
            report.TotalEmptySpaceBytes.ToString("N0", CultureInfo.InvariantCulture),
            report.TotalEmptySpaceMb,
            report.CurrentChunk,
            report.LastEventTimestampUtc,
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
        [property: JsonPropertyName("totalEmptySpaceBytesDisplay")] string TotalEmptySpaceBytesDisplay,
        [property: JsonPropertyName("totalEmptySpaceMb")] double TotalEmptySpaceMb,
        [property: JsonPropertyName("currentChunk")] string? CurrentChunk,
        [property: JsonPropertyName("lastEventTimestampUtc")] DateTime? LastEventTimestampUtc,
        [property: JsonPropertyName("groups")] IReadOnlyList<EventGroupOutput>? Groups);

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
