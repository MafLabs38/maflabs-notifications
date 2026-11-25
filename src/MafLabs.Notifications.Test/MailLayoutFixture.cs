using System.Text.RegularExpressions;

namespace MafLabs.Notifications.Test;

/// <summary>The shared layout: the accent of each level, what a mail client can render, the escaping, and the text alternative.</summary>
public sealed class MailLayoutFixture
{
    private static readonly IReadOnlyList<MailBlock> Blocks =
    [
        new MailParagraph("Portarr sees 4 data assets whose backups are failing."),
        new MailItems(["alpha", "beta", "gamma", "delta"]),
        new MailCallToAction("Open the Data page to see the last backup of each."),
    ];

    private static string Html(MailLevel level = MailLevel.Danger) => MailLayout.RenderHtml(new MailContext("Portarr", "Data", level), "Backups are failing", Blocks);

    [Theory]
    [InlineData(MailLevel.Info, "#1d4ed8", "Information")]
    [InlineData(MailLevel.Warning, "#b45309", "Warning")]
    [InlineData(MailLevel.Danger, "#b91c1c", "Critical")]
    [InlineData(MailLevel.Ok, "#15803d", "OK")]
    public void should_color_the_header_band_with_the_accent_of_the_level_and_name_the_level(MailLevel level, string accent, string label)
    {
        var html = Html(level);

        MailLayout.AccentOf(level).Should().Be(accent);
        MailLayout.LabelOf(level).Should().Be(label);
        html.Should().Contain($"background-color:{accent}").And.Contain($"border-left:4px solid {accent}").And.Contain($"<strong>{label}</strong>");
        foreach (var other in Enum.GetValues<MailLevel>().Where(other => other != level))
        {
            html.Should().NotContain(MailLayout.AccentOf(other));
        }
    }

    [Fact]
    public void should_name_the_product_the_category_the_title_the_items_and_the_footer()
    {
        var html = Html();

        html.Should().Contain("Portarr").And.Contain("Data &middot;").And.Contain("<h1").And.Contain("Backups are failing")
            .And.Contain("Portarr sees 4 data assets").And.Contain("<li").And.Contain("delta").And.Contain("Open the Data page")
            .And.Contain("Sent by Portarr; you receive this because notifications are enabled for Data.");
        Regex.Matches(html, "<li ").Should().HaveCount(4);
    }

    [Fact]
    public void should_load_nothing_from_outside_and_run_nothing()
    {
        var html = Html();

        html.Should().NotContainEquivalentOf("<script").And.NotContainEquivalentOf("<img").And.NotContainEquivalentOf("<link").And.NotContainEquivalentOf("<iframe")
            .And.NotContainEquivalentOf("url(").And.NotContainEquivalentOf("@import").And.NotContainEquivalentOf("@font-face").And.NotContain("http:").And.NotContain("https:");
    }

    [Fact]
    public void should_lay_out_with_tables_no_flex_and_no_grid_in_at_most_600_pixels()
    {
        var html = Html();

        html.Should().Contain("<table role=\"presentation\"").And.Contain("max-width:600px").And.Contain("width=\"600\"");
        html.Should().NotContainEquivalentOf("display:flex").And.NotContainEquivalentOf("display:grid").And.NotContainEquivalentOf("display: flex").And.NotContainEquivalentOf("display: grid");
        html.Should().NotMatchRegex(@"width\s*[=:]\s*""?(6[1-9]\d|[7-9]\d\d|\d{4,})");
    }

    [Fact]
    public void should_set_an_explicit_background_and_text_color_wherever_there_is_text_and_offer_a_dark_palette()
    {
        var html = Html();

        html.Should().Contain("<meta name=\"color-scheme\" content=\"light dark\">").And.Contain("supported-color-schemes").And.Contain("prefers-color-scheme:dark");
        html.Should().Contain("background-color:#ffffff;color:#1f2937", "the card sets both: no assumption about the client's own background");
        html.Should().Contain("bgcolor=\"#ffffff\"");
        html.Should().NotContain("background-color:#000000").And.NotContain("color:#000000");
    }

