using MarketingCloudSDK.Net;
using MarketingCloudSDK.Net.Maui;
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
        // The no-options overload, because this sample's credentials are typed into the page at
        // runtime (never committed - see the README's push prerequisites), so there is nothing to
        // register at build time. An app whose credentials live in secure configuration passes
        // them here instead, and the client resolves them from DI:
        //
        //     builder.UseMarketingCloud(new MarketingCloudOptions { ApplicationId = ..., ... });
        builder.UseMarketingCloud();
        builder.Services.AddSingleton<MainPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
