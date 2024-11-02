using Digital5HP.CronJobs;
using Digital5HP.Text.M3U;
using Microsoft.Extensions.Options;

namespace PPV.M3U.Api;

public class UpdatePlaylistJob(ILogger<UpdatePlaylistJob> logger, IOptions<DownloadSettings> downloadOptions, IOptions<OutputSettings> outputOptions, IHttpClientFactory httpClientFactory) : ICronJob
{
    private readonly ILogger<UpdatePlaylistJob> logger = logger;
    private readonly IHttpClientFactory httpClientFactory = httpClientFactory;
    private readonly DownloadSettings downloadSettings = downloadOptions.Value;
    private readonly OutputSettings outputSettings = outputOptions.Value;
    private int _executionCount;

    public async Task RunAsync(CancellationToken token = default)
    {
        int count = Interlocked.Increment(ref _executionCount);

        this.logger.LogInformation("Playlist Update Job is working. Count: {Count}", count);

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
                stream = await client.GetStreamAsync(this.downloadSettings.Url, token);
            }

            var originPlaylist = await Serializer.DeserializeAsync(stream, token);

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

            this.logger.LogInformation("Playlist Update Job completed. Count: {Count}", count);

        }
        catch (OperationCanceledException)
        {
            this.logger.LogWarning("Playlist Update Job canceled.");
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Playlist Update Job failed to run.");
        }
    }
}
