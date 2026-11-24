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
}
