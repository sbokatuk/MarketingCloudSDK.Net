using Xunit;

namespace MarketingCloudSDK.Net.UnitTests;

/// <summary>
/// Pins the option defaults. They are part of the public contract - an app that sets only the
/// four credentials gets exactly this behaviour, and a changed default is a behaviour change
/// every consumer inherits silently, so a failure here is the prompt to write it into the
/// release notes rather than ship it as a surprise.
/// </summary>
public class MarketingCloudOptionsTests
{
    /// <summary>Valid options with every optional member left at its default.</summary>
    internal static MarketingCloudOptions Valid() => new()
    {
        ApplicationId = "00000000-0000-0000-0000-000000000000",
        AccessToken = "unit-test-token",
        ServerUrl = "https://mc.test.device.marketingcloudapis.com/",
        Mid = "000000000",
    };

    [Fact]
    public void Inbox_defaults_off_analytics_on_delay_off()
    {
        var options = Valid();

        Assert.False(options.InboxEnabled);
        Assert.True(options.AnalyticsEnabled);
        Assert.False(options.DelayRegistrationUntilContactKeySet);
    }

    [Fact]
    public void Initialization_timeout_defaults_to_thirty_seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), Valid().InitializationTimeout);
    }

    [Fact]
    public void Log_level_defaults_to_null_leaving_the_platform_default_in_place()
    {
        Assert.Null(Valid().LogLevel);
    }

    [Fact]
    public void Options_are_a_record_so_with_expressions_work_for_partial_overrides()
    {
        var options = Valid() with { InboxEnabled = true };
        var adjusted = options with { InitializationTimeout = TimeSpan.FromSeconds(5) };

        // The point being pinned: overriding one property must not disturb the others - the
        // record-ness of the type is what makes partial overrides safe to recommend in docs.
        Assert.True(adjusted.InboxEnabled);
        Assert.Equal("unit-test-token", adjusted.AccessToken);
        Assert.Equal(TimeSpan.FromSeconds(5), adjusted.InitializationTimeout);
        Assert.Equal(TimeSpan.FromSeconds(30), options.InitializationTimeout);
    }
}
