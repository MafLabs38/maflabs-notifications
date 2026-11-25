using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MafLabs.Notifications.Test;

/// <summary>The categories a program declares: the registry's rules and what AddNotifications registers.</summary>
public sealed class NotificationCategoriesFixture
{
    private static NotificationCategories Portarr() => new NotificationCategories()
        .Add("apps", "Apps", "Applications that are down")
        .Add("data", "Data", "Backups that fail or are late", new CategoryPolicy { MinimumLevel = MailLevel.Danger, MaxPerDay = 2 })
        .Add("maintenance", "Maintenance", "Updates to apply", new CategoryPolicy { Enabled = false });

    [Fact]
    public void should_keep_the_categories_in_the_order_they_were_declared()
    {
        Portarr().All.Select(category => category.Id).Should().Equal("apps", "data", "maintenance");
    }

    [Fact]
    public void should_find_a_category_by_its_id()
    {
        var categories = Portarr();

        categories.Contains("data").Should().BeTrue();
        categories.Get("data").Title.Should().Be("Data");
        categories.Find("data")!.DefaultPolicy.MaxPerDay.Should().Be(2);
        categories.Contains("nothing").Should().BeFalse();
        categories.Find("nothing").Should().BeNull();
    }

    [Fact]
    public void should_fail_clearly_for_an_id_nobody_declared()
    {
        var get = () => Portarr().Get("dtaa");

        get.Should().Throw<KeyNotFoundException>().WithMessage("*'dtaa'*Declared: apps, data, maintenance*");
        var empty = () => new NotificationCategories().Get("x");
        empty.Should().Throw<KeyNotFoundException>().WithMessage("*Declared: none*");
    }

    [Fact]
    public void should_refuse_a_duplicate_id()
    {
        var add = () => Portarr().Add("data", "Again", "Declared twice");

        add.Should().Throw<ArgumentException>().WithMessage("*'data' is declared twice*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("Data")]
    [InlineData("1data")]
    [InlineData("da ta")]
    [InlineData("data_x")]
    [InlineData("this-id-is-far-too-long-to-be-a-good-key-in-a-log")]
    public void should_refuse_an_id_that_is_not_well_formed(string id)
    {
        var add = () => new NotificationCategories().Add(id, "Title", "Description");

        add.Should().Throw<ArgumentException>().WithMessage("*lower case*");
    }

    [Fact]
    public void should_need_a_title_a_description_and_a_policy_in_range()
    {
        var categories = new NotificationCategories();

        ((Action)(() => categories.Add("a", " ", "d"))).Should().Throw<ArgumentException>().WithMessage("*title and a description*");
        ((Action)(() => categories.Add("a", "t", ""))).Should().Throw<ArgumentException>().WithMessage("*title and a description*");
        ((Action)(() => categories.Add("a", "t", "d", new CategoryPolicy { ReminderHours = 0 }))).Should().Throw<ArgumentException>().WithMessage("*Category 'a': ReminderHours*");
        ((Action)(() => categories.Add(new NotificationCategory("a", "t", "d", null!)))).Should().Throw<ArgumentException>().WithMessage("*default policy*");
        ((Action)(() => categories.Add(null!))).Should().Throw<ArgumentNullException>();
        categories.All.Should().BeEmpty("a refused category is not half declared");
    }

    [Fact]
    public void should_use_the_default_policy_until_the_operator_sets_one()
    {
        var categories = Portarr();
        var own = new CategoryPolicy { ReminderHours = 6 };

        categories.PolicyFor("data").Should().BeSameAs(categories.Get("data").DefaultPolicy);
        categories.PolicyFor("data", new Dictionary<string, CategoryPolicy>()).Should().BeSameAs(categories.Get("data").DefaultPolicy);
        categories.PolicyFor("data", new Dictionary<string, CategoryPolicy> { ["data"] = own }).Should().BeSameAs(own);
        categories.PolicyFor("apps", new Dictionary<string, CategoryPolicy> { ["data"] = own }).Should().BeSameAs(categories.Get("apps").DefaultPolicy);
    }

    [Fact]
    public void should_not_take_a_policy_for_a_category_that_does_not_exist()
    {
        var policy = () => Portarr().PolicyFor("nothing", new Dictionary<string, CategoryPolicy> { ["nothing"] = new() });

        policy.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void should_document_the_defaults_of_a_policy()
    {
        var policy = new CategoryPolicy();

        policy.Enabled.Should().BeTrue();
        policy.MinimumLevel.Should().Be(MailLevel.Warning);
        policy.ReminderHours.Should().Be(24);
        policy.MaxReminders.Should().Be(3);
        policy.MaxPerDay.Should().Be(3);
    }

    [Fact]
    public void should_register_an_empty_registry_and_the_policy_options_without_a_database()
    {
        var services = new ServiceCollection();

        services.AddNotifications();
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<NotificationCategories>().All.Should().BeEmpty();
        provider.GetRequiredService<IOptions<NotificationPolicyOptions>>().Value.MaxPerDay.Should().Be(10);
    }

    [Fact]
    public void should_register_the_categories_the_program_declares()
    {
        var services = new ServiceCollection();

        services.AddNotifications(categories => categories.Add("data", "Data", "Backups"));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<NotificationCategories>().Get("data").Title.Should().Be("Data");
        provider.GetServices<NotificationCategories>().Should().ContainSingle();
    }

    [Fact]
    public void should_fail_at_startup_when_a_declaration_is_wrong()
    {
        var add = () => new ServiceCollection().AddNotifications(categories => categories.Add("data", "Data", "x").Add("data", "Data", "y"));

        add.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_refuse_policy_options_out_of_range_when_they_are_read()
    {
        var services = new ServiceCollection();
        services.AddNotifications();
        services.Configure<NotificationPolicyOptions>(options => options.MaxPerDay = 0);
        using var provider = services.BuildServiceProvider();

        var read = () => provider.GetRequiredService<IOptions<NotificationPolicyOptions>>().Value;

        read.Should().Throw<OptionsValidationException>().WithMessage("*MaxPerDay*");
    }

    [Fact]
    public void should_still_build_the_sender_from_the_provider_named_in_the_options()
    {
        var services = new ServiceCollection();
        services.Configure<NotificationOptions>(options => options.Provider = "Smtp");
        services.AddNotifications(categories => categories.Add("data", "Data", "Backups"));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<INotificationSender>().Should().BeOfType<SmtpNotificationSender>();
    }
}
