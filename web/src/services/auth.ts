import { post } from './api'
import type { LoginRequest, LoginResponse, RegisterRequest, User } from '@/types/auth'

/**
 * 认证服务
 */
export const authService = {
  /**
   * 用户登录
   */
  login(data: LoginRequest): Promise<LoginResponse> {
    return post<LoginResponse>('/auth/login', data)
  },

  /**
   * 用户注册
   */
  register(data: RegisterRequest): Promise<LoginResponse> {
    return post<LoginResponse>('/auth/register', data)
  },

  /**
   * 获取当前用户信息
   */
  getCurrentUser(): Promise<User> {
    return post<User>('/auth/me')
  },

  /**
   * 退出登录
   */
  logout(): Promise<void> {
    return post('/auth/logout')
  },

  /**
   * 刷新 token
   */
  refreshToken(refreshToken: string): Promise<{ token: string }> {
    return post('/auth/refresh', { refreshToken })
  },
}
