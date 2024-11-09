namespace PPV.M3U.Api;

using Digital5HP.CronJobs;
using Digital5HP.Text.M3U;

using Microsoft.Extensions.Options;

using Polly;

public class UpdatePlaylistJob(ILogger<UpdatePlaylistJob> logger, IOptions<DownloadSettings> downloadOptions, IOptions<OutputSettings> outputOptions, IHttpClientFactory httpClientFactory) : ICronJob
{
    private readonly ILogger<UpdatePlaylistJob> logger = logger;
    private readonly IHttpClientFactory httpClientFactory = httpClientFactory;
    private readonly DownloadSettings downloadSettings = downloadOptions.Value;
    private readonly OutputSettings outputSettings = outputOptions.Value;
    private int executionCount;

    public async Task RunAsync(CancellationToken token = default)
    {
        var count = Interlocked.Increment(ref this.executionCount);

        this.logger.LogInformation("Playlist Update Job is working. Count: {Count}", count);

        try
        {
            // download playlist
            Document originPlaylist;
            if (Path.IsPathFullyQualified(this.downloadSettings.Url) && Path.Exists(this.downloadSettings.Url))
            {
                using var stream = File.OpenRead(this.downloadSettings.Url);

                originPlaylist = await Serializer.DeserializeAsync(stream, token);
            }
            else
            {
                var client = this.httpClientFactory.CreateClient(nameof(UpdatePlaylistJob));
                
                var retryPolicy = Policy<Document>.Handle<HttpRequestException>()
                    .Or<HttpIOException>()
                    .WaitAndRetryAsync(this.downloadSettings.Retries,
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    (delegateResult, timeSpan, attempt, _) =>
                    {
                        this.logger.LogError(delegateResult.Exception, "Playlist download failed on attempt {RetryAttempt} after {Timeout}", attempt, timeSpan);
                    });

                originPlaylist = await retryPolicy.ExecuteAsync(async (cancellationToken) =>
                {
                    await using var stream = await client.GetStreamAsync(this.downloadSettings.Url, cancellationToken);

                    return await Serializer.DeserializeAsync(stream, cancellationToken);
                }, token);
            }

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

            this.logger.LogInformation("Playlist Update Job completed. Count: {Count}", count);

        }
        catch (OperationCanceledException)
        {
            this.logger.LogWarning("Playlist Update Job canceled. Count: {Count}", count);
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Playlist Update Job failed to run. Count: {Count}", count);
        }
    }
}
