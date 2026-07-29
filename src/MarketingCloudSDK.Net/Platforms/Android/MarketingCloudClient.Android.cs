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
    /// <c>MarketingCloudSdk.init</c> takes the application <c>Context</c> and reports the
    /// terminal <c>InitializationStatus</c> through a listener; the binding's Additions supply
    /// the <see cref="Action{T}"/> overload used here, and v11's compat entry point brings the
    /// SFMC SDK core up underneath - which is what makes the composed core identity work without
    /// this façade ever configuring the core itself. <c>Application.Context</c> rather than a
    /// caller-supplied one, deliberately: the SDK outlives any Activity, holding a shorter-lived
    /// Context would leak it, and it is what keeps the cross-platform surface free of a
    /// parameter only one platform could use.
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

        // The listener fires exactly once with the terminal InitializationStatus. Its parameter
        // is deliberately discarded unnamed: at v11 upstream deprecated every detail accessor on
        // the status (isUsable, status(), unrecoverableException() - all [Obsolete] in the
        // binding, so touching them is CS0618 under TreatWarningsAsErrors), because the compat
        // init pathway always concludes and config mistakes throw synchronously from the builder
        // instead. Dummy-credential runs on the sibling repository's emulator suite confirm the
        // listener fires and server-side failure is not an init failure.
        await AwaitNativeCompletion(
            complete => MarketingCloudSdk.Init(
                global::Android.App.Application.Context, config, _ => complete()),
            "initialization", options.InitializationTimeout, cancellationToken).ConfigureAwait(false);
    }

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
    /// </remarks>
    private partial Task ApplyEditsCore(IReadOnlyList<RegistrationEdit> edits)
    {
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        MarketingCloudSdk.RequestSdk(sdk =>
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
                            // See the remarks: no editor member at v11, so the core identity is
                            // the real write path. The recorder already validated the value.
                            _core.Identity.SetAttribute(edit.Key, edit.Value!);
                            break;
                    }
                }

                editor.Commit();
                applied.TrySetResult();
            }
            catch (Exception exception)
            {
                // A JNI failure inside the callback would otherwise vanish into the binder
                // thread; carrying it onto the awaited task is the whole reason EditAsync is a
                // Task on this platform.
                applied.TrySetException(exception);
            }
        });

        return applied.Task;
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
