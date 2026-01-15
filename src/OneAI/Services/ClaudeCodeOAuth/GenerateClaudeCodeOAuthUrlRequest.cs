using OneAI.Models;

namespace OneAI.Services.ClaudeCodeOAuth;

/// <summary>
/// 生成 Claude Code OAuth 授权URL请求模型
/// </summary>
public record GenerateClaudeCodeOAuthUrlRequest(
    ProxyConfig? Proxy = null
);

