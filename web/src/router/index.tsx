import { createBrowserRouter, Navigate } from 'react-router-dom'
import Login from '@/pages/auth/Login'
import Home from '@/pages/Home'

/**
 * 路由守卫 - 检查用户是否已登录
 */
function ProtectedRoute({ children }: { children: React.ReactNode }) {
  const token = localStorage.getItem('token')

  if (!token) {
    return <Navigate to="/login" replace />
  }

  return <>{children}</>
}

/**
 * 公开路由 - 已登录用户访问时重定向到首页
 */
function PublicRoute({ children }: { children: React.ReactNode }) {
  const token = localStorage.getItem('token')

  if (token) {
    return <Navigate to="/" replace />
  }

  return <>{children}</>
}

/**
 * 路由配置
 */
export const router = createBrowserRouter([
  {
    path: '/login',
    element: (
      <PublicRoute>
        <Login />
      </PublicRoute>
    ),
  },
  {
    path: '/',
    element: (
      <ProtectedRoute>
        <Home />
      </ProtectedRoute>
    ),
  },
  {
    path: '/accounts',
    element: (
      <ProtectedRoute>
        <Home />
      </ProtectedRoute>
    ),
  },
  {
    path: '/logs',
    element: (
      <ProtectedRoute>
        <Home />
      </ProtectedRoute>
    ),
  },
  {
    path: '/settings',
    element: (
      <ProtectedRoute>
        <Home />
      </ProtectedRoute>
    ),
  },
  {
    path: '/model-mapping',
    element: (
      <ProtectedRoute>
        <Home />
      </ProtectedRoute>
    ),
  },
  {
    path: '*',
    element: <Navigate to="/" replace />,
  },
])
