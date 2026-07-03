using ESAnalyser;
using ESAnalyser.Analysis;

if (args.Length > 0)
{
    await AnalyzerApp.RunAsync(args);
    return;
}

var configuration = AppConfiguration.Load();
var dataPath = AppConfiguration.GetRequiredValue(configuration, "OfflineReport:DataPath");
var outputPath = AppConfiguration.GetRequiredValue(configuration, "OfflineReport:OutputPath");
var maxConcurrentChunkFiles = AppConfiguration.GetIntValue(configuration, "OfflineReport:MaxConcurrentChunkFiles", 1);

var options = new ReportOutputOptions(
    IncludePayloadValues: AppConfiguration.GetBoolValue(configuration, "OfflineReport:OutputOptions:IncludePayloadValues", false),
    IncludeEventGroups: AppConfiguration.GetBoolValue(configuration, "OfflineReport:OutputOptions:IncludeEventGroups", true),
    IncludeChunkFiles: AppConfiguration.GetBoolValue(configuration, "OfflineReport:OutputOptions:IncludeChunkFiles", false));

await AnalyzerApp.WriteOfflineReportAsync(dataPath, outputPath, CancellationToken.None, options, maxConcurrentChunkFiles);
System.Console.WriteLine($"Report written to {outputPath}");
Console.ReadKey();
