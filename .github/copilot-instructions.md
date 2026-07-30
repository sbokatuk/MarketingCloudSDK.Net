# MarketingCloudSDK.Net

A cross-platform façade over Salesforce Marketing Cloud MobilePush. It binds nothing and ships no
native payload: it compiles one multi-targeted assembly against the pinned
`MarketingCloudSDK.Net.Android` / `MarketingCloudSDK.Net.iOS` bindings, with identity delegated to
the `SFMCSDK.Net` core façade. Two packages release **together from one commit**:
`MarketingCloudSDK.Net` (the façade, nine target frameworks) and `MarketingCloudSDK.Net.Maui`
(`UseMarketingCloud`/`AddMarketingCloud`, four target frameworks).

The version is `<MarketingCloudSDK iOS version>.<binding revision>` — `11.0.2.1` is iOS-native
**11.0.2**, revision **1**. The Android native line (**11.0.1**) is different on purpose; state it
wherever you state the package version.

## Layout

- `src/MarketingCloudSDK.Net/` — shared API (`MarketingCloudClient`, `IMarketingCloudClient`,
  `IMarketingCloudRegistration`, `MarketingCloudOptions`) plus `Platforms/{Android,Apple,Neutral}`.
- `src/Sfmc.Facade.props` — TFM bands, per-platform source selection, pack assets. Imported by the
  façade only; `MarketingCloudSDK.Net.Maui` deliberately does not import it.
- `build/` — `BuildNugets.sh`, `merge-packages.py`, `CheckReadmeVersions.sh`, `check-upstream.sh`,
  and the `packages.tsv` / `upstream.tsv` rosters everything else reads.
- `tests/` — `UnitTests` (neutral leg), `PackageTests` (nupkg shape), `DeviceTests` (one project,
  two heads). `samples/` sits outside the solution and consumes packed nupkgs from `artifacts/`.

## Behaviour that must not change without a release note

These are measured facts, not accidents. Do not "fix" them.

- **`InitializeAsync` is one-shot even on failure.** Never reset `_initializationClaimed`; neither
  platform's configure is retryable and a retry would race the first attempt. Validate arguments
  *before* claiming the guard.
- **iOS initialization survives a timeout.** Against an unprovisioned or unreachable tenant the
  completion block never fires, so `Platforms/Apple` reads `SFMCSdk.State` after the timeout and
  completes when `pushfeature` is reported. Keep that fallback and its `catch (TimeoutException)`.
- **Neutral heads throw, never no-op.** Every native-reaching seam in `Platforms/Neutral` throws
  `PlatformNotSupportedException` naming the `net*-android` / `net*-ios` heads. A silently
  unregistered device reads as missing from Marketing Cloud.
