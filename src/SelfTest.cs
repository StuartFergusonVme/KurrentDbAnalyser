using ESAnalyser.Analysis;
using ESAnalyser.Offline;
using ESAnalyser.Output;
using System.Globalization;
using System.IO;

namespace ESAnalyser;

public static class SelfTest
{
    public static void Run()
    {
        var aggregator = new EventAggregator();
        aggregator.Add(new EventRecord("Alpha", "stream-a", false, 10, 18));
        aggregator.Add(new EventRecord("Alpha", "stream-b", false, 14, 22));

        var groups = aggregator.Snapshot();
        if (groups.Count != 1)
        {
            throw new InvalidOperationException($"Expected 1 group, found {groups.Count}.");
        }

        var alpha = groups.Single(x => x.EventType == "Alpha");
        if (alpha.IsLinkedEvent || alpha.SourceStream != "multiple" || alpha.TotalCount != 2 || alpha.TotalPayloadSize != 24 || alpha.TotalRecordSize != 40 || Math.Abs(alpha.AvgPayloadSizePerEvent - 12) > 0.0001 || Math.Abs(alpha.AvgRecordSizePerEvent - 20) > 0.0001)
        {
            throw new InvalidOperationException("Alpha aggregate check failed.");
        }

        var linkedAggregator = new EventAggregator();
        linkedAggregator.Add(new EventRecord("OrganisationProductCreatedEvent", "OrganisationProductCreatedEvent", false, 10, 18));
        linkedAggregator.Add(new EventRecord(EventTypeName.Format("OrganisationProductCreatedEvent", "$et-OrganisationProductCreatedEvent", true), "$et-OrganisationProductCreatedEvent", true, 5, 11));
        linkedAggregator.Add(new EventRecord(EventTypeName.Format("OrganisationProductCreatedEvent", "$ce-OrganisationAggregate", true), "$ce-OrganisationAggregate", true, 5, 11));

        var linkedGroups = linkedAggregator.Snapshot();
        if (linkedGroups.Count != 3)
        {
            throw new InvalidOperationException($"Expected 3 linked groups, found {linkedGroups.Count}.");
        }

        if (linkedGroups[0].EventType != "OrganisationProductCreatedEvent" || linkedGroups[1].EventType != "OrganisationProductCreatedEvent-$et-OrganisationProductCreatedEvent" || linkedGroups[2].EventType != "OrganisationProductCreatedEvent-$ce-OrganisationAggregate")
        {
            throw new InvalidOperationException("Linked group ordering check failed.");
        }

        var baseGroup = linkedGroups[0];
        if (baseGroup.IsLinkedEvent || baseGroup.SourceStream != "OrganisationProductCreatedEvent" || baseGroup.TotalCount != 1 || baseGroup.TotalPayloadSize != 10 || baseGroup.TotalRecordSize != 18)
        {
            throw new InvalidOperationException("Base linked aggregate check failed.");
        }

        var etGroup = linkedGroups[1];
        if (!etGroup.IsLinkedEvent || etGroup.SourceStream != "$et-OrganisationProductCreatedEvent" || etGroup.TotalCount != 1 || etGroup.TotalPayloadSize != 5 || etGroup.TotalRecordSize != 11)
        {
            throw new InvalidOperationException("ET linked aggregate check failed.");
        }

        var ceGroup = linkedGroups[2];
        if (!ceGroup.IsLinkedEvent || ceGroup.SourceStream != "$ce-OrganisationAggregate" || ceGroup.TotalCount != 1 || ceGroup.TotalPayloadSize != 5 || ceGroup.TotalRecordSize != 11)
        {
            throw new InvalidOperationException("CE linked aggregate check failed.");
        }

        var report = new ReportEnvelope(
            DateTime.UnixEpoch,
            DateTime.UnixEpoch.AddMinutes(1),
            "offline",
            3,
            19,
            19.ToString("N0", CultureInfo.InvariantCulture),
            19 / 1024d / 1024d,
            40,
            40.ToString("N0", CultureInfo.InvariantCulture),
            40 / 1024d / 1024d,
            60,
            60.ToString("N0", CultureInfo.InvariantCulture),
            60 / 1024d / 1024d,
            null,
            null,
            new[]
            {
                new EventGroup("Alpha", "stream-a", false, 2, 24, 12, 40, 20)
            });

        using var minimalWriter = new StringWriter();
        JsonReportWriter.WriteAsync(report, minimalWriter, CancellationToken.None, new ReportOutputOptions(false, false, false)).GetAwaiter().GetResult();
        var minimalJson = minimalWriter.ToString();
        if (!minimalJson.Contains("completedAtUtc") || !minimalJson.Contains("totalEmptySpaceBytesDisplay") || minimalJson.Contains("totalPayloadSize") || minimalJson.Contains("groups") || minimalJson.Contains("chunkFiles") || minimalJson.Contains("eventPayloadBytesDisplay"))
        {
            throw new InvalidOperationException("Minimal output shape check failed.");
        }

        using var defaultWriter = new StringWriter();
        JsonReportWriter.WriteAsync(report, defaultWriter, CancellationToken.None).GetAwaiter().GetResult();
        var defaultJson = defaultWriter.ToString();
        if (!defaultJson.Contains("groups") || !defaultJson.Contains("totalEmptySpaceBytesDisplay") || defaultJson.Contains("chunkFiles"))
        {
            throw new InvalidOperationException("Default output shape check failed.");
        }

        using var verboseWriter = new StringWriter();
        JsonReportWriter.WriteAsync(report, verboseWriter, CancellationToken.None, new ReportOutputOptions(true, true, true)).GetAwaiter().GetResult();
        var verboseJson = verboseWriter.ToString();
        if (!verboseJson.Contains("completedAtUtc") || !verboseJson.Contains("totalPayloadSizeDisplay") || !verboseJson.Contains("groups") || !verboseJson.Contains("totalEmptySpaceBytesDisplay") || verboseJson.Contains("chunkFiles"))
        {
            throw new InvalidOperationException("Verbose output shape check failed.");
        }

        var progressDiscovery = ChunkProgressFormatter.FormatDiscoveryMessage(@"C:\Data", 3);
        if (!progressDiscovery.Contains("3 chunk files", StringComparison.Ordinal) || !progressDiscovery.Contains(@"C:\Data", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Chunk discovery progress formatting check failed.");
        }

        var progressLine = ChunkProgressFormatter.FormatProgressMessage(5, 10, TimeSpan.FromSeconds(2), "chunk-000005");
        if (!progressLine.Contains("50.0%", StringComparison.Ordinal) || !progressLine.Contains("2.5 chunk files/sec", StringComparison.Ordinal) || !progressLine.Contains("chunk-000005", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Chunk progress formatting check failed.");
        }

        var tempDirectory = Path.Combine(Path.GetTempPath(), $"es-analyser-selftest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        try
        {
            using var statusWriter = new StringWriter();
            var emptyReport = AnalyzerApp.AnalyzeOffline(tempDirectory, 1, CancellationToken.None, statusWriter: statusWriter);
            if (emptyReport.TotalCount != 0 || !statusWriter.ToString().Contains("No chunk files found in", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Offline chunk discovery integration check failed.");
            }
        }
        finally
        {
            Directory.Delete(tempDirectory, true);
        }

        Console.WriteLine("Self-test passed.");
    }
}
