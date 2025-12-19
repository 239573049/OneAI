using Microsoft.AspNetCore.Mvc;
using OneAI.Models;
using OneAI.Services;

namespace OneAI.Endpoints;

/// <summary>
/// 认证相关的 Minimal APIs
/// </summary>
public static class AuthEndpoints
{
    /// <summary>
    /// 映射认证相关的端点
    /// </summary>
    public static void MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth")
            .WithTags("认证");

        // 登录接口
        group.MapPost("/login", Login)
            .WithName("Login")
            .WithSummary("用户登录")
            .WithDescription("使用用户名和密码登录系统，返回 JWT Token")
            .Produces<ApiResponse<LoginResponse>>(200)
            .Produces<ApiResponse>(400)
            .Produces<ApiResponse>(401);

        // 获取当前用户信息接口
        group.MapPost("/me", GetCurrentUser)
            .WithName("GetCurrentUser")
            .WithSummary("获取当前用户信息")
            .WithDescription("获取当前登录用户的详细信息")
            .RequireAuthorization()
            .Produces<ApiResponse<UserDto>>(200)
            .Produces<ApiResponse>(401);
    }

    /// <summary>
    /// 登录接口处理方法
    /// </summary>
    private static async Task<IResult> Login(
        [FromBody] LoginRequest request,
        [FromServices] IAuthService authService,
        [FromServices] IJwtService jwtService)
    {
        try
        {
            // 验证用户
            var user = await authService.ValidateUserAsync(request.Username, request.Password);

            if (user == null)
            {
                return Results.Json(
                    ApiResponse.Fail("用户名或密码错误", 401),
                    statusCode: 401
                );
            }

            // 生成 JWT Token
            var token = jwtService.GenerateToken(user);

            // 返回登录响应
            var response = new LoginResponse
            {
                Token = token,
                User = new UserDto
                {
                    Id = user.Id,
                    Username = user.Username,
                    Email = user.Email,
                    Role = user.Role
                }
            };

            return Results.Json(ApiResponse<LoginResponse>.Success(response, "登录成功"));
        }
        catch (Exception ex)
        {
            return Results.Json(
                ApiResponse.Fail($"登录失败: {ex.Message}", 500),
                statusCode: 500
            );
        }
    }

    /// <summary>
    /// 获取当前用户信息
    /// </summary>
    private static IResult GetCurrentUser(HttpContext httpContext)
    {
        try
        {
            var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var username = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
            var role = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(username))
            {
                return Results.Json(
                    ApiResponse.Fail("未找到用户信息", 401),
                    statusCode: 401
                );
            }

            var userDto = new UserDto
            {
                Id = int.Parse(userId),
                Username = username,
                Role = role ?? "User"
            };

            return Results.Json(ApiResponse<UserDto>.Success(userDto));
        }
        catch (Exception ex)
        {
            return Results.Json(
                ApiResponse.Fail($"获取用户信息失败: {ex.Message}", 500),
                statusCode: 500
            );
        }
    }
}
