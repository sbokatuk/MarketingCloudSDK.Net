using Xunit;

namespace MarketingCloudSDK.Net.UnitTests;

/// <summary>
/// The shared argument validation, asserted on the neutral assembly where a validation failure
/// is distinguishable from platform behaviour by exception type alone: arguments the façade
/// refuses throw <see cref="ArgumentException"/>-family errors <em>before</em> any platform seam
/// runs, so a <see cref="PlatformNotSupportedException"/> in these tests would mean bad input
/// leaked through to a platform that would fail with something unhelpful (a server-side
/// rejection minutes later, a nil-URL crash in a native builder) in a real app.
/// </summary>
public class ArgumentValidationTests
{
    [Fact]
    public async Task Initialize_refuses_null_options()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => new MarketingCloudClient().InitializeAsync(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Initialize_refuses_a_blank_application_id_naming_the_property(string blank)
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() => new MarketingCloudClient()
            .InitializeAsync(MarketingCloudOptionsTests.Valid() with { ApplicationId = blank }));

        // The property name is the debugging handle: four credentials arrive together, and a
        // bare "value cannot be blank" would send the reader off to log all of them.
        Assert.Contains("ApplicationId", error.Message);
    }

    [Fact]
    public async Task Initialize_refuses_a_blank_access_token_and_mid()
    {
        var client = new MarketingCloudClient();

        var token = await Assert.ThrowsAsync<ArgumentException>(() => client.InitializeAsync(
            MarketingCloudOptionsTests.Valid() with { AccessToken = " " }));
        Assert.Contains("AccessToken", token.Message);

        var mid = await Assert.ThrowsAsync<ArgumentException>(() => client.InitializeAsync(
            MarketingCloudOptionsTests.Valid() with { Mid = "" }));
        Assert.Contains("Mid", mid.Message);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("mc.test.device.marketingcloudapis.com")] // no scheme - relative, not absolute
    public async Task Initialize_refuses_a_server_url_that_is_not_absolute(string url)
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() => new MarketingCloudClient()
            .InitializeAsync(MarketingCloudOptionsTests.Valid() with { ServerUrl = url }));

        Assert.Contains("ServerUrl", error.Message);
    }

    [Fact]
    public async Task Initialize_refuses_a_non_positive_timeout_before_consuming_the_guard()
    {
        var client = new MarketingCloudClient();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.InitializeAsync(
            MarketingCloudOptionsTests.Valid() with { InitializationTimeout = TimeSpan.Zero }));

        // The refused call must not have claimed the one-shot slot: the next attempt with sane
        // options reaches the platform seam (which on this neutral head reports itself).
        await Assert.ThrowsAsync<PlatformNotSupportedException>(
            () => client.InitializeAsync(MarketingCloudOptionsTests.Valid()));
    }
}