    [Fact]
    public void should_declare_the_language_the_viewport_and_the_charset()
    {
        Html().Should().StartWith("<!DOCTYPE html>").And.Contain("<html lang=\"en\">").And.Contain("charset=\"utf-8\"").And.Contain("name=\"viewport\"");
    }

    [Fact]
    public void should_encode_everything_that_comes_from_the_caller()
    {
        const string Hostile = "<script>alert(1)</script>\"quoted\" & 'single'";
        var blocks = new MailBlock[] { new MailParagraph(Hostile), new MailItems([Hostile, "<b>bold</b>"]), new MailCallToAction(Hostile) };

        var html = MailLayout.RenderHtml(new MailContext(Hostile, Hostile, MailLevel.Warning), Hostile, blocks);

        html.Should().NotContain("<script>").And.NotContain("<b>").And.NotContain("\"quoted\" &");
        Regex.Matches(html, Regex.Escape("&lt;script&gt;alert(1)&lt;/script&gt;&quot;quoted&quot; &amp; &#39;single&#39;")).Count.Should().BeGreaterThanOrEqualTo(8, "title (twice), product, category, footer (twice), paragraph, items, call to action");
        html.Should().Contain("&lt;b&gt;bold&lt;/b&gt;");
    }

    [Fact]
    public void should_make_line_breaks_inside_a_paragraph_into_br_and_drop_control_characters()
    {
        var html = MailLayout.RenderHtml(new MailContext("P", "C", MailLevel.Info), "t", [new MailParagraph("one\ntwo\r\u0007three")]);

        html.Should().Contain("one<br>two  three");
    }

    [Fact]
    public void should_leave_out_a_block_that_has_nothing_to_show()
    {
        var html = MailLayout.RenderHtml(new MailContext("P", "C", MailLevel.Info), "t", [new MailParagraph("  "), new MailItems([]), new MailCallToAction("")]);

        html.Should().NotContain("<p ").And.NotContain("<ul").And.NotContain("font-weight:600");
    }

    [Fact]
    public void should_read_well_as_text_with_the_level_the_title_the_items_and_the_footer()
    {
        var text = MailLayout.RenderText(new MailContext("Portarr", "Data", MailLevel.Danger), "Backups are failing", Blocks);

        text.Should().Be(
            "[Critical] Portarr - Data\n\nBackups are failing\n\nPortarr sees 4 data assets whose backups are failing.\n\n"
            + "- alpha\n- beta\n- gamma\n- delta\n\nOpen the Data page to see the last backup of each.\n\n"
            + "-- \nSent by Portarr; you receive this because notifications are enabled for Data.\n");
    }

    [Fact]
    public void should_keep_the_text_free_of_html_and_of_header_injection()
    {
        var text = MailLayout.RenderText(new MailContext("P\nBcc: x", "<C>", MailLevel.Info), "T\r\nBcc: y", [new MailParagraph("a & b <c>")]);

        text.Should().Contain("a & b <c>").And.NotContain("&amp;").And.StartWith("[Information] P Bcc: x - <C>\n\nT  Bcc: y\n");
    }

    [Fact]
    public void should_write_a_text_with_no_body_blocks_without_a_gap()
    {
        MailLayout.RenderText(new MailContext("P", "C", MailLevel.Ok), "Done", []).Should().Be("[OK] P - C\n\nDone\n\n-- \nSent by P; you receive this because notifications are enabled for C.\n");
    }

    [Fact]
    public void should_refuse_a_level_that_does_not_exist()
    {
        ((Action)(() => MailLayout.AccentOf((MailLevel)9))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => MailLayout.LabelOf((MailLevel)9))).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void should_refuse_a_missing_context_or_blocks()
    {
        ((Action)(() => MailLayout.RenderHtml(null!, "t", []))).Should().Throw<ArgumentNullException>();
        ((Action)(() => MailLayout.RenderText(null!, "t", []))).Should().Throw<ArgumentNullException>();
        ((Action)(() => MailLayout.RenderHtml(new MailContext("P", "C", MailLevel.Ok), "t", null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => MailLayout.RenderText(new MailContext("P", "C", MailLevel.Ok), "t", null!))).Should().Throw<ArgumentNullException>();
    }
}
