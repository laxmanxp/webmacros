using System.Globalization;
using System.Text;

namespace WebMacros.Engine.Runtime;

/// <summary>
/// Formats <c>{{!NOW:format}}</c> with iMacros tokens: yyyy, yy, mm (month), dd, hh (24h), nn (minutes), ss,
/// dow (day of week 1=Sunday..7), doy (day of year). Any other character is copied literally.
/// </summary>
public static class NowFormatter
{
    public static string Format(DateTimeOffset now, string format)
    {
        var sb = new StringBuilder();
        var i = 0;
        while (i < format.Length)
        {
            bool At(string token) => string.Compare(format, i, token, 0, token.Length, StringComparison.OrdinalIgnoreCase) == 0;
            if (At("yyyy")) { sb.Append(now.Year.ToString("0000", CultureInfo.InvariantCulture)); i += 4; }
            else if (At("yy")) { sb.Append((now.Year % 100).ToString("00", CultureInfo.InvariantCulture)); i += 2; }
            else if (At("mm")) { sb.Append(now.Month.ToString("00", CultureInfo.InvariantCulture)); i += 2; }
            else if (At("dd")) { sb.Append(now.Day.ToString("00", CultureInfo.InvariantCulture)); i += 2; }
            else if (At("hh")) { sb.Append(now.Hour.ToString("00", CultureInfo.InvariantCulture)); i += 2; }
            else if (At("nn")) { sb.Append(now.Minute.ToString("00", CultureInfo.InvariantCulture)); i += 2; }
            else if (At("ss")) { sb.Append(now.Second.ToString("00", CultureInfo.InvariantCulture)); i += 2; }
            else if (At("dow")) { sb.Append(((int)now.DayOfWeek + 1).ToString(CultureInfo.InvariantCulture)); i += 3; }
            else if (At("doy")) { sb.Append(now.DayOfYear.ToString("000", CultureInfo.InvariantCulture)); i += 3; }
            else { sb.Append(format[i]); i++; }
        }
        return sb.ToString();
    }
}
