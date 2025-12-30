using Microsoft.EntityFrameworkCore;
using OneAI.Constants;
using OneAI.Entities;

namespace OneAI.Data;

/// <summary>
/// 数据库初始化器 - 预置系统设置
/// </summary>
public static class DbInitializer
{
    /// <summary>
    /// 初始化默认设置
    /// </summary>
    public static async Task InitializeSettingsAsync(AppDbContext dbContext)
    {
        // 检查是否已有设置
        if (await dbContext.SystemSettings.AnyAsync())
        {
            var hasMapping = await dbContext.SystemSettings
                .AsNoTracking()
                .AnyAsync(s => s.Key == SettingsKeys.Model_Mapping_Rules);
            if (!hasMapping)
            {
                dbContext.SystemSettings.Add(new SystemSettings
                {
                    Key = SettingsKeys.Model_Mapping_Rules,
                    Value = "{\"anthropic\":[],\"openai_chat\":[]}",
                    Description = "模型映射规则（JSON），用于 Anthropic 与 OpenAI Chat 的模型别名映射",
                    DataType = "json",
                    IsEditable = true,
                    CreatedAt = DateTime.UtcNow
                });
                await dbContext.SaveChangesAsync();
            }
            return; // 已初始化，跳过
        }

        var settings = new List<SystemSettings>
        {
            // OAuth 设置
            new SystemSettings
            {
                Key = SettingsKeys.OAuth_OpenAI_ClientId,
                Value = null,
                Description = "OpenAI OAuth 应用的 Client ID",
                DataType = "string",
                IsEditable = true,
                CreatedAt = DateTime.UtcNow
            },
            new SystemSettings
            {
                Key = SettingsKeys.OAuth_OpenAI_ClientSecret,
                Value = null,
                Description = "OpenAI OAuth 应用的 Client Secret",
                DataType = "string",
                IsEditable = true,
                CreatedAt = DateTime.UtcNow
            },
            new SystemSettings
            {
                Key = SettingsKeys.OAuth_OpenAI_RedirectUri,
                Value = "http://localhost:8080/auth/callback",
                Description = "OpenAI OAuth 回调地址",
                DataType = "string",
                IsEditable = true,
                CreatedAt = DateTime.UtcNow
            },

            // API Key 验证设置
            new SystemSettings
            {
                Key = SettingsKeys.ApiKey_MinLength,
                Value = "10",
                Description = "API Key 最小长度",
                DataType = "int",
                IsEditable = true,
                CreatedAt = DateTime.UtcNow
            },
            new SystemSettings
            {
                Key = SettingsKeys.ApiKey_MaxLength,
                Value = "500",
                Description = "API Key 最大长度",
                DataType = "int",
                IsEditable = true,
                CreatedAt = DateTime.UtcNow
            },
            new SystemSettings
            {
                Key = SettingsKeys.ApiKey_PrefixPattern,
                Value = @"^[a-zA-Z0-9_\-]+$",
                Description = "API Key 前缀验证正则表达式",
                DataType = "string",
                IsEditable = true,
                CreatedAt = DateTime.UtcNow
            },

            // Token 刷新设置
            new SystemSettings
            {
                Key = SettingsKeys.Token_RefreshBeforeExpiryMinutes,
                Value = "5",
                Description = "Token 过期前多少分钟触发刷新",
                DataType = "int",
                IsEditable = true,
                CreatedAt = DateTime.UtcNow
            },

            // 系统设置
            new SystemSettings
            {
                Key = SettingsKeys.System_Enabled,
                Value = "true",
                Description = "系统是否启用",
                DataType = "bool",
                IsEditable = true,
                CreatedAt = DateTime.UtcNow
            },
            new SystemSettings
            {
                Key = SettingsKeys.System_ApiKey,
                Value = null,
                Description = "当前服务的 API Key（用于其他服务调用认证）",
                DataType = "string",
                IsEditable = true,
                CreatedAt = DateTime.UtcNow
            },
            new SystemSettings
            {
                Key = SettingsKeys.System_ServiceName,
                Value = "OneAI",
                Description = "服务名称",
                DataType = "string",
                IsEditable = true,
                CreatedAt = DateTime.UtcNow
            },
            new SystemSettings
            {
                Key = SettingsKeys.Model_Mapping_Rules,
                Value = "{\"anthropic\":[],\"openai_chat\":[]}",
                Description = "模型映射规则（JSON），用于 Anthropic 与 OpenAI Chat 的模型别名映射",
                DataType = "json",
                IsEditable = true,
                CreatedAt = DateTime.UtcNow
            }
        };

        await dbContext.SystemSettings.AddRangeAsync(settings);
        await dbContext.SaveChangesAsync();
    }
}
