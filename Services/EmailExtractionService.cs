using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace JobApplicationBot.Services;

public interface IEmailExtractionService
{
    /// <summary>
    /// Extracts unique, valid email addresses from free-form text (job posts, notes, pasted lists, etc.).
    /// </summary>
    IReadOnlyList<string> ExtractEmails(string? text);
}

public partial class EmailExtractionService : IEmailExtractionService
{
    private static readonly EmailAddressAttribute EmailValidator = new();

    // Matches typical email shapes, including mailto: prefixes and surrounding punctuation.
    [GeneratedRegex(
        @"(?:mailto:)?([a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 250)]
    private static partial Regex EmailRegex();

    public IReadOnlyList<string> ExtractEmails(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var matches = EmailRegex().Matches(text);
        if (matches.Count == 0)
            return [];

        return matches
            .Select(m => m.Groups[1].Value.Trim().TrimEnd('.', ',', ';', ':', ')', ']', '>', '"', '\''))
            .Where(email => !string.IsNullOrWhiteSpace(email) && EmailValidator.IsValid(email))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(email => email, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
