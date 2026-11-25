namespace MafLabs.Notifications.Test;

/// <summary>When to write: a table of decisions, as pure functions of the clock and of plain values.</summary>
public sealed class NotificationDeciderFixture
{
    private static readonly DateTimeOffset Start = new(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);

    private static readonly CategoryPolicy Policy = new() { ReminderHours = 24, MaxReminders = 3, MaxPerDay = 3, MinimumLevel = NotificationLevel.Warning };

    private static readonly SentToday Quiet = new(0, 0);

    private static EpisodeState Fresh() => new(Start, Start, 0, null, null);

    private static EpisodeState Mailed(int mails, DateTimeOffset lastMailedAt, DateTimeOffset? lastAttemptAt = null) =>
        new(Start, lastMailedAt, mails, lastMailedAt, lastAttemptAt ?? lastMailedAt);

    private static NotificationDecision Decide(DateTimeOffset now, EpisodeState episode, CategoryPolicy? policy = null, SentToday? today = null, NotificationLevel level = NotificationLevel.Danger, NotificationPolicyOptions? options = null) =>
        NotificationDecider.Decide(now, level, policy ?? Policy, episode, today ?? Quiet, options);

    [Fact]
    public void should_send_the_first_mail_once_the_problem_has_lasted_long_enough()
    {
        var decision = Decide(Start.AddMinutes(5), Fresh());

        decision.Should().Be(new NotificationDecision(NotificationAction.SendFirst, DecisionReason.FirstNotification));
        decision.Send.Should().BeTrue();
    }

    [Fact]
    public void should_wait_for_the_confirmation_delay_so_that_a_blip_is_not_a_mail()
    {
        Decide(Start.AddMinutes(4), Fresh()).Should().Be(new NotificationDecision(NotificationAction.None, DecisionReason.NotConfirmedYet));
    }

    [Fact]
    public void should_use_the_confirmation_delay_of_the_options()
    {
        var options = new NotificationPolicyOptions { ConfirmMinutes = 30 };

        Decide(Start.AddMinutes(29), Fresh(), options: options).Reason.Should().Be(DecisionReason.NotConfirmedYet);
        Decide(Start.AddMinutes(30), Fresh(), options: options).Action.Should().Be(NotificationAction.SendFirst);
    }

    [Fact]
    public void should_stay_silent_when_the_category_is_disabled()
    {
        var policy = Policy with { Enabled = false };

        Decide(Start.AddHours(1), Fresh(), policy).Should().Be(new NotificationDecision(NotificationAction.None, DecisionReason.CategoryDisabled));
    }

    [Theory]
    [InlineData(NotificationLevel.Ok, NotificationLevel.Warning, false)]
    [InlineData(NotificationLevel.Info, NotificationLevel.Warning, false)]
    [InlineData(NotificationLevel.Warning, NotificationLevel.Warning, true)]
    [InlineData(NotificationLevel.Danger, NotificationLevel.Warning, true)]
    [InlineData(NotificationLevel.Warning, NotificationLevel.Danger, false)]
    [InlineData(NotificationLevel.Danger, NotificationLevel.Danger, true)]
    [InlineData(NotificationLevel.Ok, NotificationLevel.Ok, true)]
    public void should_write_only_from_the_minimum_level_up(NotificationLevel level, NotificationLevel minimum, bool sends)
    {
        var decision = Decide(Start.AddHours(1), Fresh(), Policy with { MinimumLevel = minimum }, level: level);

        decision.Send.Should().Be(sends);
        if (!sends)
        {
            decision.Reason.Should().Be(DecisionReason.BelowMinimumLevel);
        }
    }

