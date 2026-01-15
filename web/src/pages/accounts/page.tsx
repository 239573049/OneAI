import { useEffect, useState, useMemo } from 'react'
import { motion, AnimatePresence } from 'motion/react'
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
  Settings2,
  Users,
  Activity,
  ShieldCheck,
  ZapOff,
} from 'lucide-react'
import { Button } from '@/components/animate-ui/components/buttons/button'
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
  DropdownMenuSeparator,
} from '@/components/animate-ui/components/radix/dropdown-menu'
import {
  ToggleGroup,
  ToggleGroupItem,
} from '@/components/animate-ui/components/radix/toggle-group'
import { Checkbox } from '@/components/animate-ui/components/radix/checkbox'
import { AddAccountDialog } from '@/components/add-account-dialog'
import { accountService } from '@/services/account'
import type { AIAccountDto, AccountQuotaStatus } from '@/types/account'
import { cn } from '@/lib/utils'
import { Skeleton } from '@/components/ui/skeleton'

type ProviderFilter = 'all' | string
type StatusFilter = 'all' | 'enabled' | 'disabled' | 'rate-limited'

const normalizeProviderKey = (provider: string) => {
  return provider.trim().toLowerCase().replace(/\s+/g, '-')
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
  if (hours > 0) return `${hours}小时${minutes}分钟`
  return `${minutes}分钟`
}

const formatTokens = (tokens: number): string => {
  if (tokens >= 1_000_000) return `${(tokens / 1_000_000).toFixed(1)}M`
  if (tokens >= 1_000) return `${(tokens / 1_000).toFixed(1)}K`
  return tokens.toString()
}

const getUsageColor = (percent?: number) => {
  const value = percent ?? 0
  if (value >= 90) return 'bg-destructive'
  if (value >= 70) return 'bg-orange-500'
  return 'bg-emerald-500'
}

