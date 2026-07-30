using SFMCSDK.Net;

namespace MarketingCloudSDK.Net;

/// <summary>
/// An <see cref="IMarketingCloudClient"/> that does nothing, successfully, on every target
/// framework. For the heads where MobilePush does not exist (a MAUI app's Windows or Mac Catalyst
/// head) and for tests that want the façade out of the way.
/// </summary>
/// <remarks>
/// <para>
/// Shipped rather than left as an exercise: the alternative is every consumer hand-writing the
/// same handful of no-op members, and getting them wrong in the same way - a fake that throws from
/// <see cref="DiagnosticState"/>, or one that reports <see cref="IsSupported"/> true and then does
/// nothing.
/// </para>
/// <para>
/// It is not a spy: nothing is recorded and nothing is asserted. A test that needs to see what the
/// app asked for wants its own mock (the interface is small on purpose); this type is for the case
/// where the answer is "don't care, don't crash". Registering it is a deliberate decision to send
/// no data to Marketing Cloud on that head - <see cref="MarketingCloudClient"/> throws on neutral
/// target frameworks precisely so that decision cannot be made by accident.
/// </para>
/// <example>
/// The shape this exists for, in a MAUI app whose Windows head has no SDK:
/// <code>
/// builder.Services.AddSingleton&lt;IMarketingCloudClient&gt;(
///     new MarketingCloudClient() is { IsSupported: true } client
///         ? client
///         : new NullMarketingCloudClient());
/// </code>
/// </example>
/// </remarks>
public sealed class NullMarketingCloudClient : IMarketingCloudClient
{
    /// <inheritdoc />
    public ISfmcIdentity Identity { get; } = new NullSfmcIdentity();

    /// <inheritdoc />
    public IMarketingCloudRegistration Registration { get; } = new NullRegistration();

    /// <summary>
    /// A fixed line naming this type, so a diagnostics screen or bug report says plainly that no
    /// SDK was driven rather than looking like an SDK with nothing to say.
    /// </summary>
    public string DiagnosticState => "MarketingCloudSDK.Net no-op client: no native SDK was driven.";

    /// <summary>Always false - there is no native SDK behind this client.</summary>
    public bool IsSupported => false;

    /// <summary>
    /// True once <see cref="InitializeAsync"/> has been called. The no-op initialization succeeds,
    /// so this reports the client's own honest state rather than pretending an SDK came up.
    /// </summary>
    public bool IsInitialized { get; private set; }

    /// <summary>
    /// Validates nothing, initializes nothing, and completes successfully - so app startup that
    /// awaits initialization proceeds identically on a head with no SDK.
    /// </summary>
    /// <remarks>
    /// Not one-shot: the guard exists on the real client to protect a native SDK that cannot be
    /// configured twice, and there is nothing here to protect. Cancellation is honoured, because a
    /// caller that cancels startup should see that regardless of which client it holds.
    /// </remarks>
    public Task InitializeAsync(MarketingCloudOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsInitialized = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void TrackCustomEvent(string name, IReadOnlyDictionary<string, string>? attributes = null)
    {
    }

    private sealed class NullSfmcIdentity : ISfmcIdentity
    {
        public void SetProfileId(string profileId)
        {
        }

        public void SetAttribute(string key, string value)
        {
        }

        public void ClearAttribute(string key)
        {
        }
    }

    private sealed class NullRegistration : IMarketingCloudRegistration
    {
        /// <summary>
        /// Null, which is also what the real client answers before the SDK is operational - so
        /// consumer code that handles the real null path needs no second shape for this one.
        /// </summary>
        public string? ContactKey => null;

        public string? DeviceId => null;

        public string? PushToken => null;

        /// <summary>
        /// Runs the caller's edit delegate against a recorder that discards, so argument
        /// validation still fires - a blank tag is a bug on every head, and a fake that accepted
        /// it would hide the bug exactly where it is cheapest to find.
        /// </summary>
        public Task EditAsync(Action<IRegistrationEditor> edit)
        {
            ArgumentNullException.ThrowIfNull(edit);
            edit(new NullEditor());
            return Task.CompletedTask;
        }
    }

    private sealed class NullEditor : IRegistrationEditor
    {
        public void AddTag(string tag) => ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        public void RemoveTag(string tag) => ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        public void SetAttribute(string key, string value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentNullException.ThrowIfNull(value);
        }
    }
}