- **`EditAsync` records, then applies.** The delegate runs synchronously against `RecordingEditor`
  (validation on the caller's stack), then the edits replay in one pass: one editor + `Commit()` on
  Android's `RequestSdk` instance, one `sfmc_*` selector per edit on iOS.
- **`SetAttribute` routes per platform** — the v11 Android editor has no attribute members, so it
  goes through the core identity there; `sfmc_setAttributeNamed:value:` on iOS.
- **Registration reads are local SDK state**, null until the platform can answer (Android gates on
  `IsReady`). `PushToken` is the FCM token on Android and **always null on iOS**.
- **Native accept/refuse booleans stay unsurfaced**, and `DiagnosticState` is for logs only — never
  parse it, never assert on its contents.
- **Identity is composed, never reimplemented.** The client owns an uninitialized `SfmcSdkClient`
  for `Identity` only; never call the core façade's `InitializeAsync` from here.

## Pins and versions

- `Directory.Build.props` holds `SfmcNativeVersion`, `SfmcBindingRevision`,
  `SfmcAndroidNativeVersion`, `SfmcAndroidPackageVersion`, `SfmcIosPackageVersion`,
  `SfmcCorePackageVersion` as **literal values on their own lines** — scripts and workflows
  sed-parse them. Never compose them from other properties.
- All three package pins are **exact ranges**: `[$(SfmcCorePackageVersion)]` on *every* head
  (neutral included — `ISfmcIdentity` is public surface), `[$(SfmcAndroidPackageVersion)]` on
  android heads, `[$(SfmcIosPackageVersion)]` on ios heads. `.Maui` pins the façade at this
  repository's own version. Never relax a pin to a floating minimum.
- Re-pin in this order: edit `Directory.Build.props` → update the README version map, pins and
  prose → reset `SfmcBindingRevision` to `1` on a native bump or increment it for a façade-only
  change → `./build/CheckReadmeVersions.sh` → device suites.
- `./build/check-upstream.sh` (driven by `build/upstream.tsv`) watches the three pinned **NuGet**
  packages. A pin behind nuget.org is a finding; a pin ahead is normal mid-release-train state.

## Target frameworks

- Façade (nine): `net8.0`/`net9.0`/`net10.0`, `net8.0-android34.0`/`net9.0-android35.0`/
  `net10.0-android36.0`, `net8.0-ios18.0`/`net9.0-ios18.0`/`net10.0-ios26.0`.
- `.Maui` (four): `net8.0-ios18.0`, `net9.0-ios18.0`, `net10.0-android36.0`, `net10.0-ios26.0`.
  **Android under MAUI is net10-only** — MAUI 8/9 and MobilePush 11 exact-range AndroidX Lifecycle
  into an unresolvable NU1107; the NU1202 a net8/net9 MAUI android head hits is the truthful early
  signal. Do not add those heads. `.Maui` has no neutral heads.
- Changing a TFM means editing the band lists in `Directory.Build.props` (and
  `SfmcMauiTargetFrameworks`) **and** the expected lists in
  `tests/MarketingCloudSDK.Net.PackageTests/Packages.cs`.

## Build and test — cheapest signal first

```sh
dotnet test tests/MarketingCloudSDK.Net.UnitTests -p:SfmcNeutralOnly=true   # no workloads needed
./build/BuildNugets.sh                                                      # macOS; packs into ./artifacts
dotnet test tests/MarketingCloudSDK.Net.PackageTests                        # after packing
./.github/scripts/run-simulator-tests.sh 11.0.2.1 net9.0-ios18.0
./.github/scripts/run-emulator-tests.sh 11.0.2.1 net9.0-android35.0
```

- `-p:SfmcNeutralOnly=true` is for `UnitTests` only. **Never pass it to a pack** — it would produce
  a façade with no platform legs and break the `.Maui` project outright.
- `mkdir -p artifacts` if restore fails with NU1301; commit nothing there but `.gitkeep`.
- Device suites run with dummy credentials against no tenant. They prove dependency-group wiring
  and façade-over-native behaviour, never server traffic.

## Workflows and releasing

- `build.yml` is reusable (`verify` input); `pr.yml` publishes `-beta.<pr>.<run>` (forked PRs skip
  publishing); `release.yml` guards that the tag is on the default branch, packs with
  `verify: false` and publishes via trusted publishing (OIDC, environment `nuget.org`).
- **Merging `docs/release-notes/<four-part-version>.md` to `main` *is* the release** —
  `auto-release.yml` tags it. Never tag by hand and never add a stored NuGet API key.

## House rules

- Public members carry XML docs: `GenerateDocumentationFile` + `TreatWarningsAsErrors` make CS1591
  fatal. The suppression list is `NU5104;NU1608` and nothing else — do not add to it.
- Never commit MobilePush credentials, `google-services.json`, or native binaries.
- Consumer-side workarounds (e.g. `Xamarin.AndroidX.Compose.Runtime.Annotation.Jvm 1.9.5` with
  `ExcludeAssets="all"`) belong in **application** projects — device tests and the sample — never
  as a package dependency. Do not pin AndroidX for consumers.
- Match the existing comment style: explain *why* a non-obvious decision is what it is, and say
  when it was measured against a real device, simulator or restore.
