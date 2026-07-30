using Xunit;

namespace MarketingCloudSDK.Net.UnitTests;

/// <summary>
/// Pins down the behaviour of the neutral (plain net8.0/net9.0/net10.0) leg: construction
/// succeeds, and every member that would reach the native SDK throws
/// <see cref="PlatformNotSupportedException"/> with a message that says where the real
/// implementation lives. This test project resolves the façade's real neutral assembly (see the
/// csproj), so what is asserted here is exactly what a consumer's shared class library hits.
/// </summary>
/// <remarks>
/// The message assertions name the platform heads rather than pin exact wording, so the message
/// can be reworded freely - but one that stops telling the reader where the real implementation
/// lives fails. Throwing (rather than no-oping) is itself the contract under test: a
/// registration that silently never happened on a Windows head would read as devices missing
/// from Marketing Cloud, with nothing logged anywhere.
/// </remarks>
public class NeutralPlatformTests
{
    private static void AssertNamesThePlatformHeads(PlatformNotSupportedException error)
    {
        Assert.Contains("net*-android", error.Message);
        Assert.Contains("net*-ios", error.Message);
    }

    [Fact]
    public void Construction_succeeds_so_shared_object_graphs_can_be_built_anywhere()
    {
        var client = new MarketingCloudClient();

        Assert.NotNull(client.Identity);
        Assert.NotNull(client.Registration);
    }

    [Fact]
    public async Task Initialize_throws_and_names_the_platform_heads()
    {
        var error = await Assert.ThrowsAsync<PlatformNotSupportedException>(
            () => new MarketingCloudClient().InitializeAsync(MarketingCloudOptionsTests.Valid()));

        AssertNamesThePlatformHeads(error);
    }

    [Fact]
    public void Registration_reads_throw_and_name_the_platform_heads()
    {
        var registration = new MarketingCloudClient().Registration;

        AssertNamesThePlatformHeads(
            Assert.Throws<PlatformNotSupportedException>(() => _ = registration.ContactKey));
        AssertNamesThePlatformHeads(
            Assert.Throws<PlatformNotSupportedException>(() => _ = registration.DeviceId));
        AssertNamesThePlatformHeads(
            Assert.Throws<PlatformNotSupportedException>(() => _ = registration.PushToken));
    }

    [Fact]
    public async Task EditAsync_throws_and_names_the_platform_heads()
    {
        var error = await Assert.ThrowsAsync<PlatformNotSupportedException>(
            () => new MarketingCloudClient().Registration.EditAsync(editor => editor.AddTag("vip")));

        AssertNamesThePlatformHeads(error);
    }

    [Fact]
    public void Identity_members_throw_the_composed_core_facades_neutral_contract()
    {
        // Identity is delegated to SFMCSDK.Net (v11 moved identity to the core), so the
        // exception here is that package's documented neutral contract - its message names the
        // same net*-android / net*-ios heads. Asserted through the same helper: if the core
        // façade ever stopped pointing at the platform heads, this façade's documentation
        // becomes wrong too, and this failure is the prompt.
        var identity = new MarketingCloudClient().Identity;

        AssertNamesThePlatformHeads(
            Assert.Throws<PlatformNotSupportedException>(() => identity.SetProfileId("contact-key")));
        AssertNamesThePlatformHeads(
            Assert.Throws<PlatformNotSupportedException>(() => identity.SetAttribute("plan", "pro")));
        AssertNamesThePlatformHeads(
            Assert.Throws<PlatformNotSupportedException>(() => identity.ClearAttribute("plan")));
    }

    [Fact]
    public void DiagnosticState_throws_and_names_the_platform_heads()
    {
        var error = Assert.Throws<PlatformNotSupportedException>(
            () => _ = new MarketingCloudClient().DiagnosticState);

        AssertNamesThePlatformHeads(error);
    }

    [Fact]
    public void TrackCustomEvent_throws_the_composed_core_facades_neutral_contract()
    {
        // Like Identity, events are delegated to SFMCSDK.Net - so this is that package's neutral
        // contract surfacing through this façade, and the same helper holds it to naming the heads.
        var error = Assert.Throws<PlatformNotSupportedException>(
            () => new MarketingCloudClient().TrackCustomEvent("checkout_started"));

        AssertNamesThePlatformHeads(error);
    }

    [Fact]
    public void IsSupported_and_IsInitialized_answer_instead_of_throwing()
    {
        // The two members that must never throw: they are what shared code branches on before
        // touching anything else, and a guard that needs its own guard is not a guard. This is the
        // whole contract - if either of these ever starts throwing on a neutral head, every
        // documented "check IsSupported first" pattern breaks at once.
        var client = new MarketingCloudClient();

        Assert.False(client.IsSupported);
        Assert.False(client.IsInitialized);
    }

    [Fact]
    public async Task IsInitialized_stays_false_when_initialization_throws()
    {
        var client = new MarketingCloudClient();

        await Assert.ThrowsAsync<PlatformNotSupportedException>(
            () => client.InitializeAsync(MarketingCloudOptionsTests.Valid()));

        // The one-shot claim is taken (the guard protects a native SDK that may be mid-configure),
        // but nothing came up - so the status flag and the guard deliberately disagree here.
        Assert.False(client.IsInitialized);
    }
}
