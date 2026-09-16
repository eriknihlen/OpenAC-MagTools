namespace OpenAC.MagTools.Trackers.Player;

/// <summary>Ports <c>Trackers/Player/TrackedPlayer.cs</c> verbatim.</summary>
public sealed class TrackedPlayer
{
    public TrackedPlayer(string name)
    {
        Name = name;
    }

    public TrackedPlayer(
        string name,
        DateTime lastSeen,
        int landBlock,
        double locationX,
        double locationY,
        double locationZ,
        uint id)
        : this(name)
    {
        LastSeen = lastSeen;
        LandBlock = landBlock;
        LocationX = locationX;
        LocationY = locationY;
        LocationZ = locationZ;
        Id = id;
    }

    public string Name { get; }
    public DateTime LastSeen { get; set; }
    public int LandBlock { get; set; }
    public double LocationX { get; set; }
    public double LocationY { get; set; }
    public double LocationZ { get; set; }
    public uint Id { get; set; }
}
