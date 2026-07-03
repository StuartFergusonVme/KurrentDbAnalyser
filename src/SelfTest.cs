using ESAnalyser.Analysis;
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
        aggregator.Add(new EventRecord("Alpha", "stream-a", false, 14, 22));
        aggregator.Add(new EventRecord("Beta", "stream-b", true, 5, 11));

        var groups = aggregator.Snapshot();
        if (groups.Count != 2)
        {
            throw new InvalidOperationException($"Expected 2 groups, found {groups.Count}.");
        }

        var alpha = groups.Single(x => x.EventType == "Alpha");
        if (alpha.TotalCount != 2 || alpha.TotalPayloadSize != 24 || alpha.TotalRecordSize != 40 || Math.Abs(alpha.AvgPayloadSizePerEvent - 12) > 0.0001 || Math.Abs(alpha.AvgRecordSizePerEvent - 20) > 0.0001)
        {
            throw new InvalidOperationException("Alpha aggregate check failed.");
        }

        var beta = groups.Single(x => x.EventType == "Beta");
        if (!beta.IsLinkedEvent || beta.TotalCount != 1 || beta.TotalPayloadSize != 5 || beta.TotalRecordSize != 11)
        {
            throw new InvalidOperationException("Beta aggregate check failed.");
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

        Console.WriteLine("Self-test passed.");
    }
}
