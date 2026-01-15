using OneAI.Models;

namespace OneAI.Services.FactoryOAuth;

/// <summary>
/// 生成 Factory OAuth Device Code 请求模型
/// </summary>
public record GenerateFactoryOAuthDeviceCodeRequest(
    ProxyConfig? Proxy = null
);

