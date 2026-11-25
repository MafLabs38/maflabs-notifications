using Microsoft.Extensions.Options;

namespace MafLabs.Notifications;

/// <summary>
/// The rules that are not a category's own: they hold for the whole program. Section of the application's choice, defaults ready.
/// </summary>
public sealed class NotificationPolicyOptions
{
    /// <summary>The largest number of minutes any of the delays can be (one day).</summary>
    public const int MaxMinutes = 1440;

    /// <summary>The most mails all categories together can be allowed in 24 hours.</summary>
    public const int MaxPerDayLimit = 1000;

    /// <summary>Minutes a problem must last before it is mailed: a blip is not a mail (0 to 1440). 5 by default.</summary>
    public int ConfirmMinutes { get; set; } = 5;

    /// <summary>Minutes a problem that was mailed must be gone before its episode ends, so that a slow tool or a restart does not write twice (1 to 1440). 15 by default.</summary>
    public int ClearMinutes { get; set; } = 15;

    /// <summary>Minutes to wait after a mail that could not be sent, before trying again (1 to 1440). 15 by default.</summary>
    public int RetryMinutes { get; set; } = 15;

    /// <summary>The most mails tried in 24 hours, all categories together (1 to 1000). 10 by default.</summary>
    public int MaxPerDay { get; set; } = 10;

    /// <summary>What is wrong with these settings: one sentence per wrong setting (it names the setting and its allowed range), joined by a space, or null when all are right.</summary>
    public string? Check()
    {
        var problems = new List<string>();
        if (ConfirmMinutes is < 0 or > MaxMinutes)
        {
            problems.Add($"Notification policy: ConfirmMinutes is the minutes a problem must last before it is mailed, from 0 to {MaxMinutes}.");
        }

        if (ClearMinutes is < 1 or > MaxMinutes)
        {
            problems.Add($"Notification policy: ClearMinutes is the minutes a mailed problem must be gone before its episode ends, from 1 to {MaxMinutes}.");
        }

        if (RetryMinutes is < 1 or > MaxMinutes)
        {
            problems.Add($"Notification policy: RetryMinutes is the minutes to wait after a mail that could not be sent, from 1 to {MaxMinutes}.");
        }

        if (MaxPerDay is < 1 or > MaxPerDayLimit)
        {
            problems.Add($"Notification policy: MaxPerDay is the most mails in 24 hours, all categories together, from 1 to {MaxPerDayLimit}.");
        }

        return problems.Count == 0 ? null : string.Join(" ", problems);
    }
}

/// <summary>Checks <see cref="NotificationPolicyOptions"/>, whether notifications are on or not: a value that could never work is refused.</summary>
public sealed class NotificationPolicyOptionsValidator : IValidateOptions<NotificationPolicyOptions>
{
    public ValidateOptionsResult Validate(string? name, NotificationPolicyOptions options) =>
        options.Check() is { } problem ? ValidateOptionsResult.Fail(problem) : ValidateOptionsResult.Success;
}

/// <summary>What the application has stored about a problem that is going on, by its own key. Plain values in, the application keeps the storage.</summary>
/// <param name="StartedAt">When the problem was first seen.</param>
/// <param name="LastSeenAt">When it was last seen.</param>
/// <param name="MailsSent">How many mails about it were delivered.</param>
/// <param name="LastMailedAt">When the last delivered mail left, or null when none did.</param>
/// <param name="LastAttemptAt">When a mail was last tried, delivered or not, or null when none was.</param>
public sealed record EpisodeState(DateTimeOffset StartedAt, DateTimeOffset LastSeenAt, int MailsSent, DateTimeOffset? LastMailedAt, DateTimeOffset? LastAttemptAt)
{
    /// <summary>
    /// How many mails about it were delivered. Mails delivered with no date for the last one cannot be: <see cref="ArgumentException"/> when the state
    /// is built, so that the application's store is told it is inconsistent.
    /// </summary>
    public int MailsSent { get; init; } = MailsSent > 0 && LastMailedAt is null
        ? throw new ArgumentException("An episode with delivered mails (MailsSent > 0) needs the date of the last one (LastMailedAt).", nameof(LastMailedAt))
        : MailsSent;

    /// <summary>The last try did not leave: it is later than the last mail that did.</summary>
    public bool LastAttemptFailed => LastAttemptAt is { } attempt && (LastMailedAt is null || attempt > LastMailedAt);
}

/// <summary>How many mails were tried in the last 24 hours, delivered or not (a failing server must not become a cannon either).</summary>
/// <param name="InCategory">In the category the decision is about.</param>
/// <param name="Overall">In all categories.</param>
public sealed record MailsToday(int InCategory, int Overall);

/// <summary>What to do about a problem now.</summary>
public enum NotificationAction
{
    /// <summary>Write nothing now.</summary>
    None,

    /// <summary>Write the first mail of the episode.</summary>
    SendFirst,

    /// <summary>Write a reminder.</summary>
    SendReminder,
}

/// <summary>Why a decision is what it is: a closed code, for the log and for tests, never a free text.</summary>
public enum DecisionReason
{
    /// <summary>The category is turned off.</summary>
    CategoryDisabled,

    /// <summary>The problem is below the category's minimum level.</summary>
    BelowMinimumLevel,

    /// <summary>The problem has not lasted long enough yet to be mailed.</summary>
    NotConfirmedYet,

    /// <summary>A mail could not be sent a short while ago: waiting before the next try.</summary>
    WaitingAfterFailure,

