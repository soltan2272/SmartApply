namespace JobApplicationBot.Data.Entities;

public static class SubscriptionPlans
{
    public const string Free = "Free";
    public const string Pro = "Pro";
    public const string Power = "Power";

    public static readonly string[] All = [Free, Pro, Power];

    public static bool IsValid(string? plan) =>
        !string.IsNullOrWhiteSpace(plan)
        && All.Contains(plan, StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string? plan) =>
        IsValid(plan)
            ? All.First(p => p.Equals(plan, StringComparison.OrdinalIgnoreCase))
            : Free;
}
