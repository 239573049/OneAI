import { useState, useEffect } from 'react'
import { motion } from 'motion/react'
import { useLocation } from 'react-router-dom'
import { Lock, Code2, Shield, Zap, ArrowRight, Trash2, AlertCircle, Loader, Search, Plus, MoreVertical, CheckCircle, XCircle } from 'lucide-react'
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
import { SidebarLayout } from '@/components/sidebar-layout'
import { AddAccountDialog } from '@/components/add-account-dialog'
import { SettingsView as SettingsViewComponent } from '@/components/settings-view'
import { accountService } from '@/services/account'
import type { AIAccountDto, AccountQuotaStatus } from '@/types/account'
import Logs from './Logs'
import AccountManagementView from './accounts/page'
import HomeView from './home/page'
import ModelMappingPage from './model-mapping/page'

const features = [
  { icon: Lock, text: '完整的认证系统', description: '安全的令牌管理' },
  { icon: Code2, text: '统一的 API 封装', description: '简化的 HTTP 客户端' },
  { icon: Zap, text: 'animate-ui 组件库', description: '现代化的动画组件' },
  { icon: Shield, text: 'TypeScript 类型安全', description: '完全的类型检查' },
]

const stats = [
  { label: '已完成请求', value: '1,234', icon: Zap },
  { label: '系统运行时间', value: '99.9%', icon: Shield },
  { label: '用户数', value: '42', icon: Lock },
]

export default function Home() {
  const location = useLocation()

  const getActiveMenu = () => {
    const path = location.pathname
    if (path.includes('/accounts')) return 'account'
    if (path.includes('/logs')) return 'logs'
    if (path.includes('/model-mapping')) return 'model-mapping'
    if (path.includes('/settings')) return 'settings'
    return 'home'
  }

  const renderContent = () => {
    const activeMenu = getActiveMenu()
    switch (activeMenu) {
      case 'account':
        return <AccountManagementView />
      case 'logs':
        return <Logs />
      case 'model-mapping':
        return <ModelMappingPage />
      case 'settings':
        return <SettingsViewComponent />
      default:
        return <HomeView />
    }
  }

  return (
    <SidebarLayout>
      {renderContent()}
    </SidebarLayout>
  )
}
