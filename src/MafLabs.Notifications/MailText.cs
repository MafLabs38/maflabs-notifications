using System.Globalization;
using System.Text;

namespace MafLabs.Notifications;

/// <summary>The cleaning every piece of text goes through before it reaches a mail, whatever the format.</summary>
internal static class MailText
{
    /// <summary>
    /// Replaces every control character by a space (a line break is kept when <paramref name="singleLine"/> is false, a carriage return never),
    /// removes the Unicode format characters (category Cf: the bidirectional overrides that can make a text read differently from what it is, the
    /// zero-width characters, the byte order mark), trims, and cuts to <paramref name="maxLength"/> characters with an ellipsis when it is given.
    /// </summary>
    public static string Clean(string value, bool singleLine, int? maxLength = null)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var rune in value.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format)
            {
                continue;
            }

            builder.Append(Rune.IsControl(rune) && !(rune.Value == '\n' && !singleLine) ? ' ' : rune.ToString());
        }

        var cleaned = builder.ToString().Trim();
        return maxLength is { } max && cleaned.Length > max ? cleaned[..(max - 1)] + "…" : cleaned;
    }
}
