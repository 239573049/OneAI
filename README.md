# OneAI - AI 账户管理系统

一个基于 React + .NET 10 构建的现代化全栈应用，用于管理多个 AI 服务账户。

## 项目概述

OneAI 是一个统一的 AI 账户管理平台，支持：
- 统一管理多个 AI 服务商的账户（OpenAI, Claude, Gemini 等）
- 基于 JWT 的安全认证系统
- 现代化的用户界面
- RESTful API 设计

## 技术栈

### 前端
- **React 19** - UI 框架
- **TypeScript 5** - 类型系统
- **Vite 7** - 构建工具
- **shadcn/ui** - UI 组件库
- **Tailwind CSS 4** - 样式框架
- **React Router 7** - 路由管理

### 后端
- **.NET 10** - 后端框架
- **Minimal APIs** - 轻量级 API
- **Entity Framework Core 10** - ORM
- **SQLite** - 数据库
- **JWT Bearer** - 身份认证
- **Scalar** - API 文档

## 项目结构

```
OneAI/
├── web/                    # 前端项目
│   ├── src/
│   │   ├── components/    # UI 组件
│   │   ├── pages/         # 页面组件
│   │   ├── services/      # API 服务
│   │   ├── types/         # TypeScript 类型
│   │   └── router/        # 路由配置
│   └── package.json
│
├── src/OneAI/             # 后端项目
│   ├── Data/              # 数据访问层
│   ├── Entities/          # 数据实体
│   ├── Services/          # 业务服务
│   ├── Models/            # DTO 模型
│   ├── Endpoints/         # API 端点
│   └── OneAI.csproj
│
└── README.md
```

## 快速开始

### 前置要求

- Node.js 18+
- .NET 10 SDK

### 启动后端

```bash
cd src/OneAI
dotnet run
```

后端服务运行在 `http://localhost:5000`

API 文档：`http://localhost:5000/scalar`

### 启动前端

```bash
cd web
npm install
npm run dev
```

前端服务运行在 `http://localhost:5173`

### 默认登录账户

- 用户名：`admin`
- 密码：`admin123`

可在 [src/OneAI/appsettings.json](src/OneAI/appsettings.json:18-21) 中修改。

## 功能特性

### 核心功能

#### AI 账户管理
- ✅ **多服务商支持**：OpenAI、Claude、Gemini、Gemini-Antigravity、Gemini-Business、Factory、Kiro
- ✅ **多种认证方式**：
  - API Key 认证
  - OAuth 2.0 认证（Authorization Code + PKCE）
  - Device Authorization Flow（Factory via WorkOS）
- ✅ **账户状态管理**：启用/禁用、速率限制标记
- ✅ **OAuth 流程管理**：基于会话的状态管理，支持 PKCE 安全增强
- ✅ **额度追踪**：实时缓存账户配额使用情况

#### 请求日志系统
- ✅ **异步日志记录**：基于 Channel 的队列机制，高性能批量写入
- ✅ **双数据库架构**：主数据库（AppDbContext）+ 日志数据库（LogDbContext）
- ✅ **请求追踪**：记录模型、Token 使用量、延迟、状态等
- ✅ **后台聚合服务**：按小时聚合统计数据
- ✅ **结构化日志**：Serilog + TraceId + ClientIp

#### 认证系统
- ✅ JWT Token 认证
- ✅ 登录界面（shadcn/ui 风格）
- ✅ 路由守卫
- ✅ Token 自动管理
- ✅ 优雅的 Token 处理（自定义事件处理器）

#### 系统设置
- ✅ 键值对存储系统
- ✅ 启动时内存缓存
- ✅ 类型安全的数据访问

### 后端 API
- ✅ Minimal APIs 实现
- ✅ 统一的 API 响应格式（`ApiResponse<T>`）
- ✅ CORS 配置
- ✅ Scalar API 文档
- ✅ 响应压缩（Brotli + Gzip）

### 前端架构
- ✅ 统一的 fetch 封装（自动 Token 注入、401 重定向）
- ✅ TypeScript 类型系统
- ✅ 响应式设计
- ✅ 深色模式支持
- ✅ 服务层架构（domain-specific services）

