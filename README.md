# MafLabs.Notifications

Sends a notification to **one explicit person** by mail, from a .NET application: SMTP with STARTTLS, the password read from a **file** (so it can be rotated, never an environment variable), a plain-text **template per kind of problem**, and a channel that can be swapped (`INotificationProvider`). A failed delivery is **told in the result, never thrown**.

Extracted from Mnemarr (ADR 0042) so that Mnemarr, Portarr and Orchestrarr share it. The policy (when to write, reminders, daily caps) stays in each application.

```csharp
services.Configure<NotificationOptions>(configuration.GetSection("Notifications"));
services.AddNotifications();

var template = new MailTemplate("[{{provider}}] storage at {{percent}} %", "{{provider}} uses {{used}} of {{limit}}.");
var (subject, body) = template.Render(new Dictionary<string, string> { ["provider"] = "...", ["percent"] = "82", ["used"] = "8.2 GB", ["limit"] = "10 GB" });
var result = await sender.SendAsync(new Notification(recipient, subject, body), cancellationToken);
```

Settings (`Notifications` section): `Enabled` (off by default), `Recipient`, `From`, `Smtp:Host`, `Smtp:Port` (587), `Smtp:UserName`, `Smtp:PasswordFile`, `Smtp:StartTls`, `Smtp:TimeoutSeconds`.

Port 465 (implicit TLS) is not supported; a password is never sent without STARTTLS.

## License

GPL-3.0-or-later, like Portarr and Mnemarr. See [LICENSE](LICENSE).
