using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace JobApplicationBot.Data;

/// <summary>
/// Value converter that transparently protects/unprotects strings on the way to/from the database.
/// Used for per-user Gemini API keys and Gmail App Passwords.
/// </summary>
public class EncryptedStringConverter : ValueConverter<string?, string?>
{
    private const string ProtectorPurpose = "JobApplicationBot.UserSecret.v1";

    public EncryptedStringConverter(IDataProtectionProvider provider)
        : base(
            v => Protect(provider, v),
            v => Unprotect(provider, v))
    {
    }

    private static string? Protect(IDataProtectionProvider provider, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return provider.CreateProtector(ProtectorPurpose).Protect(value);
    }

    private static string? Unprotect(IDataProtectionProvider provider, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        try
        {
            return provider.CreateProtector(ProtectorPurpose).Unprotect(value);
        }
        catch (CryptographicException)
        {
            // Key ring rotated (e.g. first deploy with PersistKeysToFileSystem).
            // Treat as missing so login/profile still work; user re-enters the secret.
            return null;
        }
    }
}
