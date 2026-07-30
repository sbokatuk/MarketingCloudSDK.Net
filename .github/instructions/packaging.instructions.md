---
description: MSBuild, pins and the two-pass pack - what the packages are allowed to say.
applyTo: "**/*.csproj,**/*.props,build/**"
---

# Packaging and pins

- Keep the version properties in `Directory.Build.props` literal and one per line
  (`SfmcNativeVersion`, `SfmcBindingRevision`, `SfmcAndroidNativeVersion`,
  `SfmcAndroidPackageVersion`, `SfmcIosPackageVersion`, `SfmcCorePackageVersion`);
  `CheckReadmeVersions.sh`, `release.yml`, `pr.yml` and `check-upstream.sh` sed-parse them.
- Reference the bindings and the core umbrella with exact bracketed ranges only. `SFMCSDK.Net` goes
  on every head including the neutral ones; `.Maui` pins the façade at `[$(SfmcFacadeDependencyVersion)]`.
- Target frameworks come from the band lists (`SfmcSdkBand` = `net9` or `net10`). `SfmcNeutralOnly`
  is a lever for `tests/MarketingCloudSDK.Net.UnitTests` alone — never set it for a pack.
- `MarketingCloudSDK.Net.Maui` does not import `src/Sfmc.Facade.props` and restates its own
  equivalents. Keep `UseMauiCore` + `UseMauiEssentials` (not `UseMaui`),
  `SkipValidateMauiImplicitPackageReferences`, and the Android-only
  `Xamarin.AndroidX.Lifecycle.LiveData.Core` reference with **both** `ExcludeAssets="all"` and
  `PrivateAssets="all"` — it is a restore aid, not a published dependency.
- `NoWarn` stays `$(NoWarn);NU5104;NU1608`. Suppressing more (CS1591 above all) is a defect.
  `AndroidGenerateResourceDesigner` stays `false` on android heads.
- Every package packs `build/icon.png`, `README.md` and `LICENSE` into `licenses/`, and declares
  plain `MIT`: this repository ships no native binaries, so Salesforce's BSD-3-Clause is not its to
  declare.
- `./build/BuildNugets.sh [version]` runs on macOS, packs each package twice (net9 band, then net10
  band from a scratch `global.json`) and merges per package **in `build/packages.tsv` order** —
  the façade must be in `artifacts/` before `.Maui` restores its exact pin.
- `merge-packages.py` grafts the net10 `lib/<tfm>/` trees **and their dependency groups**. An empty
  or missing group tells NuGet a net10 consumer needs no binding, and the app fails with the native
  SDK absent — so a merge that cannot find the group must fail loudly.
- `build/packages.tsv` and `build/upstream.tsv` are the rosters everything reads. Adding a package
  is one row plus one project under `src/`; `upstream.tsv` watches the pinned **NuGet** packages,
  not the Salesforce SDKs (the binding repositories watch those).
- `global.json` pins SDK `9.0.100` with `rollForward: latestFeature`; `NuGet.config` clears sources
  and adds `nuget.org` plus the local `artifacts` feed. Change neither casually.
