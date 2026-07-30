---
description: Façade sources - the partial-Core seam pattern and the per-platform contracts.
applyTo: "src/**/*.cs"
---

# Façade sources

- The shared half declares `private partial …Core(...)` seams; exactly one of
  `Platforms/Android`, `Platforms/Apple`, `Platforms/Neutral` supplies the bodies per target
  framework. Add a seam to **all three** directories in the same change — an unmatched target
  framework failing to compile is the design, not a bug to route around.
- Put anything platform-independent in the shared half: argument validation, the initialization
  guard, the edit recorder, the timeout mechanics (`AwaitNativeCompletion`). Only calls that reach
  a native SDK belong in `Platforms/`.
- No `#if` in shared sources, and no reflection anywhere: `EnableTrimAnalyzer` and
  `EnableAotAnalyzer` are on for every head and warnings are errors. Do not claim `IsTrimmable`.
- `Platforms/Neutral` throws `PlatformNotSupportedException` from every native-reaching seam and
  passes the core identity through unchanged. Never make a neutral seam no-op or return a
  placeholder.
- `Platforms/Android`: reach OS namespaces through `global::Android.*` (an unqualified `Android.`
  resolves against the binding's namespace first); gate reads on `MarketingCloudSdk.IsReady`; use
  `Application.Context`, never a caller-supplied `Context`; carry JNI-callback exceptions onto the
  awaited task; the status accessors are `[Obsolete]` at v11, so keep discarding the parameter.
- `Platforms/Apple`: keep the `catch (TimeoutException)` fallback that reads `SFMCSdk.State` for
  `pushfeature`, and keep `SetProfileId` writing both identity planes (core first, then
  `sfmc_setContactKey:`). Discard the selectors' accept/refuse booleans.
- Keep the public surface at what both platforms honour at v11. Single-platform features stay
  unwrapped — the raw binding namespaces arrive in the same restore.
- Document every public member, and document the platform difference where one exists (iOS
  `PushToken` is always null; Android `SetAttribute` routes to core identity). XML docs are where
  those asymmetries are recorded, and CS1591 is fatal.
- `MarketingCloudOptions` is a record with `init` setters and `required` credentials; keep it
  immutable, since initialization is one-shot.
- `MarketingCloudSDK.Net.Maui` stays two files of registration only. Nothing native runs in
  `UseMarketingCloud`/`AddMarketingCloud`, and `TryAdd*` keeps a test fake in place.
