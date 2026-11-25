using System.Text.RegularExpressions;

namespace MafLabs.Notifications;

/// <summary>A message ready to send: the subject, the title and the blocks, with the context that frames it.</summary>
/// <param name="Subject">The subject.</param>
/// <param name="Title">The heading.</param>
/// <param name="Blocks">The body, in order.</param>
/// <param name="Context">What the message is about: the product, the category and the level.</param>
public sealed record RenderedMail(string Subject, string Title, IReadOnlyList<MessageBlock> Blocks, MailContext Context)
{
    /// <summary>The text alternative of the mail, framed by its context (header line, signature, footer). For a preview or a test: a provider makes its own.</summary>
    public string Text => MailLayout.RenderText(Context, Title, Blocks);

    /// <summary>The HTML mail. For a preview or a test: the mail provider makes its own from the same content.</summary>
    public string Html => MailLayout.RenderHtml(Context, Title, Blocks);

    /// <summary>
    /// The notification that carries this message to one recipient: the bare text as <see cref="Notification.Body"/> (no mail framing), the level and the
    /// category title, and the blocks as <see cref="Notification.Content"/> for a provider that dresses them.
    /// </summary>
    public Notification ToNotification(string recipient) =>
        new(recipient, Subject, MailLayout.BodyText(Blocks), Context.Level, Context.CategoryTitle, new NotificationContent(Context.Product, Title, Blocks));
}

/// <summary>
/// A mail written once, with <c>{{name}}</c> variables: one template per kind of problem, rendered as a plain text and as an HTML mail
/// (<see cref="MailLayout"/>) from the same definition. The body is made of paragraphs separated by a blank line. A paragraph whose first
/// line starts with <c>&gt; </c> is the call to action, set apart (the marker is not shown). A paragraph that is only <c>{{@name}}</c> is a
/// list: each item is a line (<c>- item</c>) in the text and a bullet in the HTML. A value is cleaned (no control character, so no header
/// can be injected through a subject), cut to a length, and never written raw in the HTML. A variable the template names but the caller did
/// not give is a mistake of the program, told by an exception, never a half-filled mail.
/// </summary>
public sealed partial class MailTemplate
{
    /// <summary>The longest a value or a list item may be once rendered; a longer one is cut and ends with an ellipsis.</summary>
    public const int MaxValueLength = 300;

    private readonly string _subject;
    private readonly string _title;
    private readonly List<IPart> _parts;

