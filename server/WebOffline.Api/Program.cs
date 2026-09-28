using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using WebOffline.Api.Middleware;
using WebOffline.Core.Common;
using WebOffline.Core.Interfaces;
using WebOffline.Infrastructure.Data;
using WebOffline.Infrastructure.Repositories;
using WebOffline.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. Connection strings and DB Factories (Separate Databases)
var defaultConn = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Data Source=offline_task_manager.db;Cache=Shared";
var auditConn = builder.Configuration.GetConnectionString("AuditConnection") 
    ?? "Data Source=audit_logs.db;Cache=Shared";

builder.Services.AddSingleton<ISqliteDbConnectionFactory>(new SqliteDbConnectionFactory(defaultConn));
builder.Services.AddSingleton<IAuditDbConnectionFactory>(new AuditDbConnectionFactory(auditConn));

builder.Services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
builder.Services.AddScoped<DbInitializer>();
builder.Services.AddScoped<AuditDbInitializer>();

// 2. Main Repositories
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IUserSessionRepository, UserSessionRepository>();
builder.Services.AddScoped<IWorkspaceRepository, WorkspaceRepository>();
builder.Services.AddScoped<IListRepository, ListRepository>();
builder.Services.AddScoped<ITaskRepository, TaskRepository>();
builder.Services.AddScoped<ISyncRepository, SyncRepository>();

// 3. Audit Repositories & Services
builder.Services.AddScoped<IAuditRepository, AuditRepository>();
builder.Services.AddScoped<IAuditService, AuditService>();

// 4. Security, Token & ABAC Evaluator
builder.Services.AddScoped<ITokenService, JwtTokenService>();
builder.Services.AddScoped<IAbacPolicyEvaluator, AbacPolicyEvaluator>();

// 4.5 Application Services (Business Logic)
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IWorkspaceService, WorkspaceService>();
builder.Services.AddScoped<IListService, ListService>();
builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddScoped<ISyncService, SyncService>();

// 5. JWT Authentication
var jwtSecret = builder.Configuration["Jwt:Secret"] ?? "OfflineFirstTaskManagerSuperSecureKeyForJwtTokens2026!";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "WebOfflineApi";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "WebOfflineClient";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        ValidateIssuer = true,
        ValidIssuer = jwtIssuer,
        ValidateAudience = true,
        ValidAudience = jwtAudience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
    options.Events = new JwtBearerEvents
    {
        OnChallenge = context =>
        {
            context.HandleResponse();
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            var response = ApiResponse.Fail("Unauthorized: Invalid or missing token.", 401);
            return context.Response.WriteAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
        },
        OnForbidden = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            var response = ApiResponse.Fail("Forbidden: Insufficient permissions.", 403);
            return context.Response.WriteAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
        }
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAdmin", policy => policy.RequireRole("admin"));
});

// 6. CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("DevCors", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173", "http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// 7. Controllers & JSON Options
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.WriteIndented = true;
    });

// 8. Swagger / OpenAPI with Bearer support
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Offline-First Task Management API",
        Version = "v1",
        Description = "API for Task Management with Offline Sync, Token Revocation, ABAC, Separate Audit DB & Tamper-Proof Changelog."
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// Run DB Initialization & seed default admin for both primary and audit databases
using (var scope = app.Services.CreateScope())
{
    var dbInitializer = scope.ServiceProvider.GetRequiredService<DbInitializer>();
    await dbInitializer.InitializeAsync();

    var auditDbInitializer = scope.ServiceProvider.GetRequiredService<AuditDbInitializer>();
    await auditDbInitializer.InitializeAsync();
}

// HTTP Pipeline
app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("DevCors");

app.UseAuthentication();
app.UseMiddleware<SessionValidationMiddleware>();
app.UseMiddleware<AuditLoggingMiddleware>(); // Logs requests/responses after authentication populates claims
app.UseAuthorization();

app.MapControllers();

app.Run();
