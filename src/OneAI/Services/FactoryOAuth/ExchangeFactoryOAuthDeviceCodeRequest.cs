using OneAI.Models;

namespace OneAI.Services.FactoryOAuth;

/// <summary>
/// 完成 Factory OAuth Device Code 授权请求模型
/// </summary>
public record ExchangeFactoryOAuthDeviceCodeRequest(
    string SessionId, // 用于从缓存中获取 Device Code 会话数据
    string? AccountName = null,
    string? Description = null,
    string? AccountType = null,
    int? Priority = null,
    ProxyConfig? Proxy = null
);

