using Microsoft.AspNetCore.Mvc;

namespace PPV.M3U.Api;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Services.AddAuthorization();

        builder.Services.Configure<DownloadSettings>(builder.Configuration.GetRequiredSection("Download"));
        builder.Services.Configure<OutputSettings>(builder.Configuration.GetRequiredSection("Playlists"));

        builder.Services.AddHttpClient();

        builder.Services.AddHostedService<PlaylistProcessor>();

        var app = builder.Build();

        // Configure the HTTP request pipeline.

        app.UseAuthorization();

        var summaries = new[]
        {
            "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
        };

        app.MapGet("/playlist{num:int}.m3u", ([FromRoute]int num) =>
        {
            var path = $"/tmp/playlist{num}.m3u";
            if (!File.Exists(path))
                return Results.NotFound();

            var lastModified = File.GetLastWriteTime(path);
            var fileStream = File.OpenRead(path);

            return Results.Stream(fileStream, contentType: "text/plain", lastModified: lastModified, fileDownloadName: $"playlist{num}.m3u");
        });

        app.Run();
    }
}