### 待实现

- [x] ~~AI 账户前端页面（CRUD UI）~~ - 已完成
- [ ] 账户测试功能
- [ ] 使用统计可视化
- [ ] 多用户支持
- [ ] 账户分组
- [x] ~~导入/导出功能~~ - 已完成（Kiro 批量导入、Gemini-Business 批量导入）
- [ ] OAuth Token 刷新机制

## API 接口

### 认证接口

#### 登录
```http
POST /api/auth/login
Content-Type: application/json

{
  "username": "admin",
  "password": "admin123"
}
```

#### 获取当前用户信息
```http
GET /api/auth/me
Authorization: Bearer {token}
```

### AI 账户接口

#### 获取所有账户
```http
GET /api/accounts
Authorization: Bearer {token}
```

#### 获取单个账户
```http
GET /api/accounts/{id}
Authorization: Bearer {token}
```

#### 创建账户
```http
POST /api/accounts
Authorization: Bearer {token}
Content-Type: application/json

{
  "name": "My OpenAI Account",
  "provider": "OpenAI",
  "apiKey": "sk-...",
  "isEnabled": true
}
```

#### 更新账户
```http
PUT /api/accounts/{id}
Authorization: Bearer {token}
Content-Type: application/json

{
  "name": "Updated Name",
  "isEnabled": false
}
```

#### 删除账户
```http
DELETE /api/accounts/{id}
Authorization: Bearer {token}
```

### OAuth 接口

#### OAuth 登录
```http
GET /api/oauth/{provider}/login?redirectUri={uri}
```

#### OAuth 回调
```http
GET /api/oauth/{provider}/callback?code={code}&state={state}
```

#### OAuth 设备授权（Factory）
```http
POST /api/oauth/factory/device
Authorization: Bearer {token}
```

### 请求日志接口

#### 获取请求日志
```http
GET /api/logs?accountId={id}&page=1&pageSize=20
Authorization: Bearer {token}
```

#### 获取统计信息
```http
GET /api/logs/statistics?accountId={id}&startDate={date}&endDate={date}
Authorization: Bearer {token}
```

### 系统设置接口

#### 获取所有设置
```http
GET /api/settings
Authorization: Bearer {token}
```

#### 更新设置
```http
PUT /api/settings/{key}
Authorization: Bearer {token}
Content-Type: application/json

{
  "value": "new value"
}
```

### 健康检查
```http
GET /api/health
```

详细 API 文档：启动后端后访问 `http://localhost:5000/scalar`

## 配置说明

### 后端配置

