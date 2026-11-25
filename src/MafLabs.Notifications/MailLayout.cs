using System.Net;
using System.Text;

namespace MafLabs.Notifications;

/// <summary>What a mail is about, once for every mail of a program: who writes, for which category, and how much it matters.</summary>
/// <param name="Product">The name of the program that writes (<c>Portarr</c>), shown in the header band and in the footer.</param>
/// <param name="CategoryTitle">The display name of the category (<c>Data</c>), shown beside the level and in the footer.</param>
/// <param name="Level">The accent of the mail.</param>
public sealed record MailContext(string Product, string CategoryTitle, MailLevel Level);

/// <summary>A part of the body of a mail, in the order it is written. Plain text in, the layout does the encoding for each format.</summary>
public abstract record MailBlock
{
    private protected MailBlock()
    {
    }
}

/// <summary>A paragraph. A line break inside the text is kept.</summary>
public sealed record MailParagraph(string Text) : MailBlock;

/// <summary>A list of short lines (names of what is concerned): <c>- item</c> lines in the text, a bulleted list in the HTML.</summary>
public sealed record MailItems(IReadOnlyList<string> Lines) : MailBlock;

/// <summary>The one thing to do, set apart. Text only: no link is needed.</summary>
public sealed record MailCallToAction(string Text) : MailBlock;

/// <summary>
/// The look of every mail: one HTML document and its text alternative, from the same content. The HTML is made to survive the mail clients:
/// tables for the layout (no flex, no grid), inline styles, nothing loaded from outside (no image, font or script), at most 600 pixels wide,
/// explicit colors for text and background (so nothing is white on white), and a dark palette for the clients that ask for one. Everything
/// that comes from the caller is HTML-encoded, whatever it holds.
/// </summary>
public static class MailLayout
{
    private const string Font = "-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif";

    /// <summary>The accent color of a level, as a hex color: dark enough to carry white text.</summary>
    public static string AccentOf(MailLevel level) => level switch
    {
        MailLevel.Ok => "#15803d",
        MailLevel.Info => "#1d4ed8",
        MailLevel.Warning => "#b45309",
        MailLevel.Danger => "#b91c1c",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown mail level."),
    };

    /// <summary>The word that names a level in the mail, in the header band and in the text.</summary>
    public static string LabelOf(MailLevel level) => level switch
    {
        MailLevel.Ok => "OK",
        MailLevel.Info => "Information",
        MailLevel.Warning => "Warning",
        MailLevel.Danger => "Critical",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown mail level."),
    };

    /// <summary>The sentence at the foot of every mail: who wrote it and why the reader gets it.</summary>
    public static string FooterOf(MailContext context) =>
        $"Sent by {Line(context.Product)}; you receive this because notifications are enabled for {Line(context.CategoryTitle)}.";

    /// <summary>The text alternative: the level, the title, the blocks and the footer, readable as it is.</summary>
    public static string RenderText(MailContext context, string title, IReadOnlyList<MailBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(blocks);
        var builder = new StringBuilder();
        builder.Append('[').Append(LabelOf(context.Level)).Append("] ").Append(Line(context.Product)).Append(" - ").Append(Line(context.CategoryTitle)).Append("\n\n");
        builder.Append(Line(title)).Append("\n\n");
        var body = BodyText(blocks);
        if (body.Length > 0)
        {
            builder.Append(body).Append("\n\n");
        }

        builder.Append("-- \n").Append(FooterOf(context)).Append('\n');
        return builder.ToString();
    }

    /// <summary>The blocks as text, separated by a blank line, with no header and no footer.</summary>
    internal static string BodyText(IReadOnlyList<MailBlock> blocks) =>
        string.Join("\n\n", blocks.Select(BlockText).Where(text => text.Length > 0));

    private static string BlockText(MailBlock block) => block switch
    {
        MailParagraph paragraph => Text(paragraph.Text),
        MailCallToAction action => Text(action.Text),
        MailItems items => string.Join('\n', items.Lines.Select(line => $"- {Line(line)}")),
        _ => throw new ArgumentException("Unknown kind of mail block.", nameof(block)),
    };

    /// <summary>The HTML document. Every value is encoded; nothing the caller gives is ever written raw.</summary>
    public static string RenderHtml(MailContext context, string title, IReadOnlyList<MailBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(blocks);
        var accent = AccentOf(context.Level);
        var builder = new StringBuilder(4096);
        builder.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n");
        builder.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        builder.Append("<meta name=\"color-scheme\" content=\"light dark\">\n<meta name=\"supported-color-schemes\" content=\"light dark\">\n");
        builder.Append("<title>").Append(Html(title)).Append("</title>\n");
        builder.Append("<style>:root{color-scheme:light dark;supported-color-schemes:light dark}");
        builder.Append("@media (prefers-color-scheme:dark){.mp{background-color:#111827!important}.mc{background-color:#1f2937!important;color:#e5e7eb!important}");
        builder.Append(".mt{color:#e5e7eb!important}.mm{color:#9ca3af!important}.mb{background-color:#111827!important;color:#e5e7eb!important}}</style>\n");
        builder.Append("</head>\n<body class=\"mp\" style=\"margin:0;padding:0;background-color:#f3f4f6;\">\n");
        builder.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" class=\"mp\" bgcolor=\"#f3f4f6\" style=\"background-color:#f3f4f6;\">\n");
        builder.Append("<tr><td align=\"center\" style=\"padding:24px 12px;\">\n");
        builder.Append("<table role=\"presentation\" width=\"600\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"width:100%;max-width:600px;\">\n");