    /// <summary>Creates the template. <paramref name="title"/> is the heading of the HTML mail; it is the subject when it is not given.</summary>
    public MailTemplate(string subject, string body, string? title = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        _subject = subject;
        _title = string.IsNullOrWhiteSpace(title) ? subject : title;
        if (_subject.Contains("{{@", StringComparison.Ordinal))
        {
            throw new ArgumentException("A list variable ({{@name}}) cannot be in the subject or in the title: only in the body.", nameof(subject));
        }

        if (_title.Contains("{{@", StringComparison.Ordinal))
        {
            throw new ArgumentException("A list variable ({{@name}}) cannot be in the subject or in the title: only in the body.", nameof(title));
        }

        _parts = Parse(body);
        var texts = _parts.OfType<TextPart>().Select(part => part.Text);
        Variables = [.. Names(subject).Concat(Names(_title)).Concat(texts.SelectMany(Names)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        ListVariables = [.. _parts.OfType<ListPart>().Select(part => part.Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    /// <summary>The variables the template uses, sorted: what a caller has to give.</summary>
    public IReadOnlyList<string> Variables { get; }

    /// <summary>The lists the template uses (<c>{{@name}}</c>), sorted: what a caller has to give as lists.</summary>
    public IReadOnlyList<string> ListVariables { get; }

    /// <summary>
    /// Fills the template as plain text. <paramref name="values"/> and <paramref name="lists"/> may hold more than needed; they may not miss one.
    /// </summary>
    public (string Subject, string Body) Render(IReadOnlyDictionary<string, string> values, IReadOnlyDictionary<string, IReadOnlyList<string>>? lists = null)
    {
        var (subject, _, blocks) = Build(values, lists);
        return (subject, MailLayout.BodyText(blocks));
    }

    /// <summary>
    /// Fills the template as a complete mail: the subject, the text alternative and the HTML, framed by <paramref name="context"/> (product,
    /// category, level). The same missing-variable rule as <see cref="Render"/>.
    /// </summary>
    public RenderedMail RenderMail(MailContext context, IReadOnlyDictionary<string, string> values, IReadOnlyDictionary<string, IReadOnlyList<string>>? lists = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        var (subject, title, blocks) = Build(values, lists);
        return new RenderedMail(subject, title, blocks, context);
    }

    private (string Subject, string Title, List<MessageBlock> Blocks) Build(IReadOnlyDictionary<string, string> values, IReadOnlyDictionary<string, IReadOnlyList<string>>? lists)
    {
        ArgumentNullException.ThrowIfNull(values);
        var missing = Variables.Where(name => !values.ContainsKey(name)).Concat(ListVariables.Where(name => lists is null || !lists.ContainsKey(name))).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"The mail template needs: {string.Join(", ", missing)}.");
        }

        var blocks = new List<MessageBlock>();
        foreach (var part in _parts)
        {
            switch (part)
            {
                case ListPart list:
                    var lines = lists![list.Name].Select(item => MailText.Clean(item, singleLine: true, MaxValueLength)).Where(item => item.Length > 0).ToList();
                    if (lines.Count > 0)
                    {
                        blocks.Add(new MessageItems(lines));
                    }

                    break;
                case TextPart { CallToAction: true } text:
                    blocks.Add(new MessageCallToAction(Fill(text.Text, values, singleLine: false)));
                    break;
                case TextPart text:
                    blocks.Add(new MessageParagraph(Fill(text.Text, values, singleLine: false)));
                    break;
            }
        }

        return (Fill(_subject, values, singleLine: true), Fill(_title, values, singleLine: true), blocks);
    }

    /// <summary>Cuts the body into paragraphs at the blank lines, before any value is put in: a value can never make a paragraph, a list or a call to action.</summary>
    private static List<IPart> Parse(string body)
    {
        var parts = new List<IPart>();
        foreach (var paragraph in BlankLines().Split(body.Replace("\r\n", "\n", StringComparison.Ordinal).Trim()))
        {
            var text = paragraph.Trim();
            if (text.Length == 0)
            {
                continue;
            }

            if (ListMarker().Match(text) is { Success: true } list)
            {
                parts.Add(new ListPart(list.Groups[1].Value));
            }
            else if (text.Contains("{{@", StringComparison.Ordinal))
            {
                throw new ArgumentException("A list variable ({{@name}}) must be alone in its paragraph, between blank lines.", nameof(body));
            }
            else if (text.StartsWith("> ", StringComparison.Ordinal))
            {
                parts.Add(new TextPart(string.Join('\n', text.Split('\n').Select(line => line.StartsWith("> ", StringComparison.Ordinal) ? line[2..] : line)), CallToAction: true));
            }
            else
            {
                parts.Add(new TextPart(text, CallToAction: false));
            }
        }

        return parts;
    }

    private static string Fill(string text, IReadOnlyDictionary<string, string> values, bool singleLine) =>
        Placeholder().Replace(text, match => MailText.Clean(values[match.Groups[1].Value], singleLine, MaxValueLength));

    private static IEnumerable<string> Names(string text) => Placeholder().Matches(text).Select(match => match.Groups[1].Value);

    [GeneratedRegex(@"\{\{([A-Za-z][A-Za-z0-9_]*)\}\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"^\{\{@([A-Za-z][A-Za-z0-9_]*)\}\}$")]
    private static partial Regex ListMarker();

    [GeneratedRegex(@"\n[ \t]*\n(?:[ \t]*\n)*")]
    private static partial Regex BlankLines();

    private interface IPart;

    private sealed record TextPart(string Text, bool CallToAction) : IPart;

    private sealed record ListPart(string Name) : IPart;
}
