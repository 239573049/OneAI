using OneAI.Data;
using OneAI.Models;
using OneAI.Services.FactoryOAuth;
using OneAI.Services.OpenAIOAuth;

namespace OneAI.Endpoints;

/// <summary>
/// Factory OAuth 相关的 Minimal APIs（WorkOS Device Authorization Flow）
/// </summary>
public static class FactoryOAuthEndpoints
{
    /// <summary>
    /// 映射 Factory OAuth 相关的端点
    /// </summary>
    public static void MapFactoryOAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/factory/oauth")
            .WithTags("Factory OAuth")
            .RequireAuthorization();

        // 生成 Factory OAuth Device Code
        group.MapPost("/authorize", GenerateDeviceCode)
            .WithName("GenerateFactoryOAuthDeviceCode")
            .WithSummary("生成 Factory OAuth Device Code")
            .WithDescription("生成用于 Factory（WorkOS）设备码授权的验证码与链接")
            .Produces<ApiResponse<object>>(200)
            .Produces<ApiResponse>(401)
            .Produces<ApiResponse>(500);

        // 完成设备码授权并创建账户
        group.MapPost("/callback", ExchangeDeviceCode)
            .WithName("ExchangeFactoryOAuthDeviceCode")
            .WithSummary("完成 Factory OAuth 设备码授权")
            .WithDescription("轮询设备码授权状态，获取 Token 并创建账户")
            .Produces<ApiResponse<AIAccountDto>>(200)
            .Produces<ApiResponse>(400)
            .Produces<ApiResponse>(401)
            .Produces<ApiResponse>(500);
    }

    private static async Task<IResult> GenerateDeviceCode(
        GenerateFactoryOAuthDeviceCodeRequest request,
        IOAuthSessionService sessionService,
        FactoryOAuthService authService)
    {
        try
        {
            var result = await authService.GenerateFactoryOAuthDeviceCode(request, sessionService);
            return Results.Json(ApiResponse<object>.Success(result, "Device Code 生成成功"));
        }
        catch (Exception ex)
        {
            return Results.Json(
                ApiResponse.Fail($"生成 Device Code 失败: {ex.Message}", 500),
                statusCode: 500
            );
        }
    }

    private static async Task<IResult> ExchangeDeviceCode(
        ExchangeFactoryOAuthDeviceCodeRequest request,
        IOAuthSessionService sessionService,
        FactoryOAuthService authService,
        AppDbContext dbContext)
    {
        try
        {
            var account = await authService.ExchangeFactoryOAuthDeviceCode(dbContext, request, sessionService);

            return Results.Json(ApiResponse<AIAccountDto>.Success(new AIAccountDto
            {
                Id = account.Id,
                Provider = account.Provider,
                Name = account.Name,
                Email = account.Email,
                BaseUrl = account.BaseUrl,
                IsEnabled = account.IsEnabled,
                IsRateLimited = account.IsRateLimited,
                RateLimitResetTime = account.RateLimitResetTime,
                CreatedAt = account.CreatedAt,
                UpdatedAt = account.UpdatedAt,
                LastUsedAt = account.LastUsedAt,
                UsageCount = account.UsageCount,
                PromptTokens = account.PromptTokens,
                CompletionTokens = account.CompletionTokens,
                CacheTokens = account.CacheTokens,
                CreateCacheTokens = account.CreateCacheTokens
            }, "OAuth 认证成功，账户已创建"));
        }
        catch (ArgumentException ex)
        {
            return Results.Json(
                ApiResponse.Fail(ex.Message, 400),
                statusCode: 400
            );
        }
        catch (Exception ex)
        {
            return Results.Json(
                ApiResponse.Fail($"处理授权失败: {ex.Message}", 500),
                statusCode: 500
            );
        }
    }
}
