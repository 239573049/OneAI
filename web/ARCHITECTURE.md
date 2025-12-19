# 前端架构文档

## 技术栈

- **构建工具**: Vite 7.x
- **框架**: React 19.x + TypeScript 5.x
- **UI 组件库**: shadcn/ui（基于 Radix UI）
- **样式**: Tailwind CSS 4.x
- **路由**: React Router DOM 7.x
- **图标**: lucide-react
- **动画**: Motion (Framer Motion)

## 项目结构

```
web/
├── src/
│   ├── components/          # 组件
│   │   ├── ui/             # shadcn 基础组件（Button, Input, Card等）
│   │   └── animate-ui/     # 动画组件库
│   ├── pages/              # 页面组件
│   │   ├── auth/           # 认证相关页面
│   │   │   └── Login.tsx   # 登录页面
│   │   └── Home.tsx        # 首页
│   ├── services/           # API 服务层
│   │   ├── api.ts          # 统一 fetch 封装
│   │   └── auth.ts         # 认证相关 API
│   ├── types/              # TypeScript 类型定义
│   │   ├── api.ts          # API 通用类型
│   │   └── auth.ts         # 认证相关类型
│   ├── router/             # 路由配置
│   │   └── index.tsx       # 路由定义和守卫
│   ├── hooks/              # 自定义 Hooks
│   ├── lib/                # 工具函数
│   │   └── utils.ts        # 通用工具（cn 等）
│   ├── App.tsx             # 应用入口
│   ├── main.tsx            # React 挂载入口
│   └── index.css           # 全局样式
├── .env                    # 环境变量
├── .env.example            # 环境变量示例
├── vite.config.ts          # Vite 配置
├── tsconfig.json           # TypeScript 配置
└── package.json            # 依赖配置
```

## 核心功能

### 1. API 服务层（services/）

#### api.ts - 统一 fetch 封装

提供了完整的 HTTP 请求封装，特性包括：

- **自动认证**: 自动从 localStorage 读取 token 并添加到请求头
- **响应拦截**: 统一处理响应状态码和错误
- **超时控制**: 使用 AbortController 实现请求超时
- **错误处理**: 自定义 ApiException 错误类
- **401 处理**: 自动清除 token 并重定向到登录页

**使用示例**:

```typescript
import { get, post } from '@/services/api'

// GET 请求
const users = await get('/users', { page: 1, pageSize: 10 })

// POST 请求
const result = await post('/users', { name: 'John', email: 'john@example.com' })
```

**API 方法**:

- `get<T>(endpoint, params?, options?)` - GET 请求
- `post<T>(endpoint, data?, options?)` - POST 请求
- `put<T>(endpoint, data?, options?)` - PUT 请求
- `patch<T>(endpoint, data?, options?)` - PATCH 请求
- `del<T>(endpoint, options?)` - DELETE 请求
- `upload<T>(endpoint, formData, options?)` - 文件上传

**Token 管理**:

```typescript
import { setToken, clearToken } from '@/services/api'

// 设置 token
setToken('your-token')

// 清除 token
clearToken()
```

#### auth.ts - 认证服务

封装了认证相关的 API 调用：

```typescript
import { authService } from '@/services/auth'

// 登录
const { user, token } = await authService.login({ username, password })

// 注册
const result = await authService.register({ username, password, email })

// 获取当前用户
const user = await authService.getCurrentUser()

// 退出登录
await authService.logout()
```

### 2. 类型系统（types/）

#### api.ts - API 通用类型

```typescript
// API 响应格式
interface ApiResponse<T> {
  code: number
  message: string
  data: T
}

// 分页参数
interface PaginationParams {
  page: number
  pageSize: number
}

// 分页响应
interface PaginationData<T> {
  list: T[]
  total: number
  page: number
  pageSize: number
}
```

#### auth.ts - 认证类型

```typescript
// 用户信息
interface User {
  id: string
  username: string
  email?: string
  avatar?: string
  role?: string
}

// 登录请求
interface LoginRequest {
  username: string
  password: string
}

// 登录响应
interface LoginResponse {
  user: User
  token: string
  refreshToken?: string
}
```

