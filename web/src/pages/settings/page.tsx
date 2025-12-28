import { useEffect, useState } from 'react'
import { motion } from 'motion/react'
import { AlertCircle, Copy, Eye, EyeOff, Loader, Save, Key, Clock } from 'lucide-react'
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/animate-ui/components/card'
import { Input } from '@/components/animate-ui/components/input'
import { Label } from '@/components/animate-ui/components/label'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/animate-ui/components/radix/dialog'
import { Button } from '@/components/animate-ui/components/buttons/button'
import { settingsService } from '@/services/settings'
import type {
  SystemSettingsDto,
  ApiKeyInfoResponse,
  TokenRefreshInfoResponse,
} from '@/types/settings'

export default function SettingsPage() {
  const [settings, setSettings] = useState<Record<string, SystemSettingsDto>>({})
  const [apiKeyInfo, setApiKeyInfo] = useState<ApiKeyInfoResponse | null>(null)
  const [tokenRefreshInfo, setTokenRefreshInfo] = useState<TokenRefreshInfoResponse | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [editingKey, setEditingKey] = useState<string | null>(null)
  const [editValue, setEditValue] = useState('')
  const [saving, setSaving] = useState(false)
  const [showApiKey, setShowApiKey] = useState(false)
  const [copied, setCopied] = useState<string | null>(null)

  useEffect(() => {
    loadSettings()
    loadSystemInfo()
    // 定期刷新 token 信息
    const interval = setInterval(loadSystemInfo, 30000)
    return () => clearInterval(interval)
  }, [])

  const loadSettings = async () => {
    try {
      setLoading(true)
      setError(null)
      const data = await settingsService.getAllSettings()
      setSettings(data)
    } catch (err) {
      const message = err instanceof Error ? err.message : '加载设置失败'
      setError(message)
      console.error('Failed to load settings:', err)
    } finally {
      setLoading(false)
    }
  }

  const loadSystemInfo = async () => {
    try {
      const [apiKey, tokenRefresh] = await Promise.all([
        settingsService.getApiKeyInfo(),
        settingsService.getTokenRefreshInfo(),
      ])
      setApiKeyInfo(apiKey)
      setTokenRefreshInfo(tokenRefresh)
    } catch (err) {
      console.error('Failed to load system info:', err)
    }
  }

  const handleEditClick = (setting: SystemSettingsDto) => {
    setEditingKey(setting.key)
    setEditValue(setting.value || '')
  }

  const handleSave = async () => {
    if (!editingKey) return

    try {
      setSaving(true)
      await settingsService.updateSetting(editingKey, {
        value: editValue,
        description: settings[editingKey]?.description || null,
      })
      setSettings({
        ...settings,
        [editingKey]: {
          ...settings[editingKey],
          value: editValue,
          updatedAt: new Date().toISOString(),
        },
      })
      setEditingKey(null)
      setEditValue('')
    } catch (err) {
      const message = err instanceof Error ? err.message : '保存设置失败'
      setError(message)
      console.error('Failed to save setting:', err)
    } finally {
      setSaving(false)
    }
  }

  const handleCopy = async (value: string | null, key: string) => {
    if (!value) return
    try {
      await navigator.clipboard.writeText(value)
      setCopied(key)
      setTimeout(() => setCopied(null), 2000)
    } catch (err) {
      console.error('Failed to copy:', err)
    }
  }

  const formatDate = (dateString: string | null) => {
    if (!dateString) return '-'
    return new Date(dateString).toLocaleDateString('zh-CN', {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
    })
  }

  const formatSeconds = (seconds: number | null) => {
    if (seconds === null) return '-'
    const minutes = Math.floor(seconds / 60)
    const secs = seconds % 60
    return `${minutes}分${secs}秒`
  }

  return (
    <div className="p-6 space-y-6">
      <motion.div
        initial={{ opacity: 0, y: 20 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.5 }}
        className="space-y-4"
      >
        <div>
          <h2 className="text-3xl font-bold">系统设置</h2>
          <p className="text-muted-foreground mt-1">
            管理系统配置,包括 OAuth 设置、API Key 和 Token 刷新策略
          </p>
        </div>
      </motion.div>

      {error && (
        <motion.div
          initial={{ opacity: 0, y: -10 }}
          animate={{ opacity: 1, y: 0 }}
          className="flex gap-3 rounded-lg border border-red-200 bg-red-50 p-4 text-red-800 dark:border-red-800 dark:bg-red-950 dark:text-red-200"
        >
          <AlertCircle className="h-5 w-5 mt-0.5 flex-shrink-0" />
          <div>
            <p className="font-medium">出错了</p>
            <p className="text-sm">{error}</p>
          </div>
        </motion.div>
      )}

      {loading ? (
        <motion.div
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          className="flex items-center justify-center py-12"
        >
          <Loader className="h-6 w-6 animate-spin text-primary" />
          <span className="ml-2 text-muted-foreground">加载中...</span>
        </motion.div>
      ) : (
        <>
          {/* System Info Cards */}
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            transition={{ staggerChildren: 0.1, delayChildren: 0.1 }}
            className="grid gap-4 md:grid-cols-2"
          >
            {/* API Key Info */}
            <motion.div
              initial={{ opacity: 0, y: 20 }}
              animate={{ opacity: 1, y: 0 }}
            >
              <Card variant="elevated">
                <CardHeader>
                  <div className="flex items-start justify-between">
                    <div className="flex items-center gap-3">
                      <div className="p-2 rounded-lg bg-blue-100 dark:bg-blue-900">
                        <Key className="h-5 w-5 text-blue-600 dark:text-blue-300" />
                      </div>
                      <div>
                        <CardTitle className="text-base">API Key 信息</CardTitle>
                        <CardDescription className="text-xs">
                          服务认证凭证
                        </CardDescription>
                      </div>
                    </div>
                  </div>
                </CardHeader>
                <CardContent className="space-y-4">
                  <div>
                    <Label className="text-xs text-muted-foreground">状态</Label>
                    <p className="mt-1">
                      {apiKeyInfo?.hasApiKey ? (
                        <span className="inline-flex items-center rounded-full bg-green-100 px-3 py-1 text-xs font-medium text-green-800 dark:bg-green-900 dark:text-green-200">
                          已配置
                        </span>
                      ) : (
                        <span className="inline-flex items-center rounded-full bg-gray-100 px-3 py-1 text-xs font-medium text-gray-800 dark:bg-gray-800 dark:text-gray-200">
                          未配置
                        </span>
                      )}
                    </p>
                  </div>
                  {apiKeyInfo?.maskedApiKey && (
                    <div>
                      <Label className="text-xs text-muted-foreground">脱敏显示</Label>
                      <div className="mt-1 flex items-center gap-2">
                        <code className="flex-1 rounded bg-muted px-2 py-1 text-xs font-mono">
                          {apiKeyInfo.maskedApiKey}
                        </code>
                        <button
                          onClick={() => handleCopy(apiKeyInfo.maskedApiKey, 'apikey')}
                          className="p-1 hover:bg-muted rounded transition-colors"
                          title="复制"
                        >
                          <Copy className="h-4 w-4" />
                        </button>
                      </div>
                    </div>
                  )}
                  {apiKeyInfo?.apiKeyLength && (
                    <div>
                      <Label className="text-xs text-muted-foreground">长度</Label>
                      <p className="mt-1 text-sm">{apiKeyInfo.apiKeyLength} 字符</p>
                    </div>
                  )}
                </CardContent>
              </Card>
            </motion.div>

            {/* Token Refresh Info */}
            <motion.div
              initial={{ opacity: 0, y: 20 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: 0.1 }}
            >
              <Card variant="elevated">
                <CardHeader>
                  <div className="flex items-start justify-between">
                    <div className="flex items-center gap-3">
                      <div className="p-2 rounded-lg bg-purple-100 dark:bg-purple-900">
                        <Clock className="h-5 w-5 text-purple-600 dark:text-purple-300" />
                      </div>
                      <div>
                        <CardTitle className="text-base">Token 信息</CardTitle>
                        <CardDescription className="text-xs">
                          认证令牌刷新状态
                        </CardDescription>
                      </div>
                    </div>
                  </div>
                </CardHeader>
                <CardContent className="space-y-4">
                  {tokenRefreshInfo?.tokenExpiresAt ? (
                    <>
                      <div>
                        <Label className="text-xs text-muted-foreground">过期时间</Label>
                        <p className="mt-1 text-sm">
                          {formatDate(tokenRefreshInfo.tokenExpiresAt)}
                        </p>
                      </div>
                      <div>
                        <Label className="text-xs text-muted-foreground">剩余时间</Label>
                        <p className="mt-1 text-sm">
                          {formatSeconds(tokenRefreshInfo.secondsUntilExpiry)}
                        </p>
                      </div>
                      <div>
                        <Label className="text-xs text-muted-foreground">刷新状态</Label>
                        <p className="mt-1">
                          {tokenRefreshInfo.needsRefresh ? (
                            <span className="inline-flex items-center rounded-full bg-yellow-100 px-3 py-1 text-xs font-medium text-yellow-800 dark:bg-yellow-900 dark:text-yellow-200">
                              需要刷新
                            </span>
                          ) : (
                            <span className="inline-flex items-center rounded-full bg-green-100 px-3 py-1 text-xs font-medium text-green-800 dark:bg-green-900 dark:text-green-200">
                              有效
                            </span>
                          )}
                        </p>
                      </div>
                    </>
                  ) : (
                    <p className="text-sm text-muted-foreground">未登录或无有效 Token</p>
                  )}
                </CardContent>
              </Card>
            </motion.div>
          </motion.div>

          {/* Settings */}
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            transition={{ delay: 0.2 }}
          >
            <Card variant="elevated">
              <CardHeader>
                <CardTitle>系统设置项</CardTitle>
                <CardDescription>
                  管理系统各项配置参数
                </CardDescription>
              </CardHeader>
              <CardContent className="space-y-6">
                {Object.entries(settings).length === 0 ? (
                  <p className="text-muted-foreground text-sm">暂无设置项</p>
                ) : (
                  <div className="space-y-4">
                    {Object.entries(settings).map(([key, setting]) => (
                      <div
                        key={key}
                        className="flex flex-col gap-4 p-4 rounded-lg border border-border/50 bg-muted/30 hover:bg-muted/50 transition-colors"
                      >
                        <div className="flex items-start justify-between gap-4">
                          <div className="flex-1 min-w-0">
                            <Label className="text-sm font-semibold">{setting.key}</Label>
                            {setting.description && (
                              <p className="text-xs text-muted-foreground mt-1">
                                {setting.description}
                              </p>
                            )}
                          </div>
                          {setting.isEditable ? (
                            <button
                              onClick={() => handleEditClick(setting)}
                              className="px-3 py-1 text-xs rounded bg-primary text-primary-foreground hover:opacity-90 transition-opacity whitespace-nowrap"
                            >
                              编辑
                            </button>
                          ) : (
                            <span className="text-xs px-2 py-1 rounded bg-muted text-muted-foreground">
                              只读
                            </span>
                          )}
                        </div>

                        {setting.value && (
                          <div className="flex items-center gap-2">
                            {key.includes('api_key') ? (
                              <button
                                onClick={() => setShowApiKey(!showApiKey)}
                                className="p-1 hover:bg-background rounded transition-colors"
                                title={showApiKey ? '隐藏' : '显示'}
                              >
                                {showApiKey ? (
                                  <EyeOff className="h-4 w-4" />
                                ) : (
                                  <Eye className="h-4 w-4" />
                                )}
                              </button>
                            ) : null}
                            <code className="flex-1 truncate rounded bg-background px-2 py-1 text-xs font-mono">
                              {key.includes('api_key') && !showApiKey
                                ? '••••••••'
                                : setting.value}
                            </code>
                            <button
                              onClick={() => handleCopy(setting.value, key)}
                              className="p-1 hover:bg-background rounded transition-colors"
                              title={copied === key ? '已复制' : '复制'}
                            >
                              <Copy className="h-4 w-4" />
                            </button>
                          </div>
                        )}

                        {!setting.value && (
                          <p className="text-xs text-muted-foreground italic">
                            未设置
                          </p>
                        )}

                        <div className="text-xs text-muted-foreground">
                          {setting.updatedAt ? (
                            <span>最后更新: {formatDate(setting.updatedAt)}</span>
                          ) : (
                            <span>创建于: {formatDate(setting.createdAt)}</span>
                          )}
                        </div>
                      </div>
                    ))}
                  </div>
                )}
              </CardContent>
            </Card>
          </motion.div>
        </>
      )}

      {/* Edit Dialog */}
      <Dialog open={editingKey !== null} onOpenChange={(open) => {
        if (!open) setEditingKey(null)
      }}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>编辑设置</DialogTitle>
            <DialogDescription>
              修改 {editingKey} 的值
            </DialogDescription>
          </DialogHeader>

          {editingKey && settings[editingKey] && (
            <div className="space-y-4 py-4">
              <div>
                <Label className="text-sm">{editingKey}</Label>
                <p className="text-xs text-muted-foreground mt-1">
                  {settings[editingKey].description || '无描述'}
                </p>
              </div>

              <div>
                <Label className="text-sm mb-2 block">新值</Label>
                <textarea
                  value={editValue}
                  onChange={(e) => setEditValue(e.target.value)}
                  className="w-full px-3 py-2 rounded border border-input bg-background text-foreground placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-primary resize-none min-h-[100px]"
                  placeholder="输入新值"
                />
              </div>
            </div>
          )}

          <DialogFooter className="flex gap-3 justify-end">
            <Button
              variant="outline"
              onClick={() => setEditingKey(null)}
              disabled={saving}
            >
              取消
            </Button>
            <Button
              onClick={handleSave}
              disabled={saving}
              className="gap-2"
            >
              {saving ? (
                <>
                  <Loader className="h-4 w-4 animate-spin" />
                  保存中
                </>
              ) : (
                <>
                  <Save className="h-4 w-4" />
                  保存
                </>
              )}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  )
}
