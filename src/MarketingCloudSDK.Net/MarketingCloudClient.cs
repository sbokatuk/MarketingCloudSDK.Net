using SFMCSDK.Net;

namespace MarketingCloudSDK.Net;

/// <summary>
/// Cross-platform <see cref="IMarketingCloudClient"/>. The platform halves live in
/// Platforms/Android and Platforms/Apple (Platforms/Neutral on the plain target frameworks);
/// everything shared - options validation, the one-shot initialization guard, the registration
/// edit recorder, and turning the native callbacks into awaitable calls - lives here.
/// </summary>
/// <remarks>
/// <para>
/// <b>Create one and share it.</b> The native SDK on both platforms is a process-wide singleton
/// behind static entry points, so a second client would drive exactly the same native state as
/// the first. The initialization guard is per-instance - it is the instance that promises
/// one-shot semantics - which is why the client is meant to live as a singleton (register it as
/// one in DI; the MarketingCloudSDK.Net.Maui package does exactly that). Two instances would let
/// two <see cref="InitializeAsync"/> calls through to one native SDK, and upstream forbids
/// configuring it twice.
/// </para>
/// <para>
/// <b>Initialization is one-shot, even on failure.</b> The guard is deliberately not released
/// when initialization fails or times out: neither platform's configure is retryable, and on a
/// timeout the first attempt's native initialization is likely still running - a retry would
/// race it. A process that needs a fresh attempt restarts; that is the native SDKs' own model.
/// </para>
/// <para>
/// <b>Identity is composed, not reimplemented.</b> v11 moved contact key and attribute writes to
/// the SFMC SDK core on both platforms, so this client owns an (uninitialized) core
/// <see cref="SfmcSdkClient"/> purely for its <see cref="SfmcSdkClient.Identity"/> - legal
/// because the core identity operations are static-level and queue before initialization, and
/// necessary because calling the core façade's own <c>InitializeAsync</c> here would configure
/// the core a second time with an empty module set, which upstream forbids. The MobilePush
/// initialization this client performs brings the core up underneath.
/// </para>
/// </remarks>
public sealed partial class MarketingCloudClient : IMarketingCloudClient
{
    /// <summary>
    /// The core façade, for <see cref="SfmcSdkClient.Identity"/> only - never initialized here;
    /// see the class remarks.
    /// </summary>
    private readonly SfmcSdkClient _core = new();

    /// <summary>0 until <see cref="InitializeAsync"/> claims it; the claim is never returned.</summary>
    private int _initializationClaimed;

    /// <inheritdoc />
    public ISfmcIdentity Identity { get; }

    /// <inheritdoc />
    public IMarketingCloudRegistration Registration { get; }

    /// <inheritdoc />
    public string DiagnosticState => DiagnosticStateCore();

    /// <summary>
    /// Creates the client. Nothing native happens here - construction is valid on every target
    /// framework, including the neutral ones, so shared code and tests can build the object
    /// graph anywhere and only the members that reach the native SDK are platform-bound.
    /// </summary>
    public MarketingCloudClient()
    {
        Identity = CreateIdentityFacade(_core.Identity);
        Registration = new RegistrationFacade(this);
    }

    /// <inheritdoc />
    public Task InitializeAsync(MarketingCloudOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        RequireCredential(options.ApplicationId, nameof(MarketingCloudOptions.ApplicationId));
        RequireCredential(options.AccessToken, nameof(MarketingCloudOptions.AccessToken));
        RequireCredential(options.ServerUrl, nameof(MarketingCloudOptions.ServerUrl));
        RequireCredential(options.Mid, nameof(MarketingCloudOptions.Mid));

        // Once, here, rather than per platform: Android takes the URL as a string and iOS as an
        // NSURL, and without this check a malformed value would fail as a server error on one
        // platform and a nil-URL crash on the other.
        if (!Uri.TryCreate(options.ServerUrl, UriKind.Absolute, out _))
        {
            throw new ArgumentException(
                $"MarketingCloudOptions.ServerUrl '{options.ServerUrl}' is not an absolute URL. " +
                "Use the tenant-specific endpoint from MobilePush app administration, e.g. " +
                "'https://mc.xxxx.device.marketingcloudapis.com/'.",
                nameof(options));
        }

        if (options.InitializationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.InitializationTimeout,
                "MarketingCloudOptions.InitializationTimeout must be positive.");
        }

        // Validation above, claim below: a call refused for a bad argument has not consumed the
        // one initialization this client performs.
        if (Interlocked.CompareExchange(ref _initializationClaimed, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                "InitializeAsync has already been called on this MarketingCloudClient. " +
                "Initialization is one-shot: the native MobilePush SDK is a process-wide " +
                "singleton whose configure cannot be repeated, so the guard stays claimed even " +
                "if the first call failed. Await the first call instead of issuing another.");
        }

