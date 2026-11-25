namespace MafLabs.Notifications;

/// <summary>A part of the body of a message, in the order it is written. Plain text in: each channel does the encoding for its own format.</summary>
public abstract record MessageBlock
{
    private protected MessageBlock()
    {
    }
}

/// <summary>A paragraph. A line break inside the text is kept.</summary>
public sealed record MessageParagraph(string Text) : MessageBlock;

/// <summary>A list of short lines (names of what is concerned): <c>- item</c> lines in a text, a bulleted list in an HTML mail.</summary>
public sealed record MessageItems(IReadOnlyList<string> Lines) : MessageBlock;

/// <summary>The one thing to do, set apart. Text only: no link is needed.</summary>
public sealed record MessageCallToAction(string Text) : MessageBlock;

/// <summary>
/// What a notification says, before any channel has dressed it: who writes (<paramref name="Product"/>), a title, and the blocks in order. A mail provider
/// dresses it with <see cref="MailLayout"/> (header band, footer, HTML); a chat or push provider can lay the same blocks out its own way, from
/// <see cref="Notification.Level"/> and <see cref="Notification.Category"/>, without any mail framing to strip.
/// </summary>
/// <param name="Product">The name of the program that writes (for example <c>Orders</c>).</param>
/// <param name="Title">The heading of the message.</param>
/// <param name="Blocks">The body, in the order it is written.</param>
public sealed record NotificationContent(string Product, string Title, IReadOnlyList<MessageBlock> Blocks);
