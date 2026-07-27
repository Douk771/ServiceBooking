using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using ServiceBooking.API.Services;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();

// Swagger / OpenAPI
builder.Services.AddSwaggerGen(opt =>
{
    opt.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ServiceBooking API",
        Version = "v1",
        Description = "API для SaaS-платформы онлайн-записи на услуги (салоны, барбершопы и т.п.). " +
                      "Поддерживает роли Client, Master, CompanyOwner и SuperAdmin."
    });

    opt.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Вставьте JWT-токен, полученный из /api/auth/login или /api/auth/register (без слова 'Bearer')."
    });
    opt.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            []
        }
    });

    var xmlFile = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xmlFile)) opt.IncludeXmlComments(xmlFile, includeControllerXmlComments: true);
});

// Database
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Identity
builder.Services.AddIdentity<AppUser, IdentityRole>(opt =>
    {
        opt.Password.RequireNonAlphanumeric = false;
        opt.Password.RequiredLength = 8;
        opt.Lockout.MaxFailedAccessAttempts = 5;
        opt.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

// JWT
var jwtKey = builder.Configuration["Jwt:Key"]!;
builder.Services.AddAuthentication(opt =>
    {
        opt.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        opt.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };

        // Roles are baked into the JWT at login/register time. Without this, a role change made via
        // PUT /api/admin/users/{id}/roles or POST /api/companies/{id}/members only takes effect after
        // the affected user logs in again — including role *revocations*, which is a real security gap
        // (e.g. a demoted SuperAdmin keeps acting as one until their token expires, up to 7 days later).
        // Re-reading the current roles from the database on every request makes authorization reflect
        // live state instead of a point-in-time snapshot.
        opt.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var principal = context.Principal;
                var userId = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                if (userId is null) { context.Fail("Invalid token"); return; }

                var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<AppUser>>();
                var user = await userManager.FindByIdAsync(userId);
                if (user is null) { context.Fail("User no longer exists"); return; }

                var currentRoles = await userManager.GetRolesAsync(user);

                var identity = (ClaimsIdentity)principal!.Identity!;
                foreach (var staleRoleClaim in identity.FindAll(identity.RoleClaimType).ToList())
                    identity.RemoveClaim(staleRoleClaim);
                foreach (var role in currentRoles)
                    identity.AddClaim(new Claim(identity.RoleClaimType, role));
            }
        };
    });

builder.Services.AddAuthorization();

// CORS for React frontend
builder.Services.AddCors(opt =>
    opt.AddDefaultPolicy(p =>
        p.WithOrigins(builder.Configuration["AllowedOrigins"]?.Split(',') ?? ["http://localhost:5173"])
         .AllowAnyHeader()
         .AllowAnyMethod()
         .AllowCredentials()));

// Services
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<SlotService>();
builder.Services.AddScoped<SubscriptionResolver>();
builder.Services.AddHttpClient<CaptchaService>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(opt =>
{
    opt.SwaggerEndpoint("/swagger/v1/swagger.json", "ServiceBooking API v1");
    opt.RoutePrefix = "swagger";
});

app.UseCors();
app.UseStaticFiles(); // serves wwwroot/uploads/... (company logos, etc.)
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Seed roles and super-admin on startup
using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    await db.Database.MigrateAsync();

    string[] roles = ["Client", "Master", "CompanyOwner", "SuperAdmin"];
    foreach (var role in roles)
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));

    // The SuperAdmin, like every account, is now identified by phone (UserName == phone). Email is
    // optional and kept only for display. Config key SuperAdmin:Phone drives login.
    var adminPhone = builder.Configuration["SuperAdmin:Phone"];
    var adminEmail = builder.Configuration["SuperAdmin:Email"];
    var adminPassword = builder.Configuration["SuperAdmin:Password"];
    if (adminPhone is not null && adminPassword is not null)
    {
        var admin = await userManager.FindByNameAsync(adminPhone);
        if (admin is null)
        {
            admin = new AppUser { FirstName = "Super", LastName = "Admin", Email = adminEmail, UserName = adminPhone, PhoneNumber = adminPhone };
            await userManager.CreateAsync(admin, adminPassword);
            await userManager.AddToRoleAsync(admin, "SuperAdmin");
        }
    }
}

app.Run();

// Exposes the implicit Program class so the functional test project can spin up
// the app in-memory via WebApplicationFactory<Program>.
public partial class Program;
