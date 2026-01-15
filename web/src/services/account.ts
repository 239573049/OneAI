import { get, del, post, patch } from './api'
import type {
  AIAccountDto,
  GenerateOAuthUrlResponse,
  ExchangeOAuthCodeRequest,
  AccountQuotaStatus,
  GenerateFactoryDeviceCodeResponse,
  ExchangeFactoryDeviceCodeRequest,
  ImportKiroCredentialsRequest,
} from '@/types/account'

/**
 * AI 账户服务
 */
export const accountService = {
  /**
   * 获取 AI 账户列表
   */
  getAccounts(): Promise<AIAccountDto[]> {
    return get<AIAccountDto[]>('/accounts')
  },

  /**
   * 删除 AI 账户
   */
  deleteAccount(id: number): Promise<void> {
    return del<void>(`/accounts/${id}`)
  },

  /**
   * 切换 AI 账户的启用/禁用状态
   */
  toggleAccountStatus(id: number): Promise<AIAccountDto> {
    return patch<AIAccountDto>(`/accounts/${id}/toggle-status`, {})
  },

  /**
   * 批量获取账户配额状态
   */
  getAccountQuotaStatuses(accountIds: number[]): Promise<Record<number, AccountQuotaStatus>> {
    return post<Record<number, AccountQuotaStatus>>('/accounts/quota-statuses', accountIds)
  },

  /**
   * 刷新 OpenAI 账户配额状态
   */
  refreshOpenAIQuotaStatus(accountId: number): Promise<AccountQuotaStatus> {
    return post<AccountQuotaStatus>(`/accounts/${accountId}/refresh-openai-quota`, {})
  },

  /**
   * 刷新 Antigravity 账户配额状态
   */
  refreshAntigravityQuotaStatus(accountId: number): Promise<AccountQuotaStatus> {
    return post<AccountQuotaStatus>(`/accounts/${accountId}/refresh-antigravity-quota`, {})
  },

  /**
   * 刷新 Claude 账户配额状态
   */
  refreshClaudeQuotaStatus(accountId: number): Promise<AccountQuotaStatus> {
    return post<AccountQuotaStatus>(`/accounts/${accountId}/refresh-claude-quota`, {})
  },

  /**
   * 刷新 Factory 账户配额状态
   */
  refreshFactoryQuotaStatus(accountId: number): Promise<AccountQuotaStatus> {
    return post<AccountQuotaStatus>(`/accounts/${accountId}/refresh-factory-quota`, {})
  },

  /**
   * 获取 Gemini Antigravity 可用模型列表
   */
  getAntigravityModels(accountId: number): Promise<string[]> {
    return get<string[]>(`/accounts/${accountId}/antigravity-models`)
  },

  /**
   * 刷新 Kiro 账户配额状态
   */
  refreshKiroQuotaStatus(accountId: number): Promise<AccountQuotaStatus> {
    return post<AccountQuotaStatus>(`/accounts/${accountId}/refresh-kiro-quota`, {})
  },
}

/**
 * OpenAI OAuth 服务
 */
export const openaiOAuthService = {
  /**
   * 生成 OpenAI OAuth 授权链接
   */
  generateOAuthUrl(proxy?: any): Promise<GenerateOAuthUrlResponse> {
    return post<GenerateOAuthUrlResponse>('/openai/oauth/authorize', {
      proxy: proxy || null
    })
  },

  /**
   * 交换授权码获取 Token 并创建账户
   */
  exchangeOAuthCode(request: ExchangeOAuthCodeRequest): Promise<AIAccountDto> {
    return post<AIAccountDto>('/openai/oauth/callback', request)
  },
}

/**
 * Claude OAuth 服务
 */
export const claudeOAuthService = {
  /**
   * 生成 Claude OAuth 授权链接
   */
  generateOAuthUrl(proxy?: any): Promise<GenerateOAuthUrlResponse> {
    return post<GenerateOAuthUrlResponse>('/claude/oauth/authorize', {
      proxy: proxy || null
    })
  },

  /**
   * 交换授权码获取 Token 并创建账户
   */
  exchangeOAuthCode(request: ExchangeOAuthCodeRequest): Promise<AIAccountDto> {
    return post<AIAccountDto>('/claude/oauth/callback', request)
  },
}

/**
 * Factory OAuth（WorkOS 设备码）服务
 */
export const factoryOAuthService = {
  /**
   * 生成 Factory OAuth Device Code
   */
  generateDeviceCode(proxy?: any): Promise<GenerateFactoryDeviceCodeResponse> {
    return post<GenerateFactoryDeviceCodeResponse>('/factory/oauth/authorize', {
      proxy: proxy || null,
    })
  },

  /**
   * 完成设备码授权并创建账户（服务端会轮询授权结果）
   */
  exchangeDeviceCode(request: ExchangeFactoryDeviceCodeRequest): Promise<AIAccountDto> {
    return post<AIAccountDto>('/factory/oauth/callback', request, { timeout: 360000 })
  },
}

/**
 * Gemini OAuth 服务
 */
export const geminiOAuthService = {
  /**
   * 生成 Gemini OAuth 授权链接
   */
  generateOAuthUrl(proxy?: any): Promise<GenerateOAuthUrlResponse> {
    return post<GenerateOAuthUrlResponse>('/gemini/oauth/authorize', {
      proxy: proxy || null
    })
  },

  /**
   * 交换授权码获取 Token 并创建账户
   */
  exchangeOAuthCode(request: ExchangeOAuthCodeRequest): Promise<AIAccountDto> {
    return post<AIAccountDto>('/gemini/oauth/callback', request)
  },
}

/**
 * Gemini Antigravity OAuth 服务
 */
export const geminiAntigravityOAuthService = {
  /**
   * 生成 Gemini Antigravity OAuth 授权链接
   */
  generateOAuthUrl(proxy?: any): Promise<GenerateOAuthUrlResponse> {
    return post<GenerateOAuthUrlResponse>('/gemini/oauth/authorize', {
      proxy: proxy || null
    })
  },

  /**
   * 交换授权码获取 Token 并创建账户
   */
  exchangeOAuthCode(request: ExchangeOAuthCodeRequest): Promise<AIAccountDto> {
    return post<AIAccountDto>('/gemini/oauth/callback', request)
  },
}

/**
 * Kiro credentials import service
 */
export const kiroOAuthService = {
  /**
   * Import Kiro credentials and create account
   */
  importCredentials(request: ImportKiroCredentialsRequest): Promise<AIAccountDto> {
    return post<AIAccountDto>('/kiro/oauth/import', request)
  },
}
