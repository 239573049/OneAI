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
export type AccountType = 'openai' | 'claude' | 'gemini' | 'gemini-antigravity' | 'factory' | 'kiro'

export interface AntigravityModelQuota {
  model: string
  hasQuotaInfo: boolean
  remainingFraction?: number
  remainingPercent?: number
  usedPercent?: number
  resetTime?: string
  resetAfterSeconds?: number
}

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
 * 生成 Factory Device Code 响应
 */
export interface GenerateFactoryDeviceCodeResponse {
  sessionId: string
  userCode: string
  verificationUri: string
  verificationUriComplete: string
  expiresIn: number
  interval: number
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
    type?: string
    host?: string
    port?: number
    username?: string
    password?: string
  }
}

/**
 * 完成 Factory Device Code 授权请求
 */
export interface ExchangeFactoryDeviceCodeRequest {
  sessionId: string
  accountName?: string
  proxy?: {
    type?: string
    host?: string
    port?: number
    username?: string
    password?: string
  }
}

export interface ImportKiroCredentialsRequest {
  credentials: string
  accountName?: string
  email?: string
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
  // Anthropic 风格的限流信息（Factory 提供商使用）
  tokensLimit?: number
  tokensRemaining?: number
  tokensUsedPercent?: number
  inputTokensLimit?: number
  inputTokensRemaining?: number
  outputTokensLimit?: number
  outputTokensRemaining?: number
  // Anthropic Unified 限流信息（Claude 官方 API / Claude Code）
  anthropicUnifiedStatus?: string
  anthropicUnifiedFiveHourStatus?: string
  anthropicUnifiedFiveHourUtilization?: number
  anthropicUnifiedSevenDayStatus?: string
  anthropicUnifiedSevenDayUtilization?: number
  anthropicUnifiedRepresentativeClaim?: string
  anthropicUnifiedFallbackPercentage?: number
  anthropicUnifiedResetAt?: number
  anthropicUnifiedOverageDisabledReason?: string
  anthropicUnifiedOverageStatus?: string
  antigravityModelQuotas?: AntigravityModelQuota[]
}
