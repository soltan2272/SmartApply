using JobApplicationBot.Models;
using JobApplicationBot.Services;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestHeadersTotalSize = 64 * 1024;
});

builder.Services.AddControllersWithViews()
    .AddSessionStateTempDataProvider();
builder.Services.AddMemoryCache();
builder.Services.AddSession();

builder.Services.Configure<AiSettings>(builder.Configuration.GetSection("Ai"));
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("Email"));
builder.Services.Configure<UserProfile>(builder.Configuration.GetSection("UserProfile"));

builder.Services.AddHttpClient<IJobScraperService, JobScraperService>();
builder.Services.AddHttpClient<IAiService, GeminiAiService>();
builder.Services.AddScoped<IEmailSenderService, EmailSenderService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseSession();
app.UseAuthorization();
app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Job}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
