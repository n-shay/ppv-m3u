namespace PPV.M3U.Api;

public class DownloadSettings
{
    public string Cron { get; set; }

    public string Url { get; set; }

    public int Retries { get; set; } = 3;
}
