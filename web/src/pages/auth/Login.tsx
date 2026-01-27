import { useState } from 'react'
import { motion, AnimatePresence } from 'motion/react'
import { useNavigate } from 'react-router-dom'
import { Eye, EyeOff, Loader2, Sparkles, Bot, User, Lock } from 'lucide-react'
import {
  Button as AnimateUIButton,
} from '@/components/animate-ui/components/buttons/button'
import { Input } from '@/components/animate-ui/components/input'
import { Label } from '@/components/animate-ui/components/label'
import { authService } from '@/services/auth'
import { setToken } from '@/services/api'
import type { LoginRequest } from '@/types/auth'
import { ThemeToggle } from '@/components/theme-toggle'

export default function Login() {
  const navigate = useNavigate()
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [showPassword, setShowPassword] = useState(false)
  const [formData, setFormData] = useState<LoginRequest>({
    username: '',
    password: '',
  })

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError('')
    setLoading(true)

    try {
      const response = await authService.login(formData)
      setToken(response.token)

      // 登录成功，跳转到首页
      await new Promise((resolve) => setTimeout(resolve, 300))
      navigate('/')
    } catch (err: any) {
      setError(err.message || '登录失败，请重试')
      setLoading(false)
    }
  }

  const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const { name, value } = e.target
    setFormData((prev) => ({ ...prev, [name]: value }))
  }

  return (
    <div className="flex min-h-screen relative overflow-hidden">
      {/* Theme Toggle */}
      <div className="absolute top-6 right-6 z-20">
        <ThemeToggle />
      </div>

      {/* Left Panel - Branding */}
      <div className="hidden lg:flex lg:w-1/2 relative bg-gradient-to-br from-violet-600 via-purple-600 to-indigo-700 dark:from-violet-900 dark:via-purple-900 dark:to-indigo-950">
        {/* Animated Grid Background */}
        <div className="absolute inset-0 opacity-20">
          <div className="absolute inset-0" style={{
            backgroundImage: `linear-gradient(rgba(255,255,255,0.1) 1px, transparent 1px),
                             linear-gradient(90deg, rgba(255,255,255,0.1) 1px, transparent 1px)`,
            backgroundSize: '50px 50px'
          }} />
        </div>

        {/* Floating Orbs */}
        <motion.div
          className="absolute w-72 h-72 rounded-full bg-white/10 blur-3xl"
          animate={{
            x: [0, 50, 0],
            y: [0, 30, 0],
            scale: [1, 1.1, 1],
          }}
          transition={{ duration: 10, repeat: Infinity, ease: 'easeInOut' }}
          style={{ top: '10%', left: '10%' }}
        />
        <motion.div
          className="absolute w-96 h-96 rounded-full bg-pink-500/20 blur-3xl"
          animate={{
            x: [0, -40, 0],
            y: [0, 50, 0],
            scale: [1, 1.2, 1],
          }}
          transition={{ duration: 12, repeat: Infinity, ease: 'easeInOut', delay: 1 }}
          style={{ bottom: '10%', right: '5%' }}
        />
        <motion.div
          className="absolute w-64 h-64 rounded-full bg-cyan-400/15 blur-3xl"
          animate={{
            x: [0, 30, 0],
            y: [0, -40, 0],
          }}
          transition={{ duration: 8, repeat: Infinity, ease: 'easeInOut', delay: 2 }}
          style={{ top: '50%', left: '30%' }}
        />

        {/* Content */}
        <div className="relative z-10 flex flex-col justify-center px-12 xl:px-20 text-white">
          <motion.div
            initial={{ opacity: 0, y: 30 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.8, delay: 0.2 }}
          >
            {/* Logo */}
            <motion.div
              className="flex items-center gap-3 mb-8"
              whileHover={{ scale: 1.02 }}
            >
              <div className="relative">
                <div className="w-14 h-14 rounded-2xl bg-white/20 backdrop-blur-sm flex items-center justify-center">
                  <Bot className="w-8 h-8" />
                </div>
                <motion.div
                  className="absolute -top-1 -right-1 w-4 h-4 rounded-full bg-green-400"
                  animate={{ scale: [1, 1.2, 1] }}
                  transition={{ duration: 2, repeat: Infinity }}
                />
              </div>
              <div>
                <h1 className="text-3xl font-bold tracking-tight">OneAI</h1>
                <p className="text-white/70 text-sm">AI Account Manager</p>
              </div>
            </motion.div>

            {/* Tagline */}
            <h2 className="text-4xl xl:text-5xl font-bold leading-tight mb-6">
              统一管理
              <br />
              <span className="text-transparent bg-clip-text bg-gradient-to-r from-cyan-300 to-pink-300">
                多平台 AI 账户
              </span>
            </h2>
            <p className="text-lg text-white/80 mb-10 max-w-md leading-relaxed">
              集中管理 OpenAI、Claude、Gemini 等多个 AI 服务账户，
              实时监控配额使用，智能负载均衡。
            </p>

            {/* Features */}
            <div className="space-y-4">
              {[
                { icon: Sparkles, text: '多平台账户统一管理' },
                { icon: Sparkles, text: '实时配额监控与预警' },
                { icon: Sparkles, text: '请求日志与数据分析' },
              ].map((feature, index) => (
                <motion.div
                  key={index}
                  className="flex items-center gap-3"
                  initial={{ opacity: 0, x: -20 }}
                  animate={{ opacity: 1, x: 0 }}
                  transition={{ delay: 0.5 + index * 0.1 }}
                >
                  <div className="w-8 h-8 rounded-lg bg-white/10 flex items-center justify-center">
                    <feature.icon className="w-4 h-4" />
                  </div>
                  <span className="text-white/90">{feature.text}</span>
                </motion.div>
              ))}
            </div>
          </motion.div>
        </div>
      </div>

      {/* Right Panel - Login Form */}
      <div className="w-full lg:w-1/2 flex items-center justify-center p-6 sm:p-12 bg-gradient-to-br from-slate-50 to-slate-100 dark:from-slate-950 dark:to-slate-900">
        <motion.div
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.6 }}
          className="w-full max-w-md"
        >
          {/* Mobile Logo */}
          <motion.div
            className="lg:hidden flex items-center justify-center gap-3 mb-10"
            initial={{ opacity: 0, scale: 0.9 }}
            animate={{ opacity: 1, scale: 1 }}
            transition={{ delay: 0.1 }}
          >
            <div className="w-12 h-12 rounded-xl bg-gradient-to-br from-violet-600 to-indigo-600 flex items-center justify-center text-white">
              <Bot className="w-7 h-7" />
            </div>
            <div>
              <h1 className="text-2xl font-bold">OneAI</h1>
              <p className="text-muted-foreground text-xs">AI Account Manager</p>
            </div>
          </motion.div>

          {/* Form Header */}
          <div className="text-center lg:text-left mb-8">
            <motion.h2
              className="text-2xl sm:text-3xl font-bold text-foreground"
              initial={{ opacity: 0, y: 10 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: 0.2 }}
            >
              欢迎回来
            </motion.h2>
            <motion.p
              className="text-muted-foreground mt-2"
              initial={{ opacity: 0, y: 10 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: 0.25 }}
            >
              请输入您的账号密码登录系统
            </motion.p>
          </div>

          {/* Login Form */}
          <form onSubmit={handleSubmit} className="space-y-5">
            <motion.div
              className="space-y-2"
              initial={{ opacity: 0, y: 15 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: 0.3 }}
            >
              <Label htmlFor="username" className="text-sm font-medium">
                用户名
              </Label>
              <div className="relative">
                <div className="absolute left-3 top-1/2 -translate-y-1/2 text-muted-foreground">
                  <User className="h-4 w-4" />
                </div>
                <Input
                  id="username"
                  name="username"
                  type="text"
                  placeholder="请输入用户名"
                  value={formData.username}
                  onChange={handleChange}
                  required
                  disabled={loading}
                  autoComplete="username"
                  className="pl-10 h-11"
                  animated
                />
              </div>
            </motion.div>

            <motion.div
              className="space-y-2"
              initial={{ opacity: 0, y: 15 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: 0.35 }}
            >
              <Label htmlFor="password" className="text-sm font-medium">
                密码
              </Label>
              <div className="relative">
                <div className="absolute left-3 top-1/2 -translate-y-1/2 text-muted-foreground">
                  <Lock className="h-4 w-4" />
                </div>
                <Input
                  id="password"
                  name="password"
                  type={showPassword ? 'text' : 'password'}
                  placeholder="请输入密码"
                  value={formData.password}
                  onChange={handleChange}
                  required
                  disabled={loading}
                  autoComplete="current-password"
                  className="pl-10 pr-10 h-11"
                  animated
                />
                <motion.button
                  type="button"
                  className="absolute right-3 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground transition-colors"
                  onClick={() => setShowPassword(!showPassword)}
                  disabled={loading}
                  whileHover={{ scale: 1.1 }}
                  whileTap={{ scale: 0.95 }}
                >
                  {showPassword ? (
                    <Eye className="h-4 w-4" />
                  ) : (
                    <EyeOff className="h-4 w-4" />
                  )}
                </motion.button>
              </div>
            </motion.div>

            <AnimatePresence>
              {error && (
                <motion.div
                  initial={{ opacity: 0, y: -10, height: 0 }}
                  animate={{
                    opacity: 1,
                    y: 0,
                    height: 'auto',
                    x: [0, -8, 8, -8, 8, 0]
                  }}
                  exit={{ opacity: 0, y: -10, height: 0 }}
                  transition={{
                    opacity: { duration: 0.2 },
                    height: { duration: 0.2 },
                    x: { duration: 0.4, ease: 'easeInOut' }
                  }}
                  className="text-sm text-destructive bg-destructive/10 border border-destructive/20 rounded-lg p-3 flex items-center gap-2"
                >
                  <div className="w-5 h-5 rounded-full bg-destructive/20 flex items-center justify-center flex-shrink-0">
                    <span className="text-xs">!</span>
                  </div>
                  {error}
                </motion.div>
              )}
            </AnimatePresence>

            <motion.div
              initial={{ opacity: 0, y: 15 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: 0.4 }}
              className="pt-2"
            >
              <AnimateUIButton
                type="submit"
                className="w-full h-11 text-base font-medium bg-gradient-to-r from-violet-600 to-indigo-600 hover:from-violet-700 hover:to-indigo-700 dark:from-violet-600 dark:to-indigo-600"
                disabled={loading}
              >
                {loading ? (
                  <motion.span
                    className="inline-flex items-center gap-2"
                    animate={{ opacity: [0.7, 1] }}
                    transition={{ duration: 0.6, repeat: Infinity }}
                  >
                    <Loader2 className="h-4 w-4 animate-spin" />
                    登录中...
                  </motion.span>
                ) : (
                  '登录'
                )}
              </AnimateUIButton>
            </motion.div>
          </form>

          {/* Footer */}
          <motion.p
            className="text-center text-xs text-muted-foreground mt-8"
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            transition={{ delay: 0.6 }}
          >
            OneAI - 智能 AI 账户管理平台
          </motion.p>
        </motion.div>
      </div>
    </div>
  )
}
