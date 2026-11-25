using Microsoft.Extensions.Options;

namespace MafLabs.Notifications;

/// <summary>
/// A message for a person: where it goes, a short subject and a plain text. <paramref name="HtmlBody"/> is the same message as HTML, when there is
/// one: the mail then carries both (text and HTML alternatives), and the client shows the one it prefers. A provider that is not mail (a chat, a push)
/// uses <paramref name="Level"/>, <paramref name="Category"/>, <paramref name="Subject"/> and <paramref name="Body"/>, and ignores <paramref name="HtmlBody"/>.
/// </summary>
/// <param name="Recipient">Where it goes.</param>
/// <param name="Subject">A short subject.</param>
/// <param name="Body">The plain text.</param>
/// <param name="HtmlBody">
/// The same message as HTML. It is sent as it is, never encoded or cleaned by the sender: build it with <see cref="MailTemplate"/> or <see cref="MailLayout"/>,
/// which encode every value, and never concatenate a value that is not yours into it.
/// </param>
/// <param name="Level">How much it matters, for a channel that has no HTML to show it with, or null when not known.</param>
/// <param name="Category">The display name of the category it belongs to, or null when not known.</param>
public sealed record Notification(string Recipient, string Subject, string Body, string? HtmlBody = null, NotificationLevel? Level = null, string? Category = null);

/// <summary>Whether a notification was handed over, and when it was not, why in a short sentence (never a secret).</summary>
public sealed record NotificationResult(bool Delivered, string? Reason = null)
{
    public static NotificationResult Done { get; } = new(true);

    public static NotificationResult Failed(string reason) => new(false, reason);
}

/// <summary>Delivers a notification to a person. The only thing an application knows about mail, a chat or a push.</summary>
public interface INotificationSender
{
    /// <summary>Never throws for a delivery that fails: it says so in the result. A cancelled call is the only exception.</summary>
    Task<NotificationResult> SendAsync(Notification notification, CancellationToken cancellationToken);
}

/// <summary>
/// A way to deliver notifications, by name (<c>Notifications:Provider</c>). Another channel is one class that implements this and one line,
/// <c>services.AddSingleton&lt;INotificationProvider, MyProvider&gt;()</c>: it says what settings it needs and builds the <see cref="INotificationSender"/> that talks to it .
/// </summary>
public interface INotificationProvider
{
    /// <summary>The value of <c>Notifications:Provider</c> that selects it; not case sensitive.</summary>
    string Name { get; }

    /// <summary>What is wrong with the settings for this provider, or null when they are right. Called only when notifications are turned on.</summary>
    string? Validate(NotificationOptions options);

    INotificationSender Create(IOptions<NotificationOptions> options);

    /// <summary>What is wrong with the secrets the provider needs (a file that cannot be read), or null. Looked at by the readiness probe, never sent anywhere.</summary>
    string? CheckSecrets(NotificationOptions options);
}

/// <summary>The providers that come with the library, and how one is found by name.</summary>
public static class NotificationProviders
{
    public const string Smtp = "Smtp";

    public static IReadOnlyList<INotificationProvider> BuiltIn { get; } = [new SmtpNotificationProvider()];

    public static INotificationProvider? Find(IEnumerable<INotificationProvider> providers, string? name) =>
        providers.FirstOrDefault(provider => string.Equals(provider.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The sender of the provider named in the options; a sender that always fails, in words, when there is none.</summary>
    public static INotificationSender Select(IEnumerable<INotificationProvider> providers, IOptions<NotificationOptions> options) =>
        Find(providers, options.Value.Provider)?.Create(options) ?? new UnavailableNotificationSender(options.Value.Provider);
}

internal sealed class UnavailableNotificationSender(string provider) : INotificationSender
{
    public Task<NotificationResult> SendAsync(Notification notification, CancellationToken cancellationToken) =>
        Task.FromResult(NotificationResult.Failed($"There is no notification provider called '{provider}'."));
}
