namespace MafLabs.Notifications;

/// <summary>
/// How much a mail matters, from the least to the most. It colors the accent of the mail and is what a category's
/// <see cref="CategoryPolicy.MinimumLevel"/> is compared with: a category set to <see cref="Warning"/> stays silent about <see cref="Info"/> and <see cref="Ok"/>.
/// </summary>
public enum MailLevel
{
    /// <summary>Good news: a problem is over, a check passed. Green.</summary>
    Ok = 0,

    /// <summary>For information, nothing to do. Blue.</summary>
    Info = 1,

    /// <summary>Something deserves a look soon. Amber.</summary>
    Warning = 2,

    /// <summary>Something is broken and needs attention. Red.</summary>
    Danger = 3,
}