    /// <summary>Nothing was mailed about this problem yet.</summary>
    FirstMail,

    /// <summary>The last mail is old enough for a reminder.</summary>
    ReminderDue,

    /// <summary>The last mail is too recent for a reminder.</summary>
    ReminderNotDue,

    /// <summary>The problem has had all the reminders the category allows.</summary>
    RemindersExhausted,

    /// <summary>The category has had its mails for the day.</summary>
    CategoryDayCapReached,

    /// <summary>All categories together have had their mails for the day.</summary>
    DayCapReached,
}

/// <summary>The answer of <see cref="NotificationDecider.Decide"/>.</summary>
public sealed record NotificationDecision(NotificationAction Action, DecisionReason Reason)
{
    /// <summary>Whether a mail is to be written now.</summary>
    public bool Send => Action != NotificationAction.None;
}

/// <summary>
/// When to write, as functions of the clock and of plain values (the rules below): one mail when a problem has lasted long enough
/// to be an episode, a reminder at most once per interval and at most a few times while it lasts, nothing when it ends, a pause after a mail that
/// could not leave, and a cap per day for the category and for all. No clock, no storage, no log: the application reads its own store, asks, and records.
/// </summary>
public static class NotificationDecider
{
    /// <summary>The window of the daily caps.</summary>
    public static readonly TimeSpan Day = TimeSpan.FromHours(24);

    /// <summary>
    /// Whether a mail is due now for a problem of <paramref name="level"/> in a category with <paramref name="policy"/>. The checks run in order, and the
    /// first that stops the mail is the reason: disabled, below the minimum level, not confirmed, paused after a failure, then (first mail or reminder due)
    /// and last the daily caps. When <paramref name="options"/> is null the defaults of <see cref="NotificationPolicyOptions"/> apply, not what the operator
    /// configured: an application passes <c>IOptions&lt;NotificationPolicyOptions&gt;.Value</c>. <c>Decide</c> knows only the category switch
    /// (<see cref="CategoryPolicy.Enabled"/>), not the global <see cref="NotificationOptions.Enabled"/>: the application must not call it, or must not send,
    /// when that one is false (the sender refuses to send then).
    /// </summary>
    public static NotificationDecision Decide(DateTimeOffset now, NotificationLevel level, CategoryPolicy policy, EpisodeState episode, MailsToday today, NotificationPolicyOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(episode);
        ArgumentNullException.ThrowIfNull(today);
        options ??= new NotificationPolicyOptions();

        if (!policy.Enabled)
        {
            return Stop(DecisionReason.CategoryDisabled);
        }

        if (level < policy.MinimumLevel)
        {
            return Stop(DecisionReason.BelowMinimumLevel);
        }

        if (now - episode.StartedAt < TimeSpan.FromMinutes(options.ConfirmMinutes))
        {
            return Stop(DecisionReason.NotConfirmedYet);
        }

        // A bounded retry, never a tight loop: a mail that could not leave waits before the next try.
        if (episode.LastAttemptFailed && now - episode.LastAttemptAt!.Value < TimeSpan.FromMinutes(options.RetryMinutes))
        {
            return Stop(DecisionReason.WaitingAfterFailure);
        }

        var first = episode.MailsSent == 0;
        if (!first)
        {
            var lastMailedAt = episode.LastMailedAt ?? throw new ArgumentException("An episode with delivered mails needs the date of the last one (LastMailedAt).", nameof(episode));
            if (episode.MailsSent > policy.MaxReminders)
            {
                return Stop(DecisionReason.RemindersExhausted);
            }

            if (now - lastMailedAt < TimeSpan.FromHours(policy.ReminderHours))
            {
                return Stop(DecisionReason.ReminderNotDue);
            }
        }

        if (today.InCategory >= policy.MaxPerDay)
        {
            return Stop(DecisionReason.CategoryDayCapReached);
        }

        if (today.Overall >= options.MaxPerDay)
        {
            return Stop(DecisionReason.DayCapReached);
        }

        return first
            ? new NotificationDecision(NotificationAction.SendFirst, DecisionReason.FirstMail)
            : new NotificationDecision(NotificationAction.SendReminder, DecisionReason.ReminderDue);
    }

    /// <summary>
    /// Whether an episode that the findings no longer show is over. One that was never mailed is dropped at once (nothing to remember); one that was
    /// mailed is kept for <see cref="NotificationPolicyOptions.ClearMinutes"/>, counted from the later of its last sight and the start of this run:
    /// a monitor that has not read its tools yet after a restart says nothing, and a problem that flickers does not write twice.
    /// </summary>
    public static bool HasEnded(EpisodeState episode, NotificationPolicyOptions options, DateTimeOffset now, DateTimeOffset runStartedAt)
    {
        ArgumentNullException.ThrowIfNull(episode);
        ArgumentNullException.ThrowIfNull(options);
        if (episode.MailsSent == 0)
        {
            return true;
        }

        var lastSeen = episode.LastSeenAt > runStartedAt ? episode.LastSeenAt : runStartedAt;
        return now - lastSeen >= TimeSpan.FromMinutes(options.ClearMinutes);
    }

    /// <summary>Whether the program will write again about the episode after the mail it is about to send (for the sentence that ends the mail).</summary>
    public static bool WillRemind(EpisodeState episode, CategoryPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(episode);
        ArgumentNullException.ThrowIfNull(policy);
        return episode.MailsSent + 1 <= policy.MaxReminders;
    }

    private static NotificationDecision Stop(DecisionReason reason) => new(NotificationAction.None, reason);
}
