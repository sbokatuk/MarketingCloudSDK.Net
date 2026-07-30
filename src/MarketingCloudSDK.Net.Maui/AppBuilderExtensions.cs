namespace MarketingCloudSDK.Net.Maui;

/// <summary>Wires MarketingCloudSDK.Net into a MAUI app.</summary>
public static class AppBuilderExtensions
{
    /// <summary>
    /// Registers Marketing Cloud MobilePush with the app: <see cref="IMarketingCloudClient"/>
    /// becomes resolvable (as a singleton <see cref="MarketingCloudClient"/>) and
    /// <paramref name="options"/> is registered alongside it, so pages and view models take the
    /// client as a constructor dependency and the composition root owns the credentials.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registration only - nothing native runs here. Call
    /// <see cref="IMarketingCloudClient.InitializeAsync"/> (typically from your first page or
    /// app startup) with the registered options resolved from DI, and await it. This is
    /// deliberate: initialization is asynchronous and can fail, and burying it in the builder
    /// would leave the failure with nowhere to land.
    /// </para>
    /// <para>
    /// No lifecycle wiring is added, because at v11 none is needed: the SDK observes the push
    /// token itself on both platforms (Firebase on Android, APNs on iOS) - unlike the 8.x
    /// generation, which needed the app to forward tokens from its delegates. What remains the
    /// app's own job is OS-side: on Android 13+ request the runtime notification permission
    /// (<c>Permissions.RequestAsync&lt;Permissions.PostNotifications&gt;()</c> - without the
    /// grant, notifications are silently not shown) and ship your Firebase
    /// <c>google-services.json</c>; on iOS carry the APNs entitlement, ask
    /// <c>UNUserNotificationCenter</c> for authorization and call
    /// <c>RegisterForRemoteNotifications</c>.
    /// </para>
    /// <para>
    /// Registration is idempotent and never overwrites an existing
    /// <see cref="IMarketingCloudClient"/> - a fake registered by a test harness wins.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.UseMauiApp&lt;App&gt;().UseMarketingCloud(new MarketingCloudOptions
    /// {
    ///     ApplicationId = appId,      // from secure configuration - never committed
    ///     AccessToken = accessToken,
    ///     ServerUrl = serverUrl,
    ///     Mid = mid,
    /// });
    ///
    /// public MainPage(IMarketingCloudClient marketingCloud) { _sdk = marketingCloud; }
    /// </code>
    /// </example>
    /// <param name="builder">The MAUI app builder.</param>
    /// <param name="options">The MobilePush options to register for the client to consume.</param>
    /// <returns><paramref name="builder"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/> or <paramref name="options"/> is null.
    /// </exception>
    public static MauiAppBuilder UseMarketingCloud(this MauiAppBuilder builder, MarketingCloudOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddMarketingCloud(options);
        return builder;
    }

    /// <summary>
    /// Registers Marketing Cloud MobilePush with the app without options, for credentials that are
    /// not known at composition time - the app supplies them to
    /// <see cref="IMarketingCloudClient.InitializeAsync"/> later.
    /// </summary>
    /// <remarks>
    /// Everything the options overload documents applies here too; the only difference is that no
    /// <see cref="MarketingCloudOptions"/> is registered, because there is none yet. Use it when
    /// the tenant comes from a signed-in user, a runtime prompt (as this repository's sample does)
    /// or anywhere else the composition root genuinely cannot know it.
    /// </remarks>
    /// <param name="builder">The MAUI app builder.</param>
    /// <returns><paramref name="builder"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    public static MauiAppBuilder UseMarketingCloud(this MauiAppBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddMarketingCloud();
        return builder;
    }
}
