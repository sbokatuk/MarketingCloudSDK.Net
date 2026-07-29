using Foundation;

namespace MarketingCloudSDK.Net.Sample;

/// <remarks>
/// Nothing push-specific here, and that absence is the demonstration: v11 observes the APNs
/// token itself, so unlike the 8.x generation there is no token forwarding to write. A real app
/// still carries the APNs entitlement, asks UNUserNotificationCenter for authorization and calls
/// RegisterForRemoteNotifications - OS-side setup this unsigned sample cannot demonstrate; see
/// the README's push prerequisites.
/// </remarks>
[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
