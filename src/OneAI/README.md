# OneAI 后端 API

基于 .NET 10 + Minimal APIs + EF Core + SQLite 构建的现代化后端服务。

## 技术栈

- **.NET 10** - 最新的 .NET 框架
- **Minimal APIs** - 轻量级 API 实现
- **Entity Framework Core 10** - ORM 框架
- **SQLite** - 轻量级数据库
- **JWT Bearer Authentication** - 身份认证
- **Scalar** - API 文档工具

## 快速开始

### 前置要求

- .NET 10 SDK

### 运行项目

```bash
cd src/OneAI
dotnet run
```

默认端口：`http://localhost:5000` 或 `https://localhost:5001`

### API 文档

启动项目后访问：
- Scalar 文档: `http://localhost:5000/scalar`
- OpenAPI JSON: `http://localhost:5000/openapi/v1.json`

## 项目结构

```
OneAI/
├── Data/                 # 数据访问层
│   └── AppDbContext.cs  # EF Core DbContext
├── Entities/            # 数据实体
│   ├── User.cs         # 用户实体（仅用于 JWT）
│   └── AIAccount.cs    # AI 账户实体
├── Services/            # 业务服务层
│   ├── IAuthService.cs
│   ├── AuthService.cs  # 认证服务
│   ├── IJwtService.cs
│   └── JwtService.cs   # JWT 服务
├── Models/              # DTO 模型
│   ├── LoginRequest.cs
│   ├── LoginResponse.cs
│   └── ApiResponse.cs
├── Endpoints/           # Minimal API 端点
│   └── AuthEndpoints.cs
├── appsettings.json     # 配置文件
└── Program.cs           # 应用入口

```

## 配置说明

### appsettings.json

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=oneai.db"
  },
  "Jwt": {
    "SecretKey": "YourSuperSecretKeyForJWTTokenGeneration2024!",
    "Issuer": "OneAI",
    "Audience": "OneAI.Client",
    "ExpirationMinutes": 1440
  },
  "AdminAccount": {
    "Username": "admin",
    "Password": "admin123"
  }
}
```

### 配置项说明

- **ConnectionStrings:DefaultConnection** - SQLite 数据库连接字符串
- **Jwt:SecretKey** - JWT 签名密钥（生产环境请修改）
- **Jwt:Issuer** - JWT 颁发者
- **Jwt:Audience** - JWT 受众
- **Jwt:ExpirationMinutes** - Token 过期时间（分钟）
- **AdminAccount:Username** - 管理员用户名（从配置文件读取）
- **AdminAccount:Password** - 管理员密码（从配置文件读取）

## API 接口

### 1. 登录接口

**POST** `/api/auth/login`

请求体：
```json
{
  "username": "admin",
  "password": "admin123"
}
```

成功响应：
```json
{
  "code": 0,
  "message": "登录成功",
  "data": {
    "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
    "user": {
      "id": 1,
      "username": "admin",
      "email": null,
      "role": "Admin"
    }
  }
}
```

失败响应：
```json
{
  "code": 401,
  "message": "用户名或密码错误"
}
```

### 2. 获取当前用户信息

**POST** `/api/auth/me`

请求头：
```
Authorization: Bearer {token}
```

成功响应：
```json
{
  "code": 0,
  "message": "操作成功",
  "data": {
    "id": 1,
    "username": "admin",
    "email": null,
    "role": "Admin"
  }
}
```

### 3. 健康检查

**GET** `/api/health`

响应：
```json
{
  "status": "healthy",
  "timestamp": "2024-01-01T00:00:00.000Z"
}
```

## 认证机制

### JWT Token

- 使用 JWT Bearer 认证
- Token 有效期：24小时（可配置）
- Token 包含用户 ID、用户名和角色信息

### 使用 Token

在请求头中添加：
```
Authorization: Bearer {your-jwt-token}
```

## 数据库

### SQLite

- 数据库文件：`oneai.db`（在项目根目录）
- 自动创建：项目启动时自动创建数据库

### 实体

#### AIAccount（AI 账户）
- Id: 主键
- Provider: AI 提供商（如 OpenAI, Claude 等）
- ApiKey: API 密钥
- Name: 账户名称/备注
- BaseUrl: 基础 URL（可选）
- IsEnabled: 是否启用
- CreatedAt: 创建时间
- UpdatedAt: 更新时间

## CORS 配置

已配置允许以下来源：
- `http://localhost:5173` (Vite 默认端口)
- `http://localhost:3000` (React 常用端口)

如需添加其他来源，请修改 [Program.cs](Program.cs:47)。

## 开发指南

### 添加新的 Minimal API 端点

1. 在 `Endpoints/` 目录创建新的端点类
2. 实现端点映射方法
3. 在 `Program.cs` 中调用映射方法

示例：
```csharp
public static class MyEndpoints
{
    public static void MapMyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/my")
            .WithTags("我的功能");

        group.MapGet("/test", () => Results.Ok("Hello"));
    }
}
```

### 添加新服务

1. 在 `Services/` 创建接口和实现
2. 在 `Program.cs` 中注册服务

```csharp
builder.Services.AddScoped<IMyService, MyService>();
```

### 添加新实体

1. 在 `Entities/` 创建实体类
2. 在 `AppDbContext.cs` 中添加 DbSet
3. 配置实体（如需要）
4. 重新创建数据库（开发环境）

## 安全注意事项

1. **修改 JWT SecretKey**
   - 生产环境必须使用强密钥
   - 建议使用环境变量或密钥管理服务

2. **修改管理员密码**
   - 默认密码仅用于开发
   - 生产环境请使用强密码

3. **HTTPS**
   - 生产环境启用 HTTPS
   - 确保 Token 传输安全

4. **数据库**
   - SQLite 适合开发和小型项目
   - 生产环境考虑使用 PostgreSQL/MySQL

## 构建和部署

### 开发环境

```bash
dotnet run
```

### 生产构建

```bash
dotnet publish -c Release -o publish
```

### Docker 部署（待实现）

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY publish .
ENTRYPOINT ["dotnet", "OneAI.dll"]
```

## 故障排除

### 端口被占用

修改 `launchSettings.json` 或使用：
```bash
dotnet run --urls "http://localhost:8080"
```

### 数据库锁定

删除 `oneai.db` 文件重新启动

### CORS 错误

检查前端地址是否在 CORS 允许列表中

## License

MIT
