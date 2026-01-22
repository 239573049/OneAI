using OneAI.Data;
using OneAI.Models;
using OneAI.Services.ClaudeCodeOAuth;
using OneAI.Services.OpenAIOAuth;

namespace OneAI.Endpoints;

/// <summary>
/// Claude OAuth 相关的 Minimal APIs
/// </summary>
public static class ClaudeCodeOAuthEndpoints
{
    /// <summary>
    /// 映射 Claude OAuth 相关的端点
    /// </summary>
    public static void MapClaudeCodeOAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/claude/oauth")
            .WithTags("Claude OAuth")
            .RequireAuthorization();

        // 生成 Claude OAuth 授权链接
        group.MapPost("/authorize", GenerateOAuthUrl)
            .WithName("GenerateClaudeCodeOAuthUrl")
            .WithSummary("生成 Claude OAuth 授权链接")
            .WithDescription("生成用于 Claude 授权的链接")
            .Produces<ApiResponse<object>>(200)
            .Produces<ApiResponse>(401)
            .Produces<ApiResponse>(500);

        // 处理 Claude OAuth 回调
        group.MapPost("/callback", ExchangeOAuthCode)
            .WithName("ExchangeClaudeCodeOAuthCode")
            .WithSummary("处理 Claude OAuth 授权码")
            .WithDescription("交换授权码并创建账户")
            .Produces<ApiResponse<AIAccountDto>>(200)
            .Produces<ApiResponse>(400)
            .Produces<ApiResponse>(401)
            .Produces<ApiResponse>(500);
    }

    /// <summary>
    /// 生成 Claude OAuth 授权链接
    /// </summary>
    private static IResult GenerateOAuthUrl(
        GenerateClaudeCodeOAuthUrlRequest request,
        ClaudeCodeOAuthHelper oAuthHelper,
        IOAuthSessionService sessionService,
        ClaudeCodeOAuthService authService)
    {
        try
        {
            var result = authService.GenerateClaudeCodeOAuthUrl(request, oAuthHelper, sessionService);
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
    /// 处理 Claude OAuth 授权码
    /// </summary>
    private static async Task<IResult> ExchangeOAuthCode(
        ExchangeClaudeCodeOAuthCodeRequest request,
        ClaudeCodeOAuthHelper oAuthHelper,
        IOAuthSessionService sessionService,
        ClaudeCodeOAuthService authService,
        AppDbContext dbContext)
    {
        try
        {
            var account = await authService.ExchangeClaudeCodeOAuthCode(dbContext, request, oAuthHelper, sessionService);

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
                ApiResponse.Fail($"处理授权码失败: {ex.Message}", 500),
                statusCode: 500
            );
        }
    }
}
