using System.Globalization;

namespace OpenAC.MagTools.Trackers.Shared;

/// <summary>
/// Ports <c>Mag.Shared.Util.GetCoords(landblock, x, y)</c> and the Decal SDK's
/// <c>CoordsObject</c> display format verbatim (lat/lng math) with a
/// best-effort <see cref="ToString"/> — the SDK's own formatting lives in a
/// closed-source DLL we don't have decompiled, so the exact string layout is
/// a documented assumption rather than a confirmed port. See
/// docs/deviations.md.
/// </summary>
public readonly record struct Coords(double Latitude, double Longitude)
{
    /// <summary>
    /// <c>lat = ((lbLat - 0x7F) * 192 + y - 84) / 240</c>,
    /// <c>lng = ((lbLng - 0x7F) * 192 + x - 84) / 240</c>, where
    /// <c>lbLng = (landblock / 0x1000000) &amp; 0xFF</c> and
    /// <c>lbLat = (landblock / 0x10000) &amp; 0xFF</c>.
    /// </summary>
    public static Coords FromLandblock(int landblock, double x, double y)
    {
        int lbLng = (int)Math.Floor(landblock / (double)0x1000000) & 0xFF;
        int lbLat = (int)Math.Floor(landblock / (double)0x10000) & 0xFF;

        double lat = ((lbLat - 0x7F) * 192d + y - 84d) / 240d;
        double lng = ((lbLng - 0x7F) * 192d + x - 84d) / 240d;

        return new Coords(lat, lng);
    }

    /// <summary>
    /// Best-effort match of the retail/Decal coordinate display convention,
    /// e.g. "34.5S, 90.1W" — one decimal, N/S then E/W, sign folded into a
    /// compass letter (positive latitude is North, positive longitude is
    /// East). Not independently confirmed against the closed-source
    /// <c>CoordsObject.ToString()</c>; see docs/deviations.md.
    /// </summary>
    public override string ToString()
    {
        char latLetter = Latitude >= 0 ? 'N' : 'S';
        char lngLetter = Longitude >= 0 ? 'E' : 'W';
        return string.Create(CultureInfo.InvariantCulture, $"{Math.Abs(Latitude):F1}{latLetter}, {Math.Abs(Longitude):F1}{lngLetter}");
    }
}
