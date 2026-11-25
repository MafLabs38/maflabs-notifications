using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using System.Text;
using MafLabs.Notifications;
using Microsoft.Extensions.Options;

namespace MafLabs.Notifications.Test;

/// <summary>The SMTP provider: its settings, the mail it really sends (to a server of this test, on the loopback address), the failures it says in words, and the readiness probe.</summary>
public sealed class SmtpNotificationFixture : IDisposable
{
    private readonly string _passwordFile = Path.Combine(Path.GetTempPath(), $"notifications-smtp-{Guid.NewGuid():N}");

    public SmtpNotificationFixture() => File.WriteAllText(_passwordFile, "app-password\n");

    public void Dispose() => File.Delete(_passwordFile);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static NotificationOptions Settings(int port = 587, string host = "smtp.homelab.example") => new()
    {
        Enabled = true,
        Recipient = "me@homelab.example",
        From = "app@homelab.example",
        Smtp = new SmtpOptions { Host = host, Port = port, StartTls = true },
    };

    private static string? Problem(NotificationOptions options) =>
        new NotificationOptionsValidator(NotificationProviders.BuiltIn).Validate(null, options).FailureMessage;

    /// <summary>A server that says yes to everything, keeps what it was told and ends the conversation at QUIT.</summary>
    private sealed class MailServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

        public MailServer()
        {
            _listener.Start();
            Task = Task.Run(Serve);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public Task Task { get; }

        public StringBuilder Received { get; } = new();

        public void Dispose() => _listener.Stop();

        private async Task Serve()
        {
            using var client = await _listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
            await writer.WriteLineAsync("220 test ready");
            var inData = false;
            while (await reader.ReadLineAsync() is { } line)
            {
                Received.AppendLine(line);
                if (inData)
                {
                    if (line == ".")
                    {
                        inData = false;
                        await writer.WriteLineAsync("250 queued");
                    }

                    continue;
                }

                var command = line.Split(' ')[0].ToUpperInvariant();
                switch (command)
                {
                    case "EHLO" or "HELO":
                        await writer.WriteLineAsync("250 test");
                        break;
                    case "DATA":
                        inData = true;
                        await writer.WriteLineAsync("354 go on");
                        break;
                    case "QUIT":
                        await writer.WriteLineAsync("221 bye");
                        return;
                    default:
                        await writer.WriteLineAsync("250 ok");
                        break;
                }
            }
        }
    }

    [Fact]
    public async Task should_hand_the_mail_to_the_server_with_the_sender_the_recipient_the_subject_and_the_text()
    {
        using var server = new MailServer();
        var options = Settings(server.Port, "127.0.0.1");
        options.Smtp.StartTls = false;

        var result = await new SmtpNotificationSender(Options.Create(options)).SendAsync(new Notification("me@homelab.example", "Mnemarr: gemini needs you", "gemini needs you to sign in again."), Token);

        result.Should().Be(NotificationResult.Done);
        await server.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        server.Received.ToString().Should().Contain("MAIL FROM:<app@homelab.example>").And.Contain("RCPT TO:<me@homelab.example>").And.Contain("Subject: Mnemarr: gemini needs you").And.Contain(Convert.ToBase64String(Encoding.UTF8.GetBytes("gemini needs you to sign in again.")), "the text travels encoded");
    }

