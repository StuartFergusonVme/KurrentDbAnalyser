using System.Collections.Generic;
using System.Linq;

namespace ESAnalyser.Analysis;

public sealed class EventAggregator
{
    private readonly Dictionary<(string EventType, bool IsLinkedEvent), GroupState> _groups = new();

    public void Add(EventRecord record)
    {
        var key = (record.EventType, record.IsLinkedEvent);
        if (_groups.TryGetValue(key, out var current))
        {
            current.Count += 1;
            current.PayloadSize += record.PayloadSize;
            current.RecordSize += record.RecordSize;
            current.UpdateSourceStream(record.SourceStream);
            return;
        }

        _groups[key] = new GroupState(record.SourceStream, 1, record.PayloadSize, record.RecordSize);
    }

    public void AddGroup(EventGroup group)
    {
        var key = (group.EventType, group.IsLinkedEvent);
        if (_groups.TryGetValue(key, out var current))
        {
            current.Count += group.TotalCount;
            current.PayloadSize += group.TotalPayloadSize;
            current.RecordSize += group.TotalRecordSize;
            current.UpdateSourceStream(group.SourceStream);
            return;
        }

        _groups[key] = new GroupState(group.SourceStream, group.TotalCount, group.TotalPayloadSize, group.TotalRecordSize);
    }

    public IReadOnlyList<EventGroup> Snapshot() =>
        _groups
            .Select(pair => new EventGroup(
                pair.Key.EventType,
                pair.Value.SourceStream,
                pair.Key.IsLinkedEvent,
                pair.Value.Count,
                pair.Value.PayloadSize,
                pair.Value.Count == 0 ? 0 : (double)pair.Value.PayloadSize / pair.Value.Count,
                pair.Value.RecordSize,
                pair.Value.Count == 0 ? 0 : (double)pair.Value.RecordSize / pair.Value.Count))
            .OrderByDescending(x => x.TotalPayloadSize)
            .ThenBy(x => x.EventType, StringComparer.Ordinal)
            .ThenBy(x => x.IsLinkedEvent)
            .ToList();

    private sealed class GroupState
    {
        private string _sourceStream;
        private bool _multipleSourceStreams;

        public GroupState(string sourceStream, long count, long payloadSize, long recordSize)
        {
            _sourceStream = sourceStream;
            Count = count;
            PayloadSize = payloadSize;
            RecordSize = recordSize;
        }

        public long Count { get; set; }

        public long PayloadSize { get; set; }

        public long RecordSize { get; set; }

        public string SourceStream => _multipleSourceStreams ? "multiple" : _sourceStream;

        public void UpdateSourceStream(string sourceStream)
        {
            if (_multipleSourceStreams || string.Equals(_sourceStream, sourceStream, StringComparison.Ordinal))
            {
                return;
            }

            _multipleSourceStreams = true;
        }
    }
}
