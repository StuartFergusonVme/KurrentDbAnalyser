using AnalysisEventRecord = ESAnalyser.Analysis.EventRecord;
using KurrentDB.Client;

namespace ESAnalyser.Live;

public static class LiveDataReader
{
    public static async Task<IReadOnlyList<AnalysisEventRecord>> ReadAsync(string connectionString, CancellationToken cancellationToken)
    {
        using var client = KurrentDbClientFactory.Create(connectionString);
        var result = client.ReadAllAsync(Direction.Forwards, Position.Start, long.MaxValue, resolveLinkTos: true, deadline: null, userCredentials: null, cancellationToken);

        var records = new List<AnalysisEventRecord>();
        await foreach (var message in result.Messages.WithCancellation(cancellationToken))
        {
            if (message is not StreamMessage.Event eventMessage)
            {
                continue;
            }

            var resolved = eventMessage.ResolvedEvent;
            var original = resolved.OriginalEvent;
            var sourceStream = resolved.OriginalStreamId;
            var eventType = original.EventType;
            var isLinked = resolved.IsResolved;
            var payloadSize = original.Data.Length + original.Metadata.Length;

            records.Add(new AnalysisEventRecord(eventType, sourceStream, isLinked, payloadSize, payloadSize));
        }

        return records;
    }
}
