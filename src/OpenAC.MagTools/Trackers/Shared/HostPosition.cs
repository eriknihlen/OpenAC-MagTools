using AcDream.Plugin.Abstractions;

namespace OpenAC.MagTools.Trackers.Shared;

/// <summary>
/// Converts the host's <see cref="PluginNavigationPosition"/> (already in
/// GLOBAL compass units — <c>AppAutomationSurface.ProjectNavigationPosition</c>
/// divides by 240 at the source) back to the original's landblock-local
/// storage shape: a full 32-bit cell id plus per-cell local X/Y/Z in metres
/// (<c>wo.RawCoordinates()</c>'s own convention), so a corpse/player tracker's
/// exported XML stays numerically interchangeable with the original.
/// </summary>
/// <remarks>
/// This is the algebraic inverse of the host's own forward projection
/// (<c>local = compass*240 + 84 - (block-127)*192</c>) — round-tripping it
/// back through <see cref="Coords.FromLandblock"/> reproduces the exact same
/// compass value the host started with, since <c>Coords.FromLandblock</c>
/// expects the FULL cell id (it does its own <c>&gt;&gt; 16</c>/<c>&gt;&gt;
/// 24</c>-equivalent division), not a pre-shifted landblock number. See
/// docs/deviations.md.
/// </remarks>
public readonly record struct LandblockLocalPosition(int CellId, double X, double Y, double Z);

public static class HostPosition
{
    public static LandblockLocalPosition ToLandblockLocal(PluginNavigationPosition position)
    {
        uint cellId = position.CellId;
        uint blockX = (cellId >> 24) & 0xFFu;
        uint blockY = (cellId >> 16) & 0xFFu;

        double localX = position.EastWest * 240d - (blockX - 127d) * 192d + 84d;
        double localY = position.NorthSouth * 240d - (blockY - 127d) * 192d + 84d;
        double localZ = position.Elevation * 240d;

        return new LandblockLocalPosition((int)cellId, localX, localY, localZ);
    }
}
