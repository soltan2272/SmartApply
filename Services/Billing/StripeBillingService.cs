using JobApplicationBot.Data.Entities;
using JobApplicationBot.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace JobApplicationBot.Services.Billing;

public interface IStripeBillingService
{
    bool IsConfigured { get; }
    Task<string> CreateCheckoutSessionAsync(string userId, string plan, string successUrl, string cancelUrl, CancellationToken ct = default);
    Task<string?> CreateCustomerPortalSessionAsync(string userId, string returnUrl, CancellationToken ct = default);
    Task HandleWebhookAsync(string json, string stripeSignature, CancellationToken ct = default);
}

public class StripeBillingService : IStripeBillingService
{
    private readonly StripeSettings _stripe;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ISubscriptionService _subscriptions;
    private readonly ILogger<StripeBillingService> _logger;

    public StripeBillingService(
        IOptions<StripeSettings> stripe,
        UserManager<ApplicationUser> users,
        ISubscriptionService subscriptions,
        ILogger<StripeBillingService> logger)
    {
        _stripe = stripe.Value;
        _users = users;
        _subscriptions = subscriptions;
        _logger = logger;
        if (!string.IsNullOrWhiteSpace(_stripe.SecretKey))
            StripeConfiguration.ApiKey = _stripe.SecretKey;
    }

    public bool IsConfigured => _stripe.IsConfigured;

    public async Task<string> CreateCheckoutSessionAsync(
        string userId,
        string plan,
        string successUrl,
        string cancelUrl,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Stripe is not configured. Set Stripe:SecretKey and price IDs.");

        plan = SubscriptionPlans.Normalize(plan);
        if (plan == SubscriptionPlans.Free)
            throw new InvalidOperationException("Free plan does not require checkout.");

        var priceId = plan == SubscriptionPlans.Power ? _stripe.PowerPriceId : _stripe.ProPriceId;
        if (string.IsNullOrWhiteSpace(priceId))
            throw new InvalidOperationException($"Stripe price ID for {plan} is missing.");

        var user = await _users.FindByIdAsync(userId)
            ?? throw new InvalidOperationException("User was not found.");

        var options = new SessionCreateOptions
        {
            Mode = "subscription",
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
            ClientReferenceId = userId,
            CustomerEmail = string.IsNullOrWhiteSpace(user.StripeCustomerId) ? user.Email : null,
            Customer = string.IsNullOrWhiteSpace(user.StripeCustomerId) ? null : user.StripeCustomerId,
            LineItems =
            [
                new SessionLineItemOptions
                {
                    Price = priceId,
                    Quantity = 1
                }
            ],
            Metadata = new Dictionary<string, string>
            {
                ["userId"] = userId,
                ["plan"] = plan
            },
            SubscriptionData = new SessionSubscriptionDataOptions
            {
                Metadata = new Dictionary<string, string>
                {
                    ["userId"] = userId,
                    ["plan"] = plan
                }
            }
        };

        var service = new SessionService();
        var session = await service.CreateAsync(options, cancellationToken: ct);
        return session.Url;
    }

    public async Task<string?> CreateCustomerPortalSessionAsync(string userId, string returnUrl, CancellationToken ct = default)
    {
        if (!IsConfigured)
            return null;

        var user = await _users.FindByIdAsync(userId);
        if (user == null || string.IsNullOrWhiteSpace(user.StripeCustomerId))
            return null;

        var service = new Stripe.BillingPortal.SessionService();
        var session = await service.CreateAsync(new Stripe.BillingPortal.SessionCreateOptions
        {
            Customer = user.StripeCustomerId,
            ReturnUrl = returnUrl
        }, cancellationToken: ct);

        return session.Url;
    }