文件：`src/OneAI/appsettings.json`

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=oneai.db",
    "LogConnection": "Data Source=oneai-log.db"
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
  },
  "Gemini": {
    "CodeAssistEndpoint": "https://codeassist.googleapis.com"
  }
}
```

**配置项说明**：
- `ConnectionStrings:DefaultConnection` - 主数据库路径（账户、设置等）
- `ConnectionStrings:LogConnection` - 日志数据库路径（可选，默认使用主数据库）
- `Jwt:SecretKey` - JWT 签名密钥（生产环境必须修改）
- `Jwt:ExpirationMinutes` - Token 过期时间（分钟）
- `AdminAccount` - 默认管理员账户
- `Gemini:CodeAssistEndpoint` - Gemini API 端点

### 前端配置

文件：`web/.env`

```env
VITE_API_BASE_URL=http://localhost:5000/api
```

**配置项说明**：
- `VITE_API_BASE_URL` - 后端 API 基础 URL

## 架构设计

### 后端架构

#### 目录结构
```
src/OneAI/
├── Data/                    # 数据访问层
│   ├── AppDbContext.cs      # 主数据库上下文
│   ├── LogDbContext.cs      # 日志数据库上下文
│   └── DbInitializer.cs     # 数据库初始化
├── Entities/                # 数据实体
│   ├── AIAccount.cs         # AI 账户实体
│   ├── AIRequestLog.cs      # 请求日志实体
│   └── SystemSettings.cs    # 系统设置实体
├── Services/                # 业务服务
│   ├── Auth/                # 认证相关
│   ├── OAuth/               # OAuth 助手
│   ├── AccountQuotaCacheService.cs
│   └── SettingsService.cs
├── Models/                  # DTO 模型
├── Endpoints/               # API 端点（按功能分组）
│   ├── AuthEndpoints.cs
│   ├── AccountEndpoints.cs
│   ├── OAuthEndpoints.cs
│   └── ...
└── Program.cs               # 应用入口
```

#### 核心设计模式

**Minimal APIs 模式**：
- 端点按功能分组在 `Endpoints/` 目录
- 每个端点文件导出 `Map*Endpoints(IEndpointRouteBuilder)` 扩展方法
- 使用 `.WithTags()` 进行 OpenAPI 分类
- 使用 `.RequireAuthorization()` 保护路由

**服务注册**（`Program.cs` 128-153 行）：
- Scoped 服务：每次请求创建新实例（如 `DbContext`）
- Singleton 服务：全局共享（如 `AccountQuotaCacheService`、`IOAuthSessionService`）

**数据库策略**：
- 双 SQLite 数据库：`AppDbContext`（主数据）+ `LogDbContext`（日志）
- 迁移文件分别在 `AppMigrations/` 和 `LogMigrations/`
- 启动时自动初始化数据库（`Program.cs` 233-247 行）

**后台服务**：
1. `AIRequestLogWriterService` - 消费日志队列，批量写入数据库
2. `AIRequestAggregationBackgroundService` - 按小时聚合统计数据

**日志架构**：
- 基于 Channel 的异步队列（45-49 行）
- 生产者通过 `AIRequestLogService` 推送日志
- 消费者（`AIRequestLogWriterService`）批量写入
- Serilog + TraceId + ClientIp 结构化日志（183-203 行）

**OAuth 流程**：
- 会话状态存储在 `InMemoryOAuthSessionService`（Singleton）
- 各服务商 OAuth 助手：
  - `OpenAiOAuthHelper` - Authorization Code + PKCE
  - `ClaudeCodeOAuthHelper` - Authorization Code + PKCE
  - `GeminiAntigravityOAuthHelper` / `GeminiOAuthHelper` - Authorization Code + PKCE
  - `GeminiBusinessOAuthService` - Authorization Code + PKCE（business.gemini.google）
  - `FactoryOAuthService` - Device Authorization Flow（RFC 8628，通过 WorkOS）
  - `KiroOAuthService` - Amazon CodeWhisperer via Kiro OAuth
- OAuth Token 以 JSON 格式存储在 `AIAccount.OAuthToken` 字段
- Factory 使用设备授权流程：用户通过设备码授权，应用轮询 Token 端点

**额度追踪**：
- `AccountQuotaCacheService`（Singleton）维护账户额度内存缓存
- 实现速率限制逻辑

### 前端架构

#### 目录结构
```
web/src/
├── components/          # UI 组件
│   └── ui/             # shadcn/ui 原始组件
├── pages/              # 页面组件
├── services/           # API 服务
│   ├── api.ts          # 统一 fetch 封装
│   ├── auth.ts         # 认证服务
│   ├── account.ts      # 账户服务
│   ├── settings.ts     # 设置服务
│   └── logs.ts         # 日志服务
├── types/              # TypeScript 类型定义
├── router/             # 路由配置
└── main.tsx            # 应用入口
```

#### 核心设计模式

**API 层**（`services/api.ts`）：
- 统一的 fetch 封装
- 请求/响应拦截器
- 自动从 localStorage 注入 JWT Token
- 401 自动重定向到登录页
- 自定义 `ApiException` 异常处理

**服务模式**（`services/`）：
- 按领域划分的专用服务
- 所有服务返回 `ApiResponse<T>` 类型

**响应格式**：
```typescript
{
  code: number,      // 0 或 200 表示成功
  message: string,
  data: T
}
```

**路由**（`router/`）：
- React Router 7
- 需要认证的路由应检查认证状态

## 数据库架构

### 实体关系

**AIAccount**（AI 账户主实体）：
- 支持 `ApiKey` 和 `OAuthToken`（JSON 字段）两种认证方式
- `IsEnabled` - 软删除/禁用标记
- `IsRateLimited` - 配额超限标记
- `Provider` - 服务商名称（OpenAI, Claude, Factory, Gemini, Gemini-Antigravity, Gemini-Business, Kiro）
- OAuth 序列化扩展方法：
  - `GetOpenAiOauth()` / `SetOpenAIOAuth()`
  - `GetClaudeOauth()` / `SetClaudeOAuth()`
  - `GetGeminiOauth()` / `SetGeminiOAuth()`
  - `GetGeminiBusinessOauth()` / `SetGeminiBusinessOAuth()`
  - `GetFactoryOauth()` / `SetFactoryOAuth()`
  - `GetKiroOauth()` / `SetKiroOAuth()`

**AIRequestLog**（请求日志）：
- 通过 `AccountId` 关联到 `AIAccount`
- 记录模型、Token 使用量、延迟、状态
- 由后台服务聚合为小时级统计

**SystemSettings**（系统设置）：
- 键值对存储（`Key` 唯一）
- `DataType` 字段指示值类型
- `DbInitializer.InitializeSettingsAsync()` 初始化默认值

### 数据库迁移

```bash
# 创建迁移
cd src/OneAI
dotnet ef migrations add <MigrationName> --context AppDbContext
dotnet ef migrations add <MigrationName> --context LogDbContext

