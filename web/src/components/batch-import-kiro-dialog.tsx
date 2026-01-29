import { useState } from 'react'
import { motion } from 'motion/react'
import { AlertCircle, Loader, Upload, FileText, Check, X, AlertTriangle } from 'lucide-react'
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription } from '@/components/animate-ui/components/radix/dialog'
import { Button } from '@/components/animate-ui/components/buttons/button'
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/animate-ui/components/card'
import { Checkbox } from '@/components/animate-ui/components/radix/checkbox'
import { kiroOAuthService } from '@/services/account'
import type { ImportKiroBatchRequest, KiroBatchAccountItem, ImportKiroBatchResult } from '@/types/account'

interface BatchImportKiroDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  onImportCompleted?: (result: ImportKiroBatchResult) => void
}

export function BatchImportKiroDialog({ open, onOpenChange, onImportCompleted }: BatchImportKiroDialogProps) {
  const [jsonText, setJsonText] = useState('')
  const [accountNamePrefix, setAccountNamePrefix] = useState('')
  const [skipExisting, setSkipExisting] = useState(true)
  const [processing, setProcessing] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [result, setResult] = useState<ImportKiroBatchResult | null>(null)
  const [parsedAccounts, setParsedAccounts] = useState<KiroBatchAccountItem[]>([])

  const handleFileUpload = (event: React.ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0]
    if (!file) return

    const reader = new FileReader()
    reader.onload = (e) => {
      const content = e.target?.result as string
      setJsonText(content)
      // 尝试预览解析
      try {
        const parsed = JSON.parse(content)
        if (Array.isArray(parsed)) {
          setParsedAccounts(parsed)
        } else {
          setParsedAccounts([])
        }
      } catch {
        setParsedAccounts([])
      }
    }
    reader.readAsText(file)
  }

  const handleTextChange = (value: string) => {
    setJsonText(value)
    setError(null)

    // 尝试实时解析预览
    try {
      const trimmed = value.trim()
      if (trimmed) {
        const parsed = JSON.parse(trimmed)
        if (Array.isArray(parsed)) {
          setParsedAccounts(parsed)
        } else {
          setParsedAccounts([])
        }
      } else {
        setParsedAccounts([])
      }
    } catch {
      setParsedAccounts([])
    }
  }

  const handleImport = async () => {
    if (!jsonText.trim()) {
      setError('请粘贴 JSON 数据或上传文件')
      return
    }

    try {
      setProcessing(true)
      setError(null)
      setResult(null)

      // 解析JSON
      const accountsData = JSON.parse(jsonText.trim())

      if (!Array.isArray(accountsData)) {
        throw new Error('数据格式错误：必须是数组')
      }

      if (accountsData.length === 0) {
        throw new Error('数组不能为空')
      }

      // 构建请求
      const request: ImportKiroBatchRequest = {
        accounts: accountsData,
        skipExisting,
        accountNamePrefix: accountNamePrefix.trim() || undefined
      }

      // 发送请求
      const importResult = await kiroOAuthService.importBatchCredentials(request)

      setResult(importResult)

      // 如果有成功导入的，通知父组件刷新列表
      if (importResult.successCount > 0 && onImportCompleted) {
        onImportCompleted(importResult)
      }

    } catch (err) {
      const message = err instanceof Error ? err.message : '批量导入失败'
      setError(message)
    } finally {
      setProcessing(false)
    }
  }

  const handleReset = () => {
    setJsonText('')
    setAccountNamePrefix('')
    setSkipExisting(true)
    setError(null)
    setResult(null)
    setParsedAccounts([])
  }

  const handleOpenChange = (newOpen: boolean) => {
    if (!newOpen) {
      handleReset()
    }
    onOpenChange(newOpen)
  }

  // 计算统计信息
  const fileStats = {
    total: parsedAccounts.length,
    hasEmail: parsedAccounts.filter(a => a.email).length,
    hasTokens: parsedAccounts.filter(a => a.accessToken || a.refreshToken).length
  }

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogContent className="max-w-4xl w-full max-h-[90vh] overflow-hidden flex flex-col">
        <DialogHeader>
          <DialogTitle>批量导入 Kiro 账户</DialogTitle>
          <DialogDescription>
            一次性导入多个 Kiro 凭证，支持JSON数组格式
          </DialogDescription>
        </DialogHeader>

        <div className="flex-1 overflow-y-auto space-y-4 pr-2">
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

          {/* Success Result */}
          {result && (
            <motion.div
              initial={{ opacity: 0, scale: 0.95 }}
              animate={{ opacity: 1, scale: 1 }}
              className="space-y-3"
            >
              <Card className={`border ${
                result.failCount > 0 ? 'border-orange-200 bg-orange-50 dark:border-orange-800 dark:bg-orange-950' :
                'border-emerald-200 bg-emerald-50 dark:border-emerald-800 dark:bg-emerald-950'
              }`}>
                <CardHeader>
                  <CardTitle className="text-base flex items-center gap-2">
                    {result.failCount > 0 ? (
                      <>
                        <AlertTriangle className="h-5 w-5 text-orange-600" />
                        导入完成（部分失败）
                      </>
                    ) : (
                      <>
                        <Check className="h-5 w-5 text-emerald-600" />
                        导入成功
                      </>
                    )}
                  </CardTitle>
                </CardHeader>
                <CardContent>
                  <div className="grid grid-cols-3 gap-4 mb-3">
                    <div className="text-center">
                      <div className="text-2xl font-bold text-emerald-600">{result.successCount}</div>
                      <div className="text-xs text-muted-foreground">成功</div>
                    </div>
                    <div className="text-center">
                      <div className="text-2xl font-bold text-orange-600">{result.failCount}</div>
                      <div className="text-xs text-muted-foreground">失败</div>
                    </div>
                    <div className="text-center">
                      <div className="text-2xl font-bold text-gray-600">{result.skippedCount}</div>
                      <div className="text-xs text-muted-foreground">跳过</div>
                    </div>
                  </div>

                  {result.failItems.length > 0 && (
                    <div className="space-y-2 mt-4">
                      <p className="text-sm font-medium text-orange-700">失败详情：</p>
                      {result.failItems.slice(0, 5).map((item, idx) => (
                        <div key={idx} className="text-xs bg-background border border-orange-200 rounded p-2">
                          <div className="flex gap-2">
                            <span className="font-mono text-orange-600">{item.originalId || item.email || '未知'}</span>
                            <span className="text-red-600">{item.errorMessage}</span>
                          </div>
                        </div>
                      ))}
                      {result.failItems.length > 5 && (
                        <p className="text-xs text-muted-foreground">
                          还有 {result.failItems.length - 5} 个失败项...
                        </p>
                      )}
                    </div>
                  )}

                  {result.successItems.length > 0 && (
                    <div className="space-y-2 mt-4">
                      <p className="text-sm font-medium text-emerald-700">成功导入的账户：</p>
                      {result.successItems.slice(0, 3).map((item, idx) => (
                        <div key={idx} className="text-xs bg-background border border-emerald-200 rounded p-2">
                          <div className="flex justify-between">
                            <span className="font-mono">{item.accountName}</span>
                            <span className="text-muted-foreground">ID: {item.accountId}</span>
                          </div>
                          <div className="text-muted-foreground">{item.email}</div>
                        </div>
                      ))}
                      {result.successItems.length > 3 && (
                        <p className="text-xs text-muted-foreground">
                          还有 {result.successItems.length - 3} 个成功项...
                        </p>
                      )}
                    </div>
                  )}
                </CardContent>
              </Card>

              <Button onClick={handleReset} className="w-full" variant="outline">
                继续导入其他数据
              </Button>
            </motion.div>
          )}

          {/* Input Section */}
          {!result && (
            <motion.div
              initial={{ opacity: 0, y: 20 }}
              animate={{ opacity: 1, y: 0 }}
              className="space-y-4"
            >
              {/* Configuration */}
              <Card>
                <CardHeader>
                  <CardTitle className="text-base">导入配置</CardTitle>
                  <CardDescription>设置导入行为选项</CardDescription>
                </CardHeader>
                <CardContent className="space-y-3">
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                    <div className="space-y-2">
                      <label className="text-sm font-medium">账户名称前缀（可选）</label>
                      <input
                        type="text"
                        placeholder="例如: Kiro-Team"
                        value={accountNamePrefix}
                        onChange={(e) => setAccountNamePrefix(e.target.value)}
                        className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-primary"
                      />
                      <p className="text-xs text-muted-foreground">
                        批量命名，如果没有将使用邮箱或ID
                      </p>
                    </div>

                    <div className="space-y-2">
                      <label className="flex items-center gap-2 text-sm pt-6">
                        <Checkbox
                          checked={skipExisting}
                          onCheckedChange={(checked) => setSkipExisting(!!checked)}
                        />
                        跳过已存在的账户
                      </label>
                      <p className="text-xs text-muted-foreground mt-1">
                        基于邮箱判断重复，已存在的将不会被导入
                      </p>
                    </div>
                  </div>
                </CardContent>
              </Card>

              {/* Data Input */}
              <Card>
                <CardHeader>
                  <CardTitle className="text-base">数据输入</CardTitle>
                  <CardDescription>
                    支持粘贴JSON数组或上传JSON文件
                  </CardDescription>
                </CardHeader>
                <CardContent className="space-y-4">
                  {/* File Upload */}
                  <div className="flex gap-2 items-center">
                    <Button
                      variant="outline"
                      className="gap-2"
                      onClick={() => document.getElementById('file-upload')?.click()}
                    >
                      <Upload className="h-4 w-4" />
                      上传JSON文件
                    </Button>
                    <input
                      id="file-upload"
                      type="file"
                      accept=".json"
                      onChange={handleFileUpload}
                      className="hidden"
                    />
                    <span className="text-xs text-muted-foreground">
                      支持 .json 文件，内容为数组格式
                    </span>
                  </div>

                  {/* JSON Text Area */}
                  <div className="space-y-2">
                    <label className="text-sm font-medium">JSON 数据</label>
                    <textarea
                      rows={12}
                      placeholder='例如: [{"accessToken":"...","refreshToken":"...","email":"user@example.com","profileArn":"..."}]'
                      value={jsonText}
                      onChange={(e) => handleTextChange(e.target.value)}
                      className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm placeholder:text-muted-foreground font-mono focus:outline-none focus:ring-2 focus:ring-primary"
                    />
                    <p className="text-xs text-muted-foreground">
                      字段说明：accessToken, refreshToken, email, profileArn, expiresAt, authMethod(默认social), clientId, clientSecret
                    </p>
                  </div>

                  {/* Stats Preview */}
                  {parsedAccounts.length > 0 && (
                    <motion.div
                      initial={{ opacity: 0, height: 0 }}
                      animate={{ opacity: 1, height: 'auto' }}
                      className="rounded-lg border border-blue-200 bg-blue-50 p-3 dark:border-blue-800 dark:bg-blue-950"
                    >
                      <div className="flex items-center gap-2 mb-2">
                        <FileText className="h-4 w-4 text-blue-600" />
                        <span className="text-sm font-medium text-blue-800 dark:text-blue-200">
                          数据预览
                        </span>
                      </div>
                      <div className="grid grid-cols-3 gap-3 text-xs">
                        <div>
                          <span className="font-medium text-blue-700 dark:text-blue-300">总记录:</span>
                          <span className="ml-1 text-blue-900 dark:text-blue-100">{fileStats.total}</span>
                        </div>
                        <div>
                          <span className="font-medium text-blue-700 dark:text-blue-300">有邮箱:</span>
                          <span className="ml-1 text-blue-900 dark:text-blue-100">{fileStats.hasEmail}</span>
                        </div>
                        <div>
                          <span className="font-medium text-blue-700 dark:text-blue-300">有凭证:</span>
                          <span className="ml-1 text-blue-900 dark:text-blue-100">{fileStats.hasTokens}</span>
                        </div>
                      </div>
                      {parsedAccounts.slice(0, 2).map((acc, idx) => (
                        <div key={idx} className="text-xs mt-2 p-2 bg-background rounded border border-blue-200">
                          <div className="font-mono">{acc.email || acc.id || '未知邮箱'}</div>
                          <div className="text-muted-foreground">
                            {acc.accessToken ? '✓ AccessToken' : ''} {acc.refreshToken ? '✓ RefreshToken' : ''}
                          </div>
                        </div>
                      ))}
                      {parsedAccounts.length > 2 && (
                        <div className="text-xs text-muted-foreground mt-1">
                          还有 {parsedAccounts.length - 2} 个预览项...
                        </div>
                      )}
                    </motion.div>
                  )}
                </CardContent>
              </Card>
            </motion.div>
          )}
        </div>

        {/* Action Buttons */}
        <div className="flex gap-3 justify-end pt-4 border-t mt-4">
          <Button
            variant="outline"
            onClick={() => handleOpenChange(false)}
            disabled={processing}
          >
            {result ? '关闭' : '取消'}
          </Button>

          {!result && (
            <Button
              onClick={handleImport}
              disabled={!jsonText.trim() || processing}
              className="gap-2"
            >
              {processing ? (
                <>
                  <Loader className="h-4 w-4 animate-spin" />
                  导入中...
                </>
              ) : (
                <>
                  <Upload className="h-4 w-4" />
                  开始导入
                </>
              )}
            </Button>
          )}
        </div>
      </DialogContent>
    </Dialog>
  )
}