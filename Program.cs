using System.Text;
using System.Threading.RateLimiting;
using JobApplicationBot.Data;
using JobApplicationBot.Data.Entities;
using JobApplicationBot.Data.Repositories;
using JobApplicationBot.Models;
using JobApplicationBot.Services;
using JobApplicationBot.Services.Admin;
using JobApplicationBot.Services.Billing;
using JobApplicationBot.Services.Quota;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

const long MaxRequestBodyBytes = 6 * 1024 * 1024;

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestHeadersTotalSize = 64 * 1024;
    options.Limits.MaxRequestBodySize = MaxRequestBodyBytes;
});

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = MaxRequestBodyBytes;
    options.ValueLengthLimit = (int)MaxRequestBodyBytes;
});

builder.Services.AddControllersWithViews()
    .AddSessionStateTempDataProvider();
builder.Services.AddRazorPages();
builder.Services.AddMemoryCache();
builder.Services.AddSession();

var keysPath = builder.Configuration["DataProtection:KeysPath"] ?? "keys";
var keysDirectory = Path.IsPathRooted(keysPath)
    ? keysPath
    : Path.Combine(builder.Environment.ContentRootPath, keysPath);
Directory.CreateDirectory(keysDirectory);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysDirectory))
    .SetApplicationName("SmartApplyHub");

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var key = context.User.Identity?.IsAuthenticated == true
            ? context.User.Identity!.Name ?? "authenticated"
            : context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: key,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 50
            });
    });
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services
    .AddDefaultIdentity<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
        options.User.RequireUniqueEmail = true;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
{
    builder.Services.AddAuthentication()
        .AddGoogle(options =>
        {
            options.ClientId = googleClientId;
            options.ClientSecret = googleClientSecret;
        });
}

var jwtKey = builder.Configuration["Jwt:Key"];
if (!string.IsNullOrWhiteSpace(jwtKey))
{
    builder.Services.AddAuthentication()
        .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = builder.Configuration["Jwt:Issuer"],
                ValidAudience = builder.Configuration["Jwt:Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(jwtKey))
            };
        });
}

builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));

builder.Services.AddCors(options =>
{
    options.AddPolicy("MobileApp", policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

builder.Services.Configure<AiSettings>(builder.Configuration.GetSection("Ai"));
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("Email"));
builder.Services.Configure<StripeSettings>(builder.Configuration.GetSection("Stripe"));
builder.Services.Configure<SubscriptionSettings>(builder.Configuration.GetSection("Subscription"));
builder.Services.Configure<DataProtectionSettings>(builder.Configuration.GetSection("DataProtection"));
builder.Services.Configure<JobSearchSettings>(builder.Configuration.GetSection("JobSearch"));

builder.Services.AddHttpClient<IJobScraperService, JobScraperService>();
builder.Services.AddHttpClient<IAiService, GeminiAiService>();
builder.Services.AddScoped<IEmailSenderService, EmailSenderService>();
builder.Services.AddTransient<IEmailSender<ApplicationUser>, IdentityEmailSender>();
builder.Services.AddScoped<IUserAccountSetupService, UserAccountSetupService>();
builder.Services.AddScoped<IUserProfileRepository, UserProfileRepository>();
builder.Services.AddScoped<IUserCvFileRepository, UserCvFileRepository>();
builder.Services.AddScoped<IUserEmailCredentialRepository, UserEmailCredentialRepository>();
builder.Services.AddScoped<IAiUsageRepository, AiUsageRepository>();
builder.Services.AddScoped<IJobApplicationRepository, JobApplicationRepository>();
builder.Services.AddScoped<IBulkEmailDispatchRepository, BulkEmailDispatchRepository>();
builder.Services.AddScoped<IAiQuotaService, AiQuotaService>();
builder.Services.AddScoped<IApplicationTrackingService, ApplicationTrackingService>();
builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();
builder.Services.AddScoped<IStripeBillingService, StripeBillingService>();
builder.Services.AddScoped<IAdminRequestService, AdminRequestService>();
builder.Services.AddSingleton<IEmailExtractionService, EmailExtractionService>();
builder.Services.AddSingleton<ICvTextExtractionService, CvTextExtractionService>();
builder.Services.AddHostedService<BulkEmailDispatchWorker>();
builder.Services.AddHealthChecks();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "SmartApply Hub API",
        Version = "v1",
        Description = "REST API for the SmartApply Hub mobile app"
    });

    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Enter your JWT token"
    });

    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Identity/Account/Login";
    options.LogoutPath = "/Identity/Account/Logout";
    options.AccessDeniedPath = "/Identity/Account/AccessDenied";
});

var app = builder.Build();

await SeedAdminAsync(app);

var identityEmail = app.Configuration["Email:SenderEmail"];
var identityEmailPassword = app.Configuration["Email:SenderPassword"];
if (string.IsNullOrWhiteSpace(identityEmail) || string.IsNullOrWhiteSpace(identityEmailPassword))
{
    app.Logger.LogWarning(
        "Identity email (confirm / forgot password) is not fully configured. Set Email:SenderEmail and Email:SenderPassword via environment variables or user secrets.");
}
else
{
    app.Logger.LogInformation("Identity outbound email configured for {SenderEmail}.", identityEmail);
}

if (string.IsNullOrWhiteSpace(app.Configuration["Ai:ApiKey"]))
{
    app.Logger.LogWarning("Ai:ApiKey is not set. Shared-key AI features will fail until configured.");
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "SmartApply Hub API v1"));
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// In Development, skip the HTTP->HTTPS redirect so the Android emulator can
// call the API over plain HTTP (http://10.0.2.2:5210). Android rejects the
// dev HTTPS certificate (issued for "localhost", not "10.0.2.2"), so the
// redirect would break every mobile API call with a TLS handshake failure.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseRouting();
app.UseCors("MobileApp");
app.UseRateLimiter();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();
app.MapStaticAssets();
app.MapHealthChecks("/health");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages();

app.Run();

static async Task SeedAdminAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("AdminSeed");

    const string adminRole = "Admin";
    if (!await roleManager.RoleExistsAsync(adminRole))
    {
        await roleManager.CreateAsync(new IdentityRole(adminRole));
        logger.LogInformation("Created Identity role {Role}.", adminRole);
    }

    var adminEmail = app.Configuration["Admin:Email"];
    if (string.IsNullOrWhiteSpace(adminEmail))
    {
        logger.LogWarning("Admin:Email is not set. No user was granted the Admin role.");
        return;
    }

    var user = await userManager.FindByEmailAsync(adminEmail.Trim());
    if (user == null)
    {
        logger.LogWarning("Admin:Email {Email} was not found. Register that account, then restart to grant Admin.", adminEmail);
        return;
    }

    if (!await userManager.IsInRoleAsync(user, adminRole))
    {
        await userManager.AddToRoleAsync(user, adminRole);
        logger.LogInformation("Granted Admin role to {Email}.", adminEmail);
    }
}
