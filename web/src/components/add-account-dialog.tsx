import { useState } from 'react'
import { motion } from 'motion/react'
import { AlertCircle, Loader, ExternalLink, Copy, Check } from 'lucide-react'
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription } from '@/components/animate-ui/components/radix/dialog'
import { Button } from '@/components/animate-ui/components/buttons/button'
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/animate-ui/components/card'
import { openaiOAuthService, claudeOAuthService, factoryOAuthService, geminiOAuthService, geminiAntigravityOAuthService, kiroOAuthService, geminiBusinessOAuthService } from '@/services/account'
import type { AccountType, GenerateFactoryDeviceCodeResponse, AIAccountDto } from '@/types/account'

interface AddAccountDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  onAccountAdded?: (account: AIAccountDto) => void
}

export function AddAccountDialog({ open, onOpenChange, onAccountAdded }: AddAccountDialogProps) {
  const [accountType, setAccountType] = useState<AccountType>('openai')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [authUrl, setAuthUrl] = useState<string | null>(null)
  const [factoryDeviceCode, setFactoryDeviceCode] = useState<GenerateFactoryDeviceCodeResponse | null>(null)
  const [sessionId, setSessionId] = useState<string | null>(null)
  const [authCode, setAuthCode] = useState('')
  const [projectId, setProjectId] = useState('') // Gemini 项目 ID（可选）
  const [processingCode, setProcessingCode] = useState(false)
  const [copied, setCopied] = useState(false)
  const [copiedUserCode, setCopiedUserCode] = useState(false)
  const [kiroCredentials, setKiroCredentials] = useState('')
  const [kiroAccountName, setKiroAccountName] = useState('')
  const [kiroEmail, setKiroEmail] = useState('')
  const [geminiBusinessCredentials, setGeminiBusinessCredentials] = useState('')
  const [geminiBusinessAccountName, setGeminiBusinessAccountName] = useState('')
  const [geminiBusinessEmail, setGeminiBusinessEmail] = useState('')
  const [proxyEnabled, setProxyEnabled] = useState(false)
  const [proxyType, setProxyType] = useState<'http' | 'https' | 'socks5'>('http')
  const [proxyHost, setProxyHost] = useState('')
  const [proxyPort, setProxyPort] = useState('')
  const [proxyUsername, setProxyUsername] = useState('')
  const [proxyPassword, setProxyPassword] = useState('')

  const handleStartOAuth = async () => {
    try {
      setLoading(true)
      setError(null)

      if (accountType === 'factory') {
        const proxy = proxyEnabled
          ? {
              type: proxyType,
              host: proxyHost.trim(),
              port: Number(proxyPort),
              username: proxyUsername.trim() || undefined,
              password: proxyPassword || undefined,
            }
          : undefined

        if (proxyEnabled) {
          if (!proxy) {
            setError('请填写代理配置')
            return
          }
          if (!proxy.host) {
            setError('请填写代理 Host')
            return
          }
          if (!proxy.port || Number.isNaN(proxy.port)) {
            setError('请填写正确的代理端口')
            return
          }
        }

        const response = await factoryOAuthService.generateDeviceCode(proxy)
        setFactoryDeviceCode(response)
        setSessionId(response.sessionId)

        window.open(response.verificationUriComplete, '_blank')
        return
      }

      const oauthService = accountType === 'claude'
        ? claudeOAuthService
        : accountType === 'gemini'
        ? geminiOAuthService
        : accountType === 'gemini-antigravity'
        ? geminiAntigravityOAuthService
        : openaiOAuthService

      const proxy = proxyEnabled
        ? {
            type: proxyType,
            host: proxyHost.trim(),
            port: Number(proxyPort),
            username: proxyUsername.trim() || undefined,
            password: proxyPassword || undefined,
          }
        : undefined

      if (proxyEnabled) {
        if (!proxy) {
          setError('请填写代理配置')
          return
        }
        if (!proxy.host) {
          setError('请填写代理 Host')
          return
        }
        if (!proxy.port || Number.isNaN(proxy.port)) {
          setError('请填写正确的代理端口')
          return
        }
      }

      const response = await oauthService.generateOAuthUrl(proxy)
      setAuthUrl(response.authUrl)
      setSessionId(response.sessionId)

      // 自动打开授权链接到新标签页
      window.open(response.authUrl, '_blank')
    } catch (err) {
      const message = err instanceof Error ? err.message : '生成授权链接失败'
      setError(message)
      console.error('Failed to generate auth URL:', err)
    } finally {
      setLoading(false)
    }
  }

  const handleExchangeCode = async () => {
    if (!authCode.trim() || !sessionId) {
      setError('请输入授权码')
      return
    }

    try {
      setProcessingCode(true)
      setError(null)

      const oauthService = accountType === 'claude'
        ? claudeOAuthService
        : accountType === 'gemini'
        ? geminiOAuthService
        : accountType === 'gemini-antigravity'
        ? geminiAntigravityOAuthService
        : openaiOAuthService

      const proxy = proxyEnabled
        ? {
            type: proxyType,
            host: proxyHost.trim(),
            port: Number(proxyPort),
            username: proxyUsername.trim() || undefined,
            password: proxyPassword || undefined,
          }
        : undefined

      if (proxyEnabled) {
        if (!proxy) {
          setError('请填写代理配置')
          return
        }
        if (!proxy.host) {
          setError('请填写代理 Host')
          return
        }
        if (!proxy.port || Number.isNaN(proxy.port)) {
          setError('请填写正确的代理端口')
          return
        }
      }

      const account = await oauthService.exchangeOAuthCode({
        sessionId,
        authorizationCode: authCode,
        projectId: (accountType === 'gemini' || accountType === 'gemini-antigravity') && projectId.trim() ? projectId.trim() : undefined, // 仅 Gemini 使用
        proxy,
      })

      onAccountAdded?.(account)
      resetForm()
      onOpenChange(false)
    } catch (err) {
      const message = err instanceof Error ? err.message : '处理授权码失败'
      setError(message)
      console.error('Failed to exchange code:', err)
    } finally {
      setProcessingCode(false)
    }
  }

  const handleImportKiroCredentials = async () => {
    if (!kiroCredentials.trim()) {
      setError('请粘贴 Kiro 凭证内容')
      return
    }

    try {
      setProcessingCode(true)
      setError(null)

      const account = await kiroOAuthService.importCredentials({
        credentials: kiroCredentials.trim(),
        accountName: kiroAccountName.trim() || undefined,
        email: kiroEmail.trim() || undefined,
      })

      onAccountAdded?.(account)
      resetForm()
      onOpenChange(false)
    } catch (err) {
      const message = err instanceof Error ? err.message : '导入 Kiro 凭证失败'
      setError(message)
      console.error('Failed to import Kiro credentials:', err)
    } finally {
      setProcessingCode(false)
    }
  }

  const handleImportGeminiBusinessCredentials = async () => {
    if (!geminiBusinessCredentials.trim()) {
      setError('请粘贴 Gemini Business 凭证内容')
      return
    }

    try {
      setProcessingCode(true)
      setError(null)

      const account = await geminiBusinessOAuthService.importCredentials({
        credentials: geminiBusinessCredentials.trim(),
        accountName: geminiBusinessAccountName.trim() || undefined,
        email: geminiBusinessEmail.trim() || undefined,
      })

      onAccountAdded?.(account)
      resetForm()
      onOpenChange(false)
    } catch (err) {
      const message = err instanceof Error ? err.message : '导入 Gemini Business 凭证失败'
      setError(message)
      console.error('Failed to import Gemini Business credentials:', err)
    } finally {
      setProcessingCode(false)
    }
  }

  const handleCompleteFactoryDeviceCode = async () => {
    if (!sessionId) {
      setError('请先生成 Device Code')
      return
    }

    try {
      setProcessingCode(true)
      setError(null)

      const proxy = proxyEnabled
        ? {
            type: proxyType,
            host: proxyHost.trim(),
            port: Number(proxyPort),
            username: proxyUsername.trim() || undefined,
            password: proxyPassword || undefined,
          }
        : undefined

      if (proxyEnabled) {
        if (!proxy) {
          setError('请填写代理配置')
          return
        }
        if (!proxy.host) {
          setError('请填写代理 Host')
          return
        }
        if (!proxy.port || Number.isNaN(proxy.port)) {
          setError('请填写正确的代理端口')
          return
        }
      }

      const account = await factoryOAuthService.exchangeDeviceCode({
        sessionId,
        proxy,
      })

      onAccountAdded?.(account)
      resetForm()
      onOpenChange(false)
    } catch (err) {
      const message = err instanceof Error ? err.message : '处理 Device Code 授权失败'
      setError(message)
      console.error('Failed to complete device code auth:', err)
    } finally {
      setProcessingCode(false)
    }
  }

  const handleCopyAuthUrl = () => {
    if (authUrl) {
      navigator.clipboard.writeText(authUrl)
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    }
  }

  const handleCopyUserCode = () => {
    if (factoryDeviceCode?.userCode) {
      navigator.clipboard.writeText(factoryDeviceCode.userCode)
      setCopiedUserCode(true)
      setTimeout(() => setCopiedUserCode(false), 2000)
    }
  }

  const resetForm = () => {
    setAuthUrl(null)
    setFactoryDeviceCode(null)
    setSessionId(null)
    setAuthCode('')
    setProjectId('') // 重置 ProjectId
    setError(null)
    setCopied(false)
    setCopiedUserCode(false)
    setKiroCredentials('')
    setKiroAccountName('')
    setKiroEmail('')
    setGeminiBusinessCredentials('')
    setGeminiBusinessAccountName('')
    setGeminiBusinessEmail('')
    setProxyEnabled(false)
  }

  const handleOpenChange = (newOpen: boolean) => {
    if (!newOpen) {
      resetForm()
    }
    onOpenChange(newOpen)
  }

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogContent className="max-w-4xl w-full">
        <DialogHeader>
          <DialogTitle>添加 AI 账户</DialogTitle>
          <DialogDescription>
            选择账户类型并按照步骤进行授权
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-6">
          {/* Account Type Tabs */}
          <div className="flex flex-wrap gap-2 border-b pb-2">
            <button
              onClick={() => {
                setAccountType('openai')
                resetForm()
              }}
              className={`px-4 py-2 font-medium border-b-2 transition-colors ${
                accountType === 'openai'
                  ? 'border-primary text-primary'
                  : 'border-transparent text-muted-foreground hover:text-foreground'
              }`}
            >
              OpenAI
            </button>
            <button
              onClick={() => {
                setAccountType('claude')
                resetForm()
              }}
              className={`px-4 py-2 font-medium border-b-2 transition-colors ${
                accountType === 'claude'
                  ? 'border-primary text-primary'
                  : 'border-transparent text-muted-foreground hover:text-foreground'
              }`}
            >
              Claude
            </button>
            <button
              onClick={() => {
                setAccountType('factory')
                resetForm()
              }}
              className={`px-4 py-2 font-medium border-b-2 transition-colors ${
                accountType === 'factory'
                  ? 'border-primary text-primary'
                  : 'border-transparent text-muted-foreground hover:text-foreground'
              }`}
            >
              Factory
            </button>
            <button
              onClick={() => {
                setAccountType('gemini')
                resetForm()
              }}
              className={`px-4 py-2 font-medium border-b-2 transition-colors ${
                accountType === 'gemini'
                  ? 'border-primary text-primary'
                  : 'border-transparent text-muted-foreground hover:text-foreground'
              }`}
            >
              Gemini
            </button>
            <button
              onClick={() => {
                setAccountType('gemini-antigravity')
                resetForm()
              }}
              className={`px-4 py-2 font-medium border-b-2 transition-colors ${
                accountType === 'gemini-antigravity'
                  ? 'border-primary text-primary'
                  : 'border-transparent text-muted-foreground hover:text-foreground'
              }`}
            >
              Gemini-Antigravity
            </button>
            <button
              onClick={() => {
                setAccountType('gemini-business')
                resetForm()
              }}
              className={`px-4 py-2 font-medium border-b-2 transition-colors ${
                accountType === 'gemini-business'
                  ? 'border-primary text-primary'
                  : 'border-transparent text-muted-foreground hover:text-foreground'
              }`}
            >
              Gemini-Business
            </button>
            <button
              onClick={() => {
                setAccountType('kiro')
                resetForm()
              }}
              className={`px-4 py-2 font-medium border-b-2 transition-colors ${
                accountType === 'kiro'
                  ? 'border-primary text-primary'
                  : 'border-transparent text-muted-foreground hover:text-foreground'
              }`}
            >
              Kiro
            </button>
          </div>

          {/* Error Message */}
          {error && (
            <motion.div
              initial={{ opacity: 0, y: -10 }}
              animate={{ opacity: 1, y: 0 }}
              className="flex gap-3 rounded-lg border border-red-200 bg-red-50 p-4 text-red-800 dark:border-red-800 dark:bg-red-950 dark:text-red-200"
            >
              <AlertCircle className="h-5 w-5 mt-0.5 flex-shrink-0" />
              <p className="text-sm">{error}</p>
            </motion.div>
          )}

          {/* OpenAI OAuth Flow */}
          {accountType === 'openai' && (
            <motion.div
              initial={{ opacity: 0, y: 20 }}
              animate={{ opacity: 1, y: 0 }}
              className="space-y-4"
            >
              {!authUrl ? (
                <Card>
                  <CardHeader>
                    <CardTitle className="text-base">OpenAI OAuth 授权</CardTitle>
                    <CardDescription>
                      使用 OpenAI 官方账户安全授权
                    </CardDescription>
                  </CardHeader>
                  <CardContent className="space-y-4">
                    <p className="text-sm text-muted-foreground">
                      点击下方按钮，我们将引导您到 OpenAI 官网进行安全授权。授权后，您将获得一个授权码，请复制该授权码并粘贴到下方。
                    </p>
                    <Button
                      onClick={handleStartOAuth}
                      disabled={loading}
                      className="w-full gap-2"
                    >
                      {loading ? (
                        <>
                          <Loader className="h-4 w-4 animate-spin" />
                          生成授权链接中...
                        </>
                      ) : (
                        <>
                          <ExternalLink className="h-4 w-4" />
                          前往 OpenAI 授权
                        </>
                      )}
                    </Button>
                  </CardContent>
                </Card>
              ) : (
                <div className="space-y-4">
                  <Card>
                    <CardHeader>
                      <CardTitle className="text-base">步骤 1: 复制授权链接</CardTitle>
                    </CardHeader>
                    <CardContent className="space-y-3">
                      <p className="text-sm text-muted-foreground">
                        下方是您的授权链接，您可以复制后在浏览器中打开，或点击"打开链接"按钮直接打开。
                      </p>
                      <div className="flex flex-col gap-2">
                        <div className="rounded-md border border-input bg-background px-3 py-2 text-sm max-h-24 overflow-y-auto break-all">
                          <span className="text-muted-foreground text-xs leading-relaxed">{authUrl}</span>
                        </div>
                        <Button
                          variant="outline"
                          size="sm"
                          onClick={handleCopyAuthUrl}
                          className="gap-2 w-full"
                        >
                          {copied ? (
                            <>
                              <Check className="h-4 w-4" />
                              已复制
                            </>
                          ) : (
                            <>
                              <Copy className="h-4 w-4" />
                              复制链接
                            </>
                          )}
                        </Button>
                      </div>
                      <Button
                        variant="outline"
                        onClick={() => window.open(authUrl, '_blank')}
                        className="w-full gap-2"
                      >
                        <ExternalLink className="h-4 w-4" />
                        打开授权链接
                      </Button>
                    </CardContent>
                  </Card>

                  <Card>
                    <CardHeader>
                      <CardTitle className="text-base">步骤 2: 获取授权码</CardTitle>
                    </CardHeader>
                    <CardContent className="space-y-3">
                      <p className="text-sm text-muted-foreground">
                        在浏览器中完成授权后，系统会提示您一个授权码。请复制该授权码并粘贴到下方输入框。
                      </p>
                      <div className="space-y-2">
                        <label className="text-sm font-medium">授权码</label>
                        <input
                          type="text"
                          placeholder="粘贴您的授权码..."
                          value={authCode}
                          onChange={(e) => setAuthCode(e.target.value)}
                          className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-primary"
                        />
                      </div>
                    </CardContent>
                  </Card>
                </div>
              )}
            </motion.div>
          )}

          {/* Claude OAuth Flow */}
          {accountType === 'claude' && (
            <motion.div
              initial={{ opacity: 0, y: 20 }}
              animate={{ opacity: 1, y: 0 }}
              className="space-y-4"
            >
              <Card>
                <CardHeader>
                  <CardTitle className="text-base">代理（可选）</CardTitle>
                  <CardDescription>
                    如出现 403 / Cloudflare 拦截，通常需要使用可访问 Anthropic 的代理出口。
                  </CardDescription>
                </CardHeader>
                <CardContent className="space-y-3">
                  <label className="flex items-center gap-2 text-sm">
                    <input
                      type="checkbox"
                      checked={proxyEnabled}
                      onChange={(e) => setProxyEnabled(e.target.checked)}
                      className="h-4 w-4"
                    />
                    使用代理
                  </label>

                  {proxyEnabled && (
                    <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                      <div className="space-y-2">
                        <label className="text-sm font-medium">类型</label>
                        <select
                          value={proxyType}
                          onChange={(e) => setProxyType(e.target.value as 'http' | 'https' | 'socks5')}
                          className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
                        >
                          <option value="http">http</option>
                          <option value="https">https</option>
                          <option value="socks5">socks5</option>
                        </select>
                      </div>

                      <div className="space-y-2">
                        <label className="text-sm font-medium">Host</label>
                        <input
                          type="text"
                          placeholder="例如: 127.0.0.1"
                          value={proxyHost}
                          onChange={(e) => setProxyHost(e.target.value)}
                          className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-primary"
                        />
                      </div>

                      <div className="space-y-2">
                        <label className="text-sm font-medium">Port</label>
                        <input
                          type="text"
                          inputMode="numeric"
                          placeholder="例如: 7890"
                          value={proxyPort}
                          onChange={(e) => setProxyPort(e.target.value)}
                          className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-primary"
                        />
                      </div>

                      <div className="space-y-2">
                        <label className="text-sm font-medium">用户名（可选）</label>
                        <input
                          type="text"
                          value={proxyUsername}
                          onChange={(e) => setProxyUsername(e.target.value)}
                          className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-primary"
                        />
                      </div>

                      <div className="space-y-2 sm:col-span-2">
                        <label className="text-sm font-medium">密码（可选）</label>
                        <input
                          type="password"
                          value={proxyPassword}
                          onChange={(e) => setProxyPassword(e.target.value)}
                          className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-primary"
                        />
                      </div>
                    </div>
                  )}
                </CardContent>
              </Card>

              {!authUrl ? (
                <Card>
                  <CardHeader>
                    <CardTitle className="text-base">Claude OAuth 授权</CardTitle>
                    <CardDescription>
                      使用 Claude 官方账户安全授权
                    </CardDescription>
                  </CardHeader>
                  <CardContent className="space-y-4">
                    <p className="text-sm text-muted-foreground">
                      点击下方按钮，我们将引导您到 Claude 官网进行安全授权。授权后，您将获得一个授权码，请复制该授权码并粘贴到下方。
                    </p>
                    <Button
                      onClick={handleStartOAuth}
                      disabled={loading}
                      className="w-full gap-2"
                    >
                      {loading ? (
                        <>
                          <Loader className="h-4 w-4 animate-spin" />
                          生成授权链接中...
                        </>
                      ) : (
                        <>
                          <ExternalLink className="h-4 w-4" />
                          前往 Claude 授权
                        </>
                      )}
                    </Button>
                  </CardContent>
                </Card>
              ) : (
                <div className="space-y-4">
                  <Card>
                    <CardHeader>
                      <CardTitle className="text-base">步骤 1: 复制授权链接</CardTitle>
                    </CardHeader>
                    <CardContent className="space-y-3">
                      <p className="text-sm text-muted-foreground">
                        下方是您的授权链接，您可以复制后在浏览器中打开，或点击"打开链接"按钮直接打开。
                      </p>
                      <div className="flex flex-col gap-2">
                        <div className="rounded-md border border-input bg-background px-3 py-2 text-sm max-h-24 overflow-y-auto break-all">
                          <span className="text-muted-foreground text-xs leading-relaxed">{authUrl}</span>
                        </div>
                        <Button
                          variant="outline"
                          size="sm"
                          onClick={handleCopyAuthUrl}
                          className="gap-2 w-full"
                        >
                          {copied ? (
                            <>
                              <Check className="h-4 w-4" />
                              已复制
                            </>
                          ) : (
                            <>
                              <Copy className="h-4 w-4" />
                              复制链接
                            </>
                          )}
                        </Button>
                      </div>
                      <Button
                        variant="outline"
                        onClick={() => window.open(authUrl, '_blank')}
                        className="w-full gap-2"
                      >
                        <ExternalLink className="h-4 w-4" />
                        打开授权链接
                      </Button>
                    </CardContent>
                  </Card>

                  <Card>
                    <CardHeader>
                      <CardTitle className="text-base">步骤 2: 获取授权码</CardTitle>
                    </CardHeader>
                    <CardContent className="space-y-3">
                      <p className="text-sm text-muted-foreground">
                        在浏览器中完成授权后，系统会提示您一个授权码。请复制该授权码并粘贴到下方输入框。
                      </p>
                      <div className="space-y-2">
                        <label className="text-sm font-medium">授权码</label>
                        <input
                          type="text"
                          placeholder="粘贴您的授权码..."
                          value={authCode}
                          onChange={(e) => setAuthCode(e.target.value)}
                          className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-primary"
                        />
                      </div>
                    </CardContent>
                  </Card>
                </div>
              )}
            </motion.div>
          )}

          {/* Factory OAuth Flow */}
          {accountType === 'factory' && (
            <motion.div
              initial={{ opacity: 0, y: 20 }}
              animate={{ opacity: 1, y: 0 }}
              className="space-y-4"
            >
              {!factoryDeviceCode ? (
                <Card>
                  <CardHeader>
                    <CardTitle className="text-base">Factory OAuth 设备码授权</CardTitle>
                    <CardDescription>
                      使用 WorkOS Device Authorization Flow 完成登录授权
                    </CardDescription>
                  </CardHeader>
                  <CardContent className="space-y-4">
                    <p className="text-sm text-muted-foreground">
                      点击下方按钮生成 Device Code，然后在浏览器完成授权。授权完成后回到此处点击“完成授权”。
                    </p>
                    <Button
                      onClick={handleStartOAuth}
                      disabled={loading}
                      className="w-full gap-2"
                    >
                      {loading ? (
                        <>
                          <Loader className="h-4 w-4 animate-spin" />
                          生成 Device Code 中...
                        </>
                      ) : (
                        <>
                          <ExternalLink className="h-4 w-4" />
                          开始授权
                        </>
                      )}
                    </Button>
                  </CardContent>
                </Card>
              ) : (
                <div className="space-y-4">
                  <Card>
                    <CardHeader>
                      <CardTitle className="text-base">步骤 1: 打开授权链接</CardTitle>
                      <CardDescription>
                        在浏览器完成 Factory 授权（推荐使用“完整链接”）
                      </CardDescription>
                    </CardHeader>
                    <CardContent className="space-y-3">
                      <div className="flex flex-col gap-2">
                        <div className="rounded-md border border-input bg-background px-3 py-2 text-sm max-h-24 overflow-y-auto break-all">
                          <span className="text-muted-foreground text-xs leading-relaxed">
                            {factoryDeviceCode.verificationUriComplete}
                          </span>
                        </div>
                        <Button
                          variant="outline"
                          onClick={() => window.open(factoryDeviceCode.verificationUriComplete, '_blank')}
                          className="w-full gap-2"
                        >
                          <ExternalLink className="h-4 w-4" />
                          打开授权链接
                        </Button>
                      </div>

                      <div className="rounded-md border border-input bg-background px-3 py-2 text-sm">
                        <div className="flex items-center justify-between gap-3">
                          <span className="text-muted-foreground text-xs">User Code</span>
                          <span className="font-mono text-sm">{factoryDeviceCode.userCode}</span>
                        </div>
                      </div>

                      <Button
                        variant="outline"
                        size="sm"
                        onClick={handleCopyUserCode}
                        className="gap-2 w-full"
                      >
                        {copiedUserCode ? (
                          <>
                            <Check className="h-4 w-4" />
                            已复制
                          </>
                        ) : (
                          <>
                            <Copy className="h-4 w-4" />
                            复制 User Code
                          </>
                        )}
                      </Button>

                      <p className="text-xs text-muted-foreground">
                        有效期约 {Math.floor(factoryDeviceCode.expiresIn / 60)} 分钟，建议间隔 {factoryDeviceCode.interval} 秒后再点击“完成授权”。
                      </p>
                    </CardContent>
                  </Card>

                  <Card>
                    <CardHeader>
                      <CardTitle className="text-base">步骤 2: 完成授权</CardTitle>
                    </CardHeader>
                    <CardContent className="space-y-2">
                      <p className="text-sm text-muted-foreground">
                        浏览器里完成授权后，点击下方“完成授权”按钮，系统会自动轮询并创建账户。
                      </p>
                    </CardContent>
                  </Card>
                </div>
              )}
            </motion.div>
          )}

          {/* Gemini OAuth Flow */}
          {accountType === 'gemini' && (
            <motion.div
              initial={{ opacity: 0, y: 20 }}
              animate={{ opacity: 1, y: 0 }}
              className="space-y-4"
            >
              {!authUrl ? (
                <Card>
                  <CardHeader>
                    <CardTitle className="text-base">Google Gemini OAuth 授权</CardTitle>
                    <CardDescription>
                      使用 Google 账户安全授权，获取 Gemini 模型访问权限
                    </CardDescription>
                  </CardHeader>
                  <CardContent className="space-y-4">
                    <p className="text-sm text-muted-foreground">
                      点击下方按钮，我们将引导您到 Google 账户进行安全授权。授权后，您将获得一个授权码，请复制该授权码并粘贴到下方。
                    </p>
                    <Button
                      onClick={handleStartOAuth}
                      disabled={loading}
                      className="w-full gap-2"
                    >
                      {loading ? (
                        <>
                          <Loader className="h-4 w-4 animate-spin" />
                          生成授权链接中...
                        </>
                      ) : (
                        <>
                          <ExternalLink className="h-4 w-4" />
                          前往 Google 账户授权
                        </>
                      )}
                    </Button>
                  </CardContent>
                </Card>
              ) : (
                <div className="space-y-4">
                  <Card>
                    <CardHeader>
                      <CardTitle className="text-base">步骤 1: 复制授权链接</CardTitle>
                    </CardHeader>
                    <CardContent className="space-y-3">
                      <p className="text-sm text-muted-foreground">
                        下方是您的授权链接，您可以复制后在浏览器中打开，或点击"打开链接"按钮直接打开。
                      </p>
                      <div className="flex flex-col gap-2">
                        <div className="rounded-md border border-input bg-background px-3 py-2 text-sm max-h-24 overflow-y-auto break-all">
                          <span className="text-muted-foreground text-xs leading-relaxed">{authUrl}</span>
                        </div>
                        <Button
                          variant="outline"
                          size="sm"
                          onClick={handleCopyAuthUrl}
                          className="gap-2 w-full"
                        >
                          {copied ? (
                            <>
                              <Check className="h-4 w-4" />
                              已复制
                            </>
                          ) : (
                            <>
                              <Copy className="h-4 w-4" />
                              复制链接
                            </>
                          )}
                        </Button>
                      </div>
                      <Button
                        variant="outline"
                        onClick={() => window.open(authUrl, '_blank')}
                        className="w-full gap-2"
                      >
                        <ExternalLink className="h-4 w-4" />
                        打开授权链接
                      </Button>
                    </CardContent>
                  </Card>

                  <Card>
                    <CardHeader>
                      <CardTitle className="text-base">步骤 2: 获取授权码</CardTitle>
                    </CardHeader>
                    <CardContent className="space-y-3">
                      <p className="text-sm text-muted-foreground">
                        在浏览器中完成授权后，系统会提示您一个授权码。请复制该授权码并粘贴到下方输入框。
                      </p>
                      <div className="space-y-2">
                        <label className="text-sm font-medium">授权码</label>
                        <input
                          type="text"
                          placeholder="粘贴您的授权码..."
                          value={authCode}
                          onChange={(e) => setAuthCode(e.target.value)}
                          className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-primary"
                        />
                      </div>
                      <div className="space-y-2">
                        <label className="text-sm font-medium">
                          GCP 项目 ID
                          <span className="text-xs text-muted-foreground ml-1">(可选，不提供则自动检测)</span>
                        </label>
                        <input
                          type="text"
                          placeholder="例如: predictive-hexagon-zgcm5"
                          value={projectId}
                          onChange={(e) => setProjectId(e.target.value)}
                          className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-primary"
                        />
                        <p className="text-xs text-muted-foreground">
                          如果您有多个 GCP 项目，可在此指定要使用的项目ID。若不指定，系统将自动选择第一个可用的项目。
                        </p>
                      </div>
                    </CardContent>
                  </Card>
                </div>
              )}
            </motion.div>
          )}

          {/* Gemini-Antigravity OAuth Flow */}
          {accountType === 'gemini-antigravity' && (
            <motion.div
              initial={{ opacity: 0, y: 20 }}
              animate={{ opacity: 1, y: 0 }}
              className="space-y-4"
            >
              {!authUrl ? (
                <Card>
                  <CardHeader>
                    <CardTitle className="text-base">Google Gemini Antigravity OAuth 授权</CardTitle>
                    <CardDescription>
                      使用 Google 账户安全授权，获取 Gemini Antigravity 模型访问权限
                    </CardDescription>
                  </CardHeader>
                  <CardContent className="space-y-4">
                    <p className="text-sm text-muted-foreground">
                      点击下方按钮，我们将引导您到 Google 账户进行安全授权。授权后，您将获得一个授权码，请复制该授权码并粘贴到下方。
                    </p>
                    <Button
                      onClick={handleStartOAuth}
                      disabled={loading}
                      className="w-full gap-2"
                    >
                      {loading ? (
                        <>
                          <Loader className="h-4 w-4 animate-spin" />
                          生成授权链接中...
                        </>
                      ) : (
                        <>
                          <ExternalLink className="h-4 w-4" />
                          前往 Google 账户授权
                        </>
                      )}
                    </Button>
                  </CardContent>
                </Card>
              ) : (
                <div className="space-y-4">
                  <Card>
                    <CardHeader>
                      <CardTitle className="text-base">步骤 1: 复制授权链接</CardTitle>
                    </CardHeader>
                    <CardContent className="space-y-3">
                      <p className="text-sm text-muted-foreground">
                        下方是您的授权链接，您可以复制后在浏览器中打开，或点击"打开链接"按钮直接打开。
                      </p>
                      <div className="flex flex-col gap-2">
                        <div className="rounded-md border border-input bg-background px-3 py-2 text-sm max-h-24 overflow-y-auto break-all">
                          <span className="text-muted-foreground text-xs leading-relaxed">{authUrl}</span>
                        </div>
                        <Button
                          variant="outline"
                          size="sm"
                          onClick={handleCopyAuthUrl}
                          className="gap-2 w-full"
                        >
                          {copied ? (
                            <>
                              <Check className="h-4 w-4" />
                              已复制
                            </>
                          ) : (
                            <>
                              <Copy className="h-4 w-4" />
                              复制链接
                            </>
                          )}
                        </Button>
                      </div>
                      <Button
                        variant="outline"
                        onClick={() => window.open(authUrl, '_blank')}
                        className="w-full gap-2"
                      >
                        <ExternalLink className="h-4 w-4" />
                        打开授权链接
                      </Button>
                    </CardContent>
                  </Card>

                  <Card>
                    <CardHeader>
                      <CardTitle className="text-base">步骤 2: 获取授权码</CardTitle>
                    </CardHeader>
                    <CardContent className="space-y-3">
                      <p className="text-sm text-muted-foreground">
                        在浏览器中完成授权后，系统会提示您一个授权码。请复制该授权码并粘贴到下方输入框。
                      </p>
                      <div className="space-y-2">
                        <label className="text-sm font-medium">授权码</label>
                        <input
                          type="text"
                          placeholder="粘贴您的授权码..."
                          value={authCode}
                          onChange={(e) => setAuthCode(e.target.value)}
                          className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-primary"
                        />
                      </div>
                      <div className="space-y-2">
                        <label className="text-sm font-medium">
                          GCP 项目 ID
                          <span className="text-xs text-muted-foreground ml-1">(可选，不提供则自动检测)</span>
                        </label>
                        <input
                          type="text"
                          placeholder="例如: predictive-hexagon-zgcm5"
                          value={projectId}
                          onChange={(e) => setProjectId(e.target.value)}
                          className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-primary"
                        />
                        <p className="text-xs text-muted-foreground">
                          如果您有多个 GCP 项目，可在此指定要使用的项目ID。若不指定，系统将自动选择第一个可用的项目。
                        </p>
                      </div>
                    </CardContent>
                  </Card>
                </div>
              )}
            </motion.div>
          )}

          {/* Gemini Business Reverse Flow */}
          {accountType === 'gemini-business' && (
            <motion.div
              initial={{ opacity: 0, y: 20 }}
              animate={{ opacity: 1, y: 0 }}
              className="space-y-4"
            >
              <Card>
                <CardHeader>
                  <CardTitle className="text-base">Gemini Business 凭证导入</CardTitle>
                  <CardDescription>
                    粘贴 Gemini Business 凭证 JSON 或 Base64 内容（需包含 secure_c_ses / csesidx / config_id）
                  </CardDescription>
                </CardHeader>
                <CardContent className="space-y-4">
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                    <div className="space-y-2">
                      <label className="text-sm font-medium">账户名称（可选）</label>
                      <input
                        type="text"
                        placeholder="例如: GeminiBiz-主账号"
                        value={geminiBusinessAccountName}
                        onChange={(e) => setGeminiBusinessAccountName(e.target.value)}
                        className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-primary"
                      />
                    </div>
                    <div className="space-y-2">
                      <label className="text-sm font-medium">邮箱（可选）</label>
                      <input
                        type="email"
                        placeholder="账号邮箱"
                        value={geminiBusinessEmail}
                        onChange={(e) => setGeminiBusinessEmail(e.target.value)}
                        className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-primary"
                      />
                    </div>
                  </div>
                  <div className="space-y-2">
                    <label className="text-sm font-medium">Gemini Business 凭证内容</label>
                    <textarea
                      rows={6}
                      placeholder="粘贴单个账户 JSON（或 Base64）"
                      value={geminiBusinessCredentials}
                      onChange={(e) => setGeminiBusinessCredentials(e.target.value)}
                      className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-primary"
                    />
                    <p className="text-xs text-muted-foreground">
                      支持 JSON / Base64，两种格式任选其一。
                    </p>
                  </div>
                </CardContent>
              </Card>
            </motion.div>
          )}

          {/* Kiro Reverse Flow */}
          {accountType === 'kiro' && (
            <motion.div
              initial={{ opacity: 0, y: 20 }}
              animate={{ opacity: 1, y: 0 }}
              className="space-y-4"
            >
              <Card>
                <CardHeader>
                  <CardTitle className="text-base">Kiro 逆向凭证导入</CardTitle>
                  <CardDescription>
                    粘贴 Kiro 凭证 JSON 或 Base64 内容，创建可用账户
                  </CardDescription>
                </CardHeader>
                <CardContent className="space-y-4">
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                    <div className="space-y-2">
                      <label className="text-sm font-medium">账户名称（可选）</label>
                      <input
                        type="text"
                        placeholder="例如: Kiro-主账号"
                        value={kiroAccountName}
                        onChange={(e) => setKiroAccountName(e.target.value)}
                        className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-primary"
                      />
                    </div>
                    <div className="space-y-2">
                      <label className="text-sm font-medium">邮箱（可选）</label>
                      <input
                        type="email"
                        placeholder="账号邮箱"
                        value={kiroEmail}
                        onChange={(e) => setKiroEmail(e.target.value)}
                        className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-primary"
                      />
                    </div>
                  </div>
                  <div className="space-y-2">
                    <label className="text-sm font-medium">Kiro 凭证内容</label>
                    <textarea
                      rows={6}
                      placeholder="粘贴 kiro-auth-token.json 的完整内容，或 Base64 编码后的文本"
                      value={kiroCredentials}
                      onChange={(e) => setKiroCredentials(e.target.value)}
                      className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-primary"
                    />
                    <p className="text-xs text-muted-foreground">
                      支持 JSON / Base64，两种格式任选其一。
                    </p>
                  </div>
                </CardContent>
              </Card>
            </motion.div>
          )}

          {/* Action Buttons */}
          <div className="flex gap-3 justify-end">
            <Button
              variant="outline"
              onClick={() => handleOpenChange(false)}
              disabled={loading || processingCode}
            >
              取消
            </Button>
            {(accountType === 'factory'
              ? !!factoryDeviceCode
              : accountType === 'kiro' || accountType === 'gemini-business'
              ? true
              : !!authUrl) && (
              <Button
                onClick={accountType === 'factory'
                  ? handleCompleteFactoryDeviceCode
                  : accountType === 'kiro'
                  ? handleImportKiroCredentials
                  : accountType === 'gemini-business'
                  ? handleImportGeminiBusinessCredentials
                  : handleExchangeCode}
                disabled={accountType === 'factory'
                  ? processingCode
                  : accountType === 'kiro'
                  ? (!kiroCredentials.trim() || processingCode)
                  : accountType === 'gemini-business'
                  ? (!geminiBusinessCredentials.trim() || processingCode)
                  : (!authCode.trim() || processingCode)}
                className="gap-2"
              >
                {processingCode ? (
                  <>
                    <Loader className="h-4 w-4 animate-spin" />
                    处理中...
                  </>
                ) : (
                  accountType === 'kiro' || accountType === 'gemini-business' ? '保存凭证' : '完成授权'
                )}
              </Button>
            )}
          </div>
        </div>
      </DialogContent>
    </Dialog>
  )
}
