using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using SmartApplyHub.Mobile.Services;
using SmartApplyHub.Mobile.ViewModels;
using SmartApplyHub.Mobile.Pages;

namespace SmartApplyHub.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        builder.Services.AddSingleton<AuthService>();
        builder.Services.AddSingleton<AuthHandler>();
        builder.Services.AddSingleton<NavigationService>();

        builder.Services.AddHttpClient("Api", client =>
        {
            client.BaseAddress = new Uri(ApiConfig.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
        })
#if DEBUG
        // The ASP.NET Core dev server uses a self-signed HTTPS certificate that
        // Android does not trust by default, which would otherwise cause every
        // request to fail (or hang until timeout on some Android versions).
        // Only bypass certificate validation in DEBUG builds against localhost.
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        })
#endif
        .AddHttpMessageHandler<AuthHandler>();

        builder.Services.AddSingleton(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return factory.CreateClient("Api");
        });

        builder.Services.AddSingleton<ApiService>();

        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<RegisterViewModel>();
        builder.Services.AddTransient<DashboardViewModel>();
        builder.Services.AddTransient<JobSearchViewModel>();
        builder.Services.AddTransient<JobAnalyzeViewModel>();
        builder.Services.AddTransient<ApplicationsViewModel>();
        builder.Services.AddTransient<ProfileViewModel>();
        builder.Services.AddTransient<SubscriptionViewModel>();
        builder.Services.AddTransient<BulkApplyViewModel>();

        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<RegisterPage>();
        builder.Services.AddTransient<DashboardPage>();
        builder.Services.AddTransient<JobSearchPage>();
        builder.Services.AddTransient<JobAnalyzePage>();
        builder.Services.AddTransient<JobAnalyzeDetailPage>();
        builder.Services.AddTransient<ApplicationsPage>();
        builder.Services.AddTransient<ProfilePage>();
        builder.Services.AddTransient<SubscriptionPage>();
        builder.Services.AddTransient<BulkApplyPage>();


        return builder.Build();
    }
}
