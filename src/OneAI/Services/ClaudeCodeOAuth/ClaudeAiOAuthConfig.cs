namespace OneAI.Services.ClaudeCodeOAuth;

public class ClaudeAiOAuthConfig
{
    public const string AuthorizeUrl = "https://claude.ai/oauth/authorize";
    
    public const string TokenUrl = "https://console.anthropic.com/v1/oauth/token";

    // 部分地区/出口可能会被 console.anthropic.com 的 Cloudflare Challenge 拦截，可尝试该备用端点（如仍失败通常需要代理）
    public const string TokenUrlFallback = "https://api.anthropic.com/oauth/token";
    
    public const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";
    
    public const string RedirectUri = "https://console.anthropic.com/oauth/code/callback";
    
    public const string Scopes = "org:create_api_key user:profile user:inference";
}
