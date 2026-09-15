using System.Globalization;

namespace OpenAC.MagTools.Settings;

/// <summary>
/// Round-trips a setting value through the same text shapes the original wrote,
/// so an existing <c>Mag-Tools.xml</c> reads back unchanged: booleans as
/// <c>True</c>/<c>False</c>, numbers in the invariant culture.
/// </summary>
internal static class SettingValueCodec
{
    public static string TypeName(Type type)
    {
        if (type == typeof(bool))
            return "bool";
        if (type == typeof(int))
            return "int";
        if (type == typeof(double))
            return "double";
        if (type == typeof(string))
            return "string";
        throw new NotSupportedException(
            $"Setting type {type.Name} is not one of bool/int/double/string.");
    }

    public static string Format<T>(T value)
    {
        return value switch
        {
            bool flag => flag ? "True" : "False",
            int number => number.ToString(CultureInfo.InvariantCulture),
            double number => number.ToString("R", CultureInfo.InvariantCulture),
            string text => text,
            null => string.Empty,
            _ => throw new NotSupportedException(
                $"Setting type {typeof(T).Name} is not one of bool/int/double/string."),
        };
    }

    public static bool TryParse<T>(string text, out T value)
    {
        value = default!;
        if (text is null)
            return false;

        if (typeof(T) == typeof(bool))
        {
            if (!bool.TryParse(text.Trim(), out bool flag))
                return false;
            value = (T)(object)flag;
            return true;
        }

        if (typeof(T) == typeof(int))
        {
            if (!int.TryParse(
                    text.Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int number))
                return false;
            value = (T)(object)number;
            return true;
        }

        if (typeof(T) == typeof(double))
        {
            if (!double.TryParse(
                    text.Trim(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double number))
                return false;
            value = (T)(object)number;
            return true;
        }

        if (typeof(T) == typeof(string))
        {
            value = (T)(object)text;
            return true;
        }

        throw new NotSupportedException(
            $"Setting type {typeof(T).Name} is not one of bool/int/double/string.");
    }
}
