using OneAI.Data;
using OneAI.Models;
using OneAI.Services.KiroOAuth;

namespace OneAI.Endpoints;

/// <summary>
/// Kiro OAuth/Reverse credentials endpoints
/// </summary>
public static class KiroOAuthEndpoints
{
    /// <summary>
    /// Map Kiro OAuth endpoints
    /// </summary>
    public static void MapKiroOAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/kiro/oauth")
            .WithTags("Kiro OAuth")
            .RequireAuthorization();

        group.MapPost("/import", ImportKiroCredentials)
            .WithName("ImportKiroCredentials")
            .WithSummary("导入 Kiro 凭证")
            .WithDescription("导入 Kiro 逆向凭证并创建账户")
            .Produces<ApiResponse<AIAccountDto>>(200)
            .Produces<ApiResponse>(400)
            .Produces<ApiResponse>(401)
            .Produces<ApiResponse>(500);
    }

    private static async Task<IResult> ImportKiroCredentials(
        ImportKiroCredentialsRequest request,
        KiroOAuthService kiroOAuthService,
        AppDbContext dbContext)
    {
        try
        {
            var account = await kiroOAuthService.ImportKiroCredentialsAsync(dbContext, request);

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
            }, "Kiro 凭证导入成功，账户已创建"));
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
                ApiResponse.Fail($"导入 Kiro 凭证失败: {ex.Message}", 500),
                statusCode: 500
            );
        }
    }
}