const getHealthColor = (healthScore: number) => {
  if (healthScore >= 80) return 'text-emerald-500'
  if (healthScore >= 50) return 'text-orange-500'
  return 'text-destructive'
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

      if (data.length > 0) {
        try {
          const accountIds = data.map(account => account.id)
          const quotas = await accountService.getAccountQuotaStatuses(accountIds)
          setQuotaStatuses(quotas)
        } catch (quotaErr) {
          console.error('Failed to fetch quota statuses:', quotaErr)
        }
      }
    } catch (err) {
      const message = err instanceof Error ? err.message : '获取账户列表失败'
      setError(message)
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
    } finally {
      setDeletingId(null)
    }
  }

  const handleAccountAdded = async () => {
    await fetchAccounts()
  }

  const handleToggleStatus = async (id: number) => {
    try {
      setTogglingId(id)
      const updatedAccount = await accountService.toggleAccountStatus(id)
      setAccounts(accounts.map(account =>
        account.id === id ? updatedAccount : account
      ))
    } catch (err) {
      const message = err instanceof Error ? err.message : '更新状态失败'
      setError(message)
    } finally {
      setTogglingId(null)
    }
  }

  const handleRefreshQuota = async (accountId: number, provider: string) => {
    const providerKey = normalizeProviderKey(provider)
    try {
      setRefreshingQuotaId(accountId)
      let status: AccountQuotaStatus
      
      switch (providerKey) {
        case 'openai':
          status = await accountService.refreshOpenAIQuotaStatus(accountId)
          break
        case 'claude':
          status = await accountService.refreshClaudeQuotaStatus(accountId)
          break
        case 'factory':
          status = await accountService.refreshFactoryQuotaStatus(accountId)
          break
        case 'gemini-antigravity':
          status = await accountService.refreshAntigravityQuotaStatus(accountId)
          break
        default:
          return
      }

      setQuotaStatuses(prev => ({ ...prev, [accountId]: status }))
      const updatedAccounts = await accountService.getAccounts()
      setAccounts(updatedAccounts)
    } catch (err) {
      const message = err instanceof Error ? err.message : '获取用量失败'
      setError(message)
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
    } finally {
      setModelsLoading(false)
    }
  }

  const stats = useMemo(() => {
    const active = accounts.filter(a => a.isEnabled).length
    const limited = accounts.filter(a => a.isRateLimited).length
    return {
      total: accounts.length,
      active,
      disabled: accounts.length - active,
      limited,
    }
  }, [accounts])

  const providerOptions = useMemo(() => {
    const providers = new Map<string, string>()
    accounts.forEach(a => {
      const key = normalizeProviderKey(a.provider)
      if (!providers.has(key)) providers.set(key, a.provider)
    })
    
    const providerOrder: Record<string, number> = {
      openai: 0,
      claude: 1,
      kiro: 2,
      factory: 3,
      gemini: 4,
      'gemini-antigravity': 5,
    }

    return Array.from(providers.entries())
      .sort((a, b) => (providerOrder[a[0]] ?? 999) - (providerOrder[b[0]] ?? 999) || a[1].localeCompare(b[1]))
      .map(([key, label]) => ({ key, label }))
  }, [accounts])

  const filteredAccounts = useMemo(() => {
    const normalizedSearch = searchTerm.trim().toLowerCase()
    return accounts.filter((account) => {
      const matchesSearch = !normalizedSearch ||
        account.provider.toLowerCase().includes(normalizedSearch) ||
        (account.name?.toLowerCase().includes(normalizedSearch)) ||
        (account.email?.toLowerCase().includes(normalizedSearch))

      const matchesProvider = providerFilter === 'all' || normalizeProviderKey(account.provider) === providerFilter
      const matchesStatus = statusFilter === 'all' ||
        (statusFilter === 'enabled' && account.isEnabled) ||
        (statusFilter === 'disabled' && !account.isEnabled) ||
        (statusFilter === 'rate-limited' && account.isRateLimited)

      return matchesSearch && matchesProvider && matchesStatus
    })
  }, [accounts, searchTerm, providerFilter, statusFilter])

  return (
    <div className="container mx-auto p-4 sm:p-6 lg:p-8 space-y-8 max-w-7xl">
      {/* Header */}
      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <div className="space-y-1">
          <h2 className="text-3xl font-bold tracking-tight">AI 账户管理</h2>
          <p className="text-muted-foreground">
            连接并监控您的多平台 AI 服务账户，实时掌握用量与状态。
          </p>
        </div>
        <Button
          onClick={() => setAddDialogOpen(true)}
          className="w-full sm:w-auto shadow-lg shadow-primary/20 hover:shadow-primary/30 transition-all"
        >
          <Plus className="mr-2 h-4 w-4" />
          添加新账户
        </Button>
      </div>

      {/* Summary Stats */}
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
        {[
          { label: '账户总数', value: stats.total, icon: Users, color: 'bg-blue-500/10 text-blue-500' },
          { label: '活跃账户', value: stats.active, icon: ShieldCheck, color: 'bg-emerald-500/10 text-emerald-500' },
          { label: '受限账户', value: stats.limited, icon: ZapOff, color: 'bg-orange-500/10 text-orange-500' },
          { label: '已禁用', value: stats.disabled, icon: XCircle, color: 'bg-gray-500/10 text-gray-500' },
        ].map((stat, i) => (
          <Card key={i} variant="default" className="overflow-hidden border-none bg-muted/30 hover:bg-muted/50 transition-colors">
            <CardContent className="p-4 flex items-center justify-between">
              <div>
                <p className="text-xs font-medium text-muted-foreground uppercase tracking-wider">{stat.label}</p>
                <h3 className="text-2xl font-bold mt-1">{stat.value}</h3>
              </div>
              <div className={cn("p-2 rounded-xl", stat.color)}>
                <stat.icon className="h-5 w-5" />
              </div>
            </CardContent>
          </Card>
        ))}
      </div>

      {/* Filters */}
      <Card className="border-none bg-muted/30 shadow-none">
        <CardContent className="p-4 flex flex-col gap-4 lg:flex-row lg:items-center">
          <div className="relative flex-1 group">
            <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground transition-colors group-focus-within:text-primary" />
            <Input
              placeholder="搜索名称、邮箱或提供商..."
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)}
              className="pl-10 bg-background border-none focus-visible:ring-1"
            />
          </div>
          
          <div className="flex flex-wrap items-center gap-4">
            <div className="flex items-center gap-2">
              <span className="text-xs font-medium text-muted-foreground whitespace-nowrap">类型:</span>
              <ToggleGroup
                type="single"
                size="sm"
                value={providerFilter}
                onValueChange={(v) => v && setProviderFilter(v as ProviderFilter)}
                className="bg-background rounded-md p-1 border"
              >
                <ToggleGroupItem value="all" className="px-3">全部</ToggleGroupItem>
                {providerOptions.map(opt => (
                  <ToggleGroupItem key={opt.key} value={opt.key} className="px-3 whitespace-nowrap">
                    {opt.label}
                  </ToggleGroupItem>
                ))}
              </ToggleGroup>
            </div>

            <div className="flex items-center gap-2">
              <span className="text-xs font-medium text-muted-foreground whitespace-nowrap">状态:</span>
              <ToggleGroup
                type="single"
                size="sm"
                value={statusFilter}
                onValueChange={(v) => v && setStatusFilter(v as StatusFilter)}
                className="bg-background rounded-md p-1 border"
              >
                <ToggleGroupItem value="all" className="px-3">全部</ToggleGroupItem>
                <ToggleGroupItem value="enabled" className="px-3">启用</ToggleGroupItem>
                <ToggleGroupItem value="rate-limited" className="px-3 text-orange-500">限流</ToggleGroupItem>
              </ToggleGroup>
            </div>
          </div>
        </CardContent>
      </Card>

      {/* Account Cards Grid */}
      {loading ? (
        <div className="grid gap-6 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
          {[...Array(8)].map((_, i) => (
            <Card key={i} className="h-64 animate-pulse">
              <CardContent className="p-6 space-y-4">
                <div className="flex justify-between">
                  <Skeleton className="h-6 w-20" />
                  <Skeleton className="h-8 w-8 rounded-full" />
                </div>
                <Skeleton className="h-4 w-3/4" />
                <Skeleton className="h-3 w-1/2" />
                <div className="space-y-2 pt-4">
                  <Skeleton className="h-2 w-full" />
                  <Skeleton className="h-2 w-full" />
                </div>
              </CardContent>
            </Card>
          ))}
        </div>
      ) : filteredAccounts.length === 0 ? (
        <div className="flex flex-col items-center justify-center py-24 text-center">
          <div className="p-4 rounded-full bg-muted mb-4">
            <Users className="h-10 w-10 text-muted-foreground/50" />
          </div>
          <h3 className="text-xl font-semibold">未找到匹配账户</h3>
          <p className="text-muted-foreground mt-2 max-w-xs">
            {searchTerm || providerFilter !== 'all' || statusFilter !== 'all' 
              ? '请尝试调整筛选条件或搜索关键词。' 
              : '点击“添加新账户”开始管理您的 AI 服务。'}
          </p>
          {!(searchTerm || providerFilter !== 'all' || statusFilter !== 'all') && (
             <Button onClick={() => setAddDialogOpen(true)} variant="outline" className="mt-6">
                <Plus className="mr-2 h-4 w-4" /> 添加第一个账户
             </Button>
          )}
        </div>
      ) : (
        <div className="grid gap-6 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
          <AnimatePresence mode="popLayout">
            {filteredAccounts.map((account, index) => (
              <AccountCard
                key={account.id}
                account={account}
                index={index}
                quota={quotaStatuses[account.id]}
                isRefreshing={refreshingQuotaId === account.id}
                isToggling={togglingId === account.id}
                isDeleting={deletingId === account.id}
                onRefresh={() => handleRefreshQuota(account.id, account.provider)}
                onToggle={() => handleToggleStatus(account.id)}
                onDelete={() => handleDeleteClick(account.id)}
                onViewModels={() => handleViewModels(account)}
              />
            ))}
          </AnimatePresence>
        </div>
      )}

      {/* Dialogs */}
      <AddAccountDialog
        open={addDialogOpen}
        onOpenChange={setAddDialogOpen}
        onAccountAdded={handleAccountAdded}
      />

      <AntigravityModelsDialog
        open={modelsDialogOpen}
        onOpenChange={setModelsDialogOpen}
        loading={modelsLoading}
        error={modelsError}
        models={models}
        accountLabel={modelsAccountLabel}
      />

      <DeleteConfirmDialog
        open={deleteConfirmOpen}
        onOpenChange={setDeleteConfirmOpen}
        onConfirm={handleDeleteConfirm}
        isDeleting={deletingId === deleteConfirmAccountId}
      />
      
      {error && (
        <div className="fixed bottom-4 right-4 z-50 animate-in fade-in slide-in-from-bottom-5">
           <Card className="border-destructive/50 bg-destructive/5 text-destructive-foreground flex items-center gap-3 p-4 shadow-2xl backdrop-blur">
              <AlertCircle className="h-5 w-5 text-destructive" />
              <div className="flex-1">
                <p className="text-sm font-semibold">操作提示</p>
                <p className="text-xs opacity-90">{error}</p>
              </div>
              <Button size="icon-sm" variant="ghost" onClick={() => setError(null)}>
                <XCircle className="h-4 w-4" />
              </Button>
           </Card>
        </div>
      )}
    </div>
  )
}

