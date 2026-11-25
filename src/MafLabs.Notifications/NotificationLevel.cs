namespace MafLabs.Notifications;

/// <summary>
/// How much a notification matters, from the least to the most, whatever the channel (a mail, a chat message or a push all use it). In a mail it colors the accent, and it is what a category's
/// <see cref="CategoryPolicy.MinimumLevel"/> is compared with: a category set to <see cref="Warning"/> stays silent about <see cref="Info"/> and <see cref="Ok"/>.
/// </summary>
public enum NotificationLevel
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
