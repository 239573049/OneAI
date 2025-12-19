namespace OneAI.Models;

/// <summary>
/// 登录响应 DTO
/// </summary>
public class LoginResponse
{
    /// <summary>
    /// JWT Token
    /// </summary>
    public required string Token { get; set; }

    /// <summary>
    /// 用户信息
    /// </summary>
    public required UserDto User { get; set; }
}

/// <summary>
/// 用户信息 DTO
/// </summary>
public class UserDto
{
    /// <summary>
    /// 用户 ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 用户名
    /// </summary>
    public required string Username { get; set; }

    /// <summary>
    /// 邮箱
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// 角色
    /// </summary>
    public required string Role { get; set; }
}
