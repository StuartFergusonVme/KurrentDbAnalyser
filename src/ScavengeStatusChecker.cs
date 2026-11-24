using ESAnalyser.Offline;
using KurrentDB.Client;

namespace ESAnalyser;

internal sealed class ScavengeHistory
{
    private readonly HashSet<String> started = new(StringComparer.Ordinal);
    private readonly HashSet<String> completed = new(StringComparer.Ordinal);

    internal void Observe(String streamName, String eventType)
    {
        // Lifecycle events live in $scavenges-<id>; $scavenges contains links to them.
        if (!streamName.StartsWith("$scavenges-", StringComparison.Ordinal))
        {
            return;
        }

        if (eventType == "$scavengeStarted")
        {
            this.started.Add(streamName);
        }
        else if (eventType == "$scavengeCompleted")
        {
            // All completion results (including stopped, failed and interrupted) end a run.
            this.completed.Add(streamName);
        }
    }

    internal (String Message, Boolean IsWarning) GetStatus()
    {
        Int32 unfinished = this.started.Count(stream => !this.completed.Contains(stream));
        return unfinished > 0
            ? ($"WARNING: A scavenge may be in progress ({unfinished} unfinished in the available history). " +
               "Scavenging changes chunk files and removes events, which could impact the analysis results. " +
               "For reliable results, run the analyser again after scavenging has finished.", true)
            : ("Scavenge check: no unfinished scavenge found in the available history. This is a point-in-time check.", false);
    }
}

internal static class ScavengeStatusChecker
{
    internal static (String Message, Boolean IsWarning) CheckOffline(IEnumerable<String> chunkFiles, CancellationToken cancellationToken)
    {
        ScavengeHistory history = new();
        try
        {
            foreach (String chunkFile in chunkFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (PhysicalRecord record in ChunkRecordParser.ReadChunk(chunkFile))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    history.Observe(record.StreamName, record.EventType);
                }
            }

            return history.GetStatus();
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return ("WARNING: Unable to check scavenge history in the chunk files. " +
                    "A running scavenge could impact the analysis results.", true);
        }
    }

    internal static async Task<(String Message, Boolean IsWarning)> CheckLiveAsync(KurrentDBClient client, CancellationToken cancellationToken)
    {
        ScavengeHistory history = new();
        try
        {
            KurrentDBClient.ReadStreamResult result = client.ReadStreamAsync(
                Direction.Backwards, "$scavenges", StreamPosition.End, resolveLinkTos: true,
                deadline: TimeSpan.FromSeconds(10), cancellationToken: cancellationToken);

            await foreach (ResolvedEvent resolved in result.WithCancellation(cancellationToken))
            {
                if (resolved.Event is not null)
                {
                    history.Observe(resolved.Event.EventStreamId, resolved.Event.EventType);
                }
                else
                {
                    return ("WARNING: Scavenge history contains unresolved events, so the scavenge status could not be determined. " +
                            "A running scavenge could impact the analysis results.", true);
                }
            }

            return history.GetStatus();
        }
        catch (StreamNotFoundException)
        {
            return history.GetStatus();
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return ("WARNING: Unable to read scavenge history from the node (check connectivity and system-stream read permissions). " +
                    "A running scavenge could impact the analysis results.", true);
        }
    }
}
