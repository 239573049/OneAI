using OneAI.Data;
using OneAI.Models;
using OneAI.Services.GeminiBusinessOAuth;

namespace OneAI.Endpoints;

/// <summary>
/// Gemini Business credentials endpoints
/// </summary>
public static class GeminiBusinessOAuthEndpoints
{
    /// <summary>
    /// Map Gemini Business credentials endpoints
    /// </summary>
    public static void MapGeminiBusinessOAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/gemini-business/oauth")
            .WithTags("Gemini Business OAuth")
            .RequireAuthorization();

        group.MapPost("/import", ImportGeminiBusinessCredentials)
            .WithName("ImportGeminiBusinessCredentials")
            .WithSummary("导入 Gemini Business 凭证")
            .WithDescription("导入 Gemini Business 逆向凭证并创建账户")
            .Produces<ApiResponse<AIAccountDto>>(200)
            .Produces<ApiResponse>(400)
            .Produces<ApiResponse>(401)
            .Produces<ApiResponse>(500);

        group.MapPost("/import/batch", ImportGeminiBusinessBatch)
            .WithName("ImportGeminiBusinessBatch")
            .WithSummary("批量导入 Gemini Business 凭证")
            .WithDescription("批量导入多个 Gemini Business 逆向凭证并创建账户")
            .Produces<ApiResponse<ImportGeminiBusinessBatchResult>>(200)
            .Produces<ApiResponse>(400)
            .Produces<ApiResponse>(401)
            .Produces<ApiResponse>(500);
    }

    private static async Task<IResult> ImportGeminiBusinessCredentials(
        ImportGeminiBusinessCredentialsRequest request,
        GeminiBusinessOAuthService geminiBusinessOAuthService,
        AppDbContext dbContext)
    {
        try
        {
            var account = await geminiBusinessOAuthService.ImportGeminiBusinessCredentialsAsync(dbContext, request);

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
            }, "Gemini Business 凭证导入成功，账户已创建"));
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
                ApiResponse.Fail($"导入 Gemini Business 凭证失败: {ex.Message}", 500),
                statusCode: 500
            );
        }
    }

    private static async Task<IResult> ImportGeminiBusinessBatch(
        ImportGeminiBusinessBatchRequest request,
        GeminiBusinessOAuthService geminiBusinessOAuthService,
        AppDbContext dbContext)
    {
        try
        {
            if (request.Accounts == null || request.Accounts.Count == 0)
            {
                return Results.Json(
                    ApiResponse.Fail("批量导入列表不能为空", 400),
                    statusCode: 400
                );
            }

            var result = await geminiBusinessOAuthService.ImportGeminiBusinessBatchAsync(dbContext, request);

            return Results.Json(ApiResponse<ImportGeminiBusinessBatchResult>.Success(result,
                $"批量导入完成：成功 {result.SuccessCount} 个，失败 {result.FailCount} 个，跳过 {result.SkippedCount} 个"));
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
                ApiResponse.Fail($"批量导入 Gemini Business 凭证失败: {ex.Message}", 500),
                statusCode: 500
            );
        }
    }
}
