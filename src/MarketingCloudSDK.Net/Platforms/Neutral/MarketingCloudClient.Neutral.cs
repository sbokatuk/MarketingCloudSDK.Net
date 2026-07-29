using SFMCSDK.Net;

namespace MarketingCloudSDK.Net;

/// <summary>
/// The neutral-target-framework half of <see cref="MarketingCloudClient"/> - the counterpart of
/// Platforms/Android and Platforms/Apple, compiled into the plain net8.0/net9.0/net10.0 build.
/// There is no native MobilePush SDK on these target frameworks; construction succeeds so object
/// graphs and tests can be built anywhere, and every member that would reach the native SDK
/// throws instead.
/// </summary>
/// <remarks>
/// <para>
/// The leg exists so shared code - ViewModels, unit tests, a MAUI app's Windows head - can
/// reference the package and program against <see cref="IMarketingCloudClient"/> instead of
/// failing restore with NU1202. Run the same code in a net*-android or net*-ios application head
/// and the package resolves the platform bindings and these seams become the real
/// implementations.
/// </para>
/// <para>
/// <see cref="IMarketingCloudClient.Identity"/> is the one member whose neutral behaviour is not
/// authored here: it hands out the composed SFMCSDK.Net core identity unchanged, and that
/// package's own neutral leg throws its documented <see cref="PlatformNotSupportedException"/> -
/// whose message names the same net*-android / net*-ios heads this one does. One neutral
/// contract, stated once per package, never contradicting itself.
/// </para>
/// </remarks>
public sealed partial class MarketingCloudClient
{
    private static PlatformNotSupportedException NotSupported() => new(
        "MarketingCloudSDK.Net has no native MobilePush SDK on this target framework - the " +
        "neutral build exists so shared code can reference the package and program against " +
        "IMarketingCloudClient. Run in a net*-android or net*-ios application head, where the " +
        "same package resolves the MarketingCloudSDK.Net.Android / MarketingCloudSDK.Net.iOS " +
        "bindings and this member drives the real SDK.");

    // Everything below satisfies the shared half's partial declarations. Throwing rather than
    // no-oping is deliberate: a tag edit or an initialization that silently vanished on the
    // Windows head of a MAUI app would read as devices missing from Marketing Cloud, with
    // nothing logged anywhere. Code that must run on neutral heads guards the calls or injects
    // a fake.

    private partial Task InitializeCore(MarketingCloudOptions options, CancellationToken cancellationToken) =>
        throw NotSupported();

    private partial ISfmcIdentity CreateIdentityFacade(ISfmcIdentity coreIdentity) => coreIdentity;

    private partial string? ContactKeyCore() => throw NotSupported();

    private partial string? DeviceIdCore() => throw NotSupported();

    private partial string? PushTokenCore() => throw NotSupported();

    private partial Task ApplyEditsCore(IReadOnlyList<RegistrationEdit> edits) => throw NotSupported();

    private partial string DiagnosticStateCore() => throw NotSupported();
}
