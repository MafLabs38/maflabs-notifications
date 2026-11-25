namespace MafLabs.Notifications.Test;

/// <summary>The mail templates: variables found, values cleaned, a missing variable refused.</summary>
public sealed class MailTemplateFixture
{
    private static readonly MailTemplate Quota = new("[{{provider}}] storage at {{percent}} %", "{{provider}} uses {{used}} of {{limit}}.\nCleanup: {{hint}}");

    [Fact]
    public void should_list_the_variables_it_uses_once_and_sorted()
    {
        new MailTemplate("{{b}} {{a}}", "{{a}} {{c}}").Variables.Should().Equal("a", "b", "c");
    }

    [Fact]
    public void should_fill_the_subject_and_the_body()
    {
        var (subject, body) = Quota.Render(new Dictionary<string, string> { ["provider"] = "Store", ["percent"] = "82", ["used"] = "8.2 GB", ["limit"] = "10 GB", ["hint"] = "prune" });

        subject.Should().Be("[Store] storage at 82 %");
        body.Should().Be("Store uses 8.2 GB of 10 GB.\nCleanup: prune");
    }

    [Fact]
    public void should_refuse_a_variable_the_caller_did_not_give()
    {
        var render = () => Quota.Render(new Dictionary<string, string> { ["provider"] = "Store" });

        render.Should().Throw<InvalidOperationException>().WithMessage("*hint*limit*percent*used*");
    }

    [Fact]
    public void should_keep_the_subject_on_one_line_and_let_the_body_keep_its_line_breaks()
    {
        var (subject, body) = Quota.Render(new Dictionary<string, string> { ["provider"] = "Store\r\nBcc: someone@example.test", ["percent"] = "1", ["used"] = "a\nb", ["limit"] = "c", ["hint"] = "x\ny" });

        subject.Should().NotContainAny("\r", "\n");
        body.Should().Contain("a\nb").And.Contain("x\ny");
    }

    [Fact]
    public void should_cut_a_value_that_is_too_long()
    {
        var (subject, _) = Quota.Render(new Dictionary<string, string> { ["provider"] = new string('x', 1000), ["percent"] = "1", ["used"] = "a", ["limit"] = "b", ["hint"] = "c" });

        subject.Length.Should().BeLessThan(MailTemplate.MaxValueLength + 40);
        subject.Should().Contain("…");
    }

    [Theory]
    [InlineData("", "body")]
    [InlineData("subject", " ")]
    public void should_need_a_subject_and_a_body(string subject, string body)
    {
        var create = () => new MailTemplate(subject, body);

        create.Should().Throw<ArgumentException>();
    }

    private static readonly MailContext Context = new("Portarr", "Data", NotificationLevel.Danger);

    private static readonly MailTemplate Backups = new(
        "Portarr: backups {{state}}",
        """
        Portarr sees {{count}} data asset(s) whose backups are {{state}}:

        {{@subjects}}

        > Open the Data page to see the details.

        {{followUp}}
        """,
        "Backups are {{state}}");

    private static Dictionary<string, string> BackupValues(string state = "failing", string followUp = "Reminder in 24 hours.") =>
        new() { ["state"] = state, ["count"] = "2", ["followUp"] = followUp };

    private static readonly Dictionary<string, IReadOnlyList<string>> Subjects = new() { ["subjects"] = ["alpha: failing", "beta: late"] };

    [Fact]
    public void should_render_the_text_with_the_list_as_lines_and_the_call_to_action_without_its_marker()
    {
        var (subject, body) = Backups.Render(BackupValues(), Subjects);

        subject.Should().Be("Portarr: backups failing");
        body.Should().Be("Portarr sees 2 data asset(s) whose backups are failing:\n\n- alpha: failing\n- beta: late\n\nOpen the Data page to see the details.\n\nReminder in 24 hours.");
    }

    [Fact]
    public void should_render_the_html_and_the_text_from_the_same_definition()
    {
        var mail = Backups.RenderMail(Context, BackupValues(), Subjects);

        mail.Subject.Should().Be("Portarr: backups failing");
        mail.Text.Should().Contain("Backups are failing").And.Contain("- alpha: failing").And.Contain("Open the Data page to see the details.");
        mail.Html.Should().Contain("<h1").And.Contain("Backups are failing").And.Contain("<li").And.Contain("alpha: failing").And.Contain("Open the Data page to see the details.");
    }

    [Fact]
    public void should_use_the_subject_as_the_title_when_there_is_none()
    {
        var mail = Quota.RenderMail(Context, new Dictionary<string, string> { ["provider"] = "Store", ["percent"] = "82", ["used"] = "a", ["limit"] = "b", ["hint"] = "c" });

        mail.Html.Should().Contain("<h1").And.Contain("[Store] storage at 82 %");
    }

    [Fact]
    public void should_html_encode_every_value_and_never_write_one_raw()
    {
        const string Hostile = "<script>alert(\"x\")</script> & 'q'";
        var values = BackupValues(Hostile, "<img src=x onerror=alert(1)>");
        var mail = Backups.RenderMail(Context, values, new Dictionary<string, IReadOnlyList<string>> { ["subjects"] = [Hostile, "a&b"] });

        mail.Html.Should().NotContain("<script").And.NotContain("<img").And.NotContain("alert(\"");
        mail.Html.Should().Contain("&lt;script&gt;alert(&quot;x&quot;)&lt;/script&gt; &amp; &#39;q&#39;").And.Contain("a&amp;b").And.Contain("&lt;img src=x onerror=alert(1)&gt;");
    }

