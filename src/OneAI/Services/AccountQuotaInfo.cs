namespace OneAI.Services;

/// <summary>
/// 账户配额实时信息（内存缓存）
/// 用于跟踪 OpenAI Codex API 的配额使用情况
/// </summary>
public class AccountQuotaInfo
{
    /// <summary>
    /// 账户 ID
    /// </summary>
    public int AccountId { get; set; }

    /// <summary>
    /// 计划类型（如 plus, free, pro 等）
    /// </summary>
    public string? PlanType { get; set; }

    #region 主窗口配额（短期窗口，通常为5小时）

    /// <summary>
    /// 主窗口使用百分比（0-100）
    /// </summary>
    public int PrimaryUsedPercent { get; set; }

    /// <summary>
    /// 主窗口时间长度（分钟）
    /// </summary>
    public int PrimaryWindowMinutes { get; set; }

    /// <summary>
    /// 主窗口配额重置时间戳（Unix 时间戳）
    /// </summary>
    public long PrimaryResetAt { get; set; }

    /// <summary>
    /// 主窗口配额重置剩余秒数
    /// </summary>
    public int PrimaryResetAfterSeconds { get; set; }

    #endregion

    #region 次级窗口配额（长期窗口，通常为7天）

    /// <summary>
    /// 次级窗口使用百分比（0-100）
    /// </summary>
    public int SecondaryUsedPercent { get; set; }

    /// <summary>
    /// 次级窗口时间长度（分钟）
    /// </summary>
    public int SecondaryWindowMinutes { get; set; }

    /// <summary>
    /// 次级窗口配额重置时间戳（Unix 时间戳）
    /// </summary>
    public long SecondaryResetAt { get; set; }

    /// <summary>
    /// 次级窗口配额重置剩余秒数
    /// </summary>
    public int SecondaryResetAfterSeconds { get; set; }

    #endregion

    /// <summary>
    /// 主窗口超出次级窗口限制的百分比
    /// </summary>
    public int PrimaryOverSecondaryLimitPercent { get; set; }

    #region 信用额度信息

    /// <summary>
    /// 是否有信用额度
    /// </summary>
    public bool HasCredits { get; set; }

    /// <summary>
    /// 信用额度余额
    /// </summary>
    public string? CreditsBalance { get; set; }

    /// <summary>
    /// 是否有无限信用额度
    /// </summary>
    public bool CreditsUnlimited { get; set; }

    #endregion

    #region Anthropic 风格限流信息（基于 token 数量）

    /// <summary>
    /// Input tokens 限制
    /// </summary>
    public long? InputTokensLimit { get; set; }

    /// <summary>
    /// Input tokens 剩余量
    /// </summary>
    public long? InputTokensRemaining { get; set; }

    /// <summary>
    /// Input tokens 重置时间
    /// </summary>
    public DateTime? InputTokensResetAt { get; set; }

    /// <summary>
    /// Output tokens 限制
    /// </summary>
    public long? OutputTokensLimit { get; set; }

    /// <summary>
    /// Output tokens 剩余量
    /// </summary>
    public long? OutputTokensRemaining { get; set; }

    /// <summary>
    /// Output tokens 重置时间
    /// </summary>
    public DateTime? OutputTokensResetAt { get; set; }

    /// <summary>
    /// Total tokens 限制
    /// </summary>
    public long? TokensLimit { get; set; }

    /// <summary>
    /// Total tokens 剩余量
    /// </summary>
    public long? TokensRemaining { get; set; }

    /// <summary>
    /// Total tokens 重置时间
    /// </summary>
    public DateTime? TokensResetAt { get; set; }

    #endregion

    /// <summary>
    /// 最后更新时间
    /// </summary>
    public DateTime LastUpdatedAt { get; set; }

    /// <summary>
    /// 计算账户健康度分数（0-100，越高越好）
    /// 健康度分数用于智能账户分配，优先选择健康度高的账户
    /// </summary>
    /// <returns>0-100的健康度分数</returns>
    public int GetHealthScore()
    {
        // 如果有无限信用，返回最高分
        if (CreditsUnlimited)
        {
            return 100;
        }

        // 如果有信用额度，返回较高分数
        if (HasCredits)
        {
            return 95;
        }

        // 检查是否是 Anthropic 风格的限流（基于 token 数量）
        if (TokensLimit.HasValue && TokensLimit.Value > 0)
        {
            // 计算总 token 使用率
            var tokensUsedPercent = 100 - (int)((TokensRemaining ?? 0) * 100.0 / TokensLimit.Value);
            var tokensScore = Math.Max(0, 100 - tokensUsedPercent);

            // 如果有独立的 input/output tokens 限制，也考虑进去
            if (InputTokensLimit.HasValue && InputTokensLimit.Value > 0)
            {
                var inputUsedPercent = 100 - (int)((InputTokensRemaining ?? 0) * 100.0 / InputTokensLimit.Value);
                var inputScore = Math.Max(0, 100 - inputUsedPercent);

                return (int)(tokensScore * 0.7 + inputScore * 0.3);
            }

            return tokensScore;
        }

        // OpenAI Codex 风格的限流（基于百分比）
        // 优先考虑主窗口使用率（权重70%）
        // 使用率越低，分数越高
        var primaryScore = Math.Max(0, 100 - PrimaryUsedPercent);

        // 次级窗口使用率（权重30%）
        var secondaryScore = Math.Max(0, 100 - SecondaryUsedPercent);

        // 综合评分
        var healthScore = (int)(primaryScore * 0.7 + secondaryScore * 0.3);

        return Math.Max(0, Math.Min(100, healthScore));
    }

