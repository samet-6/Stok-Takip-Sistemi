using System.Text;
using System.Threading.RateLimiting;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.SignalR;
using Microsoft.OpenApi;
using StokTakip.Api;
using StokTakip.Api.Realtime;
using StokTakip.Application.Auth;
using StokTakip.Application.Categories;
using StokTakip.Application.Common;
using StokTakip.Application.Notifications;
using StokTakip.Application.Products;
using StokTakip.Application.Services;
using StokTakip.Application.StockMovements;
using StokTakip.Application.Suppliers;
using StokTakip.Application.Users;
using StokTakip.Infrastructure.Auth;
using StokTakip.Infrastructure.Services;
using StokTakip.Infrastructure.Data;
using StokTakip.Infrastructure.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

builder.Services.AddIdentityCore<ApplicationUser>()
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>();

// The password rules themselves live with the rules (PasswordPolicy), not here: wiring is this
// file's job, and a rule nothing else can read is a rule that gets copied by hand.
builder.Services.ConfigureOptions<IdentityOptionsSetup>();

// Rate limiting on the login endpoint. The lockout stops an account from being guessed; this
// stops the server from paying for the guessing — every attempt costs a query and a password
// hash verification, locked or not. Partitioned by caller address: one noisy client must not
// spend everybody else's budget.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(LoginRateLimit.PolicyName, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = LoginRateLimit.PermitLimit,
                Window = LoginRateLimit.Window,
                // No queue: a login is worth answering now or refusing now. Queuing would hold
                // connections open and turn the limit into latency instead of a refusal.
                QueueLimit = 0
            }));
});

// The integration suite turns this off (StokTakipFactory): every test class logs in, repeatedly,
// from one client, and enforcement would cut unrelated tests at random. LoginRateLimitTests
// builds its own host with it back on.
var rateLimitingEnabled = builder.Configuration.GetValue("RateLimiting:Enabled", true);

// Auth
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();

// Application services
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<ISupplierService, SupplierService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IStockMovementService, StockMovementService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IUserLookupService, UserLookupService>();
builder.Services.AddScoped<IUserService, UserService>();

// Realtime. CloseOnAuthenticationExpiration stays OFF (the default) on purpose: the hub
// ticket only authenticates the handshake and lives 30 seconds, so turning it on would
// tear every connection down half a minute after it opened. Revoked sessions are pushed
// explicitly instead of being inferred from ticket expiry.
builder.Services.AddSignalR();
builder.Services.AddSingleton<IUserIdProvider, HubUserIdProvider>();
builder.Services.AddSingleton<IRealtimeNotifier, SignalRNotifier>();

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Jwt configuration section is missing.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub",
            RoleClaimType = "role"
        };

        options.Events = JwtBearerEventHandlers.Create();
    });

builder.Services.AddAuthorization();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "StokTakip API", Version = "v1" });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Login yanıtındaki token'ı yapıştırın"
    });

    // .NET 10 / Microsoft.OpenApi 2.0: AddSecurityRequirement takes a delegate and
    // OpenApiSecuritySchemeReference replaces the old OpenApiReference pattern.
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    // Demo rows (the sample employee and twelve products) are opt-in outside development, so a
    // real deployment does not get a second login nobody asked for. The compose stack runs as
    // Production and *is* the demo, so it turns the flag on explicitly.
    var seedDemoData =
        builder.Configuration.GetValue<bool?>("Seed:Demo") ?? !app.Environment.IsProduction();

    // Migrate before seeding (idempotent — a no-op when up to date), under an advisory lock so
    // several replicas starting at once queue up instead of racing.
    await DatabaseInitializer.InitializeAsync(scope.ServiceProvider, seedDemoData);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.MapGet("/", () => Results.Redirect("/swagger"));
}

app.UseExceptionHandler();

// Before authentication: a refused request should not cost a token validation or a DB round trip.
if (rateLimitingEnabled)
    app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<StokHub>(HubRoutes.Stok);

await app.RunAsync();
