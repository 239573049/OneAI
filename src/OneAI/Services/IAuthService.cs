using OneAI.Entities;

namespace OneAI.Services;

/// <summary>
/// 认证服务接口
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// 验证用户登录
    /// </summary>
    Task<User?> ValidateUserAsync(string username, string password);

    /// <summary>
    /// 验证密码
    /// </summary>
    bool VerifyPassword(string password, string passwordHash);

    /// <summary>
    /// 哈希密码
    /// </summary>
    string HashPassword(string password);
}
