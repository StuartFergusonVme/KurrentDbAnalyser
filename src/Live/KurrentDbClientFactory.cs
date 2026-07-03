using KurrentDB.Client;

namespace ESAnalyser.Live;

public static class KurrentDbClientFactory
{
    public static KurrentDBClient Create(string connectionString)
    {
        var settings = KurrentDBClientSettings.Create(connectionString);
        return new KurrentDBClient(settings);
    }
}
