using ESAnalyser;
using ESAnalyser.Analysis;

if (args.Length > 0)
{
    await AnalyzerApp.RunAsync(args);
    return;
}

var dataPath = @"C:\visualbos2\Eposity\ESDB\data";
var outputPath = Path.Combine("c:\\temp", "es-analysis-report.json");

ReportOutputOptions options = new ReportOutputOptions(false, true, false);
await AnalyzerApp.WriteOfflineReportAsync(dataPath, outputPath, CancellationToken.None, options);
System.Console.WriteLine($"Report written to {outputPath}");
Console.ReadKey();
