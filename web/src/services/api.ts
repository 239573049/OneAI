import type { ApiResponse } from '@/types/api'

/**
 * API 配置
 */
const API_CONFIG = {
  baseURL: import.meta.env.VITE_API_BASE_URL || 'http://localhost:8080/api',
  timeout: 30000,
}

/**
 * 自定义 API 错误类
 */
export class ApiException extends Error {
  code: number
  details?: any

  constructor(message: string, code: number, details?: any) {
    super(message)
    this.name = 'ApiException'
    this.code = code
    this.details = details
  }
}

/**
 * 获取 token
 */
function getToken(): string | null {
  return localStorage.getItem('token')
}

/**
 * 设置 token
 */
export function setToken(token: string): void {
  localStorage.setItem('token', token)
}

/**
 * 清除 token
 */
export function clearToken(): void {
  localStorage.removeItem('token')
}

/**
 * 请求拦截器 - 添加认证头和其他配置
 */
function getRequestHeaders(customHeaders?: HeadersInit): HeadersInit {
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
  }

  if (customHeaders) {
    if (customHeaders instanceof Headers) {
      customHeaders.forEach((value, key) => {
        headers[key] = value
      })
    } else if (Array.isArray(customHeaders)) {
      customHeaders.forEach(([key, value]) => {
        headers[key] = value
      })
    } else {
      Object.assign(headers, customHeaders)
    }
  }

  const token = getToken()
  if (token) {
    headers['Authorization'] = `Bearer ${token}`
  }

  return headers
}

/**
 * 响应拦截器 - 处理响应和错误
 */
async function handleResponse<T>(response: Response): Promise<T> {
  // 处理 HTTP 错误状态码
  if (!response.ok) {
    const errorData = await response.json().catch(() => ({
      message: response.statusText,
      code: response.status,
    }))

    // 401 未授权 - 清除 token 并可能重定向到登录
    if (response.status === 401) {
      clearToken()
      window.location.href = '/login'
    }

    throw new ApiException(
      errorData.message || `HTTP Error: ${response.status}`,
      errorData.code || response.status,
      errorData
    )
  }

  // 解析 JSON 响应
  const data: ApiResponse<T> = await response.json()

  // 根据业务状态码判断
  if (data.code !== 0 && data.code !== 200) {
    throw new ApiException(
      data.message || 'Request failed',
      data.code,
      data.data
    )
  }

  return data.data
}

/**
 * 请求选项
 */
interface RequestOptions extends RequestInit {
  params?: Record<string, any>
  timeout?: number
}

/**
 * 通用请求方法
 */
async function request<T = any>(
  endpoint: string,
  options: RequestOptions = {}
): Promise<T> {
  const { params, timeout = API_CONFIG.timeout, headers, ...restOptions } = options

  // 构建完整 URL
  let url = `${API_CONFIG.baseURL}${endpoint}`

  // 添加查询参数
  if (params) {
    const searchParams = new URLSearchParams()
    Object.entries(params).forEach(([key, value]) => {
      if (value !== undefined && value !== null) {
        searchParams.append(key, String(value))
      }
    })
    const queryString = searchParams.toString()
    if (queryString) {
      url += `?${queryString}`
    }
  }

  // 创建 AbortController 用于超时控制
  const controller = new AbortController()
  const timeoutId = setTimeout(() => controller.abort(), timeout)

  try {
    const response = await fetch(url, {
      ...restOptions,
      headers: getRequestHeaders(headers),
      signal: controller.signal,
    })

    return await handleResponse<T>(response)
  } catch (error) {
    if (error instanceof ApiException) {
      throw error
    }

    if (error instanceof Error) {
      if (error.name === 'AbortError') {
        throw new ApiException('Request timeout', 408)
      }
      throw new ApiException(error.message, 500)
    }

    throw new ApiException('Unknown error occurred', 500)
  } finally {
    clearTimeout(timeoutId)
  }
}

/**
 * GET 请求
 */
export function get<T = any>(
  endpoint: string,
  params?: Record<string, any>,
  options?: RequestOptions
): Promise<T> {
  return request<T>(endpoint, {
    ...options,
    method: 'GET',
    params,
  })
}

/**
 * POST 请求
 */
export function post<T = any>(
  endpoint: string,
  data?: any,
  options?: RequestOptions
): Promise<T> {
  return request<T>(endpoint, {
    ...options,
    method: 'POST',
    body: JSON.stringify(data),
  })
}

/**
 * PUT 请求
 */
export function put<T = any>(
  endpoint: string,
  data?: any,
  options?: RequestOptions
): Promise<T> {
  return request<T>(endpoint, {
    ...options,
    method: 'PUT',
    body: JSON.stringify(data),
  })
}

/**
 * DELETE 请求
 */
export function del<T = any>(
  endpoint: string,
  options?: RequestOptions
): Promise<T> {
  return request<T>(endpoint, {
    ...options,
    method: 'DELETE',
  })
}

/**
 * PATCH 请求
 */
export function patch<T = any>(
  endpoint: string,
  data?: any,
  options?: RequestOptions
): Promise<T> {
  return request<T>(endpoint, {
    ...options,
    method: 'PATCH',
    body: JSON.stringify(data),
  })
}

/**
 * 文件上传
 */
export function upload<T = any>(
  endpoint: string,
  formData: FormData,
  options?: RequestOptions
): Promise<T> {
  const { headers, ...restOptions } = options || {}

  return request<T>(endpoint, {
    ...restOptions,
    method: 'POST',
    headers: {
      // 不设置 Content-Type，让浏览器自动设置（包括 boundary）
      ...headers,
      'Content-Type': undefined as any,
    },
    body: formData as any,
  })
}

export default {
  get,
  post,
  put,
  delete: del,
  patch,
  upload,
}
