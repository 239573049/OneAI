using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OneAI.Constants;
using OneAI.Data;
using OneAI.Entities;
using OneAI.Models;
using OneAI.Services;

namespace OneAI.Endpoints;

/// <summary>
/// 系统设置端点
/// </summary>
public static class SettingsEndpoints
{
    /// <summary>
    /// 映射系统设置端点
    /// </summary>
    public static void MapSettingsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/settings")
            .WithTags("系统设置");

        // 更具体的路由必须放在前面
        // 获取 API Key 信息
        group.MapGet("/info/api-key", GetApiKeyInfo)
            .WithName("GetApiKeyInfo")
            .WithSummary("获取 API Key 信息");

        // 获取 Token 刷新信息
        group.MapGet("/info/token-refresh", GetTokenRefreshInfo)
            .WithName("GetTokenRefreshInfo")
            .WithSummary("获取 Token 刷新信息");

        // 获取所有设置
        group.MapGet("/", GetAllSettings)
            .WithName("GetAllSettings")
            .WithSummary("获取所有系统设置");

        // 获取单个设置（通用路由，放在最后）
        group.MapGet("/{key}", GetSetting)
            .WithName("GetSetting")
            .WithSummary("获取单个系统设置");

        // 更新设置
        group.MapPut("/{key}", UpdateSetting)
            .WithName("UpdateSetting")
            .WithSummary("更新系统设置")
            .RequireAuthorization();
    }

    /// <summary>
    /// 获取所有设置
    /// </summary>
    private static async Task<ApiResponse<Dictionary<string, SystemSettingsDto>>> GetAllSettings(AppDbContext dbContext)
    {
        try
        {
            var settings = await dbContext.SystemSettings
                .AsNoTracking()
                .ToListAsync();

            var dtos = settings.Select(s => new SystemSettingsDto
            {
                Key = s.Key,
                Value = s.Value,
                Description = s.Description,
                DataType = s.DataType,
                IsEditable = s.IsEditable,
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt
            }).ToList();

            var result = dtos.ToDictionary(d => d.Key);
            return ApiResponse<Dictionary<string, SystemSettingsDto>>.Success(result, "获取设置成功");
        }
        catch (Exception ex)
        {
            return ApiResponse<Dictionary<string, SystemSettingsDto>>.Fail($"获取设置失败: {ex.Message}", 500);
        }
    }

    /// <summary>
    /// 获取单个设置
    /// </summary>
    private static async Task<ApiResponse<SystemSettingsDto>> GetSetting(string key, AppDbContext dbContext)
    {
        try
        {
            var setting = await dbContext.SystemSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == key);

            if (setting == null)
                return ApiResponse<SystemSettingsDto>.Fail("设置不存在", 404);

            var dto = new SystemSettingsDto
            {
                Key = setting.Key,
                Value = setting.Value,
                Description = setting.Description,
                DataType = setting.DataType,
                IsEditable = setting.IsEditable,
                CreatedAt = setting.CreatedAt,
                UpdatedAt = setting.UpdatedAt
            };

            return ApiResponse<SystemSettingsDto>.Success(dto, "获取设置成功");
        }
        catch (Exception ex)
        {
            return ApiResponse<SystemSettingsDto>.Fail($"获取设置失败: {ex.Message}", 500);
        }
    }

    /// <summary>
    /// 更新设置
    /// </summary>
    private static async Task<ApiResponse<SystemSettingsDto>> UpdateSetting(
        string key,
        UpdateSystemSettingRequest request,
        ISettingsService settingsService,
        AppDbContext dbContext)
    {
        try
        {
            var setting = await dbContext.SystemSettings
                .FirstOrDefaultAsync(s => s.Key == key);

            // 若设置不存在则自动创建，兼容旧数据库缺少 model_mapping_rules 等键的情况
            if (setting == null)
            {
                var defaultDescription = key switch
                {
                    SettingsKeys.Model_Mapping_Rules => "模型映射规则（JSON），用于 Anthropic 与 OpenAI Chat 的模型别名映射",
                    _ => "用户自定义设置"
                };
                var defaultDataType = key switch
                {
                    SettingsKeys.Model_Mapping_Rules => "json",
                    _ => "string"
                };

                setting = new SystemSettings
                {
                    Key = key,
                    Value = request.Value,
                    Description = request.Description ?? defaultDescription,
                    DataType = defaultDataType,
                    IsEditable = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                dbContext.SystemSettings.Add(setting);
                await dbContext.SaveChangesAsync();
                await settingsService.RefreshCacheAsync();
            }
            else
            {
                if (!setting.IsEditable)
                    return ApiResponse<SystemSettingsDto>.Fail("此设置不可编辑", 400);

                await settingsService.SetSettingAsync(
                    key,
                    request.Value,
                    request.Description ?? setting.Description);

                setting = await dbContext.SystemSettings
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Key == key);
            }

            var dto = new SystemSettingsDto
            {
                Key = setting!.Key,
                Value = setting.Value,
                Description = setting.Description,
                DataType = setting.DataType,
                IsEditable = setting.IsEditable,
                CreatedAt = setting.CreatedAt,
                UpdatedAt = setting.UpdatedAt
            };

            return ApiResponse<SystemSettingsDto>.Success(dto, "更新设置成功");
        }
        catch (Exception ex)
        {
            return ApiResponse<SystemSettingsDto>.Fail($"更新设置失败: {ex.Message}", 500);
        }
    }

    /// <summary>
    /// 获取 API Key 信息（当前服务的 API Key）
    /// </summary>
    private static async Task<ApiResponse<ApiKeyInfoResponse>> GetApiKeyInfo(ISettingsService settingsService)
    {
        try
        {
            var apiKey = await settingsService.GetSettingAsync(OneAI.Constants.SettingsKeys.System_ApiKey);

            var response = new ApiKeyInfoResponse
            {
                HasApiKey = !string.IsNullOrEmpty(apiKey),
                MaskedApiKey = string.IsNullOrEmpty(apiKey) ? null : MaskApiKey(apiKey),
                ApiKeyLength = string.IsNullOrEmpty(apiKey) ? null : apiKey.Length
            };

            return ApiResponse<ApiKeyInfoResponse>.Success(response, "获取 API Key 信息成功");
        }
        catch (Exception ex)
        {
            return ApiResponse<ApiKeyInfoResponse>.Fail($"获取 API Key 信息失败: {ex.Message}", 500);
        }
    }

    /// <summary>
    /// 脱敏显示 API Key（只显示首尾字符）
    /// </summary>
    private static string MaskApiKey(string apiKey)
    {
        if (apiKey.Length <= 8)
            return new string('*', apiKey.Length);

        var firstPart = apiKey[..4];
        var lastPart = apiKey[^4..];
        var maskedPart = new string('*', apiKey.Length - 8);

        return $"{firstPart}{maskedPart}{lastPart}";
    }

    /// <summary>
    /// 获取 Token 刷新信息
    /// </summary>
    private static async Task<ApiResponse<TokenRefreshInfoResponse>> GetTokenRefreshInfo(
        HttpContext httpContext,
        ISettingsService settingsService)
    {
        try
        {
            // 从 JWT Token 中提取过期时间
            var token = httpContext.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");

            var response = new TokenRefreshInfoResponse
            {
                TokenExpiresAt = null,
                SecondsUntilExpiry = null,
                NeedsRefresh = false,
                RefreshBeforeExpiryMinutes = 5
            };

            if (!string.IsNullOrEmpty(token))
            {
                // 尝试从 token 中获取过期时间
                var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                try
                {
                    var jwtToken = handler.ReadToken(token) as System.IdentityModel.Tokens.Jwt.JwtSecurityToken;
                    if (jwtToken?.ValidTo != null)
                    {
                        response.TokenExpiresAt = jwtToken.ValidTo.ToUniversalTime();
                        var secondsUntilExpiry = (int)(jwtToken.ValidTo - DateTime.UtcNow).TotalSeconds;
                        response.SecondsUntilExpiry = Math.Max(0, secondsUntilExpiry);
                        response.NeedsRefresh = secondsUntilExpiry < (response.RefreshBeforeExpiryMinutes * 60);
                    }
                }
                catch
                {
                    // Token 无效或无法解析
                }
            }

            return ApiResponse<TokenRefreshInfoResponse>.Success(response, "获取 Token 信息成功");
        }
        catch (Exception ex)
        {
            return ApiResponse<TokenRefreshInfoResponse>.Fail($"获取 Token 信息失败: {ex.Message}", 500);
        }
    }
}
