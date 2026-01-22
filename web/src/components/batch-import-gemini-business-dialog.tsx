import { useState } from 'react'
import { motion } from 'motion/react'
import { AlertCircle, Loader, Upload, FileText, Check, AlertTriangle } from 'lucide-react'
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription } from '@/components/animate-ui/components/radix/dialog'
import { Button } from '@/components/animate-ui/components/buttons/button'
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/animate-ui/components/card'
import { Checkbox } from '@/components/animate-ui/components/radix/checkbox'
import { geminiBusinessOAuthService } from '@/services/account'
import type {
  ImportGeminiBusinessBatchRequest,
  GeminiBusinessBatchAccountItem,
  ImportGeminiBusinessBatchResult,
} from '@/types/account'

interface BatchImportGeminiBusinessDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  onImportCompleted?: (result: ImportGeminiBusinessBatchResult) => void
}

export function BatchImportGeminiBusinessDialog({
  open,
  onOpenChange,
  onImportCompleted,
}: BatchImportGeminiBusinessDialogProps) {
  const [jsonText, setJsonText] = useState('')
  const [accountNamePrefix, setAccountNamePrefix] = useState('')
  const [skipExisting, setSkipExisting] = useState(true)
  const [processing, setProcessing] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [result, setResult] = useState<ImportGeminiBusinessBatchResult | null>(null)
  const [parsedAccounts, setParsedAccounts] = useState<GeminiBusinessBatchAccountItem[]>([])

  const handleFileUpload = (event: React.ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0]
    if (!file) return

    const reader = new FileReader()
    reader.onload = (e) => {
      const content = e.target?.result as string
      setJsonText(content)
      try {
        const parsed = JSON.parse(content)
        setParsedAccounts(Array.isArray(parsed) ? parsed : [])
      } catch {
        setParsedAccounts([])
      }
    }
    reader.readAsText(file)
  }

  const handleTextChange = (value: string) => {
    setJsonText(value)
    setError(null)
    try {
      const trimmed = value.trim()
      if (!trimmed) {
        setParsedAccounts([])
        return
      }
      const parsed = JSON.parse(trimmed)
      setParsedAccounts(Array.isArray(parsed) ? parsed : [])
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

      const accountsData = JSON.parse(jsonText.trim())
      if (!Array.isArray(accountsData)) {
        throw new Error('数据格式错误：必须是数组')
      }
      if (accountsData.length === 0) {
        throw new Error('数组不能为空')
      }

      const request: ImportGeminiBusinessBatchRequest = {
        accounts: accountsData,
        skipExisting,
        accountNamePrefix: accountNamePrefix.trim() || undefined,
      }

      const importResult = await geminiBusinessOAuthService.importBatchCredentials(request)
      setResult(importResult)

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

  const stats = {
    total: parsedAccounts.length,
    hasCsesidx: parsedAccounts.filter((a) => !!a.csesidx).length,
    hasConfigId: parsedAccounts.filter((a) => !!a.config_id).length,
  }

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogContent className="max-w-4xl w-full max-h-[90vh] overflow-hidden flex flex-col">
        <DialogHeader>
          <DialogTitle>批量导入 Gemini Business 账户</DialogTitle>
          <DialogDescription>一次性导入多个 Gemini Business 凭证，支持 JSON 数组格式</DialogDescription>
        </DialogHeader>

        <div className="flex-1 overflow-y-auto space-y-4 pr-2">
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

          {result && (
            <motion.div
              initial={{ opacity: 0, scale: 0.95 }}
              animate={{ opacity: 1, scale: 1 }}
              className="space-y-3"
            >
              <Card
                className={`border ${
                  result.failCount > 0
                    ? 'border-orange-200 bg-orange-50 dark:border-orange-800 dark:bg-orange-950'
                    : 'border-emerald-200 bg-emerald-50 dark:border-emerald-800 dark:bg-emerald-950'
                }`}
              >
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
                        <p className="text-xs text-muted-foreground">还有 {result.failItems.length - 5} 个失败项...</p>
                      )}
                    </div>
                  )}
                </CardContent>
              </Card>
            </motion.div>
          )}

          <Card>
            <CardHeader>
              <CardTitle className="text-base">导入设置</CardTitle>
              <CardDescription>可选：批量命名前缀与跳过已存在账户</CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                <div className="space-y-2">
                  <label className="text-sm font-medium">账户名称前缀（可选）</label>
                  <input
                    type="text"
                    placeholder="例如: GeminiBiz-Team"
                    value={accountNamePrefix}
                    onChange={(e) => setAccountNamePrefix(e.target.value)}
                    className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-primary"
                  />
                </div>
                <div className="flex items-center gap-2 pt-6">
                  <Checkbox checked={skipExisting} onCheckedChange={(v) => setSkipExisting(!!v)} />
                  <span className="text-sm">跳过已存在账户</span>
                </div>
              </div>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle className="text-base flex items-center gap-2">
                <FileText className="h-4 w-4" />
                JSON 数据
              </CardTitle>
              <CardDescription>粘贴 accounts.json 数组，或上传 JSON 文件</CardDescription>
            </CardHeader>
            <CardContent className="space-y-3">
              <div className="flex flex-wrap gap-2 items-center">
                <Button asChild variant="outline" className="gap-2">
                  <label>
                    <Upload className="h-4 w-4" />
                    上传文件
                    <input type="file" accept="application/json" className="hidden" onChange={handleFileUpload} />
                  </label>
                </Button>
                <div className="text-xs text-muted-foreground">
                  预览：{stats.total} 条，含 csesidx {stats.hasCsesidx}，含 config_id {stats.hasConfigId}
                </div>
              </div>

              <textarea
                rows={10}
                value={jsonText}
                onChange={(e) => handleTextChange(e.target.value)}
                placeholder='例如: [{"secure_c_ses":"...","csesidx":"...","config_id":"..."}]'
                className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm font-mono placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-primary"
              />
            </CardContent>
          </Card>
        </div>

        <div className="pt-4 border-t flex justify-end gap-3">
          <Button variant="outline" onClick={handleReset} disabled={processing}>
            重置
          </Button>
          <Button onClick={handleImport} disabled={processing} className="gap-2">
            {processing ? (
              <>
                <Loader className="h-4 w-4 animate-spin" />
                导入中...
              </>
            ) : (
              '开始导入'
            )}
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  )
}

