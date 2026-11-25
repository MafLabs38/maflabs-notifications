using Microsoft.Extensions.Options;

namespace MafLabs.Notifications;

/// <summary>
/// Settings of the notifications, from the <c>Notifications</c> section. Off by default: turning it on lets the application write to ONE explicit
/// recipient. When to write, how often and the caps are not here: they are the categories (<see cref="NotificationCategories"/>, <see cref="CategoryPolicy"/>),
/// the program-wide <see cref="NotificationPolicyOptions"/> and <see cref="NotificationDecider"/>. Deriving from this class is only for extra
/// settings of a provider the application adds itself.
/// </summary>
public class NotificationOptions
{
    public bool Enabled { get; set; }

    /// <summary>How the notification is delivered: the name of an <see cref="INotificationProvider"/>. Built in: <c>Smtp</c>.</summary>
    public string Provider { get; set; } = NotificationProviders.Smtp;

    /// <summary>The address the notifications go to. Explicit: nothing is ever sent to an address that data or a remote machine gave.</summary>
    public string? Recipient { get; set; }

    /// <summary>The address the notifications come from (often the one of the mailbox that sends them).</summary>
    public string? From { get; set; }

    public SmtpOptions Smtp { get; set; } = new();
}

/// <summary>The settings of the <c>Smtp</c> provider (<c>Notifications:Smtp</c>). Google: smtp.gmail.com, port 587, an application password.</summary>
public sealed class SmtpOptions
{
    public string? Host { get; set; }

    /// <summary>The port of the submission service, with STARTTLS (587). The implicit TLS of port 465 is not supported.</summary>
    public int Port { get; set; } = 587;

    public string? UserName { get; set; }

    /// <summary>A file that holds the password (a secret mounted as a file, never an environment variable). For Google, an application password.</summary>
    public string? PasswordFile { get; set; }

    /// <summary>Whether the connection is upgraded to TLS. Off only for a server without a password, such as a catcher on a test machine: a password is never sent in clear.</summary>
    public bool StartTls { get; set; } = true;

    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>Checks the settings when notifications are turned on. An application that derives <see cref="NotificationOptions"/> for extra settings of its own provider overrides <see cref="ValidatePolicy"/> to check them (the policy itself is checked by <see cref="NotificationPolicyOptionsValidator"/> and <see cref="CategoryPolicyValidator"/>).</summary>
public class NotificationOptionsValidator : IValidateOptions<NotificationOptions>
{
    private readonly INotificationProvider[] _providers;

    public NotificationOptionsValidator(IEnumerable<INotificationProvider> providers) => _providers = [.. providers];

    public ValidateOptionsResult Validate(string? name, NotificationOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        if (!Address.IsValid(options.Recipient) || !Address.IsValid(options.From))
        {
            return ValidateOptionsResult.Fail("Notifications:Recipient and Notifications:From are needed, each one a mail address.");
        }

        var provider = NotificationProviders.Find(_providers, options.Provider);
        var failure = provider is null
            ? $"Notifications:Provider is one of: {string.Join(", ", _providers.Select(known => known.Name))}."
            : provider.Validate(options);
        if (failure is not null)
        {
            return ValidateOptionsResult.Fail(failure);
        }

        failure = ValidatePolicy(options);
        return failure is null ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failure);
    }

    /// <summary>What is wrong with the extra settings of a derived <see cref="NotificationOptions"/> (not the policy: see the categories), or null. Called last, only when notifications are turned on.</summary>
    protected virtual string? ValidatePolicy(NotificationOptions options) => null;
}

internal static class Address
{
    public static bool IsValid(string? text) =>
        !string.IsNullOrWhiteSpace(text) && text.All(character => !char.IsControl(character)) && System.Net.Mail.MailAddress.TryCreate(text, out var address) && address.Address == text.Trim();
}
