using System.Text;
using System.Text.RegularExpressions;

namespace MafLabs.Notifications;

/// <summary>
/// A plain-text mail written once, with <c>{{name}}</c> variables: one template per kind of problem. The subject and the body are filled by
/// <see cref="Render"/>. A value is cleaned (no control character, so no header can be injected through a subject) and cut to a length, and a
/// variable the template names but the caller did not give is a mistake of the program, told by an exception, never a half-filled mail.
/// </summary>
public sealed partial class MailTemplate
{
    /// <summary>The longest a value may be once rendered; a longer one is cut and ends with an ellipsis.</summary>
    public const int MaxValueLength = 300;

    private readonly string _subject;
    private readonly string _body;

    public MailTemplate(string subject, string body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        _subject = subject;
        _body = body;
        Variables = [.. Names(subject).Concat(Names(body)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    /// <summary>The variables the template uses, sorted: what a caller has to give.</summary>
    public IReadOnlyList<string> Variables { get; }

    /// <summary>Fills the template. <paramref name="values"/> may hold more than needed; it may not miss one.</summary>
    public (string Subject, string Body) Render(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var missing = Variables.Where(name => !values.ContainsKey(name)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"The mail template needs: {string.Join(", ", missing)}.");
        }

        return (Fill(_subject, values, singleLine: true), Fill(_body, values, singleLine: false));
    }

    private static string Fill(string text, IReadOnlyDictionary<string, string> values, bool singleLine) =>
        Placeholder().Replace(text, match => Clean(values[match.Groups[1].Value], singleLine));

    private static IEnumerable<string> Names(string text) => Placeholder().Matches(text).Select(match => match.Groups[1].Value);

    private static string Clean(string value, bool singleLine)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsControl(character) && !(character == '\n' && !singleLine) ? ' ' : character);
        }

        var cleaned = builder.ToString().Trim();
        return cleaned.Length <= MaxValueLength ? cleaned : cleaned[..(MaxValueLength - 1)] + "…";
    }

    [GeneratedRegex(@"\{\{([A-Za-z][A-Za-z0-9_]*)\}\}")]
    private static partial Regex Placeholder();
}
