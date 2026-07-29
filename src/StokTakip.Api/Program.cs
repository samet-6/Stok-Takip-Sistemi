using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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
using StokTakip.Infrastructure.Data.Seed;
using StokTakip.Infrastructure.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    // Password policy — mirrored by the frontend zod schemas for instant UX.
    options.Password.RequiredLength = 8;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireDigit = true;
    options.Password.RequireNonAlphanumeric = true;
})
    .AddRoles<IdentityRole>()
    .AddErrorDescriber<TurkishIdentityErrorDescriber>()
    .AddEntityFrameworkStores<AppDbContext>();

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

        options.Events = new JwtBearerEvents
        {
            // The browser's WebSocket API cannot set an Authorization header on the
            // handshake, so the hub identity must travel in the query string. Accepted
            // only under /hubs — and OnTokenValidated makes sure the only thing that
            // works there is a short-lived ticket, never the session token.
            OnMessageReceived = context =>
            {
                if (context.HttpContext.Request.Path.StartsWithSegments(HubRoutes.Prefix))
                {
                    var accessToken = context.Request.Query["access_token"];
                    if (!string.IsNullOrEmpty(accessToken))
                        context.Token = accessToken;
                }

                return Task.CompletedTask;
            },

            // Per-request session validation: the token's SecurityStamp must
            // match the DB and the user must still be active — else the token is rejected
            // (401 via OnChallenge). This is what makes admin password reset / email change /
            // deactivation take effect instantly instead of waiting for token expiry.
            OnTokenValidated = async context =>
            {
                var principal = context.Principal!;

                // Two-way scope fence: a hub ticket is only good for /hubs, and /hubs
                // accepts nothing else. The second half is what keeps the 8-hour session
                // token out of query strings — and therefore out of access logs — as a
                // server-enforced invariant rather than client-side good manners.
                // Checked before the DB round trip so a misrouted token costs no query.
                var isHubPath = context.HttpContext.Request.Path.StartsWithSegments(HubRoutes.Prefix);
                var isHubTicket =
                    principal.FindFirstValue(TokenService.ScopeClaimType) == TokenService.HubScope;

                if (isHubPath != isHubTicket)
                {
                    context.Fail("Bilet bu yol için geçerli değil.");
                    return;
                }

                var userId = principal.FindFirstValue("sub");
                var tokenStamp = principal.FindFirstValue(TokenService.SecurityStampClaimType);

                var userManager = context.HttpContext.RequestServices
                    .GetRequiredService<UserManager<ApplicationUser>>();
                var user = userId is null ? null : await userManager.FindByIdAsync(userId);

                if (user is null || !user.IsActive || user.SecurityStamp != tokenStamp)
                    context.Fail("Oturum geçersiz.");
            },
            OnChallenge = async context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status401Unauthorized,
                    Title = "Kimlik doğrulama gerekli."
                };
                // The content type has to travel with the write: WriteAsJsonAsync sets its own
                // ("application/json") and would overwrite anything assigned to the response
                // beforehand. Every other error in the API is RFC 7807, these two included.
                await context.Response.WriteAsJsonAsync(
                    problem, options: null, contentType: "application/problem+json");
            },
            OnForbidden = async context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = "Bu işlem için yetkiniz yok."
                };
                await context.Response.WriteAsJsonAsync(
                    problem, options: null, contentType: "application/problem+json");
            }
        };
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
    // Apply pending migrations before seeding (idempotent — no-op when up to date),
    // so a fresh database comes up with a fully migrated schema.
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    await DbSeeder.SeedAsync(scope.ServiceProvider);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseExceptionHandler();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<StokHub>(HubRoutes.Stok);

app.Run();
