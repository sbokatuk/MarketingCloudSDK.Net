using SFMCSDK.Net;

namespace MarketingCloudSDK.Net;

/// <summary>
/// What <see cref="MarketingCloudClient.InitializeAsync"/> takes: the four MobilePush app
/// credentials, the feature toggles, and the wait mechanics. The credentials are
/// <c>required</c> - there is no anonymous MobilePush, so an options object without them is not
/// an object at all - and everything else has a default.
/// </summary>
/// <remarks>
/// <para>
/// A record with <c>init</c> setters rather than a mutable options bag: initialization is
/// one-shot (see <see cref="MarketingCloudClient"/>), so options that could be edited after the
/// call would only ever be a trap.
/// </para>
/// <para>
/// The four credentials come from MobilePush app administration in Marketing Cloud Setup. Never
/// commit them: load them from secure configuration, user secrets, or - as this repository's
/// sample does - runtime input.
/// </para>
/// </remarks>
public sealed record MarketingCloudOptions
{
    /// <summary>
    /// The MobilePush application id (a GUID-shaped string from MobilePush app administration).
    /// Must not be blank.
    /// </summary>
    public required string ApplicationId { get; init; }

    /// <summary>The MobilePush access token issued alongside the app id. Must not be blank.</summary>
    public required string AccessToken { get; init; }

    /// <summary>
    /// The tenant-specific endpoint, e.g. <c>https://mc.xxxx.device.marketingcloudapis.com/</c>.
    /// Must be an absolute URL - Android takes it as a string and iOS as an <c>NSURL</c>, and
    /// validating it once here keeps a malformed value from failing differently per platform.
    /// </summary>
    public required string ServerUrl { get; init; }

    /// <summary>The account MID the app registers against. Must not be blank.</summary>
    public required string Mid { get; init; }

    /// <summary>
    /// Whether the Inbox feature is enabled. Default: false, both platforms' own default.
    /// </summary>
    public bool InboxEnabled { get; init; }

    /// <summary>
    /// Whether Marketing Cloud analytics collection is enabled. Default: true, both platforms'
    /// own default.
    /// </summary>
    public bool AnalyticsEnabled { get; init; } = true;

    /// <summary>
    /// When true, the SDK holds its first registration until a contact key has been set - use it
    /// when the app identifies users at sign-in and an anonymous registration would create a
    /// contact you then have to merge. Default: false.
    /// </summary>
    /// <remarks>
    /// Set the contact key through <see cref="IMarketingCloudClient.Identity"/>
    /// (<see cref="SFMCSDK.Net.ISfmcIdentity.SetProfileId"/>).
    /// </remarks>
    public bool DelayRegistrationUntilContactKeySet { get; init; }

    /// <summary>
    /// How long <see cref="MarketingCloudClient.InitializeAsync"/> waits for the native SDK to
    /// report before giving up. Default: 30 seconds. Must be positive.
    /// </summary>
    /// <remarks>
    /// The two platforms spend it differently. Android's <c>MarketingCloudSdk.init</c> reports a
    /// terminal status through a callback, and expiry there is a failure
    /// (<see cref="TimeoutException"/>). iOS's completion block reports <em>concluded</em>
    /// module statuses, and against an unreachable or unprovisioned tenant the push module's
    /// initialization may never conclude - verified upstream behaviour - so expiry there falls
    /// back to inspecting the SDK's state; see
    /// <see cref="IMarketingCloudClient.InitializeAsync"/>.
    /// </remarks>
    public TimeSpan InitializationTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Native SDK log verbosity, applied immediately before initialization so the initialization
    /// itself is captured. Null - the default - leaves each platform's own default in place.
    /// </summary>
    /// <remarks>
    /// The enum is <see cref="SFMCSDK.Net.SfmcLogLevel"/>, reused from the core façade rather
    /// than duplicated here: it is the same underlying logging machinery, and two enums for one
    /// dial would guarantee they drift. Output goes to each platform's natural sink - os_log on
    /// iOS, logcat on Android.
    /// </remarks>
    public SfmcLogLevel? LogLevel { get; init; }
}
