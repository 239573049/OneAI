using System.Text;
using System.Threading.Channels;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OneAI.Data;
using OneAI.Endpoints;
using OneAI.Services;
using OneAI.Services.AI;
using OneAI.Services.Logging;
using OneAI.Services.OpenAIOAuth;
using OneAI.Services.GeminiOAuth;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Context;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

// 配置 Serilog（统一接管 ILogger，并输出请求日志）
builder.Host.UseSerilog((context, loggerConfiguration) =>
{
    loggerConfiguration
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "OneAI")
        .ReadFrom.Configuration(context.Configuration)
        .WriteTo.Console();
});

// 配置主数据库
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// 配置日志专用数据库（独立连接，避免影响主业务）
builder.Services.AddDbContext<LogDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("LogConnection") ??
                      builder.Configuration.GetConnectionString("DefaultConnection")));

// 配置日志 Channel（无界管道，确保日志不会丢失）
builder.Services.AddSingleton(Channel.CreateUnbounded<LogQueueItem>(new UnboundedChannelOptions
{
    SingleReader = true, // 单个消费者
    SingleWriter = false // 多个生产者
}));

// 配置 JWT 认证
var jwtSettings = builder.Configuration.GetSection("Jwt");
var secretKey = jwtSettings["SecretKey"] ?? throw new InvalidOperationException("JWT SecretKey is not configured");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
            ValidateIssuer = true,
            ValidIssuer = jwtSettings["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwtSettings["Audience"],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var authHeader = context.Request.Headers.Authorization.ToString();
                // 如果没有 token 或 token 格式不正确，跳过验证
                if (string.IsNullOrWhiteSpace(authHeader) ||
                    !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    context.NoResult();
                    return Task.CompletedTask;
                }

                // 提取令牌
                var token = authHeader["Bearer ".Length..].Trim();

                // 检查令牌格式（JWT 应该有两个点号）
                var parts = token.Split('.');
                if (parts.Length != 3)
                {
                    // 令牌格式不正确，跳过验证
                    context.NoResult();
                    return Task.CompletedTask;
                }

                return Task.CompletedTask;
            },
            OnAuthenticationFailed = context =>
            {
                // 记录错误但不抛出异常
                // 对于不需要授权的端点，认证失败不应该返回错误
                // 授权中间件会在需要授权的端点上进行检查
                context.NoResult();
                return Task.CompletedTask;
            },
            OnChallenge = context =>
            {
                // 对于需要授权但认证失败的请求，返回 401
                context.HandleResponse();
                context.Response.StatusCode = 401;
                context.Response.ContentType = "application/json";
                return context.Response.WriteAsJsonAsync(new
                {
                    success = false,
                    message = "未授权或令牌无效",
                    code = 401
                });
            }
        };
    });

builder.Services.AddAuthorization();

// 配置内存缓存（用于账户配额跟踪）
builder.Services.AddMemoryCache();

// 注册服务
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddSingleton<IOAuthSessionService, InMemoryOAuthSessionService>();
builder.Services.AddScoped<OpenAIOAuthService>();
builder.Services.AddScoped<OpenAiOAuthHelper>();
builder.Services.AddScoped<GeminiAntigravityOAuthService>();
builder.Services.AddScoped<GeminiAntigravityOAuthHelper>();
builder.Services.AddScoped<IModelMappingService, ModelMappingService>();
builder.Services.AddSingleton<AccountQuotaCacheService>(); // 单例模式，缓存在应用生命周期内共享
builder.Services.AddScoped<AIAccountService>();
builder.Services.AddScoped<AIRequestLogService>(); // AI请求日志服务（生产者）
builder.Services.AddHostedService<AIRequestLogWriterService>(); // 日志写入后台服务（消费者）
builder.Services.AddHostedService<AIRequestAggregationBackgroundService>(); // 数据聚合后台服务
builder.Services.AddScoped<ResponsesService>();
builder.Services.AddScoped<GeminiAPIService>();
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<GeminiOAuthService>();
builder.Services.AddScoped<GeminiOAuthHelper>();
builder.Services.AddScoped<ChatCompletionsService>();
builder.Services.AddScoped<AnthropicService>();
// 配置 CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:3000")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});
builder.Services.AddHttpClient();

// 配置响应压缩（优先使用 Brotli）
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
    {
        "application/json",
        "application/javascript",
        "text/css",
        "text/html",
        "text/plain",
        "text/json"
    });
});

// 添加 OpenAPI
builder.Services.AddOpenApi();

var app = builder.Build();

// 配置中间件管道
app.Use((context, next) =>
{
    using (LogContext.PushProperty("TraceId", context.TraceIdentifier))
    using (LogContext.PushProperty("ClientIp", context.Connection.RemoteIpAddress?.ToString()))
    {
        return next();
    }
});

app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate =
        "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms (TraceId: {TraceId})";

    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("TraceId", httpContext.TraceIdentifier);
        diagnosticContext.Set("ClientIp", httpContext.Connection.RemoteIpAddress?.ToString());
        diagnosticContext.Set("UserAgent", httpContext.Request.Headers.UserAgent.ToString());
    };
});

app.UseCors();

// 启用响应压缩
app.UseResponseCompression();

// 静态文件服务（配置缓存1天）
var staticFileOptions = new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // 设置缓存1天
        ctx.Context.Response.Headers.Append("Cache-Control", "public,max-age=86400");
    }
};

app.UseStaticFiles(staticFileOptions);

app.UseAuthentication();
app.UseAuthorization();

// 配置 OpenAPI (仅开发环境)
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference("/scalar");
}

// 初始化数据库和系统设置
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var loggerContext = scope.ServiceProvider.GetRequiredService<LogDbContext>();
    var settingsService = scope.ServiceProvider.GetRequiredService<ISettingsService>();

    await dbContext.Database.MigrateAsync();
    await loggerContext.Database.MigrateAsync();

    // 初始化默认设置到数据库
    await DbInitializer.InitializeSettingsAsync(dbContext);

    // 加载设置到内存缓存
    await settingsService.InitializeAsync();
}

app.MapAIEndpoints();

app.MapAnthropicEndpoints();

// 映射认证端点
app.MapAuthEndpoints();

// 映射 AI 账户端点
app.MapAIAccountEndpoints();

// 映射 OpenAI OAuth 端点
app.MapOpenAIOAuthEndpoints();

// 映射 Gemini OAuth 端点
app.MapGeminiOAuthEndpoints();

// 映射 Gemini API 端点
app.MapGeminiAPIEndpoints();

// 映射系统设置端点
app.MapSettingsEndpoints();

// 兼容前端埋点上报，避免 404 噪音
app.MapPost("/api/event_logging/batch", () => Results.Json(new { success = true }))
    .WithName("EventLoggingBatch")
    .WithTags("系统");

// 映射日志查询端点
app.MapAIRequestLogEndpoints();

// 健康检查端点
app.MapGet("/api/health", () => Results.Json(new { status = "healthy", timestamp = DateTime.UtcNow }))
    .WithName("HealthCheck")
    .WithTags("系统");

// SPA 路由回退：所有非 API 且未匹配的请求返回 index.html
app.MapFallback(async context =>
{
    // 跳过 API 请求
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.StatusCode = 404;
        await context.Response.WriteAsJsonAsync(new
        {
            success = false,
            message = "API endpoint not found",
            code = 404
        });
        return;
    }

    // 返回 index.html 用于前端路由
    context.Response.ContentType = "text/html";
    await context.Response.SendFileAsync(Path.Combine(app.Environment.WebRootPath, "index.html"));
});

app.Run();