    [Fact]
    public void should_keep_the_text_as_it_is_given_in_the_text_alternative()
    {
        var mail = Backups.RenderMail(Context, BackupValues("a & b <c>"), Subjects);

        mail.Text.Should().Contain("a & b <c>").And.NotContain("&amp;");
        mail.Subject.Should().Be("Portarr: backups a & b <c>");
    }

    [Fact]
    public void should_never_let_a_value_make_a_paragraph_a_list_or_a_call_to_action()
    {
        var mail = Backups.RenderMail(Context, BackupValues("x\n\n> pretend\n\n{{@subjects}}"), Subjects);

        mail.Html.Should().NotContain("pretend</td>").And.Contain("&gt; pretend");
        mail.Text.Should().Contain("> pretend").And.Contain("{{@subjects}}");
    }

    [Fact]
    public void should_clean_and_cut_the_items_like_any_other_value()
    {
        var lists = new Dictionary<string, IReadOnlyList<string>> { ["subjects"] = ["a\r\nBcc: x", new string('y', 1000), "  "] };

        var (_, body) = Backups.Render(BackupValues(), lists);

        body.Should().Contain("- a  Bcc: x").And.Contain("…");
        body.Split('\n').Count(line => line.StartsWith("- ", StringComparison.Ordinal)).Should().Be(2, "an empty item is not a line");
        body.Split('\n').Should().OnlyContain(line => line.Length < MailTemplate.MaxValueLength + 40);
    }

    [Fact]
    public void should_leave_out_a_list_that_is_empty()
    {
        var mail = Backups.RenderMail(Context, BackupValues(), new Dictionary<string, IReadOnlyList<string>> { ["subjects"] = [] });

        mail.Html.Should().NotContain("<li");
        mail.Text.Should().NotContain("\n- ").And.NotContain("\n\n\n");
    }

    [Fact]
    public void should_list_the_lists_it_uses_apart_from_the_variables()
    {
        Backups.Variables.Should().Equal("count", "followUp", "state");
        Backups.ListVariables.Should().Equal("subjects");
    }

    [Fact]
    public void should_refuse_a_list_the_caller_did_not_give()
    {
        var render = () => Backups.Render(BackupValues());
        var wrongName = () => Backups.RenderMail(Context, BackupValues(), new Dictionary<string, IReadOnlyList<string>> { ["names"] = [] });

        render.Should().Throw<InvalidOperationException>().WithMessage("*subjects*");
        wrongName.Should().Throw<InvalidOperationException>().WithMessage("*subjects*");
    }

    [Fact]
    public void should_refuse_a_variable_missing_from_the_title_too()
    {
        var template = new MailTemplate("subject", "body", "{{what}}");

        var render = () => template.RenderMail(Context, new Dictionary<string, string>());

        render.Should().Throw<InvalidOperationException>().WithMessage("*what*");
    }

    [Theory]
    [InlineData("Text {{@list}} more")]
    [InlineData("{{@list}}\n{{@other}}")]
    [InlineData("> {{@list}}")]
    public void should_need_a_list_variable_alone_in_its_paragraph(string body)
    {
        var create = () => new MailTemplate("subject", body);

        create.Should().Throw<ArgumentException>().WithMessage("*alone in its paragraph*");
    }

    [Fact]
    public void should_refuse_a_list_variable_in_the_subject_or_the_title()
    {
        ((Action)(() => new MailTemplate("{{@list}}", "body"))).Should().Throw<ArgumentException>();
        ((Action)(() => new MailTemplate("subject", "body", "{{@list}}"))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_keep_the_line_breaks_inside_a_paragraph_and_split_paragraphs_at_blank_lines()
    {
        var template = new MailTemplate("s", "one\r\ntwo\n  \n\n\nthree");

        var mail = template.RenderMail(Context, new Dictionary<string, string>());

        template.Render(new Dictionary<string, string>()).Body.Should().Be("one\ntwo\n\nthree");
        mail.Html.Should().Contain("one<br>two").And.Contain(">three</p>");
    }

    [Fact]
    public void should_hand_the_mail_to_a_notification_with_both_bodies()
    {
        var notification = Backups.RenderMail(Context, BackupValues(), Subjects).ToNotification("operator@homelab.example");

        notification.Recipient.Should().Be("operator@homelab.example");
        notification.Body.Should().Contain("- alpha: failing");
        notification.HtmlBody.Should().StartWith("<!DOCTYPE html>");
        notification.Level.Should().Be(Context.Level);
        notification.Category.Should().Be(Context.CategoryTitle);
    }

    [Fact]
    public void should_name_the_title_when_the_title_holds_a_list_variable()
    {
        var create = () => new MailTemplate("Subject", "Body", "{{@title}}");

        create.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("title");
        var subject = () => new MailTemplate("{{@subject}}", "Body");
        subject.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("subject");
    }
}