    /// <summary>
    /// 判断配额是否已耗尽
    /// </summary>
    /// <returns>true表示配额已耗尽，不应继续使用此账户</returns>
    public bool IsQuotaExhausted()
    {
        // Anthropic 风格的限流检查
        if (TokensLimit.HasValue && TokensLimit.Value > 0)
        {
            return (TokensRemaining ?? 0) <= 0;
        }

        // OpenAI Codex 风格的限流检查
        return PrimaryUsedPercent >= 100 || SecondaryUsedPercent >= 100;
    }

    /// <summary>
    /// 判断配额信息是否已过重置时间
    /// 如果配额已经过了重置时间，说明配额已刷新，不应继续使用旧的配额数据
    /// </summary>
    /// <returns>true表示配额信息已过期（已超过重置时间）</returns>
    public bool IsExpired()
    {
        var now = DateTime.UtcNow;

        // 如果主窗口或次级窗口的重置时间有任意一个已过期，则认为配额信息已过期
        // 需要从新的API响应中获取最新的配额数据
        if (PrimaryResetAt > 0)
        {
            var primaryResetTime = DateTimeOffset.FromUnixTimeSeconds(PrimaryResetAt).UtcDateTime;
            if (now >= primaryResetTime)
            {
                return true; // 主窗口配额已重置，旧数据过期
            }
        }

        if (SecondaryResetAt > 0)
        {
            var secondaryResetTime = DateTimeOffset.FromUnixTimeSeconds(SecondaryResetAt).UtcDateTime;
            if (now >= secondaryResetTime)
            {
                return true; // 次级窗口配额已重置，旧数据过期
            }
        }

        return false;
    }

    /// <summary>
    /// 获取主窗口配额重置的DateTime
    /// </summary>
    public DateTime GetPrimaryResetDateTime()
    {
        return DateTimeOffset.FromUnixTimeSeconds(PrimaryResetAt).UtcDateTime;
    }

    /// <summary>
    /// 获取次级窗口配额重置的DateTime
    /// </summary>
    public DateTime GetSecondaryResetDateTime()
    {
        return DateTimeOffset.FromUnixTimeSeconds(SecondaryResetAt).UtcDateTime;
    }

    /// <summary>
    /// 获取配额状态描述（用于日志和监控）
    /// </summary>
    public string GetStatusDescription()
    {
        if (IsQuotaExhausted())
        {
            // Anthropic 风格
            if (TokensLimit.HasValue && TokensLimit.Value > 0)
            {
                return $"配额耗尽 - Tokens: {TokensRemaining}/{TokensLimit}";
            }

            // OpenAI Codex 风格
            return $"配额耗尽 - 主窗口: {PrimaryUsedPercent}%, 次级: {SecondaryUsedPercent}%";
        }

        if (CreditsUnlimited)
        {
            return "无限信用额度";
        }

        if (HasCredits)
        {
            return $"有信用额度 (余额: {CreditsBalance})";
        }

        // Anthropic 风格
        if (TokensLimit.HasValue && TokensLimit.Value > 0)
        {
            var tokensUsedPercent = 100 - (int)((TokensRemaining ?? 0) * 100.0 / TokensLimit.Value);
            return $"正常 - Tokens: {FormatTokens(TokensRemaining ?? 0)}/{FormatTokens(TokensLimit.Value)} ({tokensUsedPercent}% 已使用), 健康度: {GetHealthScore()}";
        }

        // OpenAI Codex 风格
        return $"正常 - 主窗口: {PrimaryUsedPercent}%, 次级: {SecondaryUsedPercent}%, 健康度: {GetHealthScore()}";
    }

    /// <summary>
    /// 格式化 token 数量为易读格式（如 1M, 1.5M 等）
    /// </summary>
    private static string FormatTokens(long tokens)
    {
        if (tokens >= 1_000_000)
        {
            return $"{tokens / 1_000_000.0:F1}M";
        }

        if (tokens >= 1_000)
        {
            return $"{tokens / 1_000.0:F1}K";
        }

        return tokens.ToString();
    }
}
