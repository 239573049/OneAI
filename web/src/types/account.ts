/**
 * AI 账户数据传输对象
 */
export interface AIAccountDto {
  id: number
  provider: string
  name?: string
  email?: string
  baseUrl?: string
  isEnabled: boolean
  isRateLimited: boolean
  rateLimitResetTime?: string
  createdAt: string
  updatedAt?: string
  lastUsedAt?: string
  usageCount: number
}

/**
 * 账户类型
 */
export type AccountType = 'openai' | 'claude' | 'gemini' | 'gemini-antigravity'

/**
 * 生成OAuth URL响应
 */
export interface GenerateOAuthUrlResponse {
  authUrl: string
  sessionId: string
  state: string
  codeVerifier: string
  message: string
}

/**
 * 交换授权码请求
 */
export interface ExchangeOAuthCodeRequest {
  sessionId: string
  authorizationCode: string
  projectId?: string // 可选的 GCP 项目 ID（仅用于 Gemini），如果不提供则自动检测
  proxy?: {
    host?: string
    port?: number
    username?: string
    password?: string
  }
}

/**
 * 账户配额状态
 */
export interface AccountQuotaStatus {
  accountId: number
  healthScore?: number
  primaryUsedPercent?: number
  secondaryUsedPercent?: number
  primaryResetAfterSeconds?: number
  secondaryResetAfterSeconds?: number
  statusDescription?: string
  hasCacheData: boolean
  lastUpdatedAt?: string
}
