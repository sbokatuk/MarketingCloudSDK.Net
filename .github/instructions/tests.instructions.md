---
description: Which suite proves what, and how to keep each one able to prove it.
applyTo: "tests/**"
---

# Tests

- **UnitTests** (`net9.0`, `-p:SfmcNeutralOnly=true`) cover the shared half only: options defaults
  and validation, the one-shot guard, the recorder's argument checks, and the neutral
  `PlatformNotSupportedException` contract. Keep them workload-free — never reference a platform
  target framework from here.
- **PackageTests** run against the packed `.nupkg` in `artifacts/`, so pack first. The expected
  target-framework lists in `Packages.cs` are the assertion and are deliberately repeated there:
  update them with any TFM change, and keep the equality checks two-way — a `.Maui` net8/net9
  android asset *appearing* is as wrong as one disappearing.
- **DeviceTests** stay one project with two heads (`SfmcDeviceTargetFramework`), consuming the
  packed `MarketingCloudSDK.Net` from `artifacts/` rather than a `ProjectReference` — the
  transitively arriving bindings are the point of the suite.
- Device checks share one client and run in array order: initialize, use, then re-initialize to
  prove the guard. Preserve that ordering when adding a check, and report through `Reporter`;
  the runners look for the `SFMC_E2E_DONE PASS` marker.
- Credentials in device tests are dummy values pointing at no tenant. Null registration reads and
  server-side failures are expected results — never assert on server truth, a push token, or the
  contents of `DiagnosticState`. Allow for iOS spending the whole `InitializationTimeout`.
- Application-side workarounds live in the test app project (`Xamarin.AndroidX.Compose.Runtime.Annotation.Jvm`
  with `ExcludeAssets="all"`, `UseInterpreter`, the explicit `AndroidManifest` the net8 head needs);
  never move them into a package.
- Run them cheapest first:
  `dotnet test tests/MarketingCloudSDK.Net.UnitTests -p:SfmcNeutralOnly=true`,
  `dotnet test tests/MarketingCloudSDK.Net.PackageTests`,
  `./.github/scripts/run-simulator-tests.sh <version> <tfm>`,
  `./.github/scripts/run-emulator-tests.sh <version> <tfm>`.
- Test projects set `GenerateDocumentationFile=false`; the public-docs rule applies to the packages.
