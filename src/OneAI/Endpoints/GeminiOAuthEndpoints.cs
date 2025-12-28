using Microsoft.EntityFrameworkCore;
using OneAI.Data;
using OneAI.Models;
using OneAI.Services.GeminiOAuth;
using OneAI.Services.OpenAIOAuth;

namespace OneAI.Endpoints;

/// <summary>
/// Gemini OAuth 相关的 Minimal APIs
/// </summary>
public static class GeminiOAuthEndpoints
{
    /// <summary>
    /// 映射 Gemini OAuth 相关的端点
    /// </summary>
    public static void MapGeminiOAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/gemini/oauth")
            .WithTags("Gemini OAuth")
            .RequireAuthorization();

        // 生成 Gemini OAuth 授权链接
        group.MapPost("/authorize", GenerateOAuthUrl)
            .WithName("GenerateGeminiOAuthUrl")
            .WithSummary("生成 Gemini OAuth 授权链接")
            .WithDescription("生成用于 Gemini 授权的链接")
            .Produces<ApiResponse<object>>(200)
            .Produces<ApiResponse>(401)
            .Produces<ApiResponse>(500);

        // 处理 Gemini OAuth 回调
        group.MapPost("/callback", ExchangeOAuthCode)
            .WithName("ExchangeGeminiOAuthCode")
            .WithSummary("处理 Gemini OAuth 授权码")
            .WithDescription("交换授权码并创建账户")
            .Produces<ApiResponse<object>>(200)
            .Produces<ApiResponse>(400)
            .Produces<ApiResponse>(401)
            .Produces<ApiResponse>(500);
    }

    /// <summary>
    /// 生成 Gemini OAuth 授权链接
    /// </summary>
    private static IResult GenerateOAuthUrl(
        GenerateGeminiOAuthUrlRequest request,
        GeminiAntigravityOAuthHelper oAuthHelper,
        IOAuthSessionService sessionService,
        GeminiAntigravityOAuthService authService)
    {
        try
        {
            var result = authService.GenerateGeminiAntigravityOAuthUrl(request, oAuthHelper, sessionService);
            return Results.Json(ApiResponse<object>.Success(result, "授权链接生成成功"));
        }
        catch (Exception ex)
        {
            return Results.Json(
                ApiResponse.Fail($"生成授权链接失败: {ex.Message}", 500),
                statusCode: 500
            );
        }
    }

    /// <summary>
    /// 处理 Gemini OAuth 授权码
    /// </summary>
    private static async Task<IResult> ExchangeOAuthCode(
        ExchangeGeminiOAuthCodeRequest request,
        GeminiAntigravityOAuthHelper oAuthHelper,
        IOAuthSessionService sessionService,
        GeminiAntigravityOAuthService authService,
        AppDbContext dbContext)
    {
        try
        {
            // 创建账户（包含完整的项目ID自动检测逻辑）
            var account = await authService.ExchangeGeminiAntigravityOAuthCode(dbContext, request, oAuthHelper, sessionService);

            // 返回账户信息
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
                UsageCount = account.UsageCount
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
                ApiResponse.Fail($"处理授权码失败: {ex.Message}", 500),
                statusCode: 500
            );
        }
    }
}