    /// <summary>The parts of a multipart message as the server of this test received it: the content type of each and its decoded text.</summary>
    private static List<(string ContentType, string Text)> Parts(string received)
    {
        var boundary = System.Text.RegularExpressions.Regex.Match(received, "Content-Type: multipart/alternative;\\s*boundary=\"?([^\"\\r\\n;]+)\"?", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline).Groups[1].Value;
        var parts = new List<(string, string)>();
        foreach (var chunk in received.Split("--" + boundary).Skip(1))
        {
            var lines = chunk.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
            var blank = Array.IndexOf(lines, string.Empty, 1);
            if (blank < 0 || !lines[1].StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var headers = string.Join('\n', lines[1..blank]);
            var contentType = System.Text.RegularExpressions.Regex.Match(headers, "Content-Type: ([^;\\n]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Groups[1].Value;
            var body = string.Concat(lines[(blank + 1)..]).Trim();
            body = body.EndsWith("--", StringComparison.Ordinal) ? body[..^2] : body;
            if (headers.Contains("base64", StringComparison.OrdinalIgnoreCase))
            {
                parts.Add((contentType, Encoding.UTF8.GetString(Convert.FromBase64String(body))));
            }
            else
            {
                parts.Add((contentType, body));
            }
        }

        return parts;
    }

    [Fact]
    public async Task should_send_the_text_and_the_html_made_from_the_content_as_alternatives_of_one_mail()
    {
        using var server = new MailServer();
        var options = Settings(server.Port, "127.0.0.1");
        options.Smtp.StartTls = false;
        var content = new NotificationContent("Portarr", "Backups are failing", [new MessageParagraph("Caf\u00e9 \u2713 & the <b>backups</b> are failing"), new MessageParagraph(new string('x', 400))]);

        var result = await new SmtpNotificationSender(Options.Create(options)).SendAsync(new Notification("me@homelab.example", "Portarr: backups failing", "bare body", NotificationLevel.Warning, "Data", content), Token);

        result.Should().Be(NotificationResult.Done);
        await server.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        var received = server.Received.ToString();
        received.Should().Contain("Content-Type: multipart/alternative").And.Contain("Subject: Portarr: backups failing");
        var parts = Parts(received);
        parts.Should().HaveCount(2);
        parts[0].ContentType.Should().Be("text/plain");
        parts[0].Text.Should().Contain("[Warning] Portarr - Data").And.Contain("Caf\u00e9 \u2713 & the <b>backups</b> are failing").And.NotContain("bare body");
        parts[1].ContentType.Should().Be("text/html");
        parts[1].Text.Should().StartWith("<!DOCTYPE html>").And.Contain("&amp; the &lt;b&gt;backups&lt;/b&gt; are failing").And.NotContain("<b>backups");
        received.Should().Contain("charset=utf-8");
    }

    [Theory]
    [InlineData("", "Title", "Data")]
    [InlineData("Portarr", " ", "Data")]
    [InlineData("Portarr", "Title", null)]
    public async Task should_refuse_a_content_that_is_incomplete_without_sending_anything(string product, string title, string? category)
    {
        using var server = new MailServer();
        var options = Settings(server.Port, "127.0.0.1");
        options.Smtp.StartTls = false;
        var content = new NotificationContent(product, title, [new MessageParagraph("text")]);

        var result = await new SmtpNotificationSender(Options.Create(options)).SendAsync(new Notification("me@homelab.example", "s", "b", NotificationLevel.Info, category, content), Token);

        result.Delivered.Should().BeFalse();
        result.Reason.Should().Contain("incomplete");
        server.Received.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task should_send_nothing_when_notifications_are_turned_off()
    {
        using var server = new MailServer();
        var options = Settings(server.Port, "127.0.0.1");
        options.Smtp.StartTls = false;
        options.Enabled = false;

        options.Smtp.UserName = "me";
        options.Smtp.PasswordFile = "/does/not/exist";
        var sender = NotificationProviders.Select(NotificationProviders.BuiltIn, Options.Create(options));

        var result = await sender.SendAsync(new Notification("me@homelab.example", "s", "b"), Token);

        result.Should().Be(NotificationResult.Failed("Notifications are turned off."));
        server.Received.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task should_send_a_plain_text_mail_when_there_is_no_content()
    {
        using var server = new MailServer();
        var options = Settings(server.Port, "127.0.0.1");
        options.Smtp.StartTls = false;

        var result = await new SmtpNotificationSender(Options.Create(options)).SendAsync(new Notification("me@homelab.example", "s", "just text"), Token);

        result.Should().Be(NotificationResult.Done);
        await server.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        server.Received.ToString().Should().NotContain("multipart").And.NotContain("text/html").And.Contain("Content-Type: text/plain");
    }

    [Fact]
    public async Task should_send_a_rendered_mail_with_its_level_in_the_html_and_its_text_alternative()
    {
        using var server = new MailServer();
        var options = Settings(server.Port, "127.0.0.1");
        options.Smtp.StartTls = false;
        var template = new MailTemplate("Portarr: backups {{state}}", "Backups are {{state}}:\n\n{{@subjects}}", "Backups are {{state}}");
        var mail = template.RenderMail(new MailContext("Portarr", "Data", NotificationLevel.Danger), new Dictionary<string, string> { ["state"] = "failing" }, new Dictionary<string, IReadOnlyList<string>> { ["subjects"] = ["alpha", "<b>beta</b>"] });

        var result = await new SmtpNotificationSender(Options.Create(options)).SendAsync(mail.ToNotification("me@homelab.example"), Token);

        result.Should().Be(NotificationResult.Done);
        await server.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        var parts = Parts(server.Received.ToString());
        parts[0].Text.Should().Contain("[Critical] Portarr - Data").And.Contain("- alpha");
        parts[1].Text.Should().Contain("#b91c1c").And.Contain("&lt;b&gt;beta&lt;/b&gt;").And.NotContain("<b>beta");
    }

    [Fact]
    public async Task should_say_that_the_server_cannot_be_reached_in_a_sentence_without_the_exception()
    {
        var closed = new TcpListener(IPAddress.Loopback, 0);
        closed.Start();
        var port = ((IPEndPoint)closed.LocalEndpoint).Port;
        closed.Stop();
        var options = Settings(port, "127.0.0.1");
        options.Smtp.StartTls = false;

        var result = await new SmtpNotificationSender(Options.Create(options)).SendAsync(new Notification("me@homelab.example", "s", "b"), Token);

        result.Delivered.Should().BeFalse();
        result.Reason.Should().NotBeNullOrEmpty().And.NotContain("Exception").And.NotContain("127.0.0.1");
    }

    [Fact]
    public async Task should_let_go_of_a_server_that_accepts_the_connection_and_says_nothing()
    {
        var silent = new TcpListener(IPAddress.Loopback, 0);
        silent.Start();
        try
        {
            var options = Settings(((IPEndPoint)silent.LocalEndpoint).Port, "127.0.0.1");
            options.Smtp.StartTls = false;
            options.Smtp.TimeoutSeconds = 1;

            var result = await new SmtpNotificationSender(Options.Create(options)).SendAsync(new Notification("me@homelab.example", "s", "b"), Token);

            result.Should().Be(NotificationResult.Failed("The mail server did not answer in time."));
        }
        finally
        {
            silent.Stop();
        }
    }

    [Fact]
    public async Task should_say_that_the_file_of_the_password_cannot_be_read()
    {
        var options = Settings(2525, "127.0.0.1");
        options.Smtp.UserName = "app@homelab.example";
        options.Smtp.PasswordFile = Path.Combine(Path.GetTempPath(), "mnemarr-missing-" + Guid.NewGuid().ToString("N"));

        var result = await new SmtpNotificationSender(Options.Create(options)).SendAsync(new Notification("me@homelab.example", "s", "b"), Token);

        result.Should().Be(NotificationResult.Failed("The file of the mail password cannot be read."));
    }

    [Theory]
    [MemberData(nameof(Failures))]
    public void should_say_in_a_sentence_what_the_operator_can_act_on(Exception failure, string contains)
    {
        SmtpNotificationSender.Describe(failure).Should().Contain(contains).And.NotContain("Exception");
    }

    public static TheoryData<Exception, string> Failures() => new()
    {
        { new SmtpException(SmtpStatusCode.MustIssueStartTlsFirst), "secure connection" },
        { new SmtpException(SmtpStatusCode.ClientNotPermitted), "sign-in" },
        { new SmtpException(SmtpStatusCode.GeneralFailure), "application password" },
        { new SmtpException(SmtpStatusCode.MailboxUnavailable), "MailboxUnavailable" },
        { new SmtpException("x", new SocketException()), "could not be reached" },
        { new System.Security.Authentication.AuthenticationException("the certificate is not trusted"), "certificate" },
        { new FormatException(), "could not be sent" },
    };

    [Fact]
    public void should_accept_the_settings_of_a_google_mailbox()
    {
        var options = Settings(587, "smtp.gmail.com");
        options.Smtp.UserName = "me@homelab.example";
        options.Smtp.PasswordFile = _passwordFile;

        Problem(options).Should().BeNull();
    }

    [Fact]
    public void should_accept_anything_when_the_notifications_are_off()
    {
        Problem(new NotificationOptions { Enabled = false, Recipient = "not an address" }).Should().BeNull();
    }

    [Theory]
    [InlineData(null, "app@homelab.example")]
    [InlineData("me@homelab.example", null)]
    [InlineData("not an address", "app@homelab.example")]
    [InlineData("me@homelab.example\r\nBcc: other@homelab.example", "app@homelab.example")]
    [InlineData("Me <me@homelab.example>", "app@homelab.example")]
    public void should_need_two_plain_addresses(string? recipient, string? from)
    {
        var options = Settings();
        options.Recipient = recipient;
        options.From = from;

        Problem(options).Should().Contain("Recipient");
    }

    [Fact]
    public void should_name_the_providers_that_exist_when_the_name_is_not_one_of_them()
    {
        var options = Settings();
        options.Provider = "Pigeon";

        Problem(options).Should().Contain("Smtp");
    }

    [Theory]
    [InlineData("")]
    [InlineData("two words")]
    public void should_need_a_host(string host)
    {
        var options = Settings();
        options.Smtp.Host = host;

        Problem(options).Should().Contain("Host");
    }

    [Fact]
    public void should_explain_why_the_implicit_tls_port_is_not_supported()
    {
        Problem(Settings(465)).Should().Contain("587");
    }

    [Fact]
    public void should_need_the_user_and_the_password_file_together()
    {
        var options = Settings();
        options.Smtp.UserName = "me@homelab.example";

        Problem(options).Should().Contain("go together");
    }

    [Fact]
    public void should_never_send_a_password_in_clear()
    {
        var options = Settings();
        options.Smtp.UserName = "me@homelab.example";
        options.Smtp.PasswordFile = _passwordFile;
        options.Smtp.StartTls = false;

        Problem(options).Should().Contain("in clear");
    }
}
