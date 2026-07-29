namespace MarketingCloudSDK.Net;

/// <summary>
/// The push registration: reads of what this device install currently looks like to Marketing
/// Cloud, and edits of its tags and attributes.
/// </summary>
/// <remarks>
/// <para>
/// The reads are snapshots of the SDK's local registration record, not server truth: they answer
/// from what the SDK last stored, and return null whenever the underlying platform cannot answer
/// yet - before initialization on both platforms, and on Android additionally until the SDK
/// instance is operational (its registration lives on the instance <c>requestSdk</c> hands out
/// asynchronously, which a synchronous property cannot await).
/// </para>
/// <para>
/// The edits go through <see cref="EditAsync"/> and are fire-and-forget toward the server, like
/// everything else in this SDK family: the returned task completes when the native SDK has
/// accepted the edits into its local registration, and the SDK sends them on its own batching
/// schedule. Contact key and profile attributes are deliberately NOT here - v11 moved identity
/// to the SFMC SDK core, and the façade follows it: see
/// <see cref="IMarketingCloudClient.Identity"/>.
/// </para>
/// </remarks>
public interface IMarketingCloudRegistration
{
    /// <summary>
    /// The contact key the registration currently carries, or null when the SDK cannot answer
    /// yet (not initialized, or nothing set). This is the READ side; writes go through
    /// <see cref="IMarketingCloudClient.Identity"/> - v11 moved them to the core identity.
    /// </summary>
    string? ContactKey { get; }

    /// <summary>
    /// The SDK's device id for this install, or null when the SDK cannot answer yet.
    /// </summary>
    string? DeviceId { get; }

    /// <summary>
    /// The push token the registration carries - Android's FCM registration token
    /// (<c>systemToken</c>) - or null when the SDK cannot answer yet.
    /// </summary>
    /// <remarks>
    /// On iOS this is always null, documented rather than papered over: the v11 MobilePush
    /// category surface exposes no APNs-token read (the 8.x <c>sfmc_deviceToken</c> is gone -
    /// verified against the 11.0.2 headers the binding is written from), because the SDK
    /// observes and stores the token itself. An app that needs the raw APNs token has it in its
    /// own <c>RegisteredForRemoteNotifications</c> override; Marketing Cloud's view of the
    /// device is <see cref="DeviceId"/>.
    /// </remarks>
    string? PushToken { get; }

    /// <summary>
    /// Edits the registration: the delegate runs immediately against a recorder, and the
    /// recorded edits are then applied to the native SDK in one pass.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The batching model is each platform's own. On Android the edits replay onto one
    /// <c>RegistrationManager.Editor</c> and commit together, on the SDK instance
    /// <c>requestSdk</c> delivers - which is also why the task is asynchronous there: before
    /// initialization it waits, exactly as the raw <c>requestSdk</c> queue does. On iOS the
    /// <c>sfmc_*</c> category selectors apply each edit as it replays; there is no
    /// commit step to batch under.
    /// </para>
    /// <para>
    /// The native accept/refuse booleans those calls return are deliberately not surfaced:
    /// they report "accepted into the local registration right now", and on iOS a refusal can
    /// simply mean the module is still initializing - which, against an unprovisioned tenant,
    /// is a state it verifiably never leaves. Fire-and-forget is the one contract both
    /// platforms can honour; see the platform sources for the full reasoning.
    /// </para>
    /// </remarks>
    /// <param name="edit">Receives the editor; every call on it is recorded, then applied.</param>
    /// <returns>A task that completes when the edits have been handed to the native SDK.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="edit"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The delegate passed a blank tag, a blank attribute key, or a null attribute value -
    /// thrown synchronously, before anything reaches the platform.
    /// </exception>
    /// <exception cref="PlatformNotSupportedException">Neutral target framework.</exception>
    Task EditAsync(Action<IRegistrationEditor> edit);
}

/// <summary>
/// What <see cref="IMarketingCloudRegistration.EditAsync"/> hands to its delegate. Calls are
/// recorded in order and applied to the native SDK when the delegate returns.
/// </summary>
/// <remarks>
/// The member set is the intersection both platforms can honour at v11, mapped to each
/// platform's real members - nothing here silently no-ops anywhere. Tags map to
/// <c>Editor.addTag/removeTag</c> on Android and <c>sfmc_addTag:/sfmc_removeTag:</c> on iOS.
/// Attributes map to <c>sfmc_setAttributeNamed:value:</c> on iOS; on Android the v11 editor has
/// no attribute members at all - upstream moved attribute writes to the SFMC SDK core identity -
/// so <see cref="SetAttribute"/> routes through the same core identity write
/// <see cref="SFMCSDK.Net.ISfmcIdentity.SetAttribute"/> performs, which is the one store the
/// platform still updates. See <c>Platforms/Android</c>.
/// </remarks>
public interface IRegistrationEditor
{
    /// <summary>Adds a tag to the registration. Adding a tag it already has is a no-op.</summary>
    /// <param name="tag">The tag. Must not be null, empty or whitespace.</param>
    /// <exception cref="ArgumentException"><paramref name="tag"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="tag"/> is null.</exception>
    void AddTag(string tag);

    /// <summary>Removes a tag from the registration. Removing an absent tag is a no-op.</summary>
    /// <param name="tag">The tag. Must not be null, empty or whitespace.</param>
    /// <exception cref="ArgumentException"><paramref name="tag"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="tag"/> is null.</exception>
    void RemoveTag(string tag);

    /// <summary>
    /// Sets one string profile attribute, replacing any previous value for the key.
    /// </summary>
    /// <param name="key">The attribute name. Must not be null, empty or whitespace.</param>
    /// <param name="value">The attribute value. Must not be null; empty is allowed.</param>
    /// <exception cref="ArgumentException"><paramref name="key"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> or <paramref name="value"/> is null.</exception>
    void SetAttribute(string key, string value);
}
