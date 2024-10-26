namespace PPV.M3U.Api;

using Digital5HP.Text.M3U;
using Microsoft.Extensions.Options;

public class PlaylistProcessor(ILogger<PlaylistProcessor> logger, IOptions<DownloadSettings> downloadOptions, IOptions<OutputSettings> outputOptions, IHttpClientFactory httpClientFactory) : BackgroundService
{
    private readonly ILogger<PlaylistProcessor> logger = logger;
    private readonly IHttpClientFactory httpClientFactory = httpClientFactory;
    private readonly DownloadSettings downloadSettings = downloadOptions.Value;
    private readonly OutputSettings outputSettings = outputOptions.Value;
    private int _executionCount;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        this.logger.LogInformation("Playlist Processor Service is running.");

        // When the timer should have no due-time, then do the work once now.
        await DoWorkAsync(stoppingToken);

        using PeriodicTimer timer = new(TimeSpan.FromMinutes(downloadSettings.IntervalMinutes));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await DoWorkAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            this.logger.LogInformation("Playlist Processor Service is stopping.");
        }
    }

    private async Task DoWorkAsync(CancellationToken cancellationToken)
    {
        int count = Interlocked.Increment(ref _executionCount);

        this.logger.LogInformation("Playlist Processor Service is working. Count: {Count}", count);

        try
        {
            // download playlist
            using var client = this.httpClientFactory.CreateClient();

            Stream stream;
            if (Path.IsPathFullyQualified(this.downloadSettings.Url) && Path.Exists(this.downloadSettings.Url))
            {
                stream = File.OpenRead(this.downloadSettings.Url);
            }
            else
            {
                stream = await client.GetStreamAsync(this.downloadSettings.Url, cancellationToken);
            }

            var originPlaylist = await Serializer.DeserializeAsync(stream, cancellationToken);

            this.logger.LogInformation("Downloaded playlist with {Channels} channels from {DownloadPath}.", originPlaylist.Channels.Count, this.downloadSettings.Url);

            // verify temp folder exists
            if (!Directory.Exists("/tmp"))
            {
                Directory.CreateDirectory("/tmp");
            }

            // create playlist(s)
            var playlistCount = 0;
            foreach (var playlistSettings in this.outputSettings)
            {
                var playlist = new Document();

                foreach (var channel in originPlaylist.Channels.Where(c => c.GroupTitle == playlistSettings.Group)
                    .Take(playlistSettings.ChannelLimit))
                {
                    playlist.Channels.Add(channel);
                }

                var path = $"/tmp/playlist{playlistCount}.m3u";

                using var outputStream = File.Create(path);
                await Serializer.SerializeAsync(playlist, outputStream);

                this.logger.LogInformation("Created playlist for group '{Group}' with {Channels} channels ({Path}).", playlistSettings.Group, playlist.Channels.Count, path);

                playlistCount++;
            }

            await stream.DisposeAsync();

            this.logger.LogInformation("Playlist Processor Service work completed. Count: {Count}", count);

        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch(Exception ex)
        {
            this.logger.LogError(ex, "Playlist Processor Service failed to run.");
        }
    }
}