        // Header band: the product and, at the right, the category and the level.
        builder.Append("<tr><td bgcolor=\"").Append(accent).Append("\" style=\"background-color:").Append(accent).Append(";padding:16px 24px;color:#ffffff;font-family:").Append(Font).Append(";\">\n");
        builder.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\"><tr>");
        builder.Append("<td style=\"font-size:18px;font-weight:700;color:#ffffff;font-family:").Append(Font).Append(";\">").Append(Html(context.Product)).Append("</td>");
        builder.Append("<td align=\"right\" style=\"font-size:13px;color:#ffffff;font-family:").Append(Font).Append(";\">").Append(Html(context.CategoryTitle)).Append(" &middot; <strong>").Append(Html(LabelOf(context.Level))).Append("</strong></td>");
        builder.Append("</tr></table>\n</td></tr>\n");

        // Card: the title and the blocks.
        builder.Append("<tr><td class=\"mc\" bgcolor=\"#ffffff\" style=\"background-color:#ffffff;color:#1f2937;padding:24px 24px 8px 24px;font-family:").Append(Font).Append(";font-size:15px;line-height:1.5;\">\n");
        builder.Append("<h1 class=\"mt\" style=\"margin:0 0 16px 0;font-size:20px;line-height:1.3;font-weight:700;color:#111827;font-family:").Append(Font).Append(";\">").Append(Html(title)).Append("</h1>\n");
        foreach (var block in blocks)
        {
            AppendBlock(builder, block, accent);
        }

        builder.Append("</td></tr>\n");

        // Footer.
        builder.Append("<tr><td class=\"mm\" style=\"padding:16px 24px;font-size:12px;line-height:1.5;color:#6b7280;font-family:").Append(Font).Append(";\">").Append(Html(FooterOf(context))).Append("</td></tr>\n");
        builder.Append("</table>\n</td></tr>\n</table>\n</body>\n</html>\n");
        return builder.ToString();
    }

    private static void AppendBlock(StringBuilder builder, MailBlock block, string accent)
    {
        string content;
        switch (block)
        {
            case MailParagraph paragraph:
                if (Text(paragraph.Text).Length == 0)
                {
                    return;
                }

                content = $"<p class=\"mt\" style=\"margin:0;color:#1f2937;\">{Html(paragraph.Text)}</p>";
                break;
            case MailCallToAction action:
                if (Text(action.Text).Length == 0)
                {
                    return;
                }

                content = $"<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\"><tr><td class=\"mb mt\" bgcolor=\"#f3f4f6\" style=\"background-color:#f3f4f6;color:#111827;border-left:4px solid {accent};padding:12px 16px;font-weight:600;font-family:{Font};font-size:15px;line-height:1.5;\">{Html(action.Text)}</td></tr></table>";
                break;
            case MailItems items:
                if (items.Lines.Count == 0)
                {
                    return;
                }

                var rows = string.Concat(items.Lines.Select(line => $"<li class=\"mt\" style=\"margin:0;padding:3px 0;color:#1f2937;\">{Html(line)}</li>"));
                content = $"<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\"><tr><td class=\"mb mt\" bgcolor=\"#f3f4f6\" style=\"background-color:#f3f4f6;color:#1f2937;border-left:4px solid {accent};padding:12px 16px 12px 12px;font-family:{Font};font-size:15px;line-height:1.5;\"><ul style=\"margin:0;padding:0 0 0 20px;\">{rows}</ul></td></tr></table>";
                break;
            default:
                throw new ArgumentException("Unknown kind of mail block.", nameof(block));
        }

        // A row of its own gives the spacing between blocks: a table cell is the one box a client cannot ignore.
        builder.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\"><tr><td style=\"padding:0 0 16px 0;\">")
            .Append(content).Append("</td></tr></table>\n");
    }

    /// <summary>A value as text on one line.</summary>
    private static string Line(string value) => MailText.Clean(value, singleLine: true);

    /// <summary>A value as text, line breaks kept.</summary>
    private static string Text(string value) => MailText.Clean(value, singleLine: false);

    /// <summary>A value as HTML: cleaned of control characters, encoded, line breaks made into <c>&lt;br&gt;</c>.</summary>
    private static string Html(string value) => WebUtility.HtmlEncode(Text(value)).Replace("\n", "<br>", StringComparison.Ordinal);
}