# 应用迁移
dotnet ef database update --context AppDbContext
dotnet ef database update --context LogDbContext
```

## 开发指南

### 常用开发模式

#### 添加新的 API 端点

1. 在 `src/OneAI/Endpoints/` 创建端点文件：
```csharp
// MyFeatureEndpoints.cs
public static class MyFeatureEndpoints
{
    public static void MapMyFeatureEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/myfeature")
            .WithTags("My Feature")
            .RequireAuthorization();

        group.MapGet("", async (IMyService service) =>
        {
            return Results.Ok(await service.GetAsync());
        });
    }
}
```

2. 在 `src/OneAI/Program.cs` 注册端点（~280 行）：
```csharp
app.MapMyFeatureEndpoints();
```

#### 添加新的服务

1. 创建接口 `src/OneAI/Services/IMyService.cs`：
```csharp
public interface IMyService
{
    Task<MyResult> GetAsync();
}
```

2. 实现服务 `src/OneAI/Services/MyService.cs`：
```csharp
public class MyService : IMyService
{
    public async Task<MyResult> GetAsync()
    {
        // 实现
    }
}
```

3. 在 `src/OneAI/Program.cs` 注册服务（128-153 行）：
```csharp
builder.Services.AddScoped<IMyService, MyService>();
```

#### 添加新的前端页面

1. 创建页面组件 `web/src/pages/MyPage.tsx`：
```typescript
export default function MyPage() {
  return <div>My Page</div>;
}
```

2. 在 `web/src/router/index.tsx` 添加路由：
```typescript
import MyPage from '@/pages/MyPage';

// 在路由配置中添加
<Route path="/mypage" element={<MyPage />} />
```

3. 创建 API 服务（如需要）`web/src/services/myFeature.ts`：
```typescript
import { api } from './api';