### 3. 路由系统（router/）

#### 路由守卫

- **ProtectedRoute**: 需要认证的路由，未登录时重定向到登录页
- **PublicRoute**: 公开路由，已登录时重定向到首页

#### 路由配置

```typescript
const routes = [
  { path: '/login', element: <PublicRoute><Login /></PublicRoute> },
  { path: '/', element: <ProtectedRoute><Home /></ProtectedRoute> },
  { path: '*', element: <Navigate to="/" /> }
]
```

### 4. UI 组件（components/ui/）

已集成的 shadcn/ui 组件：

- **Button**: 按钮组件（支持多种变体和尺寸）
- **Input**: 输入框组件
- **Label**: 标签组件
- **Card**: 卡片组件（含 Header, Title, Description, Content, Footer）
- **Separator**: 分隔线组件
- **Skeleton**: 骨架屏组件

所有组件遵循 shadcn/ui 设计规范，支持深色模式。

### 5. 页面组件（pages/）

#### Login.tsx - 登录页面

特性：
- shadcn/ui 风格的表单设计
- 表单验证
- 错误提示
- 加载状态
- 响应式布局
- 支持深色模式

## 环境变量

在项目根目录创建 `.env` 文件：

```env
# API Base URL
VITE_API_BASE_URL=http://localhost:8080/api
```

在代码中使用：

```typescript
const apiUrl = import.meta.env.VITE_API_BASE_URL
```

## 开发指南

### 启动项目

```bash
cd web
npm install
npm run dev
```

### 添加新页面

1. 在 `src/pages/` 创建页面组件
2. 在 `src/router/index.tsx` 添加路由配置
3. 根据需要添加路由守卫

### 添加新 API

1. 在 `src/types/` 定义相关类型
2. 在 `src/services/` 创建服务文件
3. 使用 `api.ts` 中的 HTTP 方法封装 API 调用

### 添加新组件

1. 在 `src/components/ui/` 添加基础 UI 组件
2. 在 `src/components/` 添加业务组件

## 设计规范

### 样式规范

- 使用 Tailwind CSS 工具类
- 使用 shadcn/ui 组件保持一致性
- 响应式设计（移动优先）
- 支持深色模式

### 代码规范

- 使用 TypeScript 类型注解
- 组件使用函数式组件 + Hooks
- 遵循 React 最佳实践
- 使用 ESLint 进行代码检查

### 命名规范

- 组件文件：PascalCase（如 `LoginPage.tsx`）
- 工具函数：camelCase（如 `formatDate.ts`）
- 类型定义：PascalCase（如 `interface User {}`）
- 常量：UPPER_SNAKE_CASE（如 `API_BASE_URL`）

## 扩展建议

### 状态管理

如果应用复杂度增加，可以考虑添加：

- Zustand（轻量级状态管理）
- React Query（服务端状态管理）
- Context API（全局状态）

### 功能增强

- 添加表单验证库（如 react-hook-form + zod）
- 添加国际化支持（如 react-i18next）
- 添加单元测试（Vitest + Testing Library）
- 添加错误边界组件
- 添加全局 Loading 和 Toast 组件

## 注意事项

1. **安全性**
   - Token 存储在 localStorage（生产环境建议使用 httpOnly cookie）
   - API 请求需要做防重放攻击保护
   - 敏感数据传输使用 HTTPS

2. **性能优化**
   - 使用 React.lazy 进行代码分割
   - 图片使用懒加载
   - 大列表使用虚拟滚动

3. **用户体验**
   - 添加加载状态
   - 添加错误处理
   - 添加空状态提示
   - 优化表单交互

## 后续开发

- [ ] 添加用户注册页面
- [ ] 添加忘记密码功能
- [ ] 添加用户个人中心
- [ ] 添加主题切换功能
- [ ] 添加更多业务页面
- [ ] 完善错误处理机制
- [ ] 添加全局状态管理
- [ ] 添加单元测试
