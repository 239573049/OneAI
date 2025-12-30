import { useEffect, useState } from 'react'
import { motion } from 'motion/react'
import {
  Trash2,
  AlertCircle,
  Loader,
  Search,
  Plus,
  MoreVertical,
  CheckCircle,
  XCircle,
  RefreshCw,
  Eye,
} from 'lucide-react'
import {
  Button as AnimateUIButton,
} from '@/components/animate-ui/components/buttons/button'
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
  CardDescription,
} from '@/components/animate-ui/components/card'
import { Input } from '@/components/animate-ui/components/input'
import { Label } from '@/components/animate-ui/components/label'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from '@/components/animate-ui/components/radix/dialog'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/animate-ui/components/radix/dropdown-menu'
import {
  ToggleGroup,
  ToggleGroupItem,
} from '@/components/animate-ui/components/radix/toggle-group'
import { AddAccountDialog } from '@/components/add-account-dialog'
import { accountService } from '@/services/account'
import type { AIAccountDto, AccountQuotaStatus } from '@/types/account'

type ProviderFilter = 'all' | string
type StatusFilter = 'all' | 'enabled' | 'disabled' | 'rate-limited'

const normalizeProviderKey = (provider: string) => {
  return provider.trim().toLowerCase().replace(/\s+/g, '-')
}

