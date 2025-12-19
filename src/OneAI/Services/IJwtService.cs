using OneAI.Entities;

namespace OneAI.Services;

/// <summary>
/// JWT 服务接口
/// </summary>
public interface IJwtService
{
    /// <summary>
    /// 生成 JWT Token
    /// </summary>
    string GenerateToken(User user);

    /// <summary>
    /// 验证 Token
    /// </summary>
    bool ValidateToken(string token);
}
