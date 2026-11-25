using Microsoft.Extensions.Options;

namespace MafLabs.Notifications;

/// <summary>
/// A message for a person: where it goes, a short subject and a plain text, which is all a channel needs. <paramref name="Level"/> and
/// <paramref name="Category"/> say how much it matters and what it is about; <paramref name="Content"/> is the same message as structured blocks, when there
/// is one. Nothing here is framed for a channel: a mail provider dresses <paramref name="Content"/> (text and HTML) itself, and a chat or a push provider
/// uses <paramref name="Subject"/> and <paramref name="Body"/> as they are, or lays the blocks out its own way.
/// </summary>
/// <param name="Recipient">Where it goes.</param>
/// <param name="Subject">A short subject.</param>
/// <param name="Body">The plain text of the message, with no header, signature or footer.</param>
/// <param name="Level">How much it matters, or null when not known.</param>
/// <param name="Category">The display name of the category it belongs to, or null when not known.</param>
/// <param name="CategoryId">The stable key of the category (<c>data</c>, the key of the settings and of the log), for a channel that routes by category; null when not known.</param>
/// <param name="Content">The message as blocks, or null for a message that is only a subject and a text. Build it with <see cref="MailTemplate"/>, which also fills <paramref name="Category"/> and <paramref name="CategoryId"/>. A provider that dresses it (mail) needs <paramref name="Category"/> with it, and refuses a content without one; a missing <paramref name="Level"/> is read as <see cref="NotificationLevel.Info"/>.</param>
public sealed record Notification(string Recipient, string Subject, string Body, NotificationLevel? Level = null, string? Category = null, NotificationContent? Content = null, string? CategoryId = null);

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
