using System.Text.Json.Serialization;

namespace ESAnalyser.Analysis;

internal static class EventTypeName
{
    public static string Format(string eventType, string sourceStream, bool isLinkedEvent) =>
        isLinkedEvent ? $"{eventType}-{sourceStream}" : eventType;
}

public sealed record EventRecord(
    string EventType,
    string SourceStream,
    bool IsLinkedEvent,
    int PayloadSize,
    int RecordSize);

public sealed record EventGroup(
    [property: JsonPropertyName("EventType")] string EventType,
    [property: JsonPropertyName("sourceStream")] string SourceStream,
    [property: JsonPropertyName("isLinkedEvent")] bool IsLinkedEvent,
    [property: JsonPropertyName("totalCount")] long TotalCount,
    [property: JsonPropertyName("totalPayloadSize")] long TotalPayloadSize,
    [property: JsonPropertyName("avgPayloadSizePerEvent")] double AvgPayloadSizePerEvent,
    [property: JsonPropertyName("totalRecordSize")] long TotalRecordSize,
    [property: JsonPropertyName("avgRecordSizePerEvent")] double AvgRecordSizePerEvent);

public sealed record ChunkFileSummary(
    [property: JsonPropertyName("chunkFile")] string ChunkFile,
    [property: JsonPropertyName("sizeBytes")] long SizeBytes,
    [property: JsonPropertyName("sizeBytesDisplay")] string SizeBytesDisplay,
    [property: JsonPropertyName("sizeMb")] double SizeMb,
    [property: JsonPropertyName("eventPayloadBytes")] long EventPayloadBytes,
    [property: JsonPropertyName("eventPayloadBytesDisplay")] string EventPayloadBytesDisplay,
    [property: JsonPropertyName("eventPayloadMb")] double EventPayloadMb,
    [property: JsonPropertyName("eventRecordBytes")] long EventRecordBytes,
    [property: JsonPropertyName("eventRecordBytesDisplay")] string EventRecordBytesDisplay,
    [property: JsonPropertyName("eventRecordMb")] double EventRecordMb);

public sealed record ReportEnvelope(
    [property: JsonPropertyName("generatedAtUtc")] DateTime GeneratedAtUtc,
    [property: JsonPropertyName("completedAtUtc")] DateTime CompletedAtUtc,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("totalCount")] long TotalCount,
    [property: JsonPropertyName("totalPayloadSize")] long TotalPayloadSize,
    [property: JsonPropertyName("totalPayloadSizeDisplay")] string TotalPayloadSizeDisplay,
    [property: JsonPropertyName("totalPayloadMb")] double TotalPayloadMb,
    [property: JsonPropertyName("totalRecordSize")] long TotalRecordSize,
    [property: JsonPropertyName("totalRecordSizeDisplay")] string TotalRecordSizeDisplay,
    [property: JsonPropertyName("totalRecordMb")] double TotalRecordMb,
    [property: JsonPropertyName("currentChunk")] string? CurrentChunk,
    [property: JsonPropertyName("lastEventTimestampUtc")] DateTime? LastEventTimestampUtc,
    [property: JsonPropertyName("chunkFiles")] IReadOnlyList<ChunkFileSummary> ChunkFiles,
    [property: JsonPropertyName("groups")] IReadOnlyList<EventGroup> Groups);

public sealed record ReportOutputOptions(
    bool IncludePayloadValues,
    bool IncludeEventGroups,
    bool IncludeChunkFiles);
