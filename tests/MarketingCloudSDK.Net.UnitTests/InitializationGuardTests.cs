using Xunit;

namespace MarketingCloudSDK.Net.UnitTests;

/// <summary>
/// The one-shot initialization guard. These run against the neutral assembly, where the first
/// call's platform seam throws <see cref="PlatformNotSupportedException"/> - which is exactly
/// what makes the guard's semantics observable without a device: the second call must be refused
/// by the guard <em>even though the first never succeeded</em>, because the native configure
/// underneath is not retryable and a released slot would invite a race with the first attempt.
/// </summary>
public class InitializationGuardTests
{
    [Fact]
    public async Task Second_initialize_throws_InvalidOperationException_naming_the_rule()
    {
        var client = new MarketingCloudClient();

        // First call: claims the slot, then hits the neutral seam.
        await Assert.ThrowsAsync<PlatformNotSupportedException>(
            () => client.InitializeAsync(MarketingCloudOptionsTests.Valid()));

        // Second call: refused by the guard before any seam is reached.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.InitializeAsync(MarketingCloudOptionsTests.Valid()));

        // The message must carry the rule (one-shot, stays claimed) rather than a bare "already
        // initialized" - the exception is the documentation at the moment it is needed.
        Assert.Contains("one-shot", error.Message);
        Assert.Contains("InitializeAsync", error.Message);
    }

    [Fact]
    public async Task The_guard_is_per_instance_matching_the_documented_singleton_intent()
    {
        var first = new MarketingCloudClient();
        var second = new MarketingCloudClient();

        await Assert.ThrowsAsync<PlatformNotSupportedException>(
            () => first.InitializeAsync(MarketingCloudOptionsTests.Valid()));

        // A fresh instance has a fresh slot. Asserted so the documented model - the client is
        // meant to be a process singleton, the instance is what promises one-shot semantics -
        // stays true in code: if the guard ever became static, this test failing is the prompt
        // to rewrite the docs and the unit tests together rather than let them drift.
        await Assert.ThrowsAsync<PlatformNotSupportedException>(
            () => second.InitializeAsync(MarketingCloudOptionsTests.Valid()));
    }
}
