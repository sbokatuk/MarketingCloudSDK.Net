# MarketingCloudSDK.Net

[![NuGet](https://img.shields.io/nuget/v/MarketingCloudSDK.Net?label=nuget)](https://www.nuget.org/packages/MarketingCloudSDK.Net)
[![release](https://github.com/sbokatuk/MarketingCloudSDK.Net/actions/workflows/release.yml/badge.svg)](https://github.com/sbokatuk/MarketingCloudSDK.Net/actions/workflows/release.yml)
[![Targets: net8.0 | net9.0 | net10.0](https://img.shields.io/badge/targets-net8.0%20%7C%20net9.0%20%7C%20net10.0-512BD4)](#installing)
[![MarketingCloudSDK 11.0.2](https://img.shields.io/badge/MarketingCloudSDK-11.0.2-099DFD)](https://github.com/salesforce-marketingcloud/MarketingCloudSDK-iOS/releases)
[![marketingcloudsdk 11.0.1](https://img.shields.io/badge/marketingcloudsdk-11.0.1-099DFD)](https://salesforce-marketingcloud.github.io/MarketingCloudSDK-Android/)
[![Licence: MIT](https://img.shields.io/badge/licence-MIT-orange)](#licence)

**One Salesforce Marketing Cloud MobilePush API for .NET.** Awaitable initialization with real
credentials, identity (contact key and attributes), registration reads and tag/attribute edits
on Android and iOS, written once in shared code — over the
[MarketingCloudSDK.Net.Android](https://github.com/sbokatuk/MarketingCloudSDK.Net.Android) and
[MarketingCloudSDK.Net.iOS](https://github.com/sbokatuk/MarketingCloudSDK.Net.iOS) bindings,
with identity delegated to the [SFMCSDK.Net](https://github.com/sbokatuk/SFMCSDK.Net) core
façade underneath.

```sh
dotnet add package MarketingCloudSDK.Net
```

```csharp
using MarketingCloudSDK.Net;

var sdk = new MarketingCloudClient();   // one per process - register it as a DI singleton

await sdk.InitializeAsync(new MarketingCloudOptions
{
    ApplicationId = appId,              // from MobilePush app administration - never committed
    AccessToken = accessToken,
    ServerUrl = serverUrl,              // https://mc.xxxx.device.marketingcloudapis.com/
    Mid = mid,
});

sdk.Identity.SetProfileId("contact-key");           // the write side of identity (SFMC SDK core)
sdk.Identity.SetAttribute("plan", "pro");

await sdk.Registration.EditAsync(editor =>          // tags and attributes, batched per platform
{
    editor.AddTag("newsletter");
    editor.SetAttribute("tier", "gold");
});

var contactKey = sdk.Registration.ContactKey;       // the read side: the SDK's local record
```

That is the whole shared-code surface, and it is the same call sites on both platforms — no
`#if`, no per-platform adapter. MAUI apps add
[`MarketingCloudSDK.Net.Maui`](#packages-and-versions) for the one-line DI registration.

---

## Contents

- [Why there is a cross-platform layer](#why-there-is-a-cross-platform-layer)
- [What the façade does and does not hide](#what-the-façade-does-and-does-not-hide)
- [Push prerequisites](#push-prerequisites)
- [Packages and versions](#packages-and-versions)
- [Installing](#installing)
- [Usage notes](#usage-notes)
- [How this repository works](#how-this-repository-works)
- [Building locally](#building-locally)
- [Tests](#tests)
- [Upgrading](#upgrading)
- [Releasing](#releasing)
- [Troubleshooting](#troubleshooting)
- [Licence](#licence)

---

## Why there is a cross-platform layer

The two platform bindings are faithful projections of what Salesforce ships, and consequently
share almost no shape. The same four operations, raw:

| Operation | Android (`Com.Salesforce.Marketingcloud`) | iOS (`MarketingCloudSDK` + `SFMCSDK`) |
| --- | --- | --- |
| Initialize | `MarketingCloudSdk.Init(context, config, status => …)` — needs a `Context`, a `MarketingCloudConfig.Builder`, reports one terminal status through a listener | `SFMCSdk.InitializeSdk(new SFMCSdkConfigBuilder().SetPushFeature(pushConfig).Build(), statuses => …)` — the core entry point with a module config, reports **per-module** completion |
| Contact key | `SFMCSdk.RequestSdk(sdk => sdk.Identity = sdk.Identity.NewBuilder().SetProfileId(x).Build())` — v11 moved it to the core identity | `SFMCSdk.Identity.Edit(...)` **and** the module's own `sfmc_setContactKey:` shim |
| Tags | `sdk.RegistrationManager.Edit().AddTag(x)…Commit()` on the instance `RequestSdk` hands out asynchronously | `MobilePushSDK.SharedInstance` category selectors, one call per edit |
| Read registration | `MarketingCloudSdk.Instance.RegistrationManager` — only once `IsReady` | `sfmc_contactKey` / `sfmc_deviceIdentifier` category reads |

Writing that twice per app is the tax this package removes. It also absorbs an edge this
repository had to measure to hide: on iOS the initialization completion block reports
*concluded* module statuses, and against an unreachable or unprovisioned tenant the push module
never concludes — the block simply never fires while the SDK is, in every observable sense, up.
`InitializeAsync` knows that (see `Platforms/Apple`): when the completion has not fired within
the timeout it reads `SFMCSdk.state`, and "the pushfeature module is reported" completes the
same `await` that a firing completion completes on a provisioned tenant.

And one asymmetry is routed rather than lost: the v11 Android registration editor has **no
attribute members** (upstream moved profile writes to the SFMC SDK core), so the façade's
`editor.SetAttribute` maps to the core identity write on Android and to
`sfmc_setAttributeNamed:value:` on iOS — the real member on each platform, no silent no-op
anywhere.

## What the façade does and does not hide

The façade carries what an app does from *shared* code — initialize once, identify the user,
edit and read the registration, read a diagnostic line — and deliberately nothing else. It adds
no abstraction over things only one platform has, and it does not wrap surfaces an app touches
from platform code anyway.

Everything else stays reachable, because the packages underneath arrive with this one:

- **Android**: namespace `Com.Salesforce.Marketingcloud` (package
  `MarketingCloudSDK.Net.Android`) — the inbox manager, in-app messages, notification
  customization, geofence/beacon messaging, `RequestSdk` itself; plus
  `Com.Salesforce.Marketingcloud.Sfmcsdk` (via `SFMCSDK.Net.Android`) for the core's event
  tracking and consent.
- **iOS**: namespaces `MarketingCloudSDK` (package `MarketingCloudSDK.Net.iOS`) and `SFMCSDK`
  (package `SFMCSDK.Net.iOS`) — the full `sfmc_*` category surface (inbox, analytics, location),
  the config builders' remaining setters, the core's event model.

Mixing is fine and expected: initialize and identify through the façade, and read the inbox or
subscribe to events through the raw namespaces in the same app. Push UI plumbing is absent
because v11 made it unnecessary — the SDK observes the push token itself on both platforms — so
what remains is OS-side setup, listed under [Push prerequisites](#push-prerequisites).

On the plain `net8.0`/`net9.0`/`net10.0` target frameworks the package restores and compiles —
that is what lets a shared class library, a unit test, or a MAUI app's Windows head reference it
unconditionally — and every member that would reach the native SDK throws
`PlatformNotSupportedException` naming where the real implementation lives. Throwing, not
no-oping: a device that silently never registered would read as missing from Marketing Cloud,
with nothing logged anywhere.

## Push prerequisites

The façade does not change what MobilePush itself needs from the app:

- **Android**: a Firebase project — ship `google-services.json` in the application head — and,
  on Android 13+, the `POST_NOTIFICATIONS` runtime permission: request it
  (`Permissions.RequestAsync<Permissions.PostNotifications>()` in MAUI), or notifications are
  silently not shown. The sample requests it on its first page.
- **iOS**: an APNs-entitled app (`aps-environment`), a `UNUserNotificationCenter` authorization
  request, and `RegisterForRemoteNotifications`. Unlike the 8.x generation there is no token
  forwarding to write — v11 observes the APNs token itself.
- **Both**: the four MobilePush credentials (application id, access token, tenant endpoint,
  MID) from MobilePush app administration. **Never commit them** — load them from secure
  configuration or, as this repository's sample does, take them as runtime input.

## Packages and versions

Two packages. The version is `<MarketingCloudSDK iOS version>.<binding revision>` — `11.0.2.1`
is MarketingCloudSDK **11.0.2**, revision **1**, and the Android side of the same release is
marketingcloudsdk **11.0.1**.

> **Why one version names one SDK.** Salesforce releases the iOS and Android SDKs on separate
> cadences and their version numbers have never matched. A façade over both has to pick one line
> to name itself after or invent a third numbering that maps to nothing. It picks iOS — the same
> convention the SFMCSDK.Net core façade uses — and states the Android version everywhere it
> states its own.

| Package | What it adds | Depends on |
| --- | --- | --- |
| `MarketingCloudSDK.Net` | The cross-platform façade: `MarketingCloudClient`, options, registration, identity | `MarketingCloudSDK.Net.Android` / `.iOS` per platform; `SFMCSDK.Net` everywhere |
| `MarketingCloudSDK.Net.Maui` | `UseMarketingCloud` / `AddMarketingCloud` — the one-line MAUI DI registration | `MarketingCloudSDK.Net` (exact), `Microsoft.Maui.Controls` |

| MarketingCloudSDK.Net | MarketingCloudSDK (iOS, native) | marketingcloudsdk (Android, native) | MarketingCloudSDK.Net.iOS | MarketingCloudSDK.Net.Android | SFMCSDK.Net |
| --- | --- | --- | --- | --- | --- |
| 11.0.2.1 | 11.0.2 | 11.0.1 | 11.0.2.1 | 11.0.1.1 | 4.0.1.1 |

The dependencies are pinned **exactly** (`[11.0.2.1]` / `[11.0.1.1]` / `[4.0.1.1]`), not
floored: the façade calls each binding's hand-written convenience layer — the `Action` overloads
of `Init`/`RequestSdk` on Android, the hand-maintained `sfmc_*` category surface on iOS — and
those carry no compatibility promise across binding revisions. A newer binding is consumed by
this repository re-pinning and releasing, with the device suites in between, not by NuGet
floating a consumer onto it. The `.Maui` package pins the façade at its own version for the same
reason: the two release together from one commit.

## Installing

```xml
<PackageReference Include="MarketingCloudSDK.Net" Version="11.0.2.1" />
```

MAUI apps that want the DI wiring instead reference:

```xml
<PackageReference Include="MarketingCloudSDK.Net.Maui" Version="11.0.2.1" />
```

The façade ships nine target frameworks: `net8.0`, `net9.0`, `net10.0`, each with its
`-android` and `-ios` head — `net8.0-android34.0`, `net8.0-ios18.0`, `net9.0-android35.0`,
`net9.0-ios18.0`, `net10.0-android36.0`, `net10.0-ios26.0`. The `.Maui` package ships four:
`net8.0-ios18.0`, `net9.0-ios18.0`, `net10.0-android36.0`, `net10.0-ios26.0` — no neutral heads
(a MAUI builder extension has no neutral leg to offer; the plain-TFM story for shared code is
the façade's) and, measured rather than chosen, no net8/net9 android heads: MAUI 8/9's AndroidX
generation and MobilePush 11's exact-range each other into an unresolvable NU1107, and MAUI 10
is where the combination exists (the `.Maui` csproj records the conflict). A net8/net9 MAUI
android head referencing `.Maui` fails restore with NU1202 — the truthful early signal — while
the same app can still consume the façade directly with one `AddSingleton` line. Floors:
**iOS 12.2** (the MobilePush framework is Swift and relies on the OS Swift runtime, ABI-stable
from 12.2), **Android API 26** (the `.aar` manifests' own floor).

The platform heads pull `MarketingCloudSDK.Net.Android 11.0.1.1` /
`MarketingCloudSDK.Net.iOS 11.0.2.1` transitively — and with them the whole native graph
(Firebase Messaging and AndroidX on Android; the AppGroupSDK payload and the SFMC SDK core on
iOS). Every head, the neutral ones included, also pulls `SFMCSDK.Net`, whose `ISfmcIdentity` is
part of this façade's public surface. Apps reference only this package unless they want the raw
namespaces pinned explicitly (they may — the same versions arrive either way).

For a MAUI app with an Android head, target net10 — that is where the AndroidX generations
reconcile. The net8/net9 assets are for plain .NET Android / .NET iOS apps (and net8/net9 MAUI
iOS-only apps), which is also what the binding packages' own net8 support is for.

## Usage notes

- **One client per process.** The native SDK on both platforms is a process-wide singleton
  behind static entry points, so `MarketingCloudClient` is meant to live as a DI singleton —
  `UseMarketingCloud`/`AddMarketingCloud` in the `.Maui` package register exactly that. The
  initialization guard is per-instance — the instance is what promises one-shot semantics.
- **`InitializeAsync` is one-shot, even on failure.** A second call throws
  `InvalidOperationException`. The guard deliberately does not reset on failure: neither
  platform's configure is retryable, and after a timeout the first attempt may still be
  running — a retry would race it. A process that needs a fresh attempt restarts.
- **Identity is the core's, on purpose.** v11 moved contact key and attribute writes to the
  SFMC SDK core on both platforms, so `Identity` hands out SFMCSDK.Net's `ISfmcIdentity` — the
  same object apps using the core façade directly would hold. On iOS, `SetProfileId` also
  pushes the module's `sfmc_setContactKey:` shim so both identity planes agree. All identity
  work is fire-and-forget and legal before initialization — both SDKs queue it.
- **Registration reads are local state, not server truth.** They answer from the SDK's own
  record and return null until the platform can answer (on Android, until the SDK instance is
  operational). `PushToken` is the FCM token on Android and **always null on iOS** — the v11
  category surface exposes no APNs-token read; the SDK observes the token itself.
- **`EditAsync` batches per platform's own model.** One editor-commit on Android (on the
  asynchronously delivered instance — which is why it is a `Task`), one selector per edit on
  iOS. The native accept/refuse booleans are deliberately not surfaced; see
  `IMarketingCloudRegistration`.
- **`DiagnosticState` is for logs, never for parsing.** State JSON where the platform has it
  (the module's on iOS, the instance's on Android once ready), the coarse lifecycle phase
  before that.

## How this repository works

Nothing is bound here and nothing native is committed. The repository compiles one
multi-targeted façade assembly against the pinned binding packages: shared sources declare the
API and `private partial …Core` seams, and exactly one of `Platforms/Android`,
`Platforms/Apple` or `Platforms/Neutral` supplies the bodies per target framework (see
`src/Sfmc.Facade.props`). The `.Maui` package is two shared files over the façade — thin by
design, since v11 needs no lifecycle wiring from the app.

Each .NET SDK's android/ios workloads ship reference packs for only two target frameworks — the
.NET 9 band covers net8/net9, the .NET 10 band covers net9/net10 — so
[build/BuildNugets.sh](build/BuildNugets.sh) packs twice per package and
[build/merge-packages.py](build/merge-packages.py) grafts the net10 assets *and their dependency
groups* into one package, merging façade before Maui so the exact-pinned `PackageReference`
between them restores from `./artifacts`. The groups matter as much as the assemblies: an empty
net10 group would tell NuGet a net10 consumer needs no binding underneath, and the app would
fail with the native SDK missing.

The pins live in [Directory.Build.props](Directory.Build.props) as literal properties
(`SfmcAndroidPackageVersion`, `SfmcIosPackageVersion`, `SfmcCorePackageVersion`) that the
scripts and workflows sed-parse. `NuGet.config` adds `./artifacts` as a package source, so
locally packed dependencies (from the sibling repositories) and the locally packed façade both
resolve without publishing anything.

### Layout

| Path | What |
| --- | --- |
| `src/Sfmc.Facade.props` | TFM bands, per-platform source selection, compiler settings, pack assets |
| `src/MarketingCloudSDK.Net/` | The façade: shared half + `Platforms/{Android,Apple,Neutral}` |
| `src/MarketingCloudSDK.Net.Maui/` | The thin MAUI layer: two extension classes, platform TFMs only |
| `build/` | Pack, merge, README-check and upstream-check scripts; `packages.tsv` is the roster |
| `tests/` | `UnitTests` (neutral leg, no workloads), `PackageTests` (nupkg shape), `DeviceTests` (one project, two heads, driving the façade over the packed package) |
| `samples/` | A MAUI app driving initialization, identity, tags and registration reads through the façade, zero `#if` |
| `.github/workflows/` | `pr`, `build`, `release`, `auto-release` |

## Building locally

```sh
mkdir -p artifacts   # then, when working against unreleased sibling builds, drop their nupkgs in
./build/BuildNugets.sh                # packs both packages at 11.0.2.1 into ./artifacts
dotnet test tests/MarketingCloudSDK.Net.UnitTests -p:SfmcNeutralOnly=true
dotnet test tests/MarketingCloudSDK.Net.PackageTests
```

## Tests

Three suites, cheapest first — each catches what the previous one cannot see:

```sh
dotnet test tests/MarketingCloudSDK.Net.UnitTests -p:SfmcNeutralOnly=true   # validation, guard, neutral contract
dotnet test tests/MarketingCloudSDK.Net.PackageTests                        # TFMs, exact pins, licence, symbols
./.github/scripts/run-simulator-tests.sh 11.0.2.1 net9.0-ios18.0            # the façade over the real SDK
./.github/scripts/run-emulator-tests.sh 11.0.2.1 net9.0-android35.0
```

`-p:SfmcNeutralOnly=true` collapses the referenced façade to its neutral target frameworks, so
the unit tests restore and run on any machine with no mobile workloads — it is the pipeline's
fastest signal, and must never be set on a pack (it would produce a façade with no platform legs
and break the `.Maui` project outright).

The device checks run with dummy credentials on purpose: the configuration points at no real
tenant, so server calls fail by design. What they prove is the packaging promise — the packed
façade's dependency groups pull the right binding in on each platform, and the façade drives it:
`InitializeAsync` completes per each platform's verified semantics, registration reads answer,
`EditAsync` commits, identity crosses into native code, and the one-shot guard holds.

## Upgrading

Re-pin, verify, release — in that order:

1. Bump `SfmcAndroidPackageVersion` / `SfmcIosPackageVersion` / `SfmcCorePackageVersion` (and,
   when the native line moved, `SfmcNativeVersion` + `SfmcAndroidNativeVersion`) in
   `Directory.Build.props`.
2. Update the version map above — `./build/CheckReadmeVersions.sh` fails the build until the
   README agrees with the props, which is the point.
3. Reset `SfmcBindingRevision` to 1 on a native bump, or increment it for a façade-only change.
4. Open a PR: the pipeline packs, validates the package shape and runs both device suites
   against the new pins before anything ships.

`./build/check-upstream.sh` (shared verbatim with the binding repositories, driven by
[build/upstream.tsv](build/upstream.tsv)) reports when Salesforce publishes a native MobilePush
version newer than the pinned lines — the early signal that the binding repositories will move
and this façade will need re-pinning behind them.

## Releasing

Merging a file named `docs/release-notes/<four-part-version>.md` to `main` **is** the release:
`auto-release` tags it, `release` verifies the tag is on the default branch (the guard), packs
with verification off — the tagged commit was already verified on its pull request — and
publishes to nuget.org via trusted publishing (OIDC, no stored API key). Every pull request
publishes a `-beta.<pr>.<run>` prerelease.

## Troubleshooting

**`PlatformNotSupportedException` mentioning the neutral build.** The code ran on a plain target
framework (a unit test, a Windows head). That is the documented contract — construct freely,
guard the calls, or inject a fake of `IMarketingCloudClient`; the real implementation runs in
`net*-android` / `net*-ios` heads.

**`InvalidOperationException` from a second `InitializeAsync`.** Initialization is one-shot per
client and per process — see [Usage notes](#usage-notes). Await the first call; do not retry.

**Android build fails with D8 duplicate classes on `androidx.compose.runtime.annotation`
(JAVA0000: `Type ... is defined multiple times`).** The 2026 AndroidX generation splits
`Compose.Runtime.Annotation` into `-Android` (.aar) and `-Jvm` (.jar) flavors, and the
transitive graph resolves both, each carrying the same classes. Keep the package in the graph
but drop its duplicate assets — Android apps want the `.android` flavor:

```xml
<PackageReference Include="Xamarin.AndroidX.Compose.Runtime.Annotation.Jvm" Version="1.9.5" ExcludeAssets="all" />
```

This line belongs in the **application** project (this repository's device tests and sample
carry it); it is not a package dependency, because excluding assets is a per-app resolution
decision.

**No push token arrives.** On Android: Firebase is not configured — the app needs
`google-services.json`, and on Android 13+ the granted `POST_NOTIFICATIONS` permission. On iOS:
the app lacks the APNs entitlement or never called `RegisterForRemoteNotifications`. The
registration itself still completes; the token side stays empty.

**Restore fails with NU1301 naming `artifacts`.** The local package source must exist:
`mkdir -p artifacts` (a fresh clone has it via the committed `.gitkeep`).

**NU1608 warnings about AndroidX versions.** A property of the AndroidX graph the MobilePush
Android binding pulls in — exact-ranged packages inside it disagree with siblings that float
past them. NuGet resolves the higher version and the result is what the binding's emulator tests
pass against. This repository suppresses the warning rather than pinning AndroidX for every
consumer; pin in your app if you want it gone.

**A surface you need is missing from the façade.** Deliberate — see
[What the façade does and does not hide](#what-the-façade-does-and-does-not-hide). Use the
platform namespaces; they ship in the same restore.

## Licence

The code in this repository is [MIT](LICENSE), and both packages declare plain `MIT` — they ship
no native binaries, so Salesforce's licence is not theirs to declare. The binding packages
underneath ship the native MobilePush artifacts (© Salesforce, BSD-3-Clause) and each declares
`MIT AND BSD-3-Clause` with both texts packed; the BSD text is mirrored at
[licenses/BSD-3-Clause-Salesforce.txt](licenses/BSD-3-Clause-Salesforce.txt) for reference.
