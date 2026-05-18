using System.Text;
using KidSafe.Backend.Common;
using KidSafe.Backend.Data;
using KidSafe.Backend.Data.Entities;
using KidSafe.Backend.Hubs;
using KidSafe.Backend.Middleware;
using KidSafe.Backend.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ── Database ──────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ── JWT Auth ──────────────────────────────────────────────────────────────────
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidateAudience = true,
            ValidateLifetime = true, ValidateIssuerSigningKey = true,
            ValidIssuer    = builder.Configuration["Jwt:Issuer"],
            ValidAudience  = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
        };
        // Allow JWT via query string for SignalR
        opt.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) &&
                    ctx.HttpContext.Request.Path.StartsWithSegments("/hubs/chat"))
                    ctx.Token = token;
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title       = "KidSafe API",
        Version     = "v1",
        Description = "ASP.NET Core backend for KidSafe — AI-moderated child chat platform"
    });

    // JWT security definition
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name         = "Authorization",
        Type         = SecuritySchemeType.Http,
        Scheme       = "Bearer",
        BearerFormat = "JWT",
        In           = ParameterLocation.Header,
        Description  = "Enter: Bearer {your JWT token}"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });

    // Group endpoints by tag
    c.TagActionsBy(api =>
    {
        if (api.RelativePath?.StartsWith("auth")          == true) return ["Authentication"];
        if (api.RelativePath?.StartsWith("messages")      == true) return ["Chat"];
        if (api.RelativePath?.StartsWith("reports")       == true) return ["Reports"];
        if (api.RelativePath?.StartsWith("admin")         == true) return ["Moderation"];
        if (api.RelativePath?.StartsWith("dashboard")     == true) return ["Dashboard"];
        if (api.RelativePath?.StartsWith("notifications") == true) return ["Notifications"];
        if (api.RelativePath?.StartsWith("classes")       == true) return ["Classes"];
        return [api.GroupName ?? "General"];
    });
    c.DocInclusionPredicate((_, _) => true);
});

// ── AI HTTP client ────────────────────────────────────────────────────────────
builder.Services.AddHttpClient<IAIService, AIService>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["AI:BaseUrl"]
                                 ?? "http://localhost:8000");
    client.Timeout = TimeSpan.FromSeconds(15);
});
// Named client for health check (no base address — full URL used in controller)
builder.Services.AddHttpClient("ai-health");

// ── App services ──────────────────────────────────────────────────────────────
builder.Services.AddSingleton<INotificationService, NotificationService>();
builder.Services.AddScoped<IAuthService, AuthService>();

// Async moderation pipeline (queue + background worker)
builder.Services.AddSingleton<ModerationQueue>();
builder.Services.AddHostedService<ModerationWorker>();

// ── CORS ──────────────────────────────────────────────────────────────────────
var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>()
                     ?? ["http://localhost:5001", "https://localhost:5001"];

builder.Services.AddCors(opt =>
{
    opt.AddPolicy("DevOpen", p =>
        p.SetIsOriginAllowed(_ => true).AllowAnyMethod().AllowAnyHeader().AllowCredentials());
    opt.AddDefaultPolicy(p =>
        p.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader().AllowCredentials());
});

// ── Build ─────────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── DB init: apply migrations + seed ─────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    // Repair: migration 20260513173440_AddRollNumberAndProfile had an empty Up() body.
    // These columns exist in the EF model but were never added to the DB.
    // IF NOT EXISTS makes this a safe no-op after first run.
    await db.Database.ExecuteSqlRawAsync(@"
        IF NOT EXISTS (SELECT 1 FROM sys.columns
                       WHERE object_id = OBJECT_ID(N'[Users]') AND name = N'Phone')
            ALTER TABLE [Users] ADD [Phone] nvarchar(20) NULL;

        IF NOT EXISTS (SELECT 1 FROM sys.columns
                       WHERE object_id = OBJECT_ID(N'[Users]') AND name = N'RollNumber')
            ALTER TABLE [Users] ADD [RollNumber] nvarchar(50) NULL;
    ");

    // Repair: ensure DirectMessages table exists (safety net if migration body was empty)
    await db.Database.ExecuteSqlRawAsync(@"
        IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'DirectMessages')
        CREATE TABLE [DirectMessages] (
            [Id]         INT IDENTITY(1,1) NOT NULL,
            [SenderId]   INT NOT NULL,
            [ReceiverId] INT NOT NULL,
            [Content]    NVARCHAR(MAX) NOT NULL DEFAULT '',
            [Label]      NVARCHAR(20)  NOT NULL DEFAULT 'Safe',
            [Score]      FLOAT NOT NULL DEFAULT 0,
            [Masked]     NVARCHAR(MAX) NULL,
            [Timestamp]  DATETIME2    NOT NULL DEFAULT GETUTCDATE(),
            CONSTRAINT PK_DirectMessages PRIMARY KEY ([Id]),
            CONSTRAINT FK_DM_Sender   FOREIGN KEY ([SenderId])   REFERENCES [Users]([Id]),
            CONSTRAINT FK_DM_Receiver FOREIGN KEY ([ReceiverId]) REFERENCES [Users]([Id])
        );
    ");

    await SeedAdminAsync(db, app.Configuration);
    await SeedDemoDataAsync(db);
}

app.UseErrorHandling();

if (app.Environment.IsDevelopment())
{
    app.UseCors("DevOpen");
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "KidSafe API v1");
        c.RoutePrefix = "swagger";
        c.DocumentTitle = "KidSafe API Docs";
    });
}
else
    app.UseCors();

// Serve uploaded files
app.UseStaticFiles();
// Ensure uploads directory exists
Directory.CreateDirectory(Path.Combine(app.Environment.WebRootPath
    ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), "uploads"));

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");

app.Run();

// ── Demo seeding (dev only) ───────────────────────────────────────────────────
static async Task SeedDemoDataAsync(AppDbContext db)
{
    async Task AddUser(string email, string password, string name, string role, string avatar = "")
    {
        if (await db.Users.AnyAsync(u => u.Email == email)) return;
        var u = new User
        {
            Email        = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            DisplayName  = name,
            Role         = role,
            Status       = "active",
            AvatarEmoji  = avatar
        };
        db.Users.Add(u);
        db.Rewards.Add(new Reward { User = u });
    }

    await AddUser("teacher@demo.com", "Demo@123!", "Demo Teacher", "Teacher");
    await AddUser("parent@demo.com",  "Demo@123!", "Demo Parent",  "Parent");
    await AddUser("student@demo.com", "Demo@123!", "Demo Student", "Child", "😊");
    await db.SaveChangesAsync();
}

// ── Admin seeding ─────────────────────────────────────────────────────────────
static async Task SeedAdminAsync(AppDbContext db, IConfiguration config)
{
    if (await db.Users.AnyAsync(u => u.Role == "Admin"))
        return;  // already seeded

    var email    = config["Admin:Email"]    ?? "admin@kidsafe.app";
    var password = config["Admin:Password"] ?? "Admin@123!";
    var name     = config["Admin:Name"]     ?? "KidSafe Admin";

    var admin = new User
    {
        Email        = email,
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
        DisplayName  = name,
        Role         = "Admin",
        Status       = "active"
    };

    db.Users.Add(admin);
    await db.SaveChangesAsync();
}