    public async Task HandleWebhookAsync(string json, string stripeSignature, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_stripe.WebhookSecret))
            throw new InvalidOperationException("Stripe:WebhookSecret is not configured.");

        var stripeEvent = EventUtility.ConstructEvent(json, stripeSignature, _stripe.WebhookSecret);

        switch (stripeEvent.Type)
        {
            case EventTypes.CheckoutSessionCompleted:
            {
                if (stripeEvent.Data.Object is Session session)
                    await ApplyCheckoutSessionAsync(session, ct);
                break;
            }
            case EventTypes.CustomerSubscriptionUpdated:
            case EventTypes.CustomerSubscriptionCreated:
            {
                if (stripeEvent.Data.Object is Subscription subscription)
                    await ApplySubscriptionAsync(subscription, ct);
                break;
            }
            case EventTypes.CustomerSubscriptionDeleted:
            {
                if (stripeEvent.Data.Object is Subscription subscription)
                    await DowngradeSubscriptionAsync(subscription, ct);
                break;
            }
            default:
                _logger.LogDebug("Ignoring Stripe event {Type}", stripeEvent.Type);
                break;
        }
    }

    private async Task ApplyCheckoutSessionAsync(Session session, CancellationToken ct)
    {
        var userId = session.ClientReferenceId
            ?? session.Metadata?.GetValueOrDefault("userId");
        if (string.IsNullOrWhiteSpace(userId))
        {
            _logger.LogWarning("Checkout session {SessionId} missing userId.", session.Id);
            return;
        }

        var plan = session.Metadata?.GetValueOrDefault("plan") ?? SubscriptionPlans.Pro;
        await _subscriptions.SetPlanAsync(
            userId,
            plan,
            session.CustomerId,
            session.SubscriptionId,
            ct);

        _logger.LogInformation("User {UserId} upgraded to {Plan} via Checkout.", userId, plan);
    }

    private async Task ApplySubscriptionAsync(Subscription subscription, CancellationToken ct)
    {
        var userId = subscription.Metadata?.GetValueOrDefault("userId");
        if (string.IsNullOrWhiteSpace(userId))
        {
            var user = _users.Users.FirstOrDefault(u => u.StripeSubscriptionId == subscription.Id
                || u.StripeCustomerId == subscription.CustomerId);
            userId = user?.Id;
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            _logger.LogWarning("Subscription {SubId} could not be mapped to a user.", subscription.Id);
            return;
        }

        var plan = subscription.Metadata?.GetValueOrDefault("plan");
        if (string.IsNullOrWhiteSpace(plan))
            plan = ResolvePlanFromPrice(subscription);

        if (subscription.Status is "active" or "trialing")
        {
            await _subscriptions.SetPlanAsync(userId, plan ?? SubscriptionPlans.Pro, subscription.CustomerId, subscription.Id, ct);
        }
        else if (subscription.Status is "canceled" or "unpaid" or "incomplete_expired")
        {
            await _subscriptions.SetPlanAsync(userId, SubscriptionPlans.Free, subscription.CustomerId, subscription.Id, ct);
        }
    }

    private async Task DowngradeSubscriptionAsync(Subscription subscription, CancellationToken ct)
    {
        var user = _users.Users.FirstOrDefault(u => u.StripeSubscriptionId == subscription.Id
            || u.StripeCustomerId == subscription.CustomerId);
        if (user == null)
            return;

        await _subscriptions.SetPlanAsync(user.Id, SubscriptionPlans.Free, user.StripeCustomerId, null, ct);
        _logger.LogInformation("User {UserId} downgraded to Free after subscription cancel.", user.Id);
    }

    private string ResolvePlanFromPrice(Subscription subscription)
    {
        var priceId = subscription.Items?.Data?.FirstOrDefault()?.Price?.Id;
        if (!string.IsNullOrWhiteSpace(priceId) && priceId == _stripe.PowerPriceId)
            return SubscriptionPlans.Power;
        if (!string.IsNullOrWhiteSpace(priceId) && priceId == _stripe.ProPriceId)
            return SubscriptionPlans.Pro;
        return SubscriptionPlans.Pro;
    }
}
