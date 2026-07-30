using System.Runtime.ExceptionServices;
using Com.Salesforce.Marketingcloud;
using Com.Salesforce.Marketingcloud.Sfmcsdk;
using Com.Salesforce.Marketingcloud.Sfmcsdk.Components.Logging;
using SFMCSDK.Net;

// The binding assembly is MarketingCloudSDK.Net.Android, and inside namespace
// MarketingCloudSDK.Net an unqualified `Android.*` risks resolving against a sibling namespace
// before the global one - so the OS namespaces below are reached through global:: throughout,
// the same wart (and the same fix) the SFMCSDK.Net umbrella's Android leg carries.
using CoreLogLevel = Com.Salesforce.Marketingcloud.Sfmcsdk.Components.Logging.LogLevel;

namespace MarketingCloudSDK.Net;

/// <summary>
/// The Android half of <see cref="MarketingCloudClient"/>, over the MarketingCloudSDK.Net.Android
/// binding (namespace <c>Com.Salesforce.Marketingcloud</c>) with the SFMC SDK core binding
/// (namespace <c>Com.Salesforce.Marketingcloud.Sfmcsdk</c>) underneath it.
/// </summary>
public sealed partial class MarketingCloudClient
{
    /// <remarks>
    /// <para>
    /// Configuration goes through the SFMC SDK core, not the MobilePush module: at v11
    /// <c>MarketingCloudConfig</c> IS an <c>EngagementModuleConfig</c>, and
    /// <c>SFMCSdk.configure</c> is what supplies the module the <c>SFMCSdkComponents</c> it
    /// initializes with. <c>MarketingCloudSdk.init</c> - the pre-Unified-SDK entry point this
    /// façade used before - still exists and still calls its listener back, but it initializes
    /// nothing: it hands the engagement module a null components bag and dies inside
    /// <c>getEncryptionManager</c>, leaving <c>requestSdk</c> to never fire. That failure mode is
    /// silent through a listener-shaped wait, which is exactly why it took an emulator run to
    /// catch; the sibling MarketingCloudSDK.Net.Android repository's own device tests and sample
    /// configure the same way this does (see its 11.0.1.2 release notes).
    /// </para>
    /// <para>
    /// Bringing the core up here is also what makes the composed core identity work without this
    /// façade ever calling the core façade's own <c>InitializeAsync</c> - that would configure the
    /// core a second time with an empty module set, which upstream forbids.
    /// </para>
    /// <para>
    /// <c>Application.Context</c> rather than a caller-supplied one, deliberately: the SDK
    /// outlives any Activity, holding a shorter-lived Context would leak it, and it is what keeps
    /// the cross-platform surface free of a parameter only one platform could use.
    /// </para>
    /// </remarks>
    private async partial Task InitializeCore(MarketingCloudOptions options, CancellationToken cancellationToken)
    {
        if (options.LogLevel is { } level)
        {
            ApplyLogLevel(level);
        }

        var config = MarketingCloudConfig.InvokeBuilder()
            .SetApplicationId(options.ApplicationId)
            .SetAccessToken(options.AccessToken)
            .SetMarketingCloudServerUrl(options.ServerUrl)
            .SetMid(options.Mid)
            .SetInboxEnabled(options.InboxEnabled)
            .SetAnalyticsEnabled(options.AnalyticsEnabled)
            .SetDelayRegistrationUntilContactKeyIsSet(options.DelayRegistrationUntilContactKeySet)
            .Build(global::Android.App.Application.Context);

        var modules = new SFMCSdkModuleConfig.Builder
        {
            EngagementModuleConfig = config,
        }.Build();

        // The callback fires once the core has run the module through initialization. Its argument
        // is deliberately discarded unnamed: at v11 upstream deprecated every detail accessor on
        // the status (isUsable, status(), unrecoverableException() - all [Obsolete] in the
        // binding, so touching them is CS0618 under TreatWarningsAsErrors), and config mistakes
        // throw synchronously from the builder above instead. What "initialized" means here is
        // therefore "the SDK concluded its own startup", not "the tenant accepted us" - a
        // server-side rejection is not an init failure, as dummy-credential emulator runs in the
        // sibling repository confirm.
        await AwaitNativeCompletion(
            complete => SFMCSdk.Configure(
                global::Android.App.Application.Context, modules, _ => complete()),
            "initialization", options.InitializationTimeout, cancellationToken).ConfigureAwait(false);
    }

