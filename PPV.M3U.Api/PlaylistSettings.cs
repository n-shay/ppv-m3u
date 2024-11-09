namespace PPV.M3U.Api;

public class PlaylistSettings
{
    public int ChannelLimit { get; set; } = 200;

    public string Group { get; set; }

    public int? StartingChannelNumber { get; set; }
}
