using OneAI.Entities;

namespace OneAI.Services;

/// <summary>
/// 认证服务实现
/// </summary>
public class AuthService : IAuthService
{
    private readonly IConfiguration _configuration;

    public AuthService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// 验证用户登录（仅从配置文件验证）
    /// </summary>
    public Task<User?> ValidateUserAsync(string username, string password)
    {
        // 从配置文件中读取账户信息
        var configUsername = _configuration["AdminAccount:Username"];
        var configPassword = _configuration["AdminAccount:Password"];

        // 验证用户名和密码
        if (username == configUsername && password == configPassword)
        {
            // 创建虚拟用户对象
            var user = new User
            {
                Id = 1,
                Username = configUsername ?? "admin",
                PasswordHash = string.Empty, // 不需要存储密码哈希
                Role = "Admin",
                IsEnabled = true,
                CreatedAt = DateTime.UtcNow,
                LastLoginAt = DateTime.UtcNow
            };

            return Task.FromResult<User?>(user);
        }

        return Task.FromResult<User?>(null);
    }

    /// <summary>
    /// 验证密码（不需要实现）
    /// </summary>
    public bool VerifyPassword(string password, string passwordHash)
    {
        return false;
    }

    /// <summary>
    /// 哈希密码（不需要实现）
    /// </summary>
    public string HashPassword(string password)
    {
        return string.Empty;
    }
}
