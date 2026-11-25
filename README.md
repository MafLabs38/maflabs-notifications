# MafLabs.Notifications

Sends a notification to **one explicit person** by mail, from a .NET application: SMTP with STARTTLS, the password read from a **file** (so it can be rotated, never an environment variable), **text and HTML mails** from one template, a shared layout, **notification categories** that the operator tunes one by one, and the pure rule that decides **when to write**. The channel can be swapped (`INotificationProvider`). A failed delivery is **told in the result, never thrown**.

Extracted from Mnemarr (ADR 0042) and Portarr so that Mnemarr, Portarr and Orchestrarr share it. The library takes plain values and returns decisions; **storage** (the log of mails tried, the problems that are going on) stays in each application.

## Concepts

- **Category** (`NotificationCategory`): a kind of notification a program declares (Portarr: `apps`, `infrastructure`, `data`, `maintenance`), with an id, a display name, a description and a default policy. The program declares them once at startup in a `NotificationCategories` registry (a duplicate or malformed id, or a policy out of range, is refused at once).
- **Policy** (`CategoryPolicy`): how one category writes, so that each can be tuned apart. Defaults: `Enabled` true, `MinimumLevel` Warning, `ReminderHours` 24, `MaxReminders` 3, `MaxPerDay` 3. `CategoryPolicyValidator` says in sentences what is wrong with one. The program-wide rules are `NotificationPolicyOptions` (`ConfirmMinutes` 5, `ClearMinutes` 15, `RetryMinutes` 15, `MaxPerDay` 10 for all categories).
- **Decision** (`NotificationDecider.Decide`): a pure function (no clock, no I/O). Given `now`, the level of the problem, the category policy, the state of the problem (`EpisodeState`: when it started, was last seen, how many mails were delivered, when the last one left, when one was last tried) and how many mails were tried in the last 24 hours (`MailsToday`: in the category, overall), it answers `None`, `SendFirst` or `SendReminder` with a reason code (`DecisionReason`). `HasEnded`, `ReminderOf` and `WillRemind` complete it. The application reads its own store, asks, sends, and records.
- **Template** (`MailTemplate`): one definition, rendered as a text and as an HTML mail. `{{name}}` is a value, `{{@name}}` alone in a paragraph is a list (lines `- item` in the text, bullets in the HTML), a paragraph that starts with `> ` is the call to action. Values are cleaned and cut; in the HTML they are always encoded, never raw. A variable that is not given is an exception, never a half-filled mail.
- **Layout** (`MailLayout`): the look of every mail. Tables, inline styles, no image, font or script, 600 pixels at most, dark palette for the clients that ask for one, a header band with the product name and the accent of the level (`MailLevel`: Ok green, Info blue, Warning amber, Danger red), a title, blocks (paragraph, list, call to action) and a footer: *Sent by Portarr; you receive this because notifications are enabled for Data.* The text alternative reads well on its own.

```csharp
services.Configure<NotificationOptions>(configuration.GetSection("Notifications"));
services.AddNotifications(categories => categories
    .Add("data", "Data", "Backups that fail or are late")
    .Add("maintenance", "Maintenance", "Updates to apply", new CategoryPolicy { MinimumLevel = MailLevel.Info, ReminderHours = 168 }));

// The application keeps the operator's policy per category and the episode state in its own store.
var policy = categories.PolicyFor("data", operatorPolicies);
var decision = NotificationDecider.Decide(now, MailLevel.Danger, policy, episode, new MailsToday(inCategory, overall), policyOptions);
if (decision.Send)
{
    var template = new MailTemplate(
        "Portarr: backups {{state}}",
        """
        {{count}} data asset(s) have backups that are {{state}}:

        {{@subjects}}

        > Open the Data page to see the last backup of each.
        """,
        "Backups are {{state}}");
    var mail = template.RenderMail(
        new MailContext("Portarr", categories.Get("data").Title, MailLevel.Danger),
        new Dictionary<string, string> { ["state"] = "failing", ["count"] = "2" },
        new Dictionary<string, IReadOnlyList<string>> { ["subjects"] = ["media-library", "photos"] });
    var result = await sender.SendAsync(mail.ToNotification(recipient), cancellationToken);
}
```

A `Notification` with an `HtmlBody` is sent as `multipart/alternative` (text/plain, then text/html, UTF-8); without it, as plain text.

Settings (`Notifications` section): `Enabled` (off by default), `Recipient`, `From`, `Smtp:Host`, `Smtp:Port` (587), `Smtp:UserName`, `Smtp:PasswordFile`, `Smtp:StartTls`, `Smtp:TimeoutSeconds`.

Port 465 (implicit TLS) is not supported; a password is never sent without STARTTLS.

## Versions

- **0.2.0**: HTML mail (`Notification.HtmlBody`, multipart/alternative), `MailTemplate` renders text and HTML (`RenderMail`, list variables, call to action), `MailLayout`, categories and their policy, `NotificationDecider`, `AddNotifications` also registers the category registry and `NotificationPolicyOptions`. The text-only API of 0.1.0 still works (a body paragraph that starts with `> ` now loses that marker).
- **0.1.0**: SMTP sender, plain-text templates.

## License

GPL-3.0-or-later, like Portarr and Mnemarr. See [LICENSE](LICENSE).