function AccountCard({ 
  account, 
  quota, 
  index, 
  isRefreshing, 
  isToggling, 
  isDeleting, 
  onRefresh, 
  onToggle, 
  onDelete, 
  onViewModels 
}: { 
  account: AIAccountDto, 
  quota?: AccountQuotaStatus,
  index: number,
  isRefreshing: boolean,
  isToggling: boolean,
  isDeleting: boolean,
  onRefresh: () => void,
  onToggle: () => void,
  onDelete: () => void,
  onViewModels: () => void,
}) {
  const providerKey = normalizeProviderKey(account.provider)
  const healthScore = quota?.healthScore ?? 0

  return (
    <motion.div
      layout
      initial={{ opacity: 0, scale: 0.95 }}
      animate={{ opacity: 1, scale: 1 }}
      exit={{ opacity: 0, scale: 0.95 }}
      transition={{ delay: index * 0.05 }}
    >
      <Card variant="default" hoverable className="group relative h-full flex flex-col border border-border/50 overflow-hidden bg-card/50 backdrop-blur-sm">
        <CardContent className="p-5 flex-1 flex flex-col">
          {/* Top Row */}
          <div className="flex items-start justify-between mb-4">
            <div className="flex flex-col gap-2">
              <div className="flex items-center gap-2">
                 <span className={cn(
                    "px-2 py-0.5 rounded-full text-[10px] font-bold uppercase tracking-tighter",
                    providerKey === 'openai' ? "bg-emerald-500/10 text-emerald-600" :
                    providerKey === 'claude' ? "bg-orange-500/10 text-orange-600" :
                    providerKey === 'kiro' ? "bg-slate-500/10 text-slate-600" :
                    providerKey === 'factory' ? "bg-purple-500/10 text-purple-600" :
                    "bg-blue-500/10 text-blue-600"
                 )}>
                    {account.provider}
                 </span>
                 <div className={cn(
                    "h-2 w-2 rounded-full ring-4 ring-background",
                    account.isEnabled ? "bg-emerald-500 animate-pulse" : "bg-muted-foreground/30"
                 )} />
              </div>
            </div>

            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button variant="ghost" size="icon-sm" className="opacity-0 group-hover:opacity-100 transition-opacity">
                  <MoreVertical className="h-4 w-4" />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end" className="w-52">
                <DropdownMenuItem onClick={onRefresh} disabled={isRefreshing} className="gap-2">
                  <RefreshCw className={cn("h-4 w-4", isRefreshing && "animate-spin")} />
                  {isRefreshing ? '正在同步用量...' : '同步实时用量'}
                </DropdownMenuItem>
                {providerKey === 'gemini-antigravity' && (
                  <DropdownMenuItem onClick={onViewModels} className="gap-2">
                    <Eye className="h-4 w-4" /> 查看可用模型
                  </DropdownMenuItem>
                )}
                <DropdownMenuSeparator />
                <DropdownMenuItem onClick={onToggle} disabled={isToggling} className="gap-2">
                   {account.isEnabled ? (
                      <>
                        <ZapOff className="h-4 w-4 text-orange-500" />
                        <span className="text-orange-500">立即禁用</span>
                      </>
                   ) : (
                      <>
                        <CheckCircle className="h-4 w-4 text-emerald-500" />
                        <span className="text-emerald-500">启用账户</span>
                      </>
                   )}
                </DropdownMenuItem>
                <DropdownMenuItem onClick={onDelete} disabled={isDeleting} className="gap-2 text-destructive">
                  <Trash2 className="h-4 w-4" />
                  删除永久账户
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          </div>

          {/* Account Info */}
          <div className="mb-4 space-y-1">
             <h3 className="font-bold text-base leading-tight truncate pr-4" title={account.name || account.email}>
                {account.name || account.email || '未命名账户'}
             </h3>
             <p className="text-xs text-muted-foreground truncate opacity-70">
                {account.email || '无关联邮箱'}
             </p>
          </div>

          {account.isRateLimited && (
             <div className="mb-4 px-3 py-2 rounded-lg bg-orange-500/10 text-orange-600 border border-orange-500/20">
                <div className="flex items-center gap-1.5 text-[11px] font-bold uppercase">
                   <Activity className="h-3 w-3" />
                   限流状态
                </div>
                <p className="text-[10px] mt-0.5 opacity-80">
                   预计 {account.rateLimitResetTime ? formatDate(account.rateLimitResetTime) : '稍后'} 解除
                </p>
             </div>
          )}

          {/* Quota Stats */}
          <div className="space-y-4 mt-auto">
             {quota?.hasCacheData ? (
                <div className="space-y-4">
                   <div className="flex items-center justify-between">
                      <div className="flex items-center gap-1.5">
                         <ShieldCheck className={cn("h-3.5 w-3.5", getHealthColor(healthScore))} />
                         <span className="text-xs font-semibold">账户健康度</span>
                      </div>
                      <span className={cn("text-xs font-bold", getHealthColor(healthScore))}>{healthScore}%</span>
                   </div>

                   {/* Usage Bars */}
                   {quota.tokensLimit && quota.tokensLimit > 0 ? (
                      <div className="space-y-2.5">
                         <UsageProgress 
                            label="总 Tokens" 
                            percent={quota.tokensUsedPercent} 
                            subText={`${formatTokens(quota.tokensRemaining ?? 0)} / ${formatTokens(quota.tokensLimit)}`}
                         />
                         {quota.inputTokensLimit && (
                            <div className="grid grid-cols-2 gap-3">
                               <div className="space-y-1">
                                  <span className="text-[10px] text-muted-foreground">Input</span>
                                  <div className="text-[10px] font-medium tabular-nums">{formatTokens(quota.inputTokensRemaining ?? 0)}</div>
                               </div>
                               <div className="space-y-1">
                                  <span className="text-[10px] text-muted-foreground">Output</span>
                                  <div className="text-[10px] font-medium tabular-nums">{formatTokens(quota.outputTokensRemaining ?? 0)}</div>
                               </div>
                            </div>
                         )}
                      </div>
                   ) : (
                      <div className="grid grid-cols-2 gap-3">
                         <UsageProgress 
                            label="5H 窗口" 
                            percent={quota.primaryUsedPercent} 
                            resetText={formatTimeRemaining(quota.primaryResetAfterSeconds)}
                         />
                         <UsageProgress 
                            label="7D 窗口" 
                            percent={quota.secondaryUsedPercent} 
                            resetText={formatTimeRemaining(quota.secondaryResetAfterSeconds)}
                         />
                      </div>
                   )}
                </div>
             ) : (
                <div className="rounded-xl border border-dashed p-4 text-center bg-muted/20">
                   <p className="text-[11px] text-muted-foreground">
                      暂无配额详情数据<br/>
                      <span className="opacity-70">点击上方菜单同步状态</span>
                   </p>
                </div>
             )}
          </div>
        </CardContent>

        {/* Footer */}
        <div className="px-5 py-3 border-t bg-muted/10 flex items-center justify-between text-[10px] font-medium text-muted-foreground/60 uppercase tracking-tighter">
           <div className="flex items-center gap-1">
              <Activity className="h-3 w-3" />
              累计调用 {account.usageCount}
           </div>
           <div>{formatDate(account.createdAt)}</div>
        </div>
      </Card>
    </motion.div>
  )
}

