using Foundation;
using SFMCSDK;
using SFMCSDK.Net;

namespace MarketingCloudSDK.Net;

/// <summary>
/// The iOS half of <see cref="MarketingCloudClient"/>, over the MarketingCloudSDK.Net.iOS binding
/// (namespace <c>MarketingCloudSDK</c> - the parent of this assembly's
/// <c>MarketingCloudSDK.Net</c>, so its types resolve here without a using directive, through
/// ordinary enclosing-namespace lookup) with the SFMCSDK.Net.iOS core binding (namespace
/// <c>SFMCSDK</c>) underneath it.
/// </summary>
public sealed partial class MarketingCloudClient
{
    /// <remarks>
    /// v11 initialization on iOS is the SFMC SDK core's <c>initializeSdk</c> with the MobilePush
    /// module config attached via <c>setPushFeature</c> - there is no separate MobilePush
    /// configure any more.
    ///
    /// VERIFIED against MarketingCloudSDK 11.0.2 on a simulator (and encoded the same way in the
    /// binding repository's device tests): the completion block reports CONCLUDED module
    /// statuses, and against an unprovisioned or unreachable tenant the push module's
    /// initialization may never conclude - no APNs entitlement, no answering tenant - so the
    /// block legitimately never fires while the SDK is, in every observable sense, up. The
    /// timeout is therefore not a verdict on this platform: when it expires, the façade asks
    /// <c>SFMCSdk.state</c> whether the pushfeature module is reported, and treats "it is" as
    /// initialized. A firing completion (a provisioned environment) completes the await
    /// immediately, well inside the timeout.
    /// </remarks>
    private async partial Task InitializeCore(MarketingCloudOptions options, CancellationToken cancellationToken)
    {
        if (options.LogLevel is { } level)
        {
            // Before initializeSdk, so the initialization itself is captured. One call covers
            // core and modules alike - v11's module loggers all flow through the core's logging
            // plane on this platform, unlike Android's two dials. A fresh default outputter
            // writes to os_log - the platform-natural sink, matching logcat on Android.
            SFMCSdk.SetLogger(ToNative(level), new SFMCSdkLogOutputter());
        }

        // NSUrl.FromString returns nil for strings NSURL cannot represent. The shared half
        // already proved the value parses as an absolute System.Uri, so a nil here is the rare
        // URL the two parsers disagree about - surfaced as the argument error it is rather than
        // as a native nil-dereference inside the builder.
        var serverUrl = NSUrl.FromString(options.ServerUrl)
            ?? throw new ArgumentException(
                $"MarketingCloudOptions.ServerUrl '{options.ServerUrl}' is not representable as " +
                "an NSURL.", nameof(options));

        var pushConfig = new SFMarketingCloudSdkConfigBuilder(options.ApplicationId)
            .SetAccessToken(options.AccessToken)
            .SetMarketingCloudServerUrl(serverUrl)
            .SetMid(options.Mid)
            .SetInboxEnabled(options.InboxEnabled)
            .SetAnalyticsEnabled(options.AnalyticsEnabled)
            .SetDelayRegistrationUntilContactKeyIsSet(options.DelayRegistrationUntilContactKeySet)
            .Build();

        var config = new SFMCSdkConfigBuilder()
            .SetPushFeature(pushConfig)
            .Build();

        try
        {
            // The statuses array is deliberately discarded: the block firing at all is the
            // signal (the modules concluded), and per-module detail is DiagnosticState's job.
            await AwaitNativeCompletion(
                complete => SFMCSdk.InitializeSdk(config, _ => complete()),
                "initialization", options.InitializationTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // The verified path described above: no conclusion is not no initialization.
            //
            // What this oracle does and does not prove, measured rather than assumed (see the
            // binding repository's committed simulator-tests.log): the core reports a module
            // roster, and the pushfeature entry is present there whether or not a push config was
            // ever supplied - in an unprovisioned simulator EVERY module reads
            // "status": "inactive", "version": "unavailable", including this one. So presence
            // means "the core is up and its module roster is intact", not "the module concluded".
            // It is deliberately the weak check: the strong one does not exist on this platform
            // (nothing in either state JSON reflects the configuration we passed), and failing
            // here instead would refuse to initialize on exactly the unprovisioned tenants and
            // simulators this fallback exists for. A caller that needs the difference reads
            // DiagnosticState, where the inactive statuses are visible verbatim.
            var state = SFMCSdk.State;
            if (state is not null && state.Contains("pushfeature", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            throw;
        }
    }

    private static partial bool SupportedCore() => true;

    /// <remarks>
    /// The read-side category selectors on <c>MobilePushSDK.sharedInstance</c>; each answers
    /// null until the module has something to report. The singleton itself exists before
    /// initialization, so touching it here is safe at any point in the lifecycle.
    /// </remarks>
    private partial string? ContactKeyCore() => MobilePushSDK.SharedInstance.ContactKey;

    private partial string? DeviceIdCore() => MobilePushSDK.SharedInstance.DeviceIdentifier;

    /// <remarks>
    /// Null, always, and documented on <see cref="IMarketingCloudRegistration.PushToken"/>
    /// rather than approximated: the v11 category surface exposes no APNs-token read - the 8.x
    /// <c>sfmc_deviceToken</c> selector is gone from the headers this binding is written
    /// against - because the SDK observes the token itself and never hands it back. Returning
    /// <see cref="DeviceIdCore"/> here instead would be a lie with a GUID's face.
    /// </remarks>
    private partial string? PushTokenCore() => null;

    /// <remarks>
    /// The recorded edits replay against the <c>sfmc_*</c> category selectors, each applying as
    /// it lands - iOS has no editor/commit cycle to batch under, so "one commit" on Android and
    /// "N selectors" here are the same edits with each platform's own delivery. All three
    /// members exist on this platform (<c>sfmc_addTag:</c>, <c>sfmc_removeTag:</c>,
    /// <c>sfmc_setAttributeNamed:value:</c>), so nothing routes elsewhere and nothing throws
    /// <see cref="PlatformNotSupportedException"/>.
    ///
    /// Each selector returns a bool the façade deliberately ignores: it means "accepted into
    /// the local registration right now", and a refusal can simply mean the module is still
    /// initializing - which, against an unprovisioned tenant, is a state it verifiably never
    /// leaves (see InitializeCore). Throwing on it would make the façade fail on exactly the
    /// tenants the rest of this file goes out of its way to support; surfacing per-op booleans
    /// would promise a per-op server truth neither platform has. Fire-and-forget, as the
    /// interface documents.
    /// </remarks>
    private partial Task ApplyEditsCore(IReadOnlyList<RegistrationEdit> edits)
    {
        var sdk = MobilePushSDK.SharedInstance;

        foreach (var edit in edits)
        {
            _ = edit.Kind switch
            {
                RegistrationEditKind.AddTag => sdk.AddTag(edit.Key),
                RegistrationEditKind.RemoveTag => sdk.RemoveTag(edit.Key),
                RegistrationEditKind.SetAttribute => sdk.SetAttributeNamed(edit.Key, edit.Value!),
                _ => throw new ArgumentOutOfRangeException(nameof(edits), edit.Kind, "Unknown edit kind."),
            };
        }

        return Task.CompletedTask;
    }

    /// <remarks>
    /// The MobilePush module's own state JSON (<c>sfmc_getSDKState</c>) when it answers, else
    /// the SFMC SDK core's state JSON - which exists from the moment the core is touched and is
    /// the same oracle InitializeCore's timeout fallback reads.
    /// </remarks>
    private partial string DiagnosticStateCore() =>
        MobilePushSDK.SharedInstance.SDKState ?? SFMCSdk.State ?? "unavailable";

    /// <remarks>
    /// The decorator that keeps the two identity planes agreeing on this platform. The core
    /// identity (<c>SFMCSdk.identity</c>) is where v11 wants contact-key writes, and the core
    /// façade's implementation is used untouched - validation included. But the MobilePush
    /// module ALSO still carries its own contact-key shim (<c>sfmc_setContactKey:</c>) and its
    /// own read (<c>sfmc_contactKey</c>, surfaced as
    /// <see cref="IMarketingCloudRegistration.ContactKey"/>), and in a not-yet-concluded module
    /// the two records can disagree. Writing both - core first, so its validation gates the
    /// pair - is deliberate belt-and-braces: whichever plane a consumer (or Salesforce's own
    /// compat layer) reads, it saw the same value. The returned bool is the same
    /// accepted-locally signal ApplyEditsCore documents ignoring.
    /// </remarks>
    private partial ISfmcIdentity CreateIdentityFacade(ISfmcIdentity coreIdentity) =>
        new AppleIdentityFacade(coreIdentity);

    private sealed class AppleIdentityFacade(ISfmcIdentity core) : ISfmcIdentity
    {
        public void SetProfileId(string profileId)
        {
            core.SetProfileId(profileId);
            _ = MobilePushSDK.SharedInstance.SetContactKey(profileId);
        }

        public void SetAttribute(string key, string value) => core.SetAttribute(key, value);

        public void ClearAttribute(string key) => core.ClearAttribute(key);
    }

    private static SFMCSdkLogLevel ToNative(SfmcLogLevel level) => level switch
    {
        SfmcLogLevel.Debug => SFMCSdkLogLevel.Debug,
        SfmcLogLevel.Warning => SFMCSdkLogLevel.Warn,
        SfmcLogLevel.Error => SFMCSdkLogLevel.Error,
        SfmcLogLevel.None => SFMCSdkLogLevel.None,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown SfmcLogLevel."),
    };
}
