namespace SmartApplyHub.Mobile.Services;

public static class ApiConfig
{
    // For the Android EMULATOR, 10.0.2.2 maps to the host machine's localhost.
    // For a PHYSICAL DEVICE on the same Wi-Fi, replace 10.0.2.2 with your PC's
    // LAN IP (run `ipconfig` and use the IPv4 Address, e.g. 192.168.1.50),
    // and make sure Windows Firewall allows inbound traffic on this port.
    //
    // We intentionally use plain HTTP (not HTTPS) for local development.
    // Android performs SSL hostname verification *before* any custom
    // ServerCertificateCustomValidationCallback runs, and the ASP.NET Core
    // dev certificate is issued for "localhost", not "10.0.2.2" — so HTTPS
    // requests fail at the TLS handshake even with cert-validation bypassed.
    // The "https" launch profile also exposes plain HTTP on port 5210 (see
    // Properties/launchSettings.json), and cleartext traffic is explicitly
    // allowed for debug builds via android:usesCleartextTraffic="true" in
    // AndroidManifest.xml. Run the backend with the "https" profile
    // (`dotnet run --launch-profile https`) so both ports are listening.
#if DEBUG
    public const string BaseUrl = "http://10.0.2.2:5210/";
#else
    public const string BaseUrl = "https://your-production-url.com/";
#endif
}
