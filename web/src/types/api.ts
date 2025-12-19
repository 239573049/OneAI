/**
 * API 响应基础类型
 */
export interface ApiResponse<T = any> {
  code: number
  message: string
  data: T
}

/**
 * API 错误类型
 */
export interface ApiError {
  code: number
  message: string
  details?: any
}

/**
 * 分页请求参数
 */
export interface PaginationParams {
  page: number
  pageSize: number
}

/**
 * 分页响应数据
 */
export interface PaginationData<T> {
  list: T[]
  total: number
  page: number
  pageSize: number
}
