using MarketingCloudSDK.Net;
using SFMCSDK.Net;

namespace MarketingCloudSDK.Net.DeviceTests;

/// <summary>One check: a name and something that throws when the façade misbehaves.</summary>
public sealed record SmokeTest(string Name, Func<Task> Execute);

/// <summary>
/// Drives the FAÇADE - not the raw bindings - over the packed MarketingCloudSDK.Net package on a
/// real platform runtime. Dummy credentials: the configuration below is shaped like real values
/// but points at no tenant, so nothing registers anywhere. What is being proven is the packaging
/// promise: the dependency groups pulled the right platform binding (and the SFMCSDK.Net core)
/// in, and the façade's shared surface drives it - initialization completes per each platform's
/// verified semantics, registration reads answer, an edit commits, identity crosses into native
/// code, and the one-shot guard holds on-device exactly as the unit tests hold it on the
/// neutral leg.
/// </summary>
/// <remarks>
/// The checks share one client and run in array order, deliberately: initialize-then-use-then-
/// reinitialize is the lifecycle under test, not six independent facts. The raw-binding smoke
/// tests live in the two platform repositories; repeating them here would test the same native
/// code twice and this package's code not at all.
/// </remarks>
public static class SmokeTests
{
    /// <summary>Where progress lines go; each platform host points this at its log sink.</summary>
    public static Action<string> Reporter { get; set; } = _ => { };

    private static readonly MarketingCloudClient Client = new();

    public static readonly SmokeTest[] All =
    [
        new("initialize_completes", async () =>
        {
            // Dummy values shaped like real ones; the SDK accepts them and fails server-side.
            // Debug logging on, so a native failure under investigation is already captured in
            // the platform log the runner uploads.
            //
            // "Shaped like real ones" is load-bearing on Android, and more narrowly than it looks:
            // MarketingCloudConfig.Builder.build() runs java.util.UUID validation on the
            // application id and rejects anything that is not a version-4 variant-1 UUID, so the
            // nil UUID (all zeros) fails with "The applicationId is not a valid UUID" from inside
            // the builder - before the SDK is reached, before any callback, and therefore before
            // the timeout below can mean anything. Hence the 4 and the 8 in the third and fourth
            // groups. The access token is padded to the 24 characters a real one carries for the
            // same reason. iOS validates neither, which is why this only ever failed on one
            // platform; the sibling MarketingCloudSDK.Net.Android suite learned it first.
            //
            // The timeout is doing platform-specific work, per the façade's documented
            // semantics. On Android the init listener fires inside it - the binding
            // repository's emulator suite sees it fire with these same dummy values under a
            // 90-second allowance, and a cold CI emulator is exactly where a tighter figure
            // would flake. On iOS the completion block never concludes against an unprovisioned
            // tenant - verified upstream behaviour - so InitializeAsync spends the WHOLE
            // timeout waiting, then reads the state JSON and completes when it reports the
            // pushfeature module; the run is slower there by design, and still far inside the
            // runner scripts' verdict budget (450s of logcat polling, an open pty stream).
            await Client.InitializeAsync(new MarketingCloudOptions
            {
                ApplicationId = "00000000-0000-4000-8000-000000000000",
                AccessToken = "devicetests-dummy-token0",
                ServerUrl = "https://localhost.invalid/",
                Mid = "000000000",
                LogLevel = SfmcLogLevel.Debug,
                InitializationTimeout = TimeSpan.FromSeconds(90),
            });

            Reporter("InitializeAsync completed");
        }),

        new("diagnostic_state_reports", () =>
        {
            var state = Client.DiagnosticState;
            Reporter($"DiagnosticState: {state}");

            if (string.IsNullOrWhiteSpace(state))
            {
                throw new InvalidOperationException("DiagnosticState returned nothing.");
            }

            return Task.CompletedTask;
        }),

        new("registration_reads_answer_without_throwing", () =>
        {
            // Nulls are legal answers everywhere here - there is no tenant, so nothing may have
            // registered - but the reads themselves must cross into native code and come back.
            // PushToken in particular is CONTRACTUALLY null on iOS (no APNs-token read at v11)
            // and null on an emulator with no Firebase config on Android.
            Reporter($"ContactKey: {Client.Registration.ContactKey ?? "(null)"}");
            Reporter($"DeviceId: {Client.Registration.DeviceId ?? "(null)"}");
            Reporter($"PushToken: {Client.Registration.PushToken ?? "(null)"}");

            return Task.CompletedTask;
        }),

        new("edit_async_add_tag_commits", async () =>
        {
            // Android: replays onto a RegistrationManager.Editor and commits, on the instance
            // requestSdk delivers - initialization has completed, so the callback fires rather
            // than queueing forever. iOS: the sfmc_addTag: selector. Fire-and-forget by
            // contract - no server round-trip is asserted, there is no tenant - only that the
            // edit crosses into native code without throwing, which is what a broken binding
            // dependency breaks.
            await Client.Registration.EditAsync(editor => editor.AddTag("mcnet-e2e"));

            Reporter("EditAsync committed");
        }),

        new("identity_set_profile_id_does_not_throw", () =>
        {
            // The composed core identity (v11 moved contact-key writes there), plus - on iOS -
            // the module's sfmc_setContactKey shim underneath the same call. Both queue happily
            // with no tenant.
            Client.Identity.SetProfileId("mcnet-device-test");

            return Task.CompletedTask;
        }),

        new("second_initialize_throws_the_documented_guard", async () =>
        {
            try
            {
                await Client.InitializeAsync(new MarketingCloudOptions
                {
                    ApplicationId = "00000000-0000-4000-8000-000000000000",
                    AccessToken = "devicetests-dummy-token0",
                    ServerUrl = "https://localhost.invalid/",
                    Mid = "000000000",
                });
            }
            catch (InvalidOperationException error)
            {
                Reporter($"guard message: {error.Message}");
                return;
            }

            throw new InvalidOperationException(
                "A second InitializeAsync completed instead of throwing the one-shot guard.");
        }),
    ];
}
