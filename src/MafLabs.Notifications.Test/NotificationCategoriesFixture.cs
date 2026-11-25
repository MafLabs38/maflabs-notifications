using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MafLabs.Notifications.Test;

/// <summary>The categories a program declares: the registry's rules and what AddNotifications registers.</summary>
public sealed class NotificationCategoriesFixture
{
    private static NotificationCategories Portarr() => new NotificationCategories()
        .Add("apps", "Apps", "Applications that are down")
        .Add("data", "Data", "Backups that fail or are late", new CategoryPolicy { MinimumLevel = NotificationLevel.Danger, MaxPerDay = 2 })
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
    [InlineData("data\n")]
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

        categories.PolicyFor("data").Should().Be(categories.Get("data").DefaultPolicy);
        categories.PolicyFor("data", new Dictionary<string, CategoryPolicy>()).Should().Be(categories.Get("data").DefaultPolicy);
        categories.PolicyFor("data", new Dictionary<string, CategoryPolicy> { ["data"] = own }).Should().Be(own);
        categories.PolicyFor("apps", new Dictionary<string, CategoryPolicy> { ["data"] = own }).Should().Be(categories.Get("apps").DefaultPolicy);
    }

    [Fact]
    public void should_not_let_a_caller_change_a_validated_default()
    {
        var given = new CategoryPolicy { MaxPerDay = 2 };
        var categories = new NotificationCategories().Add("data", "Data", "Backups", given);

        given.MaxPerDay = 0;
        categories.PolicyFor("data").MaxPerDay = 0;
        categories.Get("data").DefaultPolicy.MaxPerDay = 0;
        categories.Find("data")!.DefaultPolicy.MaxPerDay = 0;
        categories.All[0].DefaultPolicy.MaxPerDay = 0;

        categories.PolicyFor("data").MaxPerDay.Should().Be(2);
        categories.PolicyFor("data").Should().NotBeSameAs(categories.PolicyFor("data"));
    }

    [Fact]
    public void should_refuse_an_operator_policy_out_of_range_with_the_sentences_of_the_validator()
    {
        var categories = Portarr();
        var corrupted = new Dictionary<string, CategoryPolicy> { ["data"] = new() { ReminderHours = 0, MaxPerDay = 5000 } };

        var policy = () => categories.PolicyFor("data", corrupted);

        policy.Should().Throw<InvalidOperationException>().WithMessage("*Category 'data': ReminderHours*Category 'data': MaxPerDay*");
        categories.PolicyFor("apps", corrupted).Should().Be(categories.Get("apps").DefaultPolicy);
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
        policy.MinimumLevel.Should().Be(NotificationLevel.Warning);
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
    public void should_keep_every_declaration_whatever_the_overloads_and_their_order()
    {
        var services = new ServiceCollection();

        services.AddNotifications(categories => categories.Add("apps", "Apps", "Applications"));
        services.AddNotifications();
        services.AddNotifications(categories => categories.Add("data", "Data", "Backups"));
        services.AddNotifications(categories => categories.Add("maintenance", "Maintenance", "Updates"));
        using var provider = services.BuildServiceProvider();

        provider.GetServices<NotificationCategories>().Should().ContainSingle();
        provider.GetRequiredService<NotificationCategories>().All.Select(category => category.Id).Should().Equal("apps", "data", "maintenance");
        var again = () => services.AddNotifications(categories => categories.Add("data", "Data", "Twice"));
        again.Should().Throw<ArgumentException>().WithMessage("*'data' is declared twice*");
    }

    [Fact]
    public void should_fail_at_startup_when_a_declaration_is_wrong()
    {
        var add = () => new ServiceCollection().AddNotifications(categories => categories.Add("data", "Data", "x").Add("data", "Data", "y"));

        add.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_bind_the_policy_options_from_the_notifications_policy_section()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Notifications:Policy:ConfirmMinutes"] = "2",
            ["Notifications:Policy:ClearMinutes"] = "30",
            ["Notifications:Policy:RetryMinutes"] = "45",
            ["Notifications:Policy:MaxPerDay"] = "7",
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddNotifications();
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<NotificationPolicyOptions>>().Value;

        options.Should().BeEquivalentTo(new NotificationPolicyOptions { ConfirmMinutes = 2, ClearMinutes = 30, RetryMinutes = 45, MaxPerDay = 7 });
    }

    [Fact]
    public void should_refuse_policy_options_out_of_range_when_they_are_read()
    {
        var services = new ServiceCollection();
        services.AddNotifications();
        services.Configure<NotificationPolicyOptions>(options => options.MaxPerDay = 0);
        using var provider = services.BuildServiceProvider();

        var read = () => provider.GetRequiredService<IOptions<NotificationPolicyOptions>>().Value;

        read.Should().Throw<OptionsValidationException>().WithMessage("*MaxPerDay is the most notifications in 24 hours, all categories together, from 1 to 1000*");
    }

    [Fact]
    public void should_validate_the_policy_options_at_startup()
    {
        var services = new ServiceCollection();
        services.AddNotifications();
        services.Configure<NotificationPolicyOptions>(options => options.ClearMinutes = 2000);
        using var provider = services.BuildServiceProvider();

        var start = () => provider.GetRequiredService<IStartupValidator>().Validate();

        start.Should().Throw<OptionsValidationException>().WithMessage("*ClearMinutes*from 1 to 1440*");
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