export default function AccountManagementView() {
  const [accounts, setAccounts] = useState<AIAccountDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [deletingId, setDeletingId] = useState<number | null>(null)
  const [searchTerm, setSearchTerm] = useState('')
  const [providerFilter, setProviderFilter] = useState<ProviderFilter>('all')
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('all')
  const [addDialogOpen, setAddDialogOpen] = useState(false)
  const [deleteConfirmOpen, setDeleteConfirmOpen] = useState(false)
  const [deleteConfirmAccountId, setDeleteConfirmAccountId] = useState<number | null>(null)
  const [togglingId, setTogglingId] = useState<number | null>(null)
  const [refreshingQuotaId, setRefreshingQuotaId] = useState<number | null>(null)
  const [quotaStatuses, setQuotaStatuses] = useState<Record<number, AccountQuotaStatus>>({})
  const [modelsDialogOpen, setModelsDialogOpen] = useState(false)
  const [modelsLoading, setModelsLoading] = useState(false)
  const [modelsError, setModelsError] = useState<string | null>(null)
  const [models, setModels] = useState<string[]>([])
  const [modelsAccountLabel, setModelsAccountLabel] = useState<string>('')

  useEffect(() => {
    fetchAccounts()
  }, [])

  const fetchAccounts = async () => {
    try {
      setLoading(true)
      setError(null)
      const data = await accountService.getAccounts()
      setAccounts(data)

      // 获取配额状态
      if (data.length > 0) {
        try {
          const accountIds = data.map(account => account.id)
          const quotas = await accountService.getAccountQuotaStatuses(accountIds)
          setQuotaStatuses(quotas)
        } catch (quotaErr) {
          console.error('Failed to fetch quota statuses:', quotaErr)
          // 配额获取失败不影响主流程
        }
      }
    } catch (err) {
      const message = err instanceof Error ? err.message : '获取账户列表失败'
      setError(message)
      console.error('Failed to fetch accounts:', err)
    } finally {
      setLoading(false)
    }
  }

  const handleDeleteClick = (id: number) => {
    setDeleteConfirmAccountId(id)
    setDeleteConfirmOpen(true)
  }

  const handleDeleteConfirm = async () => {
    if (!deleteConfirmAccountId) return

    try {
      setDeletingId(deleteConfirmAccountId)
      await accountService.deleteAccount(deleteConfirmAccountId)
      setAccounts(accounts.filter(account => account.id !== deleteConfirmAccountId))
      setDeleteConfirmOpen(false)
      setDeleteConfirmAccountId(null)
    } catch (err) {
      const message = err instanceof Error ? err.message : '删除账户失败'
      setError(message)
      console.error('Failed to delete account:', err)
    } finally {
      setDeletingId(null)
    }
  }

  const handleAccountAdded = async (account: AIAccountDto) => {
    void account
    // 刷新账户列表
    await fetchAccounts()
  }

  const handleToggleStatus = async (id: number) => {
    try {
      setTogglingId(id)
      const updatedAccount = await accountService.toggleAccountStatus(id)
      // 更新本地账户列表
      setAccounts(accounts.map(account =>
        account.id === id ? updatedAccount : account
      ))
    } catch (err) {
      const message = err instanceof Error ? err.message : '更新状态失败'
      setError(message)
      console.error('Failed to toggle account status:', err)
    } finally {
      setTogglingId(null)
    }
  }

  const handleRefreshOpenAIQuota = async (accountId: number) => {
    try {
      setRefreshingQuotaId(accountId)
      const status = await accountService.refreshOpenAIQuotaStatus(accountId)
      setQuotaStatuses(prev => ({ ...prev, [accountId]: status }))

      // 同步刷新账户状态（可能会被标记限流/禁用）
      const updatedAccounts = await accountService.getAccounts()
      setAccounts(updatedAccounts)
    } catch (err) {
      const message = err instanceof Error ? err.message : '获取用量失败'
      setError(message)
      console.error('Failed to refresh quota:', err)
    } finally {
      setRefreshingQuotaId(null)
    }
  }

  const handleRefreshAntigravityQuota = async (accountId: number) => {
    try {
      setRefreshingQuotaId(accountId)
      const status = await accountService.refreshAntigravityQuotaStatus(accountId)
      setQuotaStatuses(prev => ({ ...prev, [accountId]: status }))

      // 同步刷新账户状态（可能会被标记限流/禁用）
      const updatedAccounts = await accountService.getAccounts()
      setAccounts(updatedAccounts)
    } catch (err) {
      const message = err instanceof Error ? err.message : '获取用量失败'
      setError(message)
      console.error('Failed to refresh Antigravity quota:', err)
    } finally {
      setRefreshingQuotaId(null)
    }
  }

  const handleViewModels = async (account: AIAccountDto) => {
    setModelsDialogOpen(true)
    setModels([])
    setModelsError(null)
    setModelsAccountLabel(account.name || account.email || `账户 ${account.id}`)

    try {
      setModelsLoading(true)
      const result = await accountService.getAntigravityModels(account.id)
      setModels(result)
    } catch (err) {
      const message = err instanceof Error ? err.message : '获取可用模型失败'
      setModelsError(message)
      console.error('Failed to fetch antigravity models:', err)
    } finally {
      setModelsLoading(false)
    }
  }

  const formatDate = (dateString: string) => {
    return new Date(dateString).toLocaleDateString('zh-CN', {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
    })
  }

  const formatTimeRemaining = (seconds?: number) => {
    if (!seconds || seconds <= 0) return '-'

    const hours = Math.floor(seconds / 3600)
    const minutes = Math.floor((seconds % 3600) / 60)

    if (hours > 24) {
      const days = Math.floor(hours / 24)
      const remainingHours = hours % 24
      return `${days}天${remainingHours}小时`
    }

    if (hours > 0) {
      return `${hours}小时${minutes}分钟`
    }

    return `${minutes}分钟`
  }

  const getHealthColor = (healthScore: number) => {
    if (healthScore >= 80) return 'bg-green-500'
    if (healthScore >= 50) return 'bg-yellow-500'
    return 'bg-red-500'
  }

  const getUsageColor = (percent?: number) => {
    const value = percent ?? 0
    if (value >= 90) return 'bg-red-500'
    if (value >= 70) return 'bg-yellow-500'
    return 'bg-green-500'
  }

  const enabledCount = accounts.filter(account => account.isEnabled).length
  const rateLimitedCount = accounts.filter(account => account.isRateLimited).length
  const openaiCount = accounts.filter(account => account.provider.toLowerCase() === 'openai').length

  const providerKeyToLabel = new Map<string, string>()
  for (const account of accounts) {
    const providerKey = normalizeProviderKey(account.provider)
    if (!providerKeyToLabel.has(providerKey)) {
      providerKeyToLabel.set(providerKey, account.provider)
    }
  }

  const providerOrder: Record<string, number> = {
    openai: 0,
    claude: 1,
    gemini: 2,
    'gemini-antigravity': 3,
  }

  const providerOptions = Array.from(providerKeyToLabel.entries())
    .sort((a, b) => {
      const orderA = providerOrder[a[0]] ?? 999
      const orderB = providerOrder[b[0]] ?? 999
      if (orderA !== orderB) return orderA - orderB
      return a[1].localeCompare(b[1])
    })
    .map(([key, label]) => ({ key, label }))

  const normalizedSearch = searchTerm.trim().toLowerCase()

  const filteredAccounts = accounts.filter((account) => {
    const matchesSearch = normalizedSearch.length === 0
      || account.provider.toLowerCase().includes(normalizedSearch)
      || (account.name && account.name.toLowerCase().includes(normalizedSearch))
      || (account.email && account.email.toLowerCase().includes(normalizedSearch))

    const matchesProvider = providerFilter === 'all'
      || normalizeProviderKey(account.provider) === providerFilter

    const matchesStatus = statusFilter === 'all'
      || (statusFilter === 'enabled' && account.isEnabled)
      || (statusFilter === 'disabled' && !account.isEnabled)
      || (statusFilter === 'rate-limited' && account.isRateLimited)

    return matchesSearch && matchesProvider && matchesStatus
  })

  const hasActiveFilters = normalizedSearch.length > 0
    || providerFilter !== 'all'
    || statusFilter !== 'all'

  return (
    <div className="p-6 space-y-6">
      <motion.div
        initial={{ opacity: 0, y: 20 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.5 }}
        className="space-y-4"
      >
        <div className="flex items-center justify-between">
          <div>
            <h2 className="text-3xl font-bold">AI 账户管理</h2>
            <p className="text-muted-foreground mt-1">
              管理你的 AI 服务提供商账户，包括 OpenAI、Claude、Gemini 等
            </p>
          </div>
        </div>

        <div className="flex flex-wrap gap-2">
          <span className="inline-flex items-center rounded-full bg-muted px-3 py-1 text-xs text-muted-foreground">
            总计 {accounts.length}
          </span>
          <span className="inline-flex items-center rounded-full bg-muted px-3 py-1 text-xs text-muted-foreground">
            启用 {enabledCount}
          </span>
          <span className="inline-flex items-center rounded-full bg-muted px-3 py-1 text-xs text-muted-foreground">
            限流中 {rateLimitedCount}
          </span>
          <span className="inline-flex items-center rounded-full bg-muted px-3 py-1 text-xs text-muted-foreground">
            OpenAI {openaiCount}
          </span>
        </div>

        {/* Search and Filters */}
        <div className="flex flex-col gap-3 lg:flex-row lg:items-end lg:justify-between">
          <div className="flex flex-col gap-3 sm:flex-row sm:items-center flex-1">
            <div className="flex-1 relative">
              <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground pointer-events-none" />
              <Input
                placeholder="搜索账户名称 / 邮箱 / 提供商..."
                value={searchTerm}
                onChange={(e) => setSearchTerm(e.target.value)}
                className="pl-10"
              />
            </div>
            <AnimateUIButton
              variant="default"
              className="gap-2 sm:w-auto w-full justify-center"
              onClick={() => setAddDialogOpen(true)}
            >
              <Plus className="h-4 w-4" />
              添加账户
            </AnimateUIButton>
          </div>

          <div className="flex flex-col gap-3 sm:flex-row sm:items-end">
            <div className="space-y-1">
              <Label variant="muted" className="text-xs">账户类型</Label>
              <ToggleGroup
                type="single"
                variant="outline"
                size="sm"
                value={providerFilter}
                onValueChange={(value) => setProviderFilter((value || 'all') as ProviderFilter)}
                className="w-full sm:w-auto"
              >
                <ToggleGroupItem value="all" className="flex-none px-3">
                  全部
                </ToggleGroupItem>
                {providerOptions.map(({ key, label }) => (
                  <ToggleGroupItem key={key} value={key} className="flex-none px-3">
                    {label}
                  </ToggleGroupItem>
                ))}
              </ToggleGroup>
            </div>

            <div className="space-y-1">
              <Label variant="muted" className="text-xs">状态</Label>
              <ToggleGroup
                type="single"
                variant="outline"
                size="sm"
                value={statusFilter}
                onValueChange={(value) => setStatusFilter((value || 'all') as StatusFilter)}
                className="w-full sm:w-auto"
              >
                <ToggleGroupItem value="all" className="flex-none px-3">
                  全部
                </ToggleGroupItem>
                <ToggleGroupItem value="enabled" className="flex-none px-3">
                  启用
                </ToggleGroupItem>
                <ToggleGroupItem value="disabled" className="flex-none px-3">
                  禁用
                </ToggleGroupItem>
                <ToggleGroupItem value="rate-limited" className="flex-none px-3">
                  限流中
                </ToggleGroupItem>
              </ToggleGroup>
            </div>
          </div>
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
      ) : filteredAccounts.length === 0 && accounts.length === 0 ? (
        <Card variant="elevated">
          <CardContent className="pt-12 pb-12 text-center">
            <p className="text-muted-foreground">还没有添加任何账户</p>
          </CardContent>
        </Card>
      ) : filteredAccounts.length === 0 ? (
        <Card variant="elevated">
          <CardContent className="pt-12 pb-12 text-center">
            <p className="text-muted-foreground">未找到匹配的账户</p>
          </CardContent>
        </Card>
      ) : (
        <>
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            className="space-y-4"
          >
            <div className="flex items-center justify-between">
              <div className="text-sm text-muted-foreground">
                共 {filteredAccounts.length} {hasActiveFilters ? '个匹配的' : '个'}账户
              </div>
            </div>

            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
              {filteredAccounts.map((account, index) => {
                const providerKey = normalizeProviderKey(account.provider)
                const quota = quotaStatuses[account.id]
                const healthScore = quota?.healthScore ?? 0
                const primaryPercent = Math.min(quota?.primaryUsedPercent ?? 0, 100)
                const secondaryPercent = Math.min(quota?.secondaryUsedPercent ?? 0, 100)

                return (
                  <Card
                    key={account.id}
                    variant="elevated"
                    delay={index * 0.03}
                    className="overflow-hidden"
                  >
                    <CardContent className="p-4">
                      <div className="flex items-start justify-between gap-3">
                        <div className="flex flex-wrap items-center gap-2 min-w-0">
                          <span className="inline-flex items-center rounded-full bg-primary/10 px-3 py-1 text-xs font-medium text-primary">
                            {account.provider}
                          </span>
                          <span className={`inline-flex items-center gap-1 rounded-full px-2 py-1 text-xs font-medium ${
                            account.isEnabled
                              ? 'bg-green-500/10 text-green-600 dark:text-green-400'
                              : 'bg-muted text-muted-foreground'
                          }`}
                          >
                            <span className={`h-1.5 w-1.5 rounded-full ${
                              account.isEnabled ? 'bg-green-500' : 'bg-gray-400'
                            }`}
                            />
                            {account.isEnabled ? '启用' : '禁用'}
                          </span>
                          {account.isRateLimited && (
                            <span className="inline-flex items-center rounded-full bg-yellow-500/10 px-2 py-1 text-xs font-medium text-yellow-700 dark:text-yellow-300">
                              限流中
                            </span>
                          )}
                        </div>

                        <DropdownMenu>
                          <DropdownMenuTrigger asChild>
                            <AnimateUIButton
                              variant="ghost"
                              size="sm"
                              className="h-8 w-8 p-0"
                            >
                              <MoreVertical className="h-4 w-4" />
                            </AnimateUIButton>
                          </DropdownMenuTrigger>
                          <DropdownMenuContent align="end" className="w-48">
                            {providerKey === 'openai' && (
                              <DropdownMenuItem
                                onClick={() => handleRefreshOpenAIQuota(account.id)}
                                disabled={refreshingQuotaId === account.id}
                                className="cursor-pointer gap-2 flex"
                              >
                                {refreshingQuotaId === account.id ? (
                                  <>
                                    <Loader className="h-4 w-4 animate-spin" />
                                    <span>获取用量中</span>
                                  </>
                                ) : (
                                  <>
                                    <RefreshCw className="h-4 w-4" />
                                    <span>获取用量</span>
                                  </>
                                )}
                              </DropdownMenuItem>
                            )}
                            {providerKey === 'gemini-antigravity' && (
                              <>
                                <DropdownMenuItem
                                  onClick={() => handleRefreshAntigravityQuota(account.id)}
                                  disabled={refreshingQuotaId === account.id}
                                  className="cursor-pointer gap-2 flex"
                                >
                                  {refreshingQuotaId === account.id ? (
                                    <>
                                      <Loader className="h-4 w-4 animate-spin" />
                                      <span>获取用量中</span>
                                    </>
                                  ) : (
                                    <>
                                      <RefreshCw className="h-4 w-4" />
                                      <span>获取用量</span>
                                    </>
                                  )}
                                </DropdownMenuItem>
                                <DropdownMenuItem
                                  onClick={() => handleViewModels(account)}
                                  className="cursor-pointer gap-2 flex"
                                >
                                  <Eye className="h-4 w-4" />
                                  <span>查看可用模型</span>
                                </DropdownMenuItem>
                              </>
                            )}
                            <DropdownMenuItem
                              onClick={() => handleToggleStatus(account.id)}
                              disabled={togglingId === account.id}
                              className="cursor-pointer gap-2 flex"
                            >
                              {togglingId === account.id ? (
                                <>
                                  <Loader className="h-4 w-4 animate-spin" />
                                  <span>更新中</span>
                                </>
                              ) : account.isEnabled ? (
                                <>
                                  <XCircle className="h-4 w-4" />
                                  <span>禁用</span>
                                </>
                              ) : (
                                <>
                                  <CheckCircle className="h-4 w-4" />
                                  <span>启用</span>
                                </>
                              )}
                            </DropdownMenuItem>
                            <DropdownMenuItem
                              onClick={() => handleDeleteClick(account.id)}
                              disabled={deletingId === account.id}
                              className="cursor-pointer gap-2 flex text-red-600 hover:text-red-700"
                            >
                              {deletingId === account.id ? (
                                <>
                                  <Loader className="h-4 w-4 animate-spin" />
                                  <span>删除中</span>
                                </>
                              ) : (
                                <>
                                  <Trash2 className="h-4 w-4" />
                                  <span>删除</span>
                                </>
                              )}
                            </DropdownMenuItem>
                          </DropdownMenuContent>
                        </DropdownMenu>
                      </div>

                      <div className="mt-3 min-w-0">
                        <div className="text-sm font-semibold truncate">
                          {account.name || account.email || '未命名账户'}
                        </div>
                        <div
                          className="mt-1 text-xs text-muted-foreground truncate"
                          title={account.email || undefined}
                        >
                          {account.email || '-'}
                        </div>
                      </div>

                      {account.isRateLimited && account.rateLimitResetTime && (
                        <div className="mt-2 text-xs text-muted-foreground">
                          预计 {formatDate(account.rateLimitResetTime)} 后解除限流
                        </div>
                      )}

                      <div className="mt-4 space-y-3">
                        {quota?.hasCacheData ? (
                          <>
                            <div className="flex items-center justify-between gap-3">
                              <span className="text-xs text-muted-foreground">健康度</span>
                              <div className="flex items-center gap-2 text-xs">
                                <span className={`h-2 w-2 rounded-full ${getHealthColor(healthScore)}`} />
                                <span className="font-medium">{healthScore}</span>
                              </div>
                            </div>

                            {!!quota.statusDescription && (
                              <div
                                className="text-xs text-muted-foreground truncate"
                                title={quota.statusDescription}
                              >
                                {quota.statusDescription}
                              </div>
                            )}

                            <div className="grid grid-cols-2 gap-3">
                              <div className="space-y-1">
                                <div className="flex items-center justify-between text-xs">
                                  <span className="text-muted-foreground">5小时</span>
                                  <span className="font-medium">{quota.primaryUsedPercent ?? 0}%</span>
                                </div>
                                <div className="h-2 w-full bg-muted rounded-full overflow-hidden">
                                  <div
                                    className={`h-full transition-all ${getUsageColor(quota.primaryUsedPercent)}`}
                                    style={{ width: `${primaryPercent}%` }}
                                  />
                                </div>
                                <div className="text-[11px] text-muted-foreground">
                                  {formatTimeRemaining(quota.primaryResetAfterSeconds)}后重置
                                </div>
                              </div>

                              <div className="space-y-1">
                                <div className="flex items-center justify-between text-xs">
                                  <span className="text-muted-foreground">7天</span>
                                  <span className="font-medium">{quota.secondaryUsedPercent ?? 0}%</span>
                                </div>
                                <div className="h-2 w-full bg-muted rounded-full overflow-hidden">
                                  <div
                                    className={`h-full transition-all ${getUsageColor(quota.secondaryUsedPercent)}`}
                                    style={{ width: `${secondaryPercent}%` }}
                                  />
                                </div>
                                <div className="text-[11px] text-muted-foreground">
                                  {formatTimeRemaining(quota.secondaryResetAfterSeconds)}后重置
                                </div>
                              </div>
                            </div>
                          </>
                        ) : (
                          <div className="rounded-md border border-dashed p-3 text-xs text-muted-foreground">
                            暂无配额数据
                            {providerKey === 'openai' && (
                              <span>（可在右上角菜单手动获取）</span>
                            )}
                          </div>
                        )}
                      </div>

                      <div className="mt-4 flex items-center justify-between gap-3 text-xs text-muted-foreground">
                        <span>使用 {account.usageCount} 次</span>
                        <span className="shrink-0">{formatDate(account.createdAt)}</span>
                      </div>
                    </CardContent>
                  </Card>
                )
              })}
            </div>
          </motion.div>

          {false && (
        <motion.div
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          transition={{ staggerChildren: 0.05, delayChildren: 0.1 }}
        >
          <Card variant="elevated">
            <CardHeader>
              <CardTitle>账户列表</CardTitle>
              <CardDescription>
                共 {filteredAccounts.length} {searchTerm ? '个匹配的' : '个'}账户
              </CardDescription>
            </CardHeader>
            <CardContent>
              <div className="overflow-x-auto rounded-lg border">
                <table className="w-full min-w-[1100px] text-sm">
                  <thead className="bg-muted/30">
                    <tr className="border-b">
                      <th className="px-4 py-3 text-left text-xs font-medium text-muted-foreground">提供商</th>
                      <th className="px-4 py-3 text-left text-xs font-medium text-muted-foreground">账户名称</th>
                      <th className="px-4 py-3 text-left text-xs font-medium text-muted-foreground">邮箱</th>
                      <th className="px-4 py-3 text-left text-xs font-medium text-muted-foreground">状态</th>
                      <th className="px-4 py-3 text-left text-xs font-medium text-muted-foreground">健康度</th>
                      <th className="px-4 py-3 text-left text-xs font-medium text-muted-foreground">5小时窗口</th>
                      <th className="px-4 py-3 text-left text-xs font-medium text-muted-foreground">7天窗口</th>
                      <th className="px-4 py-3 text-left text-xs font-medium text-muted-foreground">使用次数</th>
                      <th className="px-4 py-3 text-left text-xs font-medium text-muted-foreground">创建时间</th>
                      <th className="px-4 py-3 text-right text-xs font-medium text-muted-foreground">操作</th>
                    </tr>
                  </thead>
                  <tbody>
                    {filteredAccounts.map((account, index) => (
                      <motion.tr
                        key={account.id}
                        initial={{ opacity: 0, y: 10 }}
                        animate={{ opacity: 1, y: 0 }}
                        transition={{ delay: index * 0.05 }}
                        className="border-b transition-colors hover:bg-muted/30"
                      >
                        <td className="px-4 py-3">
                          <span className="inline-flex items-center rounded-full bg-primary/10 px-3 py-1 text-xs font-medium text-primary">
                            {account.provider}
                          </span>
                        </td>
                        <td className="px-4 py-3">
                          {account.name || '-'}
                        </td>
                        <td className="px-4 py-3 text-xs text-muted-foreground">
                          {account.email || '-'}
                        </td>
                        <td className="px-4 py-3">
                          <div className="flex items-center gap-2">
                            <div className={`h-2 w-2 rounded-full ${
                              account.isEnabled
                                ? 'bg-green-500'
                                : 'bg-gray-400'
                            }`} />
                            <span>
                              {account.isEnabled ? '启用' : '禁用'}
                              {account.isRateLimited && ' (限流中)'}
                            </span>
                          </div>
                        </td>
                        <td className="px-4 py-3">
                          {quotaStatuses[account.id]?.hasCacheData ? (
                            <div className="min-w-[160px]">
                              <div className="flex items-center gap-2">
                                <div className={`h-2 w-2 rounded-full ${
                                  (quotaStatuses[account.id].healthScore ?? 0) >= 80
                                    ? 'bg-green-500'
                                    : (quotaStatuses[account.id].healthScore ?? 0) >= 50
                                    ? 'bg-yellow-500'
                                    : 'bg-red-500'
                                }`} />
                                <span>{quotaStatuses[account.id].healthScore ?? 0}</span>
                              </div>
                              {!!quotaStatuses[account.id].statusDescription && (
                                <div
                                  className="mt-1 max-w-[240px] truncate text-xs text-muted-foreground"
                                  title={quotaStatuses[account.id].statusDescription}
                                >
                                  {quotaStatuses[account.id].statusDescription}
                                </div>
                              )}
                            </div>
                          ) : (
                            <span className="text-muted-foreground">-</span>
                          )}
                        </td>
                        <td className="px-4 py-3">
                          {quotaStatuses[account.id]?.hasCacheData ? (
                            <div className="space-y-1 min-w-[120px]">
                              <div className="flex items-center justify-between text-xs">
                                <span className="text-muted-foreground">{quotaStatuses[account.id].primaryUsedPercent ?? 0}%</span>
                              </div>
                              <div className="h-2 w-full bg-muted rounded-full overflow-hidden">
                                <div
                                  className={`h-full transition-all ${
                                    (quotaStatuses[account.id].primaryUsedPercent ?? 0) >= 90
                                      ? 'bg-red-500'
                                      : (quotaStatuses[account.id].primaryUsedPercent ?? 0) >= 70
                                      ? 'bg-yellow-500'
                                      : 'bg-green-500'
                                  }`}
                                  style={{ width: `${Math.min(quotaStatuses[account.id].primaryUsedPercent ?? 0, 100)}%` }}
                                />
                              </div>
                              <div className="text-xs text-muted-foreground">
                                {formatTimeRemaining(quotaStatuses[account.id].primaryResetAfterSeconds)}后重置
                              </div>
                            </div>
                          ) : (
                            <span className="text-muted-foreground">-</span>
                          )}
                        </td>
                        <td className="px-4 py-3">
                          {quotaStatuses[account.id]?.hasCacheData ? (
                            <div className="space-y-1 min-w-[120px]">
                              <div className="flex items-center justify-between text-xs">
                                <span className="text-muted-foreground">{quotaStatuses[account.id].secondaryUsedPercent ?? 0}%</span>
                              </div>
                              <div className="h-2 w-full bg-muted rounded-full overflow-hidden">
                                <div
                                  className={`h-full transition-all ${
                                    (quotaStatuses[account.id].secondaryUsedPercent ?? 0) >= 90
                                      ? 'bg-red-500'
                                      : (quotaStatuses[account.id].secondaryUsedPercent ?? 0) >= 70
                                      ? 'bg-yellow-500'
                                      : 'bg-green-500'
                                  }`}
                                  style={{ width: `${Math.min(quotaStatuses[account.id].secondaryUsedPercent ?? 0, 100)}%` }}
                                />
                              </div>
                              <div className="text-xs text-muted-foreground">
                                {formatTimeRemaining(quotaStatuses[account.id].secondaryResetAfterSeconds)}后重置
                              </div>
                            </div>
                          ) : (
                            <span className="text-muted-foreground">-</span>
                          )}
                        </td>
                        <td className="px-4 py-3">
                          {account.usageCount}
                        </td>
                        <td className="px-4 py-3 text-xs text-muted-foreground">
                          {formatDate(account.createdAt)}
                        </td>
                        <td className="px-4 py-3 text-right">
                          <DropdownMenu>
                            <DropdownMenuTrigger asChild>
                              <AnimateUIButton
                                variant="ghost"
                                size="sm"
                                className="h-8 w-8 p-0"
                              >
                                <MoreVertical className="h-4 w-4" />
                              </AnimateUIButton>
                            </DropdownMenuTrigger>
                            <DropdownMenuContent align="end" className="w-48">
                              {account.provider.toLowerCase() === 'openai' && (
                                <DropdownMenuItem
                                  onClick={() => handleRefreshOpenAIQuota(account.id)}
                                  disabled={refreshingQuotaId === account.id}
                                  className="cursor-pointer gap-2 flex"
                                >
                                  {refreshingQuotaId === account.id ? (
                                    <>
                                      <Loader className="h-4 w-4 animate-spin" />
                                      <span>获取用量中</span>
                                    </>
                                  ) : (
                                    <>
                                      <RefreshCw className="h-4 w-4" />
                                      <span>获取用量</span>
                                    </>
                                  )}
                                </DropdownMenuItem>
                              )}
                              <DropdownMenuItem
                                onClick={() => handleToggleStatus(account.id)}
                                disabled={togglingId === account.id}
                                className="cursor-pointer gap-2 flex"
                              >
                                {togglingId === account.id ? (
                                  <>
                                    <Loader className="h-4 w-4 animate-spin" />
                                    <span>更新中</span>
                                  </>
                                ) : account.isEnabled ? (
                                  <>
                                    <XCircle className="h-4 w-4" />
                                    <span>禁用</span>
                                  </>
                                ) : (
                                  <>
                                    <CheckCircle className="h-4 w-4" />
                                    <span>启用</span>
                                  </>
                                )}
                              </DropdownMenuItem>
                              <DropdownMenuItem
                                onClick={() => handleDeleteClick(account.id)}
                                disabled={deletingId === account.id}
                                className="cursor-pointer gap-2 flex text-red-600 hover:text-red-700"
                              >
                                {deletingId === account.id ? (
                                  <>
                                    <Loader className="h-4 w-4 animate-spin" />
                                    <span>删除中</span>
                                  </>
                                ) : (
                                  <>
                                    <Trash2 className="h-4 w-4" />
                                    <span>删除</span>
                                  </>
                                )}
                              </DropdownMenuItem>
                            </DropdownMenuContent>
                          </DropdownMenu>
                        </td>
                      </motion.tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </CardContent>
          </Card>
        </motion.div>
          )}
        </>
      )}

      {/* Add Account Dialog */}
      <AddAccountDialog
        open={addDialogOpen}
        onOpenChange={setAddDialogOpen}
        onAccountAdded={handleAccountAdded}
      />

      {/* Antigravity Models Dialog */}
      <Dialog
        open={modelsDialogOpen}
        onOpenChange={(open) => {
          if (!open) {
            setModels([])
            setModelsError(null)
            setModelsAccountLabel('')
          }
          setModelsDialogOpen(open)
        }}
      >
        <DialogContent>
          <DialogHeader>
            <DialogTitle>可用模型</DialogTitle>
            <DialogDescription>
              {modelsAccountLabel ? `账户：${modelsAccountLabel}` : 'Gemini Antigravity 模型列表'}
            </DialogDescription>
          </DialogHeader>

          {modelsLoading ? (
            <div className="flex items-center gap-2 text-sm text-muted-foreground">
              <Loader className="h-4 w-4 animate-spin" />
              <span>加载中...</span>
            </div>
          ) : modelsError ? (
            <div className="text-sm text-red-600">{modelsError}</div>
          ) : models.length === 0 ? (
            <div className="text-sm text-muted-foreground">暂无模型返回</div>
          ) : (
            <div className="space-y-2 max-h-80 overflow-auto">
              {models.map((m) => (
                <div
                  key={m}
                  className="rounded border border-border/60 bg-muted/40 px-3 py-2 text-sm font-mono break-all"
                >
                  {m}
                </div>
              ))}
            </div>
          )}
        </DialogContent>
      </Dialog>

      {/* Delete Confirmation Dialog */}
      <Dialog open={deleteConfirmOpen} onOpenChange={setDeleteConfirmOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>删除账户</DialogTitle>
            <DialogDescription>
              确定要删除这个账户吗？此操作不可撤销。
            </DialogDescription>
          </DialogHeader>
          <DialogFooter className="flex gap-3 justify-end">
            <AnimateUIButton
              variant="outline"
              onClick={() => setDeleteConfirmOpen(false)}
              disabled={deletingId === deleteConfirmAccountId}
            >
              取消
            </AnimateUIButton>
            <AnimateUIButton
              variant="destructive"
              onClick={handleDeleteConfirm}
              disabled={deletingId === deleteConfirmAccountId}
              className="gap-2"
            >
              {deletingId === deleteConfirmAccountId ? (
                <>
                  <Loader className="h-4 w-4 animate-spin" />
                  删除中
                </>
              ) : (
                '删除'
              )}
            </AnimateUIButton>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  )
}
