using Android.App;
using Android.OS;
using Android.Util;

namespace MarketingCloudSDK.Net.DeviceTests;

/// <summary>
/// Host for the on-emulator checks. Runs every one on create and reports the outcome to logcat
/// under a single tag, which the runner script turns into an exit code.
/// </summary>
/// <remarks>
/// <c>Name</c> is pinned rather than left to the generated <c>crc64…</c> value so that
/// <c>adb shell am start</c> has a stable target across builds; see
/// .github/scripts/run-emulator-tests.sh.
/// </remarks>
[Activity(
    Name = "com.sbokatuk.mcnet.devicetests.MainActivity",
    Label = "MarketingCloudSDK.Net e2e",
    MainLauncher = true)]
public sealed class MainActivity : Activity
{
    /// <summary>The logcat tag the runner script filters on.</summary>
    private const string LogTag = "SfmcNetE2E";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Off the UI thread, matching the sibling binding repository's device tests: the
        // MobilePush SDK does file and network work during initialization, and a StrictMode
        // violation on the main thread would be reported as a failure of whichever check
        // happened to be running.
        _ = Task.Run(() => TestRunner.RunAndReportAsync(
            message => Log.Info(LogTag, message),
            // Not exiting the process: the runner reads the verdict from logcat, and killing
            // the app here would race the log being flushed.
            _ => { }));
    }
}
