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
export type AccountType =
  | 'openai'
  | 'claude'
  | 'gemini'
  | 'gemini-antigravity'
  | 'gemini-business'
  | 'factory'
  | 'kiro'

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
 * Kiro 使用明细
 */
export interface KiroUsageBreakdown {
  displayName?: string
  currentUsage: number
  usageLimit: number
  nextDateReset: number
  usedPercent?: number
  remaining?: number
  remainingPercent?: number
  resetAfterSeconds?: number
  freeTrialInfo?: KiroFreeTrialInfo
}

/**
 * Kiro 免费试用信息
 */
export interface KiroFreeTrialInfo {
  freeTrialStatus?: string
  currentUsage: number
  usageLimit: number
  usedPercent?: number
  remaining?: number
  remainingPercent?: number
  freeTrialExpiry: number
  expiryAfterSeconds?: number
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

export interface ImportGeminiBusinessCredentialsRequest {
  credentials: string
  accountName?: string
  email?: string
}

/**
 * Gemini Business批量导入单个账户的数据结构
 */
export interface GeminiBusinessBatchAccountItem {
  secure_c_ses?: string
  host_c_oses?: string
  csesidx?: string
  config_id?: string
  expires_at?: string
  disabled?: boolean
  email?: string
  id?: string
}

/**
 * Gemini Business批量导入请求
 */
export interface ImportGeminiBusinessBatchRequest {
  accounts: GeminiBusinessBatchAccountItem[]
  skipExisting?: boolean
  accountNamePrefix?: string
}

/**
 * Gemini Business批量导入结果
 */
export interface ImportGeminiBusinessBatchResult {
  successCount: number
  failCount: number
  skippedCount: number
  successItems: ImportSuccessItem[]
  failItems: ImportFailItem[]
}

/**
 * Kiro批量导入单个账户的数据结构
 */
export interface KiroBatchAccountItem {
  accessToken?: string
  refreshToken?: string
  profileArn?: string
  expiresAt?: string
  authMethod?: string
  provider?: string
  email?: string
  addedAt?: string
  id?: string
  usageLimit?: {
    limit: number
    used: number
    remaining: number
  }
  clientId?: string
  clientSecret?: string
}

/**
 * Kiro批量导入请求
 */
export interface ImportKiroBatchRequest {
  accounts: KiroBatchAccountItem[]
  skipExisting?: boolean
  accountNamePrefix?: string
}

/**
 * 批量导入成功项
 */
export interface ImportSuccessItem {
  originalId?: string
  accountId: number
  email?: string
  accountName?: string
}

/**
 * 批量导入失败项
 */
export interface ImportFailItem {
  originalId?: string
  email?: string
  errorMessage: string
}

/**
 * 批量导入结果
 */
export interface ImportKiroBatchResult {
  successCount: number
  failCount: number
  skippedCount: number
  successItems: ImportSuccessItem[]
  failItems: ImportFailItem[]
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
  kiroUsageBreakdownList?: KiroUsageBreakdown[]
  kiroFreeTrialInfo?: KiroFreeTrialInfo
}

/**
 * 批量操作结果
 */
export interface BatchOperationResult {
  successCount: number
  failedCount: number
  totalCount: number
  failedIds: number[]
}
