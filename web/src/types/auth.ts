/**
 * 用户信息
 */
export interface User {
  id: string
  username: string
  email?: string
  avatar?: string
  role?: string
}

/**
 * 登录请求参数
 */
export interface LoginRequest {
  username: string
  password: string
}

/**
 * 登录响应数据
 */
export interface LoginResponse {
  user: User
  token: string
  refreshToken?: string
}

/**
 * 注册请求参数
 */
export interface RegisterRequest {
  username: string
  password: string
  email?: string
}
