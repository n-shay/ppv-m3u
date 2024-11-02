namespace PPV.M3U.Api;

using Digital5HP.CronJobs;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Services.AddAuthorization();

        builder.Services.Configure<DownloadSettings>(builder.Configuration.GetRequiredSection("Download"));
        builder.Services.Configure<OutputSettings>(builder.Configuration.GetRequiredSection("Playlists"));

        builder.Services.AddHttpClient();

        builder.Services.AddCronJob<UpdatePlaylistJob>(serviceProvider => serviceProvider.GetRequiredService<IOptions<DownloadSettings>>().Value.Cron);

        var app = builder.Build();

        // Before starting web app, retrieve and initialize playlist(s)

        var job = app.Services.GetRequiredService<UpdatePlaylistJob>();
        await job.RunAsync();

        // Configure the HTTP request pipeline.

        app.UseAuthorization();

        app.MapGet("/playlist{num:int}.m3u", ([FromRoute]int num) =>
        {
            var path = $"/tmp/playlist{num}.m3u";
            if (!File.Exists(path))
                return Results.NotFound();

            var lastModified = File.GetLastWriteTime(path);
            var fileStream = File.OpenRead(path);

            return Results.Stream(fileStream, contentType: "text/plain", lastModified: lastModified, fileDownloadName: $"playlist{num}.m3u");
        });

        await app.RunAsync();
    }
}