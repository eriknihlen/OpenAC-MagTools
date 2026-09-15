using System.Xml;

namespace OpenAC.MagTools.Settings;

/// <summary>
/// Builds the element paths the original used to scope a setting to one
/// account/server/character or to one server.
/// </summary>
/// <remarks>
/// Path segments are literal element names, so a server or character name has
/// to be a legal XML name. <see cref="XmlConvert.EncodeName"/> makes it one and
/// leaves ordinary names untouched, and the leading underscore keeps a name
/// that starts with a digit legal.
/// </remarks>
public static class SettingsScope
{
    /// <summary>
    /// <c>_&lt;account&gt;_&lt;server&gt;/&lt;character&gt;</c>.
    /// </summary>
    public static string Character(string account, string server, string character)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(character);

        return "_" + XmlConvert.EncodeName(account)
            + "_" + XmlConvert.EncodeName(server)
            + "/" + XmlConvert.EncodeName(character);
    }

    /// <summary><c>_&lt;server&gt;</c>.</summary>
    public static string Server(string server)
    {
        ArgumentNullException.ThrowIfNull(server);
        return "_" + XmlConvert.EncodeName(server);
    }
}
