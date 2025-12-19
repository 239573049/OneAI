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

### 已实现

#### 认证系统
- ✅ JWT Token 认证
- ✅ 登录界面（shadcn/ui 风格）
- ✅ 路由守卫
- ✅ Token 自动管理

#### 后端 API
- ✅ Minimal APIs 实现
- ✅ 统一的 API 响应格式
- ✅ CORS 配置
- ✅ OpenAPI 文档

#### 前端架构
- ✅ 统一的 fetch 封装
- ✅ TypeScript 类型系统
- ✅ 响应式设计
- ✅ 深色模式支持

### 待实现

- [ ] AI 账户 CRUD 功能
- [ ] 账户测试功能
- [ ] 使用统计
- [ ] 多用户支持
- [ ] 账户分组
- [ ] 导入/导出功能

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
POST /api/auth/me
Authorization: Bearer {token}
```

#### 健康检查
```http
GET /api/health
```

详细 API 文档：启动后端后访问 `/scalar`

## 配置说明

### 后端配置

文件：`src/OneAI/appsettings.json`

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

### 前端配置

文件：`web/.env`

```env
VITE_API_BASE_URL=http://localhost:5000/api
```

## 开发指南

### 前端开发

详细文档：[web/README.md](web/README.md) 和 [web/ARCHITECTURE.md](web/ARCHITECTURE.md)

添加新页面：
1. 在 `web/src/pages/` 创建页面组件
2. 在 `web/src/router/index.tsx` 添加路由
3. 根据需要添加 API 服务

### 后端开发

详细文档：[src/OneAI/README.md](src/OneAI/README.md)

添加新 API：
1. 在 `Endpoints/` 创建端点类
2. 实现 Minimal API 映射
3. 在 `Program.cs` 注册端点

## 安全注意事项

⚠️ **重要：生产环境部署前请务必修改以下配置**

1. **JWT SecretKey** - 使用强密钥（至少 32 字符）
2. **管理员密码** - 修改默认密码
3. **HTTPS** - 启用 HTTPS 传输
4. **CORS** - 限制允许的来源
5. **数据库** - 考虑使用生产级数据库

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

### 使用 Docker（推荐）

待实现...

### 手动部署

1. 构建前端和后端
2. 将前端构建产物部署到 Web 服务器（Nginx/Apache）
3. 运行后端应用
4. 配置反向代理

## 故障排除

### 前端无法连接后端

检查：
1. 后端是否正常运行
2. `web/.env` 中的 API 地址是否正确
3. 浏览器控制台是否有 CORS 错误

### 登录失败

检查：
1. 用户名密码是否正确（默认 admin/admin123）
2. 后端日志是否有错误
3. JWT 配置是否正确

### 数据库错误

1. 删除 `oneai.db` 文件
2. 重启后端（自动重新创建数据库）

## 贡献指南

欢迎提交 Issue 和 Pull Request！

## License

MIT License

## 联系方式

- GitHub: [OneAI](https://github.com/yourusername/oneai)
- Email: your.email@example.com

---

**注意**：本项目仍在开发中，部分功能尚未完成。
