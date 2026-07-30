using MarketingCloudSDK.Net;
using SFMCSDK.Net;

namespace MarketingCloudSDK.Net.Sample;

/// <summary>
/// Everything on this page goes through <see cref="IMarketingCloudClient"/> - no platform
/// namespace is imported and no <c>#if</c> appears anywhere in this app, which is the point
/// being demonstrated. Compare with the two binding repositories' samples, which drive the same
/// operations through the raw per-platform surfaces.
/// </summary>
public partial class MainPage : ContentPage
{
    private readonly IMarketingCloudClient _sdk;

    public MainPage(IMarketingCloudClient sdk)
    {
        InitializeComponent();
        _sdk = sdk;
    }

    private async void OnInitializeClicked(object? sender, EventArgs e)
    {
        var applicationId = ApplicationIdEntry.Text?.Trim();
        var accessToken = AccessTokenEntry.Text?.Trim();
        var serverUrl = ServerUrlEntry.Text?.Trim();
        var mid = MidEntry.Text?.Trim();

        if (string.IsNullOrEmpty(applicationId) || string.IsNullOrEmpty(accessToken)
            || string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(mid))
        {
            AppendLog("Fill in all four credentials first (MobilePush app administration).");
            return;
        }

        InitializeButton.IsEnabled = false;
        AppendLog("Initializing…");

        // Android 13+ shows no notification without the runtime POST_NOTIFICATIONS grant, so
        // ask before registering the device. The same call is a granted no-op everywhere else -
        // MAUI's permission model is what keeps this page free of #if - and a denial is worth
        // logging, not failing: the SDK still registers, the notification just will not show.
        var notifications = await Permissions.RequestAsync<Permissions.PostNotifications>();
        AppendLog($"Notification permission: {notifications}.");

        try
        {
            // Debug logging routes the native SDK's own lines to logcat / os_log, so what the
            // SDK does with these calls is observable in the platform log while you poke at the
            // buttons. The timeout's meaning differs per platform by design - see
            // IMarketingCloudClient.InitializeAsync - and both outcomes land on this one await.
            await _sdk.InitializeAsync(new MarketingCloudOptions
            {
                ApplicationId = applicationId,
                AccessToken = accessToken,
                ServerUrl = serverUrl,
                Mid = mid,
                LogLevel = SfmcLogLevel.Debug,
            });

            StatusLabel.Text = "Initialized.";
            AppendLog("InitializeAsync completed.");
            ContactKeyEntry.IsEnabled = true;
            ContactKeyButton.IsEnabled = true;
            TagEntry.IsEnabled = true;
            TagButton.IsEnabled = true;
            EventButton.IsEnabled = true;
            RegistrationButton.IsEnabled = true;
        }
        catch (Exception exception)
        {
            // InvalidOperationException here means the one-shot guard: initialization already
            // ran (or is running) on this client. The button stays disabled either way - a
            // second attempt is exactly what the guard exists to refuse.
            StatusLabel.Text = "Initialization failed.";
            AppendLog($"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private void OnSetContactKeyClicked(object? sender, EventArgs e)
    {
        var contactKey = ContactKeyEntry.Text?.Trim();
        if (string.IsNullOrEmpty(contactKey))
        {
            AppendLog("Enter a contact key first.");
            return;
        }

        // The write side of identity is the SFMC SDK core's (v11 moved it there); the façade
        // hands the core's ISfmcIdentity out and, on iOS, also updates the module's own
        // contact-key shim underneath this one call. Fire-and-forget by contract: the SDK
        // batches identity changes on its own schedule.
        _sdk.Identity.SetProfileId(contactKey);
        AppendLog($"Contact key set to '{contactKey}'.");
    }

    private async void OnAddTagClicked(object? sender, EventArgs e)
    {
        var tag = TagEntry.Text?.Trim();
        if (string.IsNullOrEmpty(tag))
        {
            AppendLog("Enter a tag first.");
            return;
        }

        try
        {
            // One recorded batch, applied with each platform's own delivery - an editor commit
            // on Android, category selectors on iOS. The await is the edits reaching the native
            // SDK, not the server.
            await _sdk.Registration.EditAsync(editor => editor.AddTag(tag));
            AppendLog($"Tag '{tag}' added.");
        }
        catch (ArgumentException exception)
        {
            AppendLog($"Edit rejected: {exception.Message}");
        }
    }

    private void OnTrackEventClicked(object? sender, EventArgs e)
    {
        // Custom events belong to the SFMC SDK core too (same move as identity at v11), and the
        // façade forwards them - so an app driving MobilePush never has to reach for a second
        // client to send one. String attribute values only: that is the shape both platforms agree
        // on. Fire-and-forget, like identity.
        _sdk.TrackCustomEvent("sample_button_tapped", new Dictionary<string, string>
        {
            ["source"] = "MarketingCloudSDK.Net.Sample",
        });

        AppendLog("Custom event 'sample_button_tapped' tracked.");
    }

    private void OnShowRegistrationClicked(object? sender, EventArgs e)
    {
        // Reads answer from the SDK's local registration record; null is a legal answer
        // (nothing set yet, no token yet - and PushToken is documented null on iOS, where v11
        // exposes no APNs-token read).
        RegistrationLabel.Text =
            $"IsSupported: {_sdk.IsSupported}\n" +
            $"IsInitialized: {_sdk.IsInitialized}\n" +
            $"ContactKey: {_sdk.Registration.ContactKey ?? "(null)"}\n" +
            $"DeviceId: {_sdk.Registration.DeviceId ?? "(null)"}\n" +
            $"PushToken: {_sdk.Registration.PushToken ?? "(null)"}\n" +
            $"DiagnosticState: {_sdk.DiagnosticState}";

        AppendLog("Registration refreshed.");
    }

    private void AppendLog(string line)
        => LogLabel.Text = $"{DateTime.Now:HH:mm:ss}  {line}\n{LogLabel.Text}";
}