    private static partial bool SupportedCore() => true;

    /// <summary>
    /// Both logging planes, because Android still has two at v11: the MobilePush module logs
    /// through <c>MCLogListener</c> (attached here to its own logcat outputter), and the SFMC
    /// SDK core underneath logs through <c>SFMCSdk.setLogging</c>. iOS unified these; setting
    /// only one here would capture half the story the option promises.
    /// </summary>
    private static void ApplyLogLevel(SfmcLogLevel level)
    {
        if (level == SfmcLogLevel.None)
        {
            // No listener attached means no MobilePush output - null clears one a previous run
            // of this process may have attached (there is none in this client's own lifecycle,
            // but the native surface is process-global and cheap to be explicit with).
            MarketingCloudSdk.SetLogListener(null!);
        }
        else
        {
            MarketingCloudSdk.LogLevel = ToNativeMobilePush(level);
            MarketingCloudSdk.SetLogListener(new IMCLogListener.AndroidLogListener());
        }

        SFMCSdk.SetLogging(ToNativeCore(level), new ILogListener.AndroidLogger());
    }

    /// <remarks>
    /// The reads answer synchronously from the SDK instance, which only exists once the SDK is
    /// operational - before that (and before <see cref="InitializeAsync"/>) they return null, as
    /// <see cref="IMarketingCloudRegistration"/> documents. <c>IsReady</c> is the binding's own
    /// static gate for exactly this.
    /// </remarks>
    private partial string? ContactKeyCore() =>
        MarketingCloudSdk.IsReady ? MarketingCloudSdk.Instance.RegistrationManager.ContactKey : null;

    private partial string? DeviceIdCore() =>
        MarketingCloudSdk.IsReady ? MarketingCloudSdk.Instance.RegistrationManager.DeviceId : null;

    /// <remarks>The FCM registration token - <c>systemToken</c> in the native record.</remarks>
    private partial string? PushTokenCore() =>
        MarketingCloudSdk.IsReady ? MarketingCloudSdk.Instance.RegistrationManager.SystemToken : null;