        return InitializeCore(options, cancellationToken);
    }

    /// <summary>
    /// Refuses a blank credential, naming the offending property - `required` guarantees the
    /// property was set at construction, but nothing stops a null-forgiven or whitespace value,
    /// and letting one through would fail as a server-side rejection minutes later with no clue
    /// which of the four values was wrong.
    /// </summary>
    private static void RequireCredential(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                $"MarketingCloudOptions.{name} must not be null, empty or whitespace. The four " +
                "credentials come from MobilePush app administration in Marketing Cloud Setup.",
                "options");
        }
    }

    // The platform seams. One implementation directory supplies the bodies per target framework -
    // Platforms/Android, Platforms/Apple, or Platforms/Neutral (which throws from every seam) -
    // selected in the csproj. A target framework matching none of them fails to compile, which is
    // the intended outcome: an assembly whose API silently did nothing would be worse.

    private partial Task InitializeCore(MarketingCloudOptions options, CancellationToken cancellationToken);

    /// <summary>
    /// Wraps (or passes through) the core identity per platform: Android and Neutral hand the
    /// core's <see cref="ISfmcIdentity"/> straight out, while iOS decorates
    /// <see cref="ISfmcIdentity.SetProfileId"/> to also push the MobilePush category's
    /// <c>sfmc_setContactKey:</c> - see Platforms/Apple for why both planes are written.
    /// </summary>
    private partial ISfmcIdentity CreateIdentityFacade(ISfmcIdentity coreIdentity);

    private partial string? ContactKeyCore();

    private partial string? DeviceIdCore();

    private partial string? PushTokenCore();

    private partial Task ApplyEditsCore(IReadOnlyList<RegistrationEdit> edits);

    private partial string DiagnosticStateCore();

    /// <summary>
    /// Runs one native start call and awaits the completion it reports, bounded by the configured
    /// timeout and the caller's token. Shared so the mechanics are written (and tested) once;
    /// both platform initializations run through it - Android straightforwardly, iOS with a
    /// catch around the timeout because there an expiry is not necessarily a failure (see
    /// Platforms/Apple).
    /// </summary>
    /// <param name="start">Starts the native call; the argument is invoked once on completion.</param>
    /// <param name="operation">Human-readable name for the timeout message.</param>
    /// <param name="timeout">How long to wait before failing with <see cref="TimeoutException"/>.</param>
    /// <param name="cancellationToken">The caller's token - cancellation wins over the timeout.</param>
    private static async Task AwaitNativeCompletion(
        Action<Action> start, string operation, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var expiry = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, expiry.Token);

        await using var registration = linked.Token.Register(() =>
        {
            // Distinguishes the two reasons the linked token can fire: the caller's cancellation
            // surfaces as OperationCanceledException carrying their token, an expired timeout as
            // TimeoutException naming the operation - so a hung callback is tellable from an
            // impatient caller in a crash report.
            if (cancellationToken.IsCancellationRequested)
            {
                pending.TrySetCanceled(cancellationToken);
            }
            else
            {
                pending.TrySetException(new TimeoutException(
                    $"The native MobilePush SDK did not report {operation} within " +
                    $"{timeout.TotalSeconds:0}s. The wait was abandoned, not the SDK - its own " +
                    "initialization may still complete in the background."));
            }
        }).ConfigureAwait(false);

        start(() => pending.TrySetResult(true));

        await pending.Task.ConfigureAwait(false);
    }

    /// <summary>One recorded registration edit, replayed by the platform half in order.</summary>
    internal readonly record struct RegistrationEdit(RegistrationEditKind Kind, string Key, string? Value);

    /// <summary>Which editor member produced a <see cref="RegistrationEdit"/>.</summary>
    internal enum RegistrationEditKind
    {
        /// <summary><see cref="IRegistrationEditor.AddTag"/>.</summary>
        AddTag,

        /// <summary><see cref="IRegistrationEditor.RemoveTag"/>.</summary>
        RemoveTag,

        /// <summary><see cref="IRegistrationEditor.SetAttribute"/>.</summary>
        SetAttribute,
    }

    /// <summary>
    /// The <see cref="IMarketingCloudRegistration"/> handed out by <see cref="Registration"/>:
    /// shared validation and recording, then the owning client's platform seams. A nested type
    /// rather than the client implementing the interface itself, so
    /// <c>client.EditAsync(...)</c> is not an API - the registration operations read as what
    /// they are, work on one sub-object.
    /// </summary>
    private sealed class RegistrationFacade(MarketingCloudClient owner) : IMarketingCloudRegistration
    {
        public string? ContactKey => owner.ContactKeyCore();

        public string? DeviceId => owner.DeviceIdCore();

        public string? PushToken => owner.PushTokenCore();

        public Task EditAsync(Action<IRegistrationEditor> edit)
        {
            ArgumentNullException.ThrowIfNull(edit);

            // The delegate runs HERE, synchronously, against a recorder - not against a native
            // editor on some callback thread. That keeps user code on the calling thread (no
            // surprise re-entrancy from a JNI callback), lets validation throw synchronously
            // with the caller's stack, and hands the platforms a plain list to replay.
            var recorder = new RecordingEditor();
            edit(recorder);

            // An empty edit still crosses to the platform seam deliberately: on the neutral
            // build that is what makes EditAsync throw its documented
            // PlatformNotSupportedException instead of quietly succeeding, and on the platforms
            // an empty replay is harmless.
            return owner.ApplyEditsCore(recorder.Edits);
        }
    }

    /// <summary>
    /// Records editor calls, validating as it goes so bad arguments fail on the caller's stack
    /// before anything is queued toward a platform.
    /// </summary>
    private sealed class RecordingEditor : IRegistrationEditor
    {
        private readonly List<RegistrationEdit> _edits = [];

        public IReadOnlyList<RegistrationEdit> Edits => _edits;

        public void AddTag(string tag)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tag);
            _edits.Add(new RegistrationEdit(RegistrationEditKind.AddTag, tag, null));
        }

        public void RemoveTag(string tag)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tag);
            _edits.Add(new RegistrationEdit(RegistrationEditKind.RemoveTag, tag, null));
        }

        public void SetAttribute(string key, string value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentNullException.ThrowIfNull(value);
            _edits.Add(new RegistrationEdit(RegistrationEditKind.SetAttribute, key, value));
        }
    }
}
