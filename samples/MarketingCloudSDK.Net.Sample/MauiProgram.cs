using MarketingCloudSDK.Net;
using Microsoft.Extensions.Logging;

namespace MarketingCloudSDK.Net.Sample;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // One client per process, registered as a singleton and injected as
        // IMarketingCloudClient. The whole page programs against the interface - which is also
        // what makes the page unit-testable against a fake on a plain target framework.
        //
        // An app whose credentials live in configuration would instead call the
        // MarketingCloudSDK.Net.Maui package's one-liner, which registers the same singleton
        // WITH its options:
        //
        //     builder.UseMarketingCloud(new MarketingCloudOptions { ApplicationId = ..., ... });
        //
        // This sample deliberately does not: its credentials are typed into the page at runtime
        // (never committed - see the README's push prerequisites), so there are no options to
        // register at build time and the page constructs them itself.
        builder.Services.AddSingleton<IMarketingCloudClient, MarketingCloudClient>();
        builder.Services.AddSingleton<MainPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
