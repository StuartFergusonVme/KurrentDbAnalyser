using ESAnalyser;
using ESAnalyser.Analysis;
using System.Globalization;

if (args.Length > 0)
{
    await AnalyzerApp.RunAsync(args);
    return;
}

var configuration = AppConfiguration.Load();
var dataPath = AppConfiguration.GetRequiredValue(configuration, "OfflineReport:DataPath");
var outputPath = AppConfiguration.GetRequiredValue(configuration, "OfflineReport:OutputPath");
outputPath = MakeTimestampedReportPath(outputPath);
var maxConcurrentChunkFiles = AppConfiguration.GetIntValue(configuration, "OfflineReport:MaxConcurrentChunkFiles", 1);

var options = new ReportOutputOptions(
    IncludePayloadValues: AppConfiguration.GetBoolValue(configuration, "OfflineReport:OutputOptions:IncludePayloadValues", false),
    IncludeEventGroups: AppConfiguration.GetBoolValue(configuration, "OfflineReport:OutputOptions:IncludeEventGroups", true),
    IncludeChunkFiles: AppConfiguration.GetBoolValue(configuration, "OfflineReport:OutputOptions:IncludeChunkFiles", false));

await AnalyzerApp.WriteOfflineReportAsync(dataPath, outputPath, CancellationToken.None, options, maxConcurrentChunkFiles, Console.Error);
System.Console.WriteLine($"Report written to {outputPath}");
Console.ReadKey();

static string MakeTimestampedReportPath(string outputPath)
{
    var directory = Path.GetDirectoryName(outputPath);
    var fileName = Path.GetFileName(outputPath);
    var extension = Path.GetExtension(fileName);
    var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
    var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
    var uniqueFileName = string.IsNullOrWhiteSpace(extension)
        ? $"{nameWithoutExtension}.{timestamp}"
        : $"{nameWithoutExtension}.{timestamp}{extension}";

    return string.IsNullOrWhiteSpace(directory)
        ? uniqueFileName
        : Path.Combine(directory, uniqueFileName);
}
