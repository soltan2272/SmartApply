namespace SmartApplyHub.Mobile.Services;

public static class ApiConfig
{
    // For Android emulator, 10.0.2.2 maps to host machine's localhost.
    // For a physical device, use your machine's LAN IP.
#if DEBUG
    public const string BaseUrl = "https://10.0.2.2:7001/";
#else
    public const string BaseUrl = "https://your-production-url.com/";
#endif
}
