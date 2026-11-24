using System.Text;
using System.Text.Json;

namespace ESAnalyser;

internal static class ScavengeSelfTest
{
    internal static void Run()
    {
        ScavengeHistory history = new();
        history.Observe("orders", "$scavengeStarted");
        ScavengeSelfTest.Assert(!history.GetStatus().IsWarning, "Unrelated streams must not trigger a scavenge warning.");

        history.Observe("$scavenges-first", "$scavengeStarted");
        history.Observe("$scavenges-first", "$scavengeChunksCompleted");
        ScavengeSelfTest.Assert(history.GetStatus().IsWarning, "Chunk completion must not mark a whole scavenge complete.");
        history.Observe("$scavenges-second", "$scavengeCompleted");
        ScavengeSelfTest.Assert(history.GetStatus().IsWarning, "Another scavenge completing must not hide an unfinished run.");
        history.Observe("$scavenges-first", "$scavengeCompleted");
        history.Observe("$scavenges-second", "$scavengeStarted");
        ScavengeSelfTest.Assert(!history.GetStatus().IsWarning, "Completed runs must not warn, regardless of read order.");

        String directory = Path.Combine(Path.GetTempPath(), $"es-analyser-scavenge-selftest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            String startedChunk = Path.Combine(directory, "chunk-000000.000000");
            String completedChunk = Path.Combine(directory, "chunk-000001.000000");
            ScavengeSelfTest.WriteChunk(startedChunk, "$scavengeStarted");

            using StringWriter statusWriter = new();
            using StringWriter reportWriter = new();
            AnalyzerApp.WriteOfflineReportAsync(directory, reportWriter, CancellationToken.None,
                new Analysis.ReportOutputOptions(false, true, false), 1, statusWriter).GetAwaiter().GetResult();
            String status = statusWriter.ToString();
            ScavengeSelfTest.Assert(status.Contains("WARNING: A scavenge may be in progress", StringComparison.Ordinal) &&
                                    status.Contains("impact the analysis results", StringComparison.Ordinal),
                "An unfinished scavenge must produce a warning describing the impact.");
            ScavengeSelfTest.Assert(status.IndexOf("WARNING:", StringComparison.Ordinal) < status.IndexOf("Chunk trace:", StringComparison.Ordinal),
                "The scavenge warning must appear before chunk analysis.");
            using JsonDocument report = JsonDocument.Parse(reportWriter.ToString());
            ScavengeSelfTest.Assert(report.RootElement.GetProperty("totalCount").GetInt32() == 1,
                "Warning output must preserve valid JSON and allow analysis to continue.");

            ScavengeSelfTest.WriteChunk(completedChunk, "$scavengeCompleted");
            ScavengeSelfTest.Assert(!ScavengeStatusChecker.CheckOffline(new[] { startedChunk, completedChunk }, CancellationToken.None).IsWarning,
                "A completion in a later chunk must clear the warning.");
            ScavengeSelfTest.Assert(ScavengeStatusChecker.CheckOffline(new[] { Path.Combine(directory, "missing") }, CancellationToken.None).IsWarning,
                "Unreadable history must warn instead of reporting that no scavenge is running.");

            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            try
            {
                ScavengeStatusChecker.CheckOffline(new[] { startedChunk }, cancellation.Token);
                throw new InvalidOperationException("Scavenge checks must respect cancellation.");
            }
            catch (OperationCanceledException)
            {
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }

        Console.WriteLine("Scavenge self-tests passed.");
    }

    private static void Assert(Boolean condition, String message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void WriteChunk(String path, String eventType)
    {
        using MemoryStream body = new();
        using (BinaryWriter record = new(body, Encoding.UTF8, leaveOpen: true))
        {
            record.Write((Byte)0); // Prepare record.
            record.Write((Byte)1);
            record.Write((Int64)0); // Log position.
            record.Write((UInt16)0);
            record.Write((Int64)0); // Transaction position.
            record.Write((Int32)0); // Transaction offset.
            record.Write((Int64)0); // Expected version.
            record.Write("$scavenges-test");
            record.Write(Guid.NewGuid().ToByteArray());
            record.Write(Guid.NewGuid().ToByteArray());
            record.Write(DateTime.UtcNow.Ticks);
            record.Write(eventType);
            record.Write((Int32)0); // Data length.
            record.Write((Int32)0); // Metadata length.
        }

        using BinaryWriter chunk = new(File.Create(path));
        chunk.Write(new Byte[128]);
        chunk.Write((Int32)body.Length);
        chunk.Write(body.ToArray());
        chunk.Write((Int32)body.Length);
    }
}
