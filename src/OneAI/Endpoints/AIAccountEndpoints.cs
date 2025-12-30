using OneAI.Models;
using OneAI.Services;

namespace OneAI.Endpoints;

/// <summary>
/// AI 账户相关的 Minimal APIs
/// </summary>
public static class AIAccountEndpoints
{
    /// <summary>
    /// 映射 AI 账户相关的端点
    /// </summary>
    public static void MapAIAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/accounts")
            .WithTags("AI账户")
            .RequireAuthorization();

        // 获取 AI 账户列表
        group.MapGet("/", GetAIAccounts)
            .WithName("GetAIAccounts")
            .WithSummary("获取AI账户列表")
            .WithDescription("获取所有AI账户的列表")
            .Produces<ApiResponse<List<AIAccountDto>>>(200)
            .Produces<ApiResponse>(401)
            .Produces<ApiResponse>(500);

        // 删除 AI 账户
        group.MapDelete("/{id}", DeleteAIAccount)
            .WithName("DeleteAIAccount")
            .WithSummary("删除AI账户")
            .WithDescription("根据ID删除指定的AI账户")
            .Produces<ApiResponse>(200)
            .Produces<ApiResponse>(401)
            .Produces<ApiResponse>(404)
            .Produces<ApiResponse>(500);

        // 启用/禁用 AI 账户
        group.MapPatch("/{id}/toggle-status", ToggleAccountStatus)
            .WithName("ToggleAccountStatus")
            .WithSummary("启用/禁用AI账户")
            .WithDescription("切换AI账户的启用/禁用状态")
            .Produces<ApiResponse<AIAccountDto>>(200)
            .Produces<ApiResponse>(401)
            .Produces<ApiResponse>(404)
            .Produces<ApiResponse>(500);

        // 批量获取账户配额状态
        group.MapPost("/quota-statuses", GetAccountQuotaStatuses)
            .WithName("GetAccountQuotaStatuses")
            .WithSummary("批量获取账户配额状态")
            .WithDescription("从缓存中批量获取账户的配额状态（健康度、限流时间等）")
            .Produces<ApiResponse<Dictionary<int, AccountQuotaStatusDto>>>(200)
            .Produces<ApiResponse>(401)
            .Produces<ApiResponse>(500);

        // 刷新 OpenAI 账户配额状态
        group.MapPost("/{id}/refresh-openai-quota", RefreshOpenAIQuotaStatus)
            .WithName("RefreshOpenAIQuotaStatus")
            .WithSummary("刷新OpenAI账户配额状态")
            .WithDescription("手动刷新指定OpenAI账户的配额状态信息")
            .Produces<ApiResponse<AccountQuotaStatusDto>>(200)
            .Produces<ApiResponse>(401)
            .Produces<ApiResponse>(404)
            .Produces<ApiResponse>(500);

        // 刷新 Gemini Antigravity 账户配额状态
        group.MapPost("/{id}/refresh-antigravity-quota", RefreshAntigravityQuotaStatus)
            .WithName("RefreshAntigravityQuotaStatus")
            .WithSummary("刷新Gemini Antigravity账户配额状态")
            .WithDescription("手动刷新指定Gemini Antigravity账户的配额状态信息")
            .Produces<ApiResponse<AccountQuotaStatusDto>>(200)
            .Produces<ApiResponse>(401)
            .Produces<ApiResponse>(404)
            .Produces<ApiResponse>(500);

