using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MarketingCloudSDK.Net.Maui;

/// <summary>Registers MarketingCloudSDK.Net with a Microsoft.Extensions service collection.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IMarketingCloudClient"/> as a singleton
    /// <see cref="MarketingCloudClient"/> and <paramref name="options"/> as the singleton
    /// <see cref="MarketingCloudOptions"/> for it to be initialized with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A singleton is not a style choice here: the native SDK on both platforms is a
    /// process-wide singleton and <see cref="MarketingCloudClient"/>'s one-shot initialization
    /// guard is per-instance, so exactly one client per process is the documented model.
    /// Registration is idempotent via TryAdd: an existing <see cref="IMarketingCloudClient"/> or
    /// <see cref="MarketingCloudOptions"/> - including a test fake - is left in place.
    /// </para>
    /// <para>
    /// Nothing native runs here; the app awaits
    /// <see cref="IMarketingCloudClient.InitializeAsync"/> itself, with the registered options.
    /// See <see cref="AppBuilderExtensions.UseMarketingCloud(MauiAppBuilder, MarketingCloudOptions)"/>
    /// for the push prerequisites that
    /// remain the app's job (Android 13+ <c>POST_NOTIFICATIONS</c> via
    /// <c>Permissions.PostNotifications</c>, Firebase configuration, APNs
    /// entitlement/authorization) - and for what v11 made unnecessary.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="options">The MobilePush options to register.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> or <paramref name="options"/> is null.
    /// </exception>
    public static IServiceCollection AddMarketingCloud(this IServiceCollection services, MarketingCloudOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.TryAddSingleton(options);
        return services.AddMarketingCloud();
    }

    /// <summary>
    /// Registers <see cref="IMarketingCloudClient"/> as a singleton
    /// <see cref="MarketingCloudClient"/> without options, for apps whose credentials are not
    /// known at composition time.
    /// </summary>
    /// <remarks>
    /// The overload taking <see cref="MarketingCloudOptions"/> is the usual one - credentials from
    /// secure configuration, registered once at startup. This one exists because credentials do
    /// not always come from configuration: an app that reads its tenant from a signed-in user's
    /// profile, prompts for it, or switches tenants at runtime has nothing to register up front,
    /// and would otherwise get nothing from this package and hand-write the same
    /// <c>AddSingleton</c> the overload performs. Pass the options to
    /// <see cref="IMarketingCloudClient.InitializeAsync"/> when you have them; nothing else
    /// differs, and no <see cref="MarketingCloudOptions"/> is resolvable from DI.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddMarketingCloud(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IMarketingCloudClient, MarketingCloudClient>();
        return services;
    }
}