    [Theory]
    [InlineData(23, DecisionReason.ReminderNotDue)]
    [InlineData(24, DecisionReason.ReminderDue)]
    [InlineData(30, DecisionReason.ReminderDue)]
    public void should_space_the_reminders_by_the_hours_of_the_category(int hoursAfterTheMail, DecisionReason reason)
    {
        var mailedAt = Start.AddMinutes(5);

        var decision = Decide(mailedAt.AddHours(hoursAfterTheMail), Mailed(1, mailedAt));

        decision.Reason.Should().Be(reason);
        decision.Action.Should().Be(reason == DecisionReason.ReminderDue ? NotificationAction.SendReminder : NotificationAction.None);
    }

    [Fact]
    public void should_follow_the_hours_of_each_category_apart()
    {
        var mailedAt = Start.AddMinutes(5);
        var hourly = Policy with { ReminderHours = 1 };

        Decide(mailedAt.AddHours(1), Mailed(1, mailedAt), hourly).Action.Should().Be(NotificationAction.SendReminder);
        Decide(mailedAt.AddHours(1), Mailed(1, mailedAt)).Action.Should().Be(NotificationAction.None);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    public void should_stop_after_the_most_reminders_the_category_allows(int mailsSent, bool reminds)
    {
        var mailedAt = Start.AddMinutes(5);

        var decision = Decide(mailedAt.AddDays(2), Mailed(mailsSent, mailedAt));

        decision.Send.Should().Be(reminds);
        if (!reminds)
        {
            decision.Reason.Should().Be(DecisionReason.RemindersExhausted);
        }
    }

    [Fact]
    public void should_send_a_single_mail_when_no_reminder_is_allowed()
    {
        var once = Policy with { MaxReminders = 0 };
        var mailedAt = Start.AddMinutes(5);

        Decide(Start.AddMinutes(5), Fresh(), once).Action.Should().Be(NotificationAction.SendFirst);
        Decide(mailedAt.AddDays(2), Mailed(1, mailedAt), once).Reason.Should().Be(DecisionReason.RemindersExhausted);
    }

    [Fact]
    public void should_pause_after_a_mail_that_could_not_leave_then_try_again()
    {
        var failed = new EpisodeState(Start, Start.AddMinutes(20), 0, null, Start.AddMinutes(10));

        Decide(Start.AddMinutes(24), failed).Should().Be(new NotificationDecision(NotificationAction.None, DecisionReason.WaitingAfterFailure));
        Decide(Start.AddMinutes(25), failed).Should().Be(new NotificationDecision(NotificationAction.SendFirst, DecisionReason.FirstNotification));
    }

    [Fact]
    public void should_pause_after_a_reminder_that_could_not_leave()
    {
        var mailedAt = Start.AddMinutes(5);
        var failedAt = mailedAt.AddHours(24);
        var episode = Mailed(1, mailedAt, failedAt);

        episode.LastAttemptFailed.Should().BeTrue();
        Decide(failedAt.AddMinutes(14), episode).Reason.Should().Be(DecisionReason.WaitingAfterFailure);
        Decide(failedAt.AddMinutes(15), episode).Action.Should().Be(NotificationAction.SendReminder);
    }

    [Fact]
    public void should_not_see_a_failure_in_an_attempt_that_went_through()
    {
        Mailed(1, Start.AddMinutes(5)).LastAttemptFailed.Should().BeFalse();
        Fresh().LastAttemptFailed.Should().BeFalse();
    }

    [Fact]
    public void should_use_the_retry_delay_of_the_options()
    {
        var failed = new EpisodeState(Start, Start.AddMinutes(20), 0, null, Start.AddMinutes(10));

        Decide(Start.AddMinutes(11), failed, options: new NotificationPolicyOptions { RetryMinutes = 1 }).Action.Should().Be(NotificationAction.SendFirst);
    }

    [Theory]
    [InlineData(2, 0, false)]
    [InlineData(3, 0, true)]
    [InlineData(0, 9, false)]
    [InlineData(0, 10, true)]
    public void should_hold_the_daily_caps(int inCategory, int overall, bool capped)
    {
        var decision = Decide(Start.AddHours(1), Fresh(), today: new SentToday(inCategory, overall));

        decision.Send.Should().Be(!capped);
        if (capped)
        {
            decision.Reason.Should().Be(inCategory >= 3 ? DecisionReason.CategoryDayCapReached : DecisionReason.DayCapReached);
        }
    }

    [Fact]
    public void should_cap_a_reminder_too()
    {
        var mailedAt = Start.AddMinutes(5);

        Decide(mailedAt.AddHours(24), Mailed(1, mailedAt), today: new SentToday(3, 3)).Reason.Should().Be(DecisionReason.CategoryDayCapReached);
    }

    [Fact]
    public void should_cap_a_category_by_its_own_number_and_all_by_the_options()
    {
        var one = Policy with { MaxPerDay = 1 };

        Decide(Start.AddHours(1), Fresh(), one, new SentToday(1, 1)).Reason.Should().Be(DecisionReason.CategoryDayCapReached);
        Decide(Start.AddHours(1), Fresh(), Policy, new SentToday(0, 2), options: new NotificationPolicyOptions { MaxPerDay = 2 }).Reason.Should().Be(DecisionReason.DayCapReached);
    }

    [Fact]
    public void should_refuse_an_episode_with_mails_delivered_and_no_date_for_the_last()
    {
        var odd = () => new EpisodeState(Start, Start, 2, null, null);

        odd.Should().Throw<ArgumentException>().WithMessage("*LastSentAt*");
        var fresh = Fresh() with { NotificationsSent = 0 };
        Decide(Start.AddHours(1), fresh).Reason.Should().Be(DecisionReason.FirstNotification);
        var bypassed = Fresh() with { NotificationsSent = 2 };
        var decide = () => Decide(Start.AddHours(1), bypassed);
        decide.Should().Throw<ArgumentException>().WithMessage("*LastSentAt*");
    }

    [Fact]
    public void should_drop_at_once_an_episode_that_was_never_mailed()
    {
        NotificationDecider.HasEnded(Fresh(), new NotificationPolicyOptions(), Start.AddMinutes(1), Start).Should().BeTrue();
    }

    [Fact]
    public void should_keep_a_mailed_episode_until_the_clear_delay_has_passed()
    {
        var episode = Mailed(1, Start.AddMinutes(5));
        var options = new NotificationPolicyOptions { ClearMinutes = 15 };
        var seenAt = episode.LastSeenAt;

        NotificationDecider.HasEnded(episode, options, seenAt.AddMinutes(14), Start).Should().BeFalse();
        NotificationDecider.HasEnded(episode, options, seenAt.AddMinutes(15), Start).Should().BeTrue();
    }

    [Fact]
    public void should_count_the_clear_delay_from_the_start_of_the_run_after_a_restart()
    {
        var episode = Mailed(1, Start.AddMinutes(5));
        var restart = episode.LastSeenAt.AddHours(3);
        var options = new NotificationPolicyOptions { ClearMinutes = 15 };

        NotificationDecider.HasEnded(episode, options, restart.AddMinutes(1), restart).Should().BeFalse();
        NotificationDecider.HasEnded(episode, options, restart.AddMinutes(15), restart).Should().BeTrue();
    }

    [Fact]
    public void should_say_whether_another_reminder_follows()
    {
        var mailedAt = Start.AddMinutes(5);

        NotificationDecider.WillRemind(Fresh(), Policy).Should().BeTrue();
        NotificationDecider.WillRemind(Mailed(2, mailedAt), Policy).Should().BeTrue();
        NotificationDecider.WillRemind(Mailed(3, mailedAt), Policy).Should().BeFalse();
        NotificationDecider.WillRemind(Fresh(), Policy with { MaxReminders = 0 }).Should().BeFalse();
    }

    [Fact]
    public void should_have_defaults_that_pass_the_validators()
    {
        new NotificationPolicyOptions().Check().Should().BeNull();
        CategoryPolicyValidator.Validate("data", new CategoryPolicy()).Should().BeEmpty();
        new NotificationPolicyOptionsValidator().Validate(null, new NotificationPolicyOptions()).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(-1, 15, 15, 10, "ConfirmMinutes is the minutes a problem must last before it is notified, from 0 to 1440.")]
    [InlineData(1441, 15, 15, 10, "ConfirmMinutes is the minutes a problem must last before it is notified, from 0 to 1440.")]
    [InlineData(5, 0, 15, 10, "ClearMinutes is the minutes a notified problem must be gone before its episode ends, from 1 to 1440.")]
    [InlineData(5, 1441, 15, 10, "ClearMinutes is the minutes a notified problem must be gone before its episode ends, from 1 to 1440.")]
    [InlineData(5, 15, 0, 10, "RetryMinutes is the minutes to wait after a notification that could not be sent, from 1 to 1440.")]
    [InlineData(5, 15, 1441, 10, "RetryMinutes is the minutes to wait after a notification that could not be sent, from 1 to 1440.")]
    [InlineData(5, 15, 15, 0, "MaxPerDay is the most notifications in 24 hours, all categories together, from 1 to 1000.")]
    [InlineData(5, 15, 15, 1001, "MaxPerDay is the most notifications in 24 hours, all categories together, from 1 to 1000.")]
    public void should_refuse_options_out_of_range_with_the_sentence_of_the_wrong_setting(int confirm, int clear, int retry, int perDay, string sentence)
    {
        var options = new NotificationPolicyOptions { ConfirmMinutes = confirm, ClearMinutes = clear, RetryMinutes = retry, MaxPerDay = perDay };

        options.Check().Should().Be("Notification policy: " + sentence);
        var result = new NotificationPolicyOptionsValidator().Validate(null, options);
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Be("Notification policy: " + sentence);
    }

    [Fact]
    public void should_say_every_wrong_option_not_only_the_first()
    {
        var options = new NotificationPolicyOptions { ConfirmMinutes = -1, MaxPerDay = 0 };

        options.Check().Should().Contain("ConfirmMinutes").And.Contain("MaxPerDay").And.NotContain("ClearMinutes");
    }

    [Theory]
    [InlineData(0, 3, 3, "ReminderHours")]
    [InlineData(721, 3, 3, "ReminderHours")]
    [InlineData(24, -1, 3, "MaxReminders")]
    [InlineData(24, 31, 3, "MaxReminders")]
    [InlineData(24, 3, 0, "MaxPerDay")]
    [InlineData(24, 3, 1001, "MaxPerDay")]
    public void should_say_in_a_sentence_what_is_wrong_with_a_category_policy(int hours, int reminders, int perDay, string setting)
    {
        var problems = CategoryPolicyValidator.Validate("data", new CategoryPolicy { ReminderHours = hours, MaxReminders = reminders, MaxPerDay = perDay });

        problems.Should().ContainSingle().Which.Should().StartWith("Category 'data':").And.Contain(setting);
    }

    [Fact]
    public void should_refuse_a_level_that_does_not_exist()
    {
        CategoryPolicyValidator.Validate("data", new CategoryPolicy { MinimumLevel = (NotificationLevel)42 }).Should().ContainSingle().Which.Should().Contain("MinimumLevel").And.Contain("Danger");
    }

    [Fact]
    public void should_list_every_problem_not_only_the_first()
    {
        CategoryPolicyValidator.Validate("data", new CategoryPolicy { ReminderHours = 0, MaxReminders = 99, MaxPerDay = 0 }).Should().HaveCount(3);
    }

    [Fact]
    public void should_refuse_a_negative_count()
    {
        var now = DateTimeOffset.UnixEpoch;
        ((Action)(() => _ = new EpisodeState(now, now, -1, null, null))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => _ = new SentToday(-1, 0))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => _ = new SentToday(0, -1))).Should().Throw<ArgumentOutOfRangeException>();
    }
}
