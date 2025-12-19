# OneAI Web 前端

基于 React + TypeScript + Vite + shadcn/ui 构建的现代化前端应用。

## 快速开始

### 安装依赖

```bash
npm install
```

### 配置环境变量

复制 `.env.example` 为 `.env` 并配置：

```bash
cp .env.example .env
```

修改 `.env` 文件中的 API 地址：

```env
VITE_API_BASE_URL=http://localhost:8080/api
```

### 启动开发服务器

```bash
npm run dev
```

访问 http://localhost:5173

### 构建生产版本

```bash
npm run build
```

### 预览生产版本

```bash
npm run preview
```

## 项目特性

- ✅ **统一的 API 封装** - 基于 fetch 的完整 HTTP 请求封装
- ✅ **shadcn/ui 组件库** - 现代化、可定制的 UI 组件
- ✅ **TypeScript** - 完整的类型安全支持
- ✅ **路由系统** - 基于 React Router 的路由配置和守卫
- ✅ **认证系统** - 完整的登录流程和 token 管理
- ✅ **深色模式** - 支持亮色/深色主题切换
- ✅ **响应式设计** - 适配各种屏幕尺寸

## 项目结构

```
src/
├── components/       # UI 组件
│   └── ui/          # shadcn 基础组件
├── pages/           # 页面组件
│   ├── auth/        # 认证相关页面
│   └── Home.tsx     # 首页
├── services/        # API 服务层
│   ├── api.ts       # fetch 封装
│   └── auth.ts      # 认证 API
├── types/           # TypeScript 类型
├── router/          # 路由配置
└── lib/             # 工具函数
```

## 功能说明

### 登录功能

- 路径: `/login`
- 功能:
  - 用户名/密码登录
  - 表单验证
  - 错误提示
  - 加载状态

### 首页

- 路径: `/`
- 需要认证才能访问
- 展示应用功能概览

### API 服务

所有 API 请求都通过 `services/api.ts` 统一处理：

```typescript
import { get, post } from '@/services/api'

// GET 请求
const data = await get('/endpoint')

// POST 请求
const result = await post('/endpoint', { key: 'value' })
```

### 认证服务

使用 `services/auth.ts` 处理认证：

```typescript
import { authService } from '@/services/auth'

// 登录
const { token, user } = await authService.login({ username, password })

// 获取当前用户
const user = await authService.getCurrentUser()
```

## 技术栈

- **React 19** - UI 框架
- **TypeScript 5** - 类型系统
- **Vite 7** - 构建工具
- **Tailwind CSS 4** - 样式框架
- **shadcn/ui** - UI 组件库
- **React Router 7** - 路由管理
- **Lucide React** - 图标库

## 开发指南

详细的架构文档请查看 [ARCHITECTURE.md](./ARCHITECTURE.md)

### 添加新页面

1. 在 `src/pages/` 创建页面组件
2. 在 `src/router/index.tsx` 添加路由
3. 根据需要添加路由守卫

### 添加新 API

1. 在 `src/types/` 定义类型
2. 在 `src/services/` 创建服务文件
3. 使用统一的 API 封装

### 添加新组件

基础 UI 组件放在 `src/components/ui/`
业务组件放在 `src/components/`

## 注意事项

### TypeScript 错误

项目中的 `animate-ui` 组件可能存在一些 TypeScript 类型错误，这些是预装的动画组件库的问题，不影响核心功能使用。如果不需要使用动画组件，可以忽略这些错误。

### API 配置

确保后端 API 地址配置正确，默认为 `http://localhost:8080/api`

### Token 存储

Token 目前存储在 localStorage 中，生产环境建议使用 httpOnly cookie 提高安全性。

## License

MIT