export const myFeatureService = {
  async getData() {
    return api.get<MyData>('/myfeature');
  }
};
```

4. 定义类型（如需要）`web/src/types/myFeature.ts`：
```typescript
export interface MyData {
  id: number;
  name: string;
}
```

#### 添加数据库实体

1. 创建实体 `src/OneAI/Entities/MyEntity.cs`：
```csharp
public class MyEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
```

2. 添加到 DbContext `src/OneAI/Data/AppDbContext.cs`：
```csharp
public DbSet<MyEntity> MyEntities { get; set; }
```

3. 在 `OnModelCreating` 配置关系（如需要）

4. 创建并应用迁移：
```bash
cd src/OneAI
dotnet ef migrations add AddMyEntity --context AppDbContext
dotnet ef database update --context AppDbContext
```

### 代码风格

**C# 代码规范**：
- 4 空格缩进
- PascalCase 命名类型和方法
- 接口使用 `I*` 前缀（如 `IAccountService`）
- 启用可空引用类型

**TypeScript/React 代码规范**：
- 2 空格缩进
- 组件使用 PascalCase（如 `UserProfile`）
- 变量和函数使用 camelCase（如 `getUserData`）
- 优先使用函数组件和 Hooks

**端点分组**：
- 使用 `.WithTags()` 进行 OpenAPI 分类
- 受保护路由使用 `.RequireAuthorization()`
- 公开端点不添加授权要求

## 安全注意事项

⚠️ **重要：生产环境部署前请务必修改以下配置**

1. **JWT SecretKey**（`appsettings.json`）- 使用强密钥（至少 32 字符，随机生成）
2. **管理员密码**（`appsettings.json`）- 修改默认密码 `admin123`
3. **HTTPS** - 生产环境必须启用 HTTPS 传输
4. **CORS**（`Program.cs` 154-163 行）- 限制 `AllowedOrigins` 为已知域名
5. **数据库** - 考虑使用生产级数据库（PostgreSQL/SQL Server）
6. **OAuth Token** - OAuth Token 以 JSON 格式存储在数据库，生产环境考虑加密
7. **Token 存储** - 当前 JWT Token 存储在 localStorage，考虑升级为 httpOnly Cookies

### JWT 事件处理

后端实现了自定义 JWT 事件处理器（`Program.cs` 70-122 行）：
- `OnMessageReceived` - 优雅处理格式错误的 Token
- `OnAuthenticationFailed` - 未授权端点不因无效 Token 失败
- `OnChallenge` - 仅受保护端点返回 401

### 静态文件服务

后端从 `wwwroot/` 目录提供前端 SPA（`Program.cs` 280-298 行）：
- API 路由：`/api/*`
- Fallback 路由：其他所有路由返回 `index.html`（支持客户端路由）

## 构建生产版本

### 前端

```bash
cd web
npm run build
```

构建产物：`web/dist/`

### 后端

```bash
cd src/OneAI
dotnet publish -c Release -o publish
```

发布文件：`src/OneAI/publish/`

## 部署

### 开发环境部署

#### 1. 完整启动（开发模式）

**终端 1 - 启动后端**：
```bash
cd src/OneAI
dotnet run
```

**终端 2 - 启动前端**：
```bash
cd web
npm run dev
```

访问：`http://localhost:5173`

### 生产环境部署

#### 方式 1：集成部署（推荐）

后端服务前端静态文件，单一进程部署。

**1. 构建前端**：
```bash
cd web
npm run build
```

**2. 复制前端构建产物到后端 wwwroot**：
```bash
# Windows
xcopy /E /I /Y web\dist src\OneAI\wwwroot

# Linux/macOS
cp -r web/dist/* src/OneAI/wwwroot/
```

**3. 发布后端**：
```bash
cd src/OneAI
dotnet publish -c Release -o publish
```

**4. 运行**：
```bash
cd publish
dotnet OneAI.dll
```

访问：`http://localhost:5000`

#### 方式 2：分离部署

前端和后端分开部署，使用反向代理。

**1. 构建前端**：
```bash
cd web
npm run build
```

**2. 部署前端**（以 Nginx 为例）：
```nginx
server {
    listen 80;
    root /path/to/web/dist;
    index index.html;

    location / {
        try_files $uri $uri/ /index.html;
    }

    location /api/ {
        proxy_pass http://localhost:5000/api/;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
    }
}
```

**3. 部署后端**：
```bash
cd src/OneAI
dotnet publish -c Release -o publish
cd publish
dotnet OneAI.dll
```

#### 方式 3：Docker 部署

创建 `Dockerfile`（项目根目录）：
```dockerfile
# 多阶段构建
FROM node:18-alpine AS frontend-build
WORKDIR /app/web
COPY web/package*.json ./
RUN npm install
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY src/OneAI/OneAI.csproj .
COPY src/OneAI/*.csproj ./
RUN dotnet restore
COPY src/OneAI/ ./
COPY --from=frontend-build /app/web/dist ./wwwroot
EXPOSE 5000
ENTRYPOINT ["dotnet", "OneAI.dll"]
```

构建并运行：
```bash
docker build -t oneai:latest .
docker run -d -p 5000:5000 --name oneai oneai:latest
```

### 环境变量配置

生产环境建议使用环境变量覆盖配置：

```bash
# Linux/macOS
export ConnectionStrings__DefaultConnection="Data Source=/var/data/oneai.db"
export Jwt__SecretKey="your-production-secret-key"
export Jwt__ExpirationMinutes="1440"

# Windows (PowerShell)
$env:ConnectionStrings__DefaultConnection="Data Source=C:\data\oneai.db"
$env:Jwt__SecretKey="your-production-secret-key"
$env:Jwt__ExpirationMinutes="1440"
```

### 进程管理（生产环境）

#### 使用 systemd（Linux）

创建 `/etc/systemd/system/oneai.service`：
```ini
[Unit]
Description=OneAI API
After=network.target

[Service]
Type=notify
WorkingDirectory=/opt/oneai
ExecStart=/usr/bin/dotnet /opt/oneai/OneAI.dll
Restart=always
RestartSec=10
SyslogIdentifier=oneai
User=www-data
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

[Install]
WantedBy=multi-user.target
```

启动服务：
```bash
sudo systemctl daemon-reload
sudo systemctl enable oneai
sudo systemctl start oneai
sudo systemctl status oneai
```

#### 使用 PM2（Node.js 生态）

```bash
npm install -g pm2
pm2 start "dotnet run --project src/OneAI" --name oneai
pm2 save
pm2 startup
```

## 故障排除

### 前端无法连接后端

**症状**：浏览器控制台显示网络错误或 CORS 错误

**排查步骤**：
1. 检查后端是否正常运行：
   ```bash
   curl http://localhost:5000/api/health
   ```
2. 检查 `web/.env` 中的 `VITE_API_BASE_URL` 是否正确
3. 检查浏览器控制台是否有 CORS 错误
4. 确认后端 CORS 配置（`Program.cs` 154-163 行）包含前端地址
5. 清除浏览器缓存并刷新页面

### 登录失败

**症状**：输入正确用户名密码后无法登录

**排查步骤**：
1. 确认默认账户：用户名 `admin`，密码 `admin123`
2. 检查后端日志是否有错误信息
3. 检查 JWT 配置（`appsettings.json`）：
   - `SecretKey` 是否已设置
   - `ExpirationMinutes` 是否为正整数
4. 清除浏览器 localStorage：
   ```javascript
   // 在浏览器控制台执行
   localStorage.clear()
   location.reload()
   ```

### 数据库错误

**症状**：启动后端时数据库相关错误

**解决方案**：

1. **删除数据库文件，重新创建**：
   ```bash
   # Windows
   del src\OneAI\oneai.db
   del src\OneAI\oneai-log.db

   # Linux/macOS
   rm src/OneAI/oneai.db
   rm src/OneAI/oneai-log.db
   ```

2. **重置迁移**（如需要）：
   ```bash
   cd src/OneAI
   dotnet ef database drop --context AppDbContext
   dotnet ef database drop --context LogDbContext
   dotnet ef database update --context AppDbContext
   dotnet ef database update --context LogDbContext
   ```

3. **检查数据库文件权限**：确保应用进程有读写权限

### OAuth 授权失败

**症状**：OAuth 授权流程中断或 Token 获取失败

**排查步骤**：
1. 检查 OAuth 回调 URL 是否正确配置
2. 检查 OAuth 应用的 Client ID 和 Client Secret
3. 查看后端日志中的 OAuth 相关错误
4. 确认 OAuth 会话状态（`InMemoryOAuthSessionService`）正常

### 前端构建失败

**症状**：`npm run build` 报错

**排查步骤**：
1. 清除 node_modules 并重新安装：
   ```bash
   cd web
   rm -rf node_modules package-lock.json
   npm install
   ```

2. 检查 TypeScript 类型错误：
   ```bash
   npm run build
   ```

3. 检查 ESLint 错误：
   ```bash
   npm run lint
   ```

### 后端构建失败

**症状**：`dotnet build` 报错

**排查步骤**：
1. 清除并重新构建：
   ```bash
   cd src/OneAI
   dotnet clean
   dotnet restore
   dotnet build
   ```

2. 检查 .NET 版本：
   ```bash
   dotnet --version
   ```
   确保版本为 10.0 或更高

3. 检查依赖包版本冲突

### 日志记录不工作

**症状**：请求日志未记录到数据库

**排查步骤**：
1. 检查 `AIRequestLogWriterService` 后台服务是否启动
2. 检查日志数据库连接字符串配置
3. 查看后端日志中的 Channel 相关错误
4. 确认 `AIRequestLogService` 正确注入和使用

### 性能问题

**症状**：响应缓慢或内存占用高

**优化建议**：
1. 检查日志数据库大小，考虑定期清理旧日志
2. 启用响应压缩（已配置 Brotli + Gzip）
3. 检查 `AccountQuotaCacheService` 缓存命中率
4. 考虑使用生产级数据库（PostgreSQL 替代 SQLite）
5. 检查后台服务是否正常运行（不阻塞）

### 端口占用

**症状**：启动时提示端口 5000 或 5173 已被占用

**解决方案**：

**Windows**：
```bash
# 查找占用进程
netstat -ano | findstr :5000

# 终止进程
taskkill /F /PID <进程ID>
```

**Linux/macOS**：
```bash
# 查找占用进程
lsof -i :5000

# 终止进程
kill -9 <进程ID>
```

或修改端口：
- 后端：修改 `Properties/launchSettings.json`
- 前端：修改 `web/.env` 和 `Vite` 配置

## 性能优化建议

### 数据库优化
1. 为常用查询字段添加索引
2. 定期清理历史日志数据
3. 考虑读写分离（主库 + 日志库）
4. 生产环境使用 PostgreSQL 或 SQL Server

### 缓存优化
1. 调整 `AccountQuotaCacheService` 缓存过期时间
2. 考虑使用 Redis 作为分布式缓存
3. 实现设置变更的缓存失效机制

### 前端优化
1. 实现路由懒加载（React.lazy）
2. 优化打包体积（代码分割）
3. 启用 CDN 加速静态资源
4. 实现请求去重和防抖

### 后端优化
1. 实现 HTTP 缓存头（ETag、Last-Modified）
2. 优化日志批量写入大小
3. 考虑使用连接池
4. 实现 API 限流

## 测试

### 后端测试

```bash
cd src/OneAI
dotnet test
```

### 前端测试

```bash
cd web
npm run test
```

## 监控和日志

### 后端日志

后端使用 Serilog 进行结构化日志记录：
- 控制台输出：带颜色和级别
- TraceId：请求追踪
- ClientIp：客户端 IP
- 请求耗时：性能分析

查看日志：
```bash
# 实时查看（Linux/macOS）
tail -f logs/oneai.log

# Windows（PowerShell）
Get-Content logs/oneai.log -Wait
```

### 前端监控

建议集成：
- Sentry：错误追踪
- Google Analytics：用户行为分析
- Vercel Analytics：性能监控（如使用 Vercel 部署）

## 贡献指南

欢迎贡献代码！请遵循以下流程：

1. Fork 本仓库
2. 创建特性分支：`git checkout -b feature/AmazingFeature`
3. 提交更改：`git commit -m 'Add some AmazingFeature'`
4. 推送分支：`git push origin feature/AmazingFeature`
5. 提交 Pull Request

### 代码审查标准
- 遵循现有代码风格
- 添加必要的注释
- 更新相关文档
- 确保测试通过
- 不破坏现有功能

## 许可证

MIT License - 详见 [LICENSE](LICENSE) 文件

## 致谢

感谢以下开源项目：
- [.NET](https://dotnet.microsoft.com/)
- [React](https://react.dev/)
- [Vite](https://vitejs.dev/)
- [shadcn/ui](https://ui.shadcn.com/)
- [Tailwind CSS](https://tailwindcss.com/)
- [Serilog](https://serilog.net/)

## 联系方式

- GitHub Issues: [OneAI/issues](https://github.com/yourusername/oneai/issues)
- Email: your.email@example.com

---

**注意**：本项目仍在积极开发中，部分功能尚未完成。欢迎提交 Feature Request！
