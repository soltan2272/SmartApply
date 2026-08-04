using JobApplicationBot.Data.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace JobApplicationBot.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    private readonly IDataProtectionProvider? _dataProtectionProvider;

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        IDataProtectionProvider? dataProtectionProvider = null)
        : base(options)
    {
        _dataProtectionProvider = dataProtectionProvider;
    }

    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<AiUsage> AiUsages => Set<AiUsage>();
    public DbSet<UserCvFile> UserCvFiles => Set<UserCvFile>();
    public DbSet<UserEmailCredential> UserEmailCredentials => Set<UserEmailCredential>();
    public DbSet<JobApplication> JobApplications => Set<JobApplication>();
    public DbSet<BulkEmailDispatch> BulkEmailDispatches => Set<BulkEmailDispatch>();
    public DbSet<SubscriptionRequest> SubscriptionRequests => Set<SubscriptionRequest>();
    public DbSet<TokenResetRequest> TokenResetRequests => Set<TokenResetRequest>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<UserProfile>(entity =>
        {
            entity.HasOne(p => p.User)
                  .WithOne(u => u.Profile)
                  .HasForeignKey<UserProfile>(p => p.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            if (_dataProtectionProvider != null)
            {
                entity.Property(p => p.PersonalGeminiApiKey)
                      .HasConversion(new EncryptedStringConverter(_dataProtectionProvider));
            }
        });

        builder.Entity<AiUsage>(entity =>
        {
            entity.HasOne(u => u.User)
                  .WithMany()
                  .HasForeignKey(u => u.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(u => new { u.UserId, u.Year, u.Month })
                  .IsUnique()
                  .HasDatabaseName("IX_AiUsages_User_Year_Month");
        });

        builder.Entity<UserCvFile>(entity =>
        {
            entity.HasOne(c => c.User)
                  .WithOne()
                  .HasForeignKey<UserCvFile>(c => c.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.Property(c => c.Content).HasColumnType("varbinary(max)");
        });

        builder.Entity<UserEmailCredential>(entity =>
        {
            entity.HasOne(c => c.User)
                  .WithOne()
                  .HasForeignKey<UserEmailCredential>(c => c.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            if (_dataProtectionProvider != null)
            {
                entity.Property(c => c.Secret)
                      .HasConversion(new EncryptedStringConverter(_dataProtectionProvider));
            }
        });

        builder.Entity<BulkEmailDispatch>(entity =>
        {
            entity.HasOne(d => d.User)
                .WithMany()
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(d => new { d.UserId, d.CreatedAt })
                .HasDatabaseName("IX_BulkEmailDispatches_User_CreatedAt");
            entity.HasIndex(d => d.Status)
                .HasDatabaseName("IX_BulkEmailDispatches_Status");
            entity.HasIndex(d => d.LeaseExpiresAt)
                .HasDatabaseName("IX_BulkEmailDispatches_LeaseExpiresAt");
        });

        builder.Entity<JobApplication>(entity =>
        {
            entity.HasOne(a => a.User)
                .WithMany()
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(a => a.BulkDispatch)
                .WithMany(d => d.Applications)
                .HasForeignKey(a => a.BulkDispatchId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasIndex(a => new { a.UserId, a.CreatedAt })
                .HasDatabaseName("IX_JobApplications_User_CreatedAt");
            entity.HasIndex(a => new { a.UserId, a.Status })
                .HasDatabaseName("IX_JobApplications_User_Status");
            entity.HasIndex(a => new { a.BulkDispatchId, a.Status })
                .HasDatabaseName("IX_JobApplications_Dispatch_Status");
            entity.HasIndex(a => new { a.UserId, a.NextFollowUpAt })
                .HasDatabaseName("IX_JobApplications_User_FollowUp");
            entity.HasIndex(a => new { a.UserId, a.PipelineStatus })
                .HasDatabaseName("IX_JobApplications_User_Pipeline");
        });

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(u => u.SubscriptionPlan)
                .HasMaxLength(20)
                .HasDefaultValue(SubscriptionPlans.Free);
            entity.Property(u => u.StripeCustomerId).HasMaxLength(100);
            entity.Property(u => u.StripeSubscriptionId).HasMaxLength(100);
        });

        builder.Entity<SubscriptionRequest>(entity =>
        {
            entity.HasOne(r => r.User)
                .WithMany()
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(r => new { r.UserId, r.Status })
                .HasDatabaseName("IX_SubscriptionRequests_User_Status");
            entity.HasIndex(r => r.CreatedAt)
                .HasDatabaseName("IX_SubscriptionRequests_CreatedAt");
        });

        builder.Entity<TokenResetRequest>(entity =>
        {
            entity.HasOne(r => r.User)
                .WithMany()
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(r => new { r.UserId, r.Status })
                .HasDatabaseName("IX_TokenResetRequests_User_Status");
            entity.HasIndex(r => r.CreatedAt)
                .HasDatabaseName("IX_TokenResetRequests_CreatedAt");
        });
    }
}