        // 获取 Gemini Antigravity 可用模型列表
        group.MapGet("/{id}/antigravity-models", GetAntigravityModels)
            .WithName("GetAntigravityModels")
            .WithSummary("获取Gemini Antigravity可用模型列表")
            .WithDescription("获取指定Gemini Antigravity账户的可用模型列表")
            .Produces<ApiResponse<List<string>>>(200)
            .Produces<ApiResponse>(401)
            .Produces<ApiResponse>(404)
            .Produces<ApiResponse>(500);
    }

    /// <summary>
    /// 获取 AI 账户列表
    /// </summary>
    private static async Task<ApiResponse<List<AIAccountDto>>> GetAIAccounts(AIAccountService accountService)
    {
        try
        {
            var accounts = await accountService.GetAllAccountsAsync();
            return ApiResponse<List<AIAccountDto>>.Success(accounts, "获取列表成功");
        }
        catch (Exception ex)
        {
            return ApiResponse<List<AIAccountDto>>.Fail($"获取列表失败: {ex.Message}", 500);
        }
    }

    /// <summary>
    /// 删除 AI 账户
    /// </summary>
    private static async Task<ApiResponse> DeleteAIAccount(int id, AIAccountService accountService)
    {
        try
        {
            var result = await accountService.DeleteAccountAsync(id);
            if (!result)
            {
                return ApiResponse.Fail("账户不存在", 404);
            }

            return ApiResponse.Success("删除成功");
        }
        catch (Exception ex)
        {
            return ApiResponse.Fail($"删除失败: {ex.Message}", 500);
        }
    }

    /// <summary>
    /// 切换 AI 账户的启用/禁用状态
    /// </summary>
    private static async Task<ApiResponse<AIAccountDto>> ToggleAccountStatus(int id, AIAccountService accountService)
    {
        try
        {
            var accountDto = await accountService.ToggleAccountStatusAsync(id);
            if (accountDto == null)
            {
                return ApiResponse<AIAccountDto>.Fail("账户不存在", 404);
            }

            return ApiResponse<AIAccountDto>.Success(accountDto, "状态更新成功");
        }
        catch (Exception ex)
        {
            return ApiResponse<AIAccountDto>.Fail($"更新状态失败: {ex.Message}", 500);
        }
    }

    /// <summary>
    /// 批量获取账户配额状态
    /// </summary>
    private static ApiResponse<Dictionary<int, AccountQuotaStatusDto>> GetAccountQuotaStatuses(
        List<int> accountIds,
        AIAccountService accountService)
    {
        try
        {
            var statuses = accountService.GetAccountQuotaStatuses(accountIds);
            return ApiResponse<Dictionary<int, AccountQuotaStatusDto>>.Success(statuses, "获取配额状态成功");
        }
        catch (Exception ex)
        {
            return ApiResponse<Dictionary<int, AccountQuotaStatusDto>>.Fail($"获取配额状态失败: {ex.Message}", 500);
        }
    }

    /// <summary>
    /// 刷新 OpenAI 账户配额状态
    /// </summary>
    private static async Task<ApiResponse<AccountQuotaStatusDto>> RefreshOpenAIQuotaStatus(
        int id,
        AIAccountService accountService)
    {
        try
        {
            var status = await accountService.RefreshOpenAIQuotaStatusAsync(id);
            if (status == null)
            {
                return ApiResponse<AccountQuotaStatusDto>.Fail("账户不存在或刷新失败", 404);
            }

            return ApiResponse<AccountQuotaStatusDto>.Success(status, "刷新配额状态成功");
        }
        catch (Exception ex)
        {
            return ApiResponse<AccountQuotaStatusDto>.Fail($"刷新配额状态失败: {ex.Message}", 500);
        }
    }

    /// <summary>
    /// 刷新 Gemini Antigravity 账户配额状态
    /// </summary>
    private static async Task<ApiResponse<AccountQuotaStatusDto>> RefreshAntigravityQuotaStatus(
        int id,
        AIAccountService accountService)
    {
        try
        {
            var status = await accountService.RefreshAntigravityQuotaStatusAsync(id);
            if (status == null)
            {
                return ApiResponse<AccountQuotaStatusDto>.Fail("账户不存在或刷新失败", 404);
            }

            return ApiResponse<AccountQuotaStatusDto>.Success(status, "刷新配额状态成功");
        }
        catch (Exception ex)
        {
            return ApiResponse<AccountQuotaStatusDto>.Fail($"刷新配额状态失败: {ex.Message}", 500);
        }
    }

    /// <summary>
    /// 获取 Gemini Antigravity 可用模型列表
    /// </summary>
    private static async Task<ApiResponse<List<string>>> GetAntigravityModels(
        int id,
        AIAccountService accountService)
    {
        try
        {
            var models = await accountService.GetAntigravityAvailableModelsAsync(id);
            if (models == null)
            {
                return ApiResponse<List<string>>.Fail("账户不存在或获取模型失败", 404);
            }

            return ApiResponse<List<string>>.Success(models, "获取模型列表成功");
        }
        catch (Exception ex)
        {
            return ApiResponse<List<string>>.Fail($"获取模型列表失败: {ex.Message}", 500);
        }
    }
}
