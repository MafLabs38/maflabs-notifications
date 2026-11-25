using System.Text.RegularExpressions;

namespace MafLabs.Notifications;

/// <summary>
/// How one category of notifications behaves, so that the operator can tune each one apart: Portarr's <c>data</c> mails can be daily while its
/// <c>maintenance</c> mails are off. A plain settings class (bind it from configuration or from what the operator edits); <see cref="CategoryPolicyValidator"/>
/// says what is wrong with one. The defaults are the ones Portarr and Mnemarr settled on.
/// </summary>
public sealed record CategoryPolicy
{
    /// <summary>The longest wait between two reminders, in hours (30 days).</summary>
    public const int MaxReminderHours = 720;

    /// <summary>The most reminders one problem can get.</summary>
    public const int MaxRemindersLimit = 30;

    /// <summary>The most mails a category can be allowed in 24 hours.</summary>
    public const int MaxPerDayLimit = 1000;

    /// <summary>Whether the category writes at all. On by default.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The lowest level that writes: a problem below it stays on the pages. <see cref="MailLevel.Warning"/> by default.</summary>
    public MailLevel MinimumLevel { get; set; } = MailLevel.Warning;

    /// <summary>Hours between two reminders while a problem lasts (1 to 720). 24 by default: at most daily.</summary>
    public int ReminderHours { get; set; } = 24;

    /// <summary>How many reminders follow the first mail of a problem, at most (0 to 30; 0 = one mail only). 3 by default.</summary>
    public int MaxReminders { get; set; } = 3;

    /// <summary>The most mails tried in 24 hours for this category, whether they went through or not (1 to 1000). 3 by default: a flapping tool is not a mail cannon.</summary>
    public int MaxPerDay { get; set; } = 3;
}

/// <summary>Says what is wrong with a category's settings, in a sentence an operator can act on.</summary>
public static class CategoryPolicyValidator
{
    /// <summary>The sentences that say what is wrong with <paramref name="policy"/>, empty when it is right. <paramref name="categoryId"/> only names the category in them.</summary>
    public static IReadOnlyList<string> Validate(string categoryId, CategoryPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var problems = new List<string>();
        if (!Enum.IsDefined(policy.MinimumLevel))
        {
            problems.Add($"Category '{categoryId}': MinimumLevel must be one of {string.Join(", ", Enum.GetNames<MailLevel>())}.");
        }

        if (policy.ReminderHours is < 1 or > CategoryPolicy.MaxReminderHours)
        {
            problems.Add($"Category '{categoryId}': ReminderHours is the number of hours between two reminders, from 1 to {CategoryPolicy.MaxReminderHours}.");
        }

        if (policy.MaxReminders is < 0 or > CategoryPolicy.MaxRemindersLimit)
        {
            problems.Add($"Category '{categoryId}': MaxReminders is how many reminders follow the first mail, from 0 (a single mail) to {CategoryPolicy.MaxRemindersLimit}.");
        }

        if (policy.MaxPerDay is < 1 or > CategoryPolicy.MaxPerDayLimit)
        {
            problems.Add($"Category '{categoryId}': MaxPerDay is the most mails in 24 hours, from 1 to {CategoryPolicy.MaxPerDayLimit}. To stop a category, turn Enabled off.");
        }

        return problems;
    }
}

/// <summary>A kind of notification a program declares: <c>data</c>, <c>apps</c>... with the name the operator sees and the policy it starts with.</summary>
/// <param name="Id">The stable name, lower case: letters, digits and hyphens, starting with a letter (<c>data</c>, <c>infrastructure</c>). It keys the operator's settings and the log.</param>
/// <param name="Title">The display name (<c>Data</c>), shown in the mail footer and in the settings.</param>
/// <param name="Description">One sentence that says what the category writes about, for the settings page.</param>
/// <param name="DefaultPolicy">The policy that applies until the operator changes it.</param>
public sealed record NotificationCategory(string Id, string Title, string Description, CategoryPolicy DefaultPolicy);

/// <summary>
/// The categories a program declares at startup (see <c>AddNotifications</c>). A duplicate id, an id that is not well formed or a default policy that
/// is out of range is refused when it is added, not later when a mail is due. Fill it during startup, then only read it.
/// </summary>
public sealed partial class NotificationCategories
{
    private readonly Dictionary<string, NotificationCategory> _byId = new(StringComparer.Ordinal);
    private readonly List<NotificationCategory> _ordered = [];

    /// <summary>The categories, in the order they were declared.</summary>
    public IReadOnlyList<NotificationCategory> All => _ordered;

    /// <summary>Declares a category. Returns this collection, so that declarations chain.</summary>
    public NotificationCategories Add(NotificationCategory category)
    {
        ArgumentNullException.ThrowIfNull(category);
        if (category.Id is null || !IdPattern().IsMatch(category.Id))
        {
            throw new ArgumentException($"A category id is lower case letters, digits and hyphens, starting with a letter, 40 characters at most: '{category.Id}' is not.", nameof(category));
        }

        if (string.IsNullOrWhiteSpace(category.Title) || string.IsNullOrWhiteSpace(category.Description))
        {
            throw new ArgumentException($"Category '{category.Id}' needs a title and a description: the operator sees both.", nameof(category));
        }

        if (category.DefaultPolicy is null)
        {
            throw new ArgumentException($"Category '{category.Id}' needs a default policy.", nameof(category));
        }

        var problems = CategoryPolicyValidator.Validate(category.Id, category.DefaultPolicy);
        if (problems.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", problems), nameof(category));
        }

        if (!_byId.TryAdd(category.Id, category))
        {
            throw new ArgumentException($"Category '{category.Id}' is declared twice: each id is declared once.", nameof(category));
        }

        _ordered.Add(category);
        return this;
    }

    /// <summary>Declares a category from its parts.</summary>
    public NotificationCategories Add(string id, string title, string description, CategoryPolicy? defaultPolicy = null) =>
        Add(new NotificationCategory(id, title, description, defaultPolicy ?? new CategoryPolicy()));

    /// <summary>Whether a category of this id is declared.</summary>
    public bool Contains(string id) => _byId.ContainsKey(id);

    /// <summary>The category of this id, or null when it is not declared.</summary>
    public NotificationCategory? Find(string id) => _byId.GetValueOrDefault(id);

    /// <summary>The category of this id. An id nobody declared is a mistake of the program: it is told, with the ids that exist.</summary>
    public NotificationCategory Get(string id) =>
        Find(id) ?? throw new KeyNotFoundException($"There is no notification category '{id}'. Declared: {(_ordered.Count == 0 ? "none" : string.Join(", ", _ordered.Select(category => category.Id)))}.");

    /// <summary>
    /// The policy that applies to a category: the operator's when there is one for it, the category's default otherwise. The operator's policies are
    /// kept by the application, by category id.
    /// </summary>
    public CategoryPolicy PolicyFor(string id, IReadOnlyDictionary<string, CategoryPolicy>? operatorPolicies = null) =>
        operatorPolicies is not null && operatorPolicies.TryGetValue(id, out var own) && Find(id) is not null ? own : Get(id).DefaultPolicy;

    [GeneratedRegex("^[a-z][a-z0-9-]{0,39}$")]
    private static partial Regex IdPattern();
}
