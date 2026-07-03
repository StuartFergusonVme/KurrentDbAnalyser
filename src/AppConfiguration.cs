using Microsoft.Extensions.Configuration;

namespace ESAnalyser;

internal static class AppConfiguration
{
    public static IConfigurationRoot Load()
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);

        var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

        if (string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase))
        {
            builder.AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false);
        }

        return builder.Build();
    }

    public static string GetRequiredValue(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Missing required configuration value '{key}'.");
        }

        return value;
    }

    public static bool GetBoolValue(IConfiguration configuration, string key, bool defaultValue)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    public static int GetIntValue(IConfiguration configuration, string key, int defaultValue)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : defaultValue;
    }
}
