using Microsoft.EntityFrameworkCore;
using OneAI.Constants;
using OneAI.Data;
using OneAI.Entities;
using OneAI.Services.GeminiOAuth;

namespace OneAI.Services.OpenAIOAuth;

/// <summary>
/// 处理结果
/// </summary>
internal record ProcessResult(bool Refreshed, bool Skipped, bool Error);

/// <summary>
/// 后台服务：定期检查并刷新即将过期的 OAuth Token（支持 OpenAI 和 Gemini）
/// </summary>
public class OAuthTokenRefreshBackgroundService(
    IServiceProvider serviceProvider,
    ILogger<OAuthTokenRefreshBackgroundService> logger)
    : BackgroundService
{
    // 检查间隔：每 5 分钟检查一次
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(1);

    // 提前刷新时间：当 token 还有 1 小时过期时就开始刷新
    private readonly TimeSpan _refreshThreshold = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("OAuth Token 刷新后台服务已启动");

        // 启动后延迟 1 分钟再开始第一次检查
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndRefreshTokensAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "检查和刷新 OAuth Token 时发生错误");
            }

            // 等待下一次检查
            await Task.Delay(_checkInterval, stoppingToken);
        }

        logger.LogInformation("OAuth Token 刷新后台服务已停止");
    }

    private async Task CheckAndRefreshTokensAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var openAiOAuthService = scope.ServiceProvider.GetRequiredService<OpenAIOAuthService>();
        var geminiOAuthService = scope.ServiceProvider.GetRequiredService<GeminiOAuthService>();
        var geminiAntigravityOAuthService = scope.ServiceProvider.GetRequiredService<GeminiAntigravityOAuthService>();

        // 获取所有启用的 OAuth 账户（OpenAI 和 Gemini）
        var oauthAccounts = await dbContext.AIAccounts
            .Where(a => (a.Provider == AIProviders.OpenAI || a.Provider == AIProviders.Gemini ||
                         a.Provider == AIProviders.GeminiAntigravity) && a.IsEnabled)
            .ToListAsync(cancellationToken);

        if (!oauthAccounts.Any())
        {
            logger.LogDebug("没有找到需要检查的 OAuth 账户");
            return;
        }

        logger.LogInformation("开始检查 {Count} 个 OAuth 账户的 Token", oauthAccounts.Count);

        var refreshedCount = 0;
        var skippedCount = 0;
        var errorCount = 0;

        foreach (var account in oauthAccounts)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                ProcessResult result;
                if (account.Provider == AIProviders.OpenAI)
                {
                    result = await ProcessOpenAiAccount(account, openAiOAuthService);
                }
                else if (account.Provider == AIProviders.Gemini)
                {
                    result = await ProcessGeminiAccount(account, geminiOAuthService);
                }
                else if (account.Provider == AIProviders.GeminiAntigravity)
                {
                    result = await ProcessGeminiAntigravityAccount(account, geminiAntigravityOAuthService);
                }
                else
                {
                    continue;
                }

                refreshedCount += result.Refreshed ? 1 : 0;
                skippedCount += result.Skipped ? 1 : 0;
                errorCount += result.Error ? 1 : 0;
            }
            catch (Exception ex)
            {
                errorCount++;

                logger.LogError(
                    ex,
                    "处理 {Provider} 账户 {AccountId} ({Name}) 时发生错误",
                    account.Provider,
                    account.Id,
                    account.Name ?? "未命名");
            }
        }

        logger.LogInformation(
            "OAuth Token 检查完成 - 总数: {Total}, 刷新: {Refreshed}, 跳过: {Skipped}, 错误: {Errors}",
            oauthAccounts.Count,
            refreshedCount,
            skippedCount,
            errorCount);
    }

    private async Task<ProcessResult> ProcessOpenAiAccount(
        AIAccount account,
        OpenAIOAuthService oAuthService)
    {
        var oauthData = account.GetOpenAiOauth();

        // 如果没有 OAuth 数据或没有刷新令牌，跳过
        if (oauthData == null || string.IsNullOrEmpty(oauthData.RefreshToken))
        {
            logger.LogDebug(
                "OpenAI 账户 {AccountId} ({Name}) 没有 OAuth 数据或刷新令牌，跳过",
                account.Id,
                account.Name ?? "未命名");
            return new ProcessResult(Refreshed: false, Skipped: true, Error: false);
        }

        // 检查 token 是否即将过期
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(oauthData.ExpiresAt).UtcDateTime;
        var now = DateTime.UtcNow;
        var timeUntilExpiry = expiresAt - now;

        logger.LogDebug(
            "OpenAI 账户 {AccountId} ({Name}) Token 将在 {Minutes} 分钟后过期 (过期时间: {ExpiresAt})",
            account.Id,
            account.Name ?? "未命名",
            timeUntilExpiry.TotalMinutes,
            expiresAt);

        // 如果 token 即将过期（或已过期），则刷新
        if (timeUntilExpiry <= _refreshThreshold)
        {
            logger.LogInformation(
                "OpenAI 账户 {AccountId} ({Name}) 的 Token 即将过期，开始刷新... (剩余时间: {Minutes} 分钟)",
                account.Id,
                account.Name ?? "未命名",
                timeUntilExpiry.TotalMinutes);

            try
            {
                await oAuthService.RefreshOpenAiOAuthTokenAsync(account);

                logger.LogInformation(
                    "成功刷新 OpenAI 账户 {AccountId} ({Name}) 的 OAuth Token",
                    account.Id,
                    account.Name ?? "未命名");

                return new ProcessResult(Refreshed: true, Skipped: false, Error: false);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "刷新 OpenAI 账户 {AccountId} ({Name}) 的 OAuth Token 失败",
                    account.Id,
                    account.Name ?? "未命名");

                return new ProcessResult(Refreshed: false, Skipped: false, Error: true);
            }
        }
        else
        {
            logger.LogDebug(
                "OpenAI 账户 {AccountId} ({Name}) Token 尚未到刷新阈值，跳过 (剩余: {Minutes} 分钟, 阈值: {Threshold} 分钟)",
                account.Id,
                account.Name ?? "未命名",
                timeUntilExpiry.TotalMinutes,
                _refreshThreshold.TotalMinutes);
            return new ProcessResult(Refreshed: false, Skipped: true, Error: false);
        }
    }

    private async Task<ProcessResult> ProcessGeminiAntigravityAccount(
        AIAccount account,
        GeminiAntigravityOAuthService geminiAntigravityOAuthService)
    {
        var oauthData = account.GetGeminiOauth();

        if (oauthData == null || string.IsNullOrEmpty(oauthData.RefreshToken))
        {
            return new ProcessResult(Refreshed: false, Skipped: true, Error: false);
        }


        // 检查 token 是否即将过期
        // Gemini OAuth 存储的是 ISO 8601 格式的过期时间
        if (string.IsNullOrEmpty(oauthData.Expiry))
        {
            return new ProcessResult(Refreshed: false, Skipped: true, Error: false);
        }

        DateTime expiresAt;
        try
        {
            expiresAt = DateTime.Parse(oauthData.Expiry).ToUniversalTime();
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Gemini 账户 {AccountId} ({Name}) 过期时间格式无效: {Expiry}，跳过",
                account.Id,
                account.Name ?? "未命名",
                oauthData.Expiry);
            return new ProcessResult(Refreshed: false, Skipped: true, Error: false);
        }

        var now = DateTime.UtcNow;
        var timeUntilExpiry = expiresAt - now;

        logger.LogDebug(
            "Gemini 账户 {AccountId} ({Name}) Token 将在 {Minutes} 分钟后过期 (过期时间: {ExpiresAt})",
            account.Id,
            account.Name ?? "未命名",
            timeUntilExpiry.TotalMinutes,
            expiresAt);

        // 如果 token 即将过期（或已过期），则刷新
        if (timeUntilExpiry <= _refreshThreshold)
        {
            logger.LogInformation(
                "Gemini 账户 {AccountId} ({Name}) 的 Token 即将过期，开始刷新... (剩余时间: {Minutes} 分钟)",
                account.Id,
                account.Name ?? "未命名",
                timeUntilExpiry.TotalMinutes);

            try
            {
                await geminiAntigravityOAuthService.RefreshGeminiAntigravityOAuthTokenAsync(account);

                logger.LogInformation(
                    "成功刷新 Gemini 账户 {AccountId} ({Name}) 的 OAuth Token",
                    account.Id,
                    account.Name ?? "未命名");

                return new ProcessResult(Refreshed: true, Skipped: false, Error: false);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "刷新 Gemini 账户 {AccountId} ({Name}) 的 OAuth Token 失败",
                    account.Id,
                    account.Name ?? "未命名");

                return new ProcessResult(Refreshed: false, Skipped: false, Error: true);
            }
        }
        else
        {
            logger.LogDebug(
                "Gemini 账户 {AccountId} ({Name}) Token 尚未到刷新阈值，跳过 (剩余: {Minutes} 分钟, 阈值: {Threshold} 分钟)",
                account.Id,
                account.Name ?? "未命名",
                timeUntilExpiry.TotalMinutes,
                _refreshThreshold.TotalMinutes);
            return new ProcessResult(Refreshed: false, Skipped: true, Error: false);
        }
    }

    private async Task<ProcessResult> ProcessGeminiAccount(
        AIAccount account,
        GeminiOAuthService oAuthService)
    {
        var oauthData = account.GetGeminiOauth();

        // 如果没有 OAuth 数据或没有刷新令牌，跳过
        if (oauthData == null || string.IsNullOrEmpty(oauthData.RefreshToken))
        {
            logger.LogDebug(
                "Gemini 账户 {AccountId} ({Name}) 没有 OAuth 数据或刷新令牌，跳过",
                account.Id,
                account.Name ?? "未命名");
            return new ProcessResult(Refreshed: false, Skipped: true, Error: false);
        }

        // 检查 token 是否即将过期
        // Gemini OAuth 存储的是 ISO 8601 格式的过期时间
        if (string.IsNullOrEmpty(oauthData.Expiry))
        {
            logger.LogDebug(
                "Gemini 账户 {AccountId} ({Name}) 没有过期时间信息，跳过",
                account.Id,
                account.Name ?? "未命名");
            return new ProcessResult(Refreshed: false, Skipped: true, Error: false);
        }

        DateTime expiresAt;
        try
        {
            expiresAt = DateTime.Parse(oauthData.Expiry).ToUniversalTime();
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Gemini 账户 {AccountId} ({Name}) 过期时间格式无效: {Expiry}，跳过",
                account.Id,
                account.Name ?? "未命名",
                oauthData.Expiry);
            return new ProcessResult(Refreshed: false, Skipped: true, Error: false);
        }

        var now = DateTime.UtcNow;
        var timeUntilExpiry = expiresAt - now;

        logger.LogDebug(
            "Gemini 账户 {AccountId} ({Name}) Token 将在 {Minutes} 分钟后过期 (过期时间: {ExpiresAt})",
            account.Id,
            account.Name ?? "未命名",
            timeUntilExpiry.TotalMinutes,
            expiresAt);

        // 如果 token 即将过期（或已过期），则刷新
        if (timeUntilExpiry <= _refreshThreshold)
        {
            logger.LogInformation(
                "Gemini 账户 {AccountId} ({Name}) 的 Token 即将过期，开始刷新... (剩余时间: {Minutes} 分钟)",
                account.Id,
                account.Name ?? "未命名",
                timeUntilExpiry.TotalMinutes);

            try
            {
                await oAuthService.RefreshGeminiOAuthTokenAsync(account);

                logger.LogInformation(
                    "成功刷新 Gemini 账户 {AccountId} ({Name}) 的 OAuth Token",
                    account.Id,
                    account.Name ?? "未命名");

                return new ProcessResult(Refreshed: true, Skipped: false, Error: false);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "刷新 Gemini 账户 {AccountId} ({Name}) 的 OAuth Token 失败",
                    account.Id,
                    account.Name ?? "未命名");

                return new ProcessResult(Refreshed: false, Skipped: false, Error: true);
            }
        }
        else
        {
            logger.LogDebug(
                "Gemini 账户 {AccountId} ({Name}) Token 尚未到刷新阈值，跳过 (剩余: {Minutes} 分钟, 阈值: {Threshold} 分钟)",
                account.Id,
                account.Name ?? "未命名",
                timeUntilExpiry.TotalMinutes,
                _refreshThreshold.TotalMinutes);
            return new ProcessResult(Refreshed: false, Skipped: true, Error: false);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("正在停止 OAuth Token 刷新后台服务...");
        await base.StopAsync(cancellationToken);
    }
}