    /// <remarks>
    /// <para>
    /// The recorded edits replay onto one <c>RegistrationManager.Editor</c> and commit together,
    /// on the instance <c>requestSdk</c> delivers - the binding's Additions supply the
    /// <see cref="Action{T}"/> overload. <c>requestSdk</c> queues the callback until the SDK is
    /// operational, which is what makes an early EditAsync legal (it waits, exactly as the raw
    /// queue does) and why the task is asynchronous on this platform.
    /// </para>
    /// <para>
    /// SetAttribute is the one member with no editor counterpart: the v11 editor carries only
    /// tag and signed-string members (verified against the generated binding - there is no
    /// setAttribute and no setContactKey at this generation), because upstream moved profile
    /// writes to the SFMC SDK core identity. So the attribute edit routes through the same core
    /// identity write <see cref="SFMCSDK.Net.ISfmcIdentity.SetAttribute"/> performs - the real
    /// v11 member for the job, and the same store the iOS category shim updates - rather than
    /// throwing away half the editor on one platform or silently dropping the call.
    /// </para>
    /// <para>
    /// <c>commit()</c> returns a bool the façade deliberately ignores, matching the
    /// fire-and-forget stance <see cref="IMarketingCloudRegistration.EditAsync"/> documents: it
    /// reports "accepted into the local registration", not server truth.
    /// </para>
    /// <para>
    /// The wait is bounded. <c>requestSdk</c> queues indefinitely, so an edit issued against an
    /// SDK that never becomes operational - a failed initialization, or none attempted - would
    /// otherwise leave the returned task pending for the lifetime of the process, which reads to a
    /// caller as a hung <c>await</c> with nothing logged. Expiry surfaces as
    /// <see cref="TimeoutException"/> like initialization's does.
    /// </para>
    /// </remarks>
    private async partial Task ApplyEditsCore(IReadOnlyList<RegistrationEdit> edits)
    {
        // Captured rather than thrown from inside the callback: the completion has to be signalled
        // either way (an exception escaping the JNI callback would vanish into the binder thread),
        // and rethrowing here with the original stack intact is what makes a JNI failure legible.
        Exception? failure = null;

        await AwaitNativeCompletion(
            complete => MarketingCloudSdk.RequestSdk(sdk =>
            {
                try
                {
                    var editor = sdk.RegistrationManager.Edit();

                    foreach (var edit in edits)
                    {
                        switch (edit.Kind)
                        {
                            case RegistrationEditKind.AddTag:
                                editor.AddTag(edit.Key);
                                break;

                            case RegistrationEditKind.RemoveTag:
                                editor.RemoveTag(edit.Key);
                                break;

                            case RegistrationEditKind.SetAttribute:
                                // See the remarks: no editor member at v11, so the core identity
                                // is the real write path. The recorder already validated the value.
                                _core.Identity.SetAttribute(edit.Key, edit.Value!);
                                break;
                        }
                    }

                    editor.Commit();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
                finally
                {
                    complete();
                }
            }),
            "the registration edit", _nativeCallbackTimeout, CancellationToken.None).ConfigureAwait(false);

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    /// <remarks>
    /// Once the SDK is ready, its own state JSON (<c>sdkState</c>) - the richest thing a
    /// synchronous property can reach, since the deeper detail lives behind async delivery.
    /// Before that, the coarse lifecycle phase, so a log line taken mid-startup still says
    /// something true.
    /// </remarks>
    private partial string DiagnosticStateCore()
    {
        if (MarketingCloudSdk.IsReady)
        {
            return MarketingCloudSdk.Instance.SdkState?.ToString() ?? "READY";
        }

        return MarketingCloudSdk.IsInitializing ? "INITIALIZING" : "NONE";
    }

    /// <remarks>
    /// Android and Neutral hand the core identity straight through - contact key and attribute
    /// writes ARE core identity writes at v11 on this platform, with nothing module-side left to
    /// mirror them into. Compare Platforms/Apple, where the module still carries its own
    /// contact-key shim and the façade writes both.
    /// </remarks>
    private partial ISfmcIdentity CreateIdentityFacade(ISfmcIdentity coreIdentity) => coreIdentity;

    /// <summary>
    /// MobilePush log levels are <c>android.util.Log</c> integers surfaced as constants on the
    /// listener interface. <see cref="SfmcLogLevel.None"/> never reaches this - it is handled by
    /// clearing the listener - so mapping it to a level would be dead code wearing a meaning.
    /// </summary>
    private static int ToNativeMobilePush(SfmcLogLevel level) => level switch
    {
        SfmcLogLevel.Debug => IMCLogListener.Debug,
        SfmcLogLevel.Warning => IMCLogListener.Warn,
        SfmcLogLevel.Error => IMCLogListener.Error,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown SfmcLogLevel."),
    };

    private static CoreLogLevel ToNativeCore(SfmcLogLevel level) => level switch
    {
        SfmcLogLevel.Debug => CoreLogLevel.Debug!,
        SfmcLogLevel.Warning => CoreLogLevel.Warn!,
        SfmcLogLevel.Error => CoreLogLevel.Error!,
        SfmcLogLevel.None => CoreLogLevel.None!,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown SfmcLogLevel."),
    };
}