function UsageProgress({ label, percent, subText, resetText }: { label: string, percent?: number, subText?: string, resetText?: string }) {
  const p = Math.min(percent ?? 0, 100)
  return (
    <div className="space-y-1.5">
       <div className="flex items-center justify-between text-[10px] font-bold uppercase tracking-tight text-muted-foreground">
          <span>{label}</span>
          <span>{p}%</span>
       </div>
       <div className="h-1.5 w-full bg-muted rounded-full overflow-hidden">
          <motion.div 
             initial={{ width: 0 }}
             animate={{ width: `${p}%` }}
             className={cn("h-full transition-all duration-1000", getUsageColor(p))} 
          />
       </div>
       {subText && <div className="text-[9px] text-muted-foreground/70 font-mono">{subText}</div>}
       {resetText && <div className="text-[9px] text-muted-foreground/70">{resetText}后重置</div>}
    </div>
  )
}

function AntigravityModelsDialog({ open, onOpenChange, loading, error, models, accountLabel }: any) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>Gemini 可用模型</DialogTitle>
          <DialogDescription className="truncate">
            账户：{accountLabel}
          </DialogDescription>
        </DialogHeader>
        <div className="max-h-[60vh] overflow-y-auto space-y-2 pr-2">
          {loading ? (
             <div className="flex flex-col items-center justify-center py-10 gap-3">
                <Loader className="h-6 w-6 animate-spin text-primary" />
                <span className="text-sm text-muted-foreground">正在获取模型列表...</span>
             </div>
          ) : error ? (
             <div className="p-4 rounded-lg bg-destructive/10 text-destructive text-sm flex gap-2">
                <AlertCircle className="h-4 w-4 shrink-0 mt-0.5" />
                {error}
             </div>
          ) : models.length === 0 ? (
             <p className="text-center py-10 text-muted-foreground text-sm">该账户暂未返回可用模型</p>
          ) : (
             models.map((m: string) => (
               <div key={m} className="px-3 py-2 rounded-md bg-muted/50 text-xs font-mono border flex items-center justify-between group hover:border-primary/50 transition-colors">
                  {m}
                  <button onClick={() => navigator.clipboard.writeText(m)} className="opacity-0 group-hover:opacity-100 p-1 hover:bg-background rounded">
                     <Eye className="h-3 w-3" />
                  </button>
               </div>
             ))
          )}
        </div>
      </DialogContent>
    </Dialog>
  )
}

function DeleteConfirmDialog({ open, onOpenChange, onConfirm, isDeleting }: any) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>确认删除账户？</DialogTitle>
          <DialogDescription>
            此操作将永久移除该账户及其所有的配额统计记录，且无法恢复。
          </DialogDescription>
        </DialogHeader>
        <DialogFooter className="gap-3 mt-4">
          <Button variant="outline" onClick={() => onOpenChange(false)} disabled={isDeleting}>
            取消
          </Button>
          <Button variant="destructive" onClick={onConfirm} disabled={isDeleting} className="gap-2">
            {isDeleting && <Loader className="h-4 w-4 animate-spin" />}
            确认删除
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
