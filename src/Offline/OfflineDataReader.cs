using AnalysisEventRecord = ESAnalyser.Analysis.EventRecord;

namespace ESAnalyser.Offline;

internal sealed record LogicalAnalysisRecord(AnalysisEventRecord Record, DateTime TimestampUtc);

public static class OfflineDataReader
{
    public static IReadOnlyList<AnalysisEventRecord> Read(string dataDirectory)
    {
        var chunkFiles = ChunkDirectoryScanner.EnumerateChunkFiles(dataDirectory).ToArray();
        return ReadLogicalRecordsWithTimestamps(chunkFiles).Select(x => x.Record).ToList();
    }

    internal static IReadOnlyDictionary<(string Stream, int EventNumber), string> BuildSourceEventTypes(IEnumerable<string> chunkFiles)
    {
        var sourceEventTypes = new Dictionary<(string Stream, int EventNumber), string>();
        var nextEventNumbers = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var chunkFile in chunkFiles)
        {
            foreach (var record in ChunkRecordParser.ReadChunk(chunkFile))
            {
                if (record.IsLinkedEvent)
                {
                    continue;
                }

                var eventNumber = nextEventNumbers.TryGetValue(record.StreamName, out var current) ? current : 0;
                sourceEventTypes[(record.StreamName, eventNumber)] = record.EventType;
                nextEventNumbers[record.StreamName] = eventNumber + 1;
            }
        }

        return sourceEventTypes;
    }

    internal static IEnumerable<AnalysisEventRecord> ReadLogicalRecords(
        IEnumerable<string> chunkFiles,
        IReadOnlyDictionary<(string Stream, int EventNumber), string> sourceEventTypes)
    {
        foreach (var logicalRecord in ReadLogicalRecordsWithTimestamps(chunkFiles, sourceEventTypes))
        {
            yield return logicalRecord.Record;
        }
    }

    internal static IEnumerable<LogicalAnalysisRecord> ReadLogicalRecordsWithTimestamps(
        IEnumerable<string> chunkFiles)
    {
        var sourceEventTypes = new Dictionary<(string Stream, int EventNumber), string>();
        var nextEventNumbers = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var chunkFile in chunkFiles)
        {
            foreach (var record in ChunkRecordParser.ReadChunk(chunkFile))
            {
                var resolvedEventType = record.EventType;
                if (record.IsLinkedEvent && record.LinkTargetStream is not null && record.LinkTargetEventNumber is not null)
                {
                    if (sourceEventTypes.TryGetValue((record.LinkTargetStream, record.LinkTargetEventNumber.Value), out var linkedEventType))
                    {
                        resolvedEventType = linkedEventType;
                    }
                }

                if (!record.IsLinkedEvent)
                {
                    var eventNumber = nextEventNumbers.TryGetValue(record.StreamName, out var current) ? current : 0;
                    sourceEventTypes[(record.StreamName, eventNumber)] = record.EventType;
                    nextEventNumbers[record.StreamName] = eventNumber + 1;
                }

                yield return new LogicalAnalysisRecord(new AnalysisEventRecord(resolvedEventType, record.StreamName, record.IsLinkedEvent, record.PayloadSize, record.RecordSize), record.TimestampUtc);
            }
        }
    }

    internal static IEnumerable<LogicalAnalysisRecord> ReadLogicalRecordsWithTimestamps(
        IEnumerable<string> chunkFiles,
        IReadOnlyDictionary<(string Stream, int EventNumber), string> sourceEventTypes)
    {
        foreach (var chunkFile in chunkFiles)
        {
            foreach (var record in ChunkRecordParser.ReadChunk(chunkFile))
            {
                if (record.IsLinkedEvent && record.LinkTargetStream is not null && record.LinkTargetEventNumber is not null)
                {
                    if (sourceEventTypes.TryGetValue((record.LinkTargetStream, record.LinkTargetEventNumber.Value), out var resolvedEventType))
                    {
                        yield return new LogicalAnalysisRecord(new AnalysisEventRecord(resolvedEventType, record.StreamName, true, record.PayloadSize, record.RecordSize), record.TimestampUtc);
                        continue;
                    }

                    yield return new LogicalAnalysisRecord(new AnalysisEventRecord(record.EventType, record.StreamName, true, record.PayloadSize, record.RecordSize), record.TimestampUtc);
                    continue;
                }

                yield return new LogicalAnalysisRecord(new AnalysisEventRecord(record.EventType, record.StreamName, false, record.PayloadSize, record.RecordSize), record.TimestampUtc);
            }
        }
    }
}
