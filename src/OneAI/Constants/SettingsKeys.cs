namespace OneAI.Constants;

/// <summary>
/// 系统设置 Key 常量
/// </summary>
public static class SettingsKeys
{
    /// <summary>
    /// OAuth 配置 - OpenAI Client ID
    /// </summary>
    public const string OAuth_OpenAI_ClientId = "oauth_openai_client_id";

    /// <summary>
    /// OAuth 配置 - OpenAI Client Secret
    /// </summary>
    public const string OAuth_OpenAI_ClientSecret = "oauth_openai_client_secret";

    /// <summary>
    /// OAuth 配置 - OpenAI Redirect URI
    /// </summary>
    public const string OAuth_OpenAI_RedirectUri = "oauth_openai_redirect_uri";

    /// <summary>
    /// API Key 格式验证 - 最小长度
    /// </summary>
    public const string ApiKey_MinLength = "apikey_min_length";

    /// <summary>
    /// API Key 格式验证 - 最大长度
    /// </summary>
    public const string ApiKey_MaxLength = "apikey_max_length";

    /// <summary>
    /// API Key 格式验证 - 前缀规则（正则表达式）
    /// </summary>
    public const string ApiKey_PrefixPattern = "apikey_prefix_pattern";

    /// <summary>
    /// Token 刷新配置 - 提前刷新的时间（分钟）
    /// </summary>
    public const string Token_RefreshBeforeExpiryMinutes = "token_refresh_before_expiry_minutes";

    /// <summary>
    /// 系统设置 - 是否启用
    /// </summary>
    public const string System_Enabled = "system_enabled";

    /// <summary>
    /// 系统设置 - 服务 API Key（用于其他服务调用时的认证）
    /// </summary>
    public const string System_ApiKey = "system_api_key";

    /// <summary>
    /// 系统设置 - 服务名称
    /// </summary>
    public const string System_ServiceName = "system_service_name";

    /// <summary>
    /// 模型映射规则（JSON）
    /// </summary>
    public const string Model_Mapping_Rules = "model_mapping_rules";
}
