using OneAI.Models;

namespace OneAI.Services.ClaudeCodeOAuth;

/// <summary>
/// 处理 Claude Code OAuth 授权码请求模型
/// </summary>
public record ExchangeClaudeCodeOAuthCodeRequest(
    string AuthorizationCode,
    string SessionId, // 用于从缓存中获取OAuth会话数据
    string? AccountName = null,
    string? Description = null,
    string? AccountType = null,
    int? Priority = null,
    ProxyConfig? Proxy = null
);

