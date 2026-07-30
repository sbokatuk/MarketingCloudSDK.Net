using Xunit;

namespace MarketingCloudSDK.Net.UnitTests;

/// <summary>
/// Pins down <see cref="NullMarketingCloudClient"/> - the no-op client shipped for heads with no
/// MobilePush SDK. What matters about it is the pair of promises consumers register it on: nothing
/// throws, and nothing silently swallows a caller's own bug.
/// </summary>
public class NullMarketingCloudClientTests
{
    private static readonly MarketingCloudOptions Options = MarketingCloudOptionsTests.Valid();

    [Fact]
    public async Task Every_member_answers_without_throwing()
    {
        // Deliberately one test over the whole surface: the promise is "no member of this type
        // throws", and asserting it member-by-member would let a newly added member slip through
        // unasserted while still reading as covered.
        IMarketingCloudClient client = new NullMarketingCloudClient();

        Assert.False(client.IsSupported);
        Assert.False(client.IsInitialized);

        await client.InitializeAsync(Options);
        Assert.True(client.IsInitialized);

        client.Identity.SetProfileId("contact-key");
        client.Identity.SetAttribute("plan", "pro");
        client.Identity.ClearAttribute("plan");
        client.TrackCustomEvent("checkout_started", new Dictionary<string, string> { ["step"] = "1" });

        Assert.Null(client.Registration.ContactKey);
        Assert.Null(client.Registration.DeviceId);
        Assert.Null(client.Registration.PushToken);
        await client.Registration.EditAsync(editor =>
        {
            editor.AddTag("vip");
            editor.RemoveTag("trial");
            editor.SetAttribute("plan", "pro");
        });

        Assert.NotEmpty(client.DiagnosticState);
    }

    [Fact]
    public void DiagnosticState_says_plainly_that_no_sdk_ran()
    {
        // A diagnostics screen or bug report must not read as "an SDK with nothing to say".
        Assert.Contains("no-op", new NullMarketingCloudClient().DiagnosticState, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Initialization_is_not_one_shot()
    {
        // The real client's guard protects a native SDK that cannot be configured twice. There is
        // nothing here to protect, and a fake that threw on a second call would fail tests that
        // exercise a restart path.
        var client = new NullMarketingCloudClient();

        await client.InitializeAsync(Options);
        await client.InitializeAsync(Options);
    }

    [Fact]
    public async Task Cancellation_is_honoured()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new NullMarketingCloudClient().InitializeAsync(Options, cancelled.Token));
    }

    [Fact]
    public async Task Caller_bugs_still_surface()
    {
        // The line between "does nothing" and "hides mistakes". A blank tag is a bug on every
        // head, and the fake is often the only implementation a unit test ever runs - so the
        // validation has to fire here too, or the bug is found in production instead.
        var client = new NullMarketingCloudClient();

        await Assert.ThrowsAsync<ArgumentNullException>(() => client.Registration.EditAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(
            () => client.Registration.EditAsync(editor => editor.AddTag("   ")));
        await Assert.ThrowsAsync<ArgumentException>(
            () => client.Registration.EditAsync(editor => editor.SetAttribute("", "pro")));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => client.Registration.EditAsync(editor => editor.SetAttribute("plan", null!)));
    }
}
