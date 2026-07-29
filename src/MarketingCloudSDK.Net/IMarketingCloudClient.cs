using SFMCSDK.Net;

namespace MarketingCloudSDK.Net;

/// <summary>
/// Initializes Salesforce Marketing Cloud MobilePush and drives what an app does with it from
/// shared code: identity (contact key and attributes), registration reads and edits, and a
/// diagnostic line - the same surface on Android and iOS.
///
/// The platform bindings underneath do not resemble each other. Android's v11 entry point is
/// <c>MarketingCloudSdk.init</c> with a <c>Context</c> and a builder-built config, reporting one
/// terminal status through a listener, with the SDK instance handed out asynchronously by
/// <c>requestSdk</c>; iOS's is the SFMC SDK core's <c>initializeSdk</c> with a
/// <c>setPushFeature</c> module config, reporting per-module completion blocks, with the module
/// surface living on <c>MobilePushSDK.sharedInstance</c>'s <c>sfmc_*</c> categories. This is the
/// layer that hides the difference behind one awaitable initialize and two ordinary sub-objects.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately small. The façade carries the operations an app performs from shared code on
/// every screen - initialize, identify, tag, read the registration - and nothing else. For
/// everything platform-shaped (inbox models, in-app messages, notification customization,
/// location messaging, the event bus), reach past it to the bindings it is built on: namespace
/// <c>Com.Salesforce.Marketingcloud</c> on Android (package MarketingCloudSDK.Net.Android) and
/// namespaces <c>MarketingCloudSDK</c> + <c>SFMCSDK</c> on iOS (packages
/// MarketingCloudSDK.Net.iOS + SFMCSDK.Net.iOS). All arrive with this package on their platform,
/// so the escape hatch needs no extra reference.
/// </para>
/// <para>
/// Push plumbing this façade does NOT wrap, because at v11 there is nothing left to wrap: the
/// SDK observes the push token itself on both platforms (Firebase on Android, APNs via method
/// interception on iOS), so no app-delegate or service forwarding is required - unlike the 8.x
/// generation. What remains the app's job is the OS-side setup: a Firebase project
/// (google-services.json) and the Android 13+ <c>POST_NOTIFICATIONS</c> runtime permission on
/// Android; the APNs entitlement, a <c>UNUserNotificationCenter</c> authorization request and
/// <c>RegisterForRemoteNotifications</c> on iOS. See the README's push prerequisites.
/// </para>
/// <para>
/// On the plain (neutral) target frameworks the interface exists so shared code can compile and
/// inject against it, and every member of the packaged implementation throws
/// <see cref="PlatformNotSupportedException"/> - see <see cref="MarketingCloudClient"/>.
/// </para>
/// </remarks>
public interface IMarketingCloudClient
{
    /// <summary>
    /// Initializes MobilePush with the given credentials, completing when the SDK has reported -
    /// Android's terminal initialization listener, or iOS's module completion block.
    /// </summary>
    /// <remarks>
    /// One asymmetry is deliberately absorbed rather than surfaced, because it is verified
    /// upstream behaviour: on iOS the completion block reports <em>concluded</em> module
    /// statuses, and against an unreachable or unprovisioned tenant the push module never
    /// concludes - so when the block has not fired within
    /// <see cref="MarketingCloudOptions.InitializationTimeout"/>, the façade inspects
    /// <c>SFMCSdk.state</c> and treats "the pushfeature module is reported" as initialized,
    /// exactly as the binding repository's device tests encode it. See <c>Platforms/Apple</c>.
    /// </remarks>
    /// <param name="options">Credentials, feature toggles, timeout and log level.</param>
    /// <param name="cancellationToken">
    /// Cancels the wait, not the native initialization - neither SDK exposes an abort.
    /// </param>
    /// <returns>A task that completes when the SDK is up.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// A credential is blank, or <see cref="MarketingCloudOptions.ServerUrl"/> is not an
    /// absolute URL.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="MarketingCloudOptions.InitializationTimeout"/> is zero or negative.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Called a second time on the same client. Initialization is one-shot; see
    /// <see cref="MarketingCloudClient.InitializeAsync"/> for why the guard does not reset.
    /// </exception>
    /// <exception cref="TimeoutException">
    /// The native SDK reported nothing within the timeout - and, on iOS, its state does not
    /// report the push module either.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> fired.</exception>
    /// <exception cref="PlatformNotSupportedException">Neutral target framework.</exception>
    Task InitializeAsync(MarketingCloudOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// The identity plane: contact key (profile id) and profile attributes, delegated to the
    /// SFMCSDK.Net core façade - v11 moved identity writes to the SFMC SDK core on both
    /// platforms, and this façade does not reimplement what the core one already carries.
    /// </summary>
    /// <remarks>
    /// Usable before or after <see cref="InitializeAsync"/> - both SDKs queue early identity
    /// work - and always fire-and-forget; see <see cref="ISfmcIdentity"/>. One platform nuance
    /// is handled underneath: on iOS, <see cref="ISfmcIdentity.SetProfileId"/> ALSO pushes the
    /// MobilePush category's <c>sfmc_setContactKey:</c>, so the module's own registration record
    /// and the core identity agree - see <c>Platforms/Apple</c> for why the double write is
    /// deliberate.
    /// </remarks>
    ISfmcIdentity Identity { get; }

    /// <summary>
    /// The push registration: what this device install looks like to Marketing Cloud (reads),
    /// and its tags and attributes (edits).
    /// </summary>
    IMarketingCloudRegistration Registration { get; }

    /// <summary>
    /// What the native SDK says about itself right now, for logs and bug reports - never parse
    /// it. The shape is platform-owned and asymmetric by nature: iOS returns the MobilePush
    /// module's state JSON (falling back to the SFMC SDK core's); Android's rich state lives on
    /// the instance <c>requestSdk</c> delivers asynchronously, so a synchronous property reports
    /// the instance state JSON once the SDK is ready and the coarse lifecycle phase
    /// (<c>INITIALIZING</c>/<c>NONE</c>) before that.
    /// </summary>
    /// <exception cref="PlatformNotSupportedException">Neutral target framework.</exception>
    string DiagnosticState { get; }
}
