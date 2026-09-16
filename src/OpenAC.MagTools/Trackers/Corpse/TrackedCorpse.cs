namespace OpenAC.MagTools.Trackers.Corpse;

/// <summary>Ports <c>Trackers/Corpse/TrackedCorpse.cs</c> verbatim.</summary>
public sealed class TrackedCorpse
{
    public TrackedCorpse(uint id)
    {
        Id = id;
    }

    public TrackedCorpse(
        uint id,
        DateTime timeStamp,
        int landBlock,
        double locationX,
        double locationY,
        double locationZ,
        string description,
        bool opened = false)
        : this(id)
    {
        TimeStamp = timeStamp;
        LandBlock = landBlock;
        LocationX = locationX;
        LocationY = locationY;
        LocationZ = locationZ;
        Description = description;
        Opened = opened;
    }

    public uint Id { get; }
    public DateTime TimeStamp { get; set; }
    public int LandBlock { get; set; }
    public double LocationX { get; set; }
    public double LocationY { get; set; }
    public double LocationZ { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool Opened { get; set; }
}
