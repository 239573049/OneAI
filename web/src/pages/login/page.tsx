import { useState } from 'react'
import { motion, AnimatePresence } from 'motion/react'
import { useNavigate } from 'react-router-dom'
import { Eye, EyeOff, Loader2, HelpCircle } from 'lucide-react'
import {
  Button as AnimateUIButton,
} from '@/components/animate-ui/components/buttons/button'
import { Input } from '@/components/animate-ui/components/input'
import { Label } from '@/components/animate-ui/components/label'
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/animate-ui/components/card'
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from '@/components/animate-ui/components/animate/tooltip'
import { authService } from '@/services/auth'
import { setToken } from '@/services/api'
import type { LoginRequest } from '@/types/auth'
import { scaleInCenter } from '@/lib/animations'
import { ThemeToggle } from '@/components/theme-toggle'

export default function LoginPage() {
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
    <div className="flex min-h-screen items-center justify-center bg-gradient-to-br from-slate-50 via-blue-50 to-slate-100 dark:from-slate-950 dark:via-slate-900 dark:to-slate-800 p-4 relative overflow-hidden">
      {/* Theme Toggle */}
      <div className="absolute top-4 right-4 z-20">
        <ThemeToggle />
      </div>

      {/* Animated background elements */}
      <div className="absolute inset-0 overflow-hidden pointer-events-none">
        <motion.div
          className="absolute w-96 h-96 rounded-full bg-gradient-to-r from-blue-400/20 to-cyan-400/20 blur-3xl dark:from-blue-500/10 dark:to-cyan-500/10"
          animate={{
            x: [0, 30, 0],
            y: [0, 20, 0],
          }}
          transition={{ duration: 8, repeat: Infinity, ease: 'easeInOut' }}
          style={{ top: '-50px', left: '-50px' }}
        />
        <motion.div
          className="absolute w-96 h-96 rounded-full bg-gradient-to-r from-purple-400/20 to-pink-400/20 blur-3xl dark:from-purple-500/10 dark:to-pink-500/10"
          animate={{
            x: [0, -30, 0],
            y: [0, -20, 0],
          }}
          transition={{ duration: 8, repeat: Infinity, ease: 'easeInOut', delay: 0.5 }}
          style={{ bottom: '-50px', right: '-50px' }}
        />
      </div>

      {/* Content */}
      <motion.div {...scaleInCenter} className="relative z-10 w-full max-w-md">
        <Card className="w-full" hoverable={false} variant="elevated">
          <CardHeader className="space-y-1">
            <motion.div
              initial={{ opacity: 0, y: -10 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: 0.1, duration: 0.5 }}
            >
              <CardTitle className="text-2xl font-bold text-center">
                欢迎回来
              </CardTitle>
            </motion.div>
            <motion.div
              initial={{ opacity: 0, y: -10 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: 0.15, duration: 0.5 }}
            >
              <CardDescription className="text-center">
                请输入您的账号密码登录
              </CardDescription>
            </motion.div>
          </CardHeader>
          <CardContent>
            <form onSubmit={handleSubmit} className="space-y-4">
              <motion.div
                className="space-y-2"
                initial={{ opacity: 0, y: 10 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ delay: 0.2, duration: 0.5 }}
              >
                <Label htmlFor="username" required animated>
                  用户名
                </Label>
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
                  animated
                />
              </motion.div>

              <motion.div
                className="space-y-2"
                initial={{ opacity: 0, y: 10 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ delay: 0.25, duration: 0.5 }}
              >
                <div className="flex items-center justify-between">
                  <Label htmlFor="password" required animated>
                    密码
                  </Label>
                  <Tooltip>
                    <TooltipTrigger asChild>
                      <AnimateUIButton
                        type="button"
                        variant="link"
                        className="px-0 text-xs inline-flex items-center gap-1"
                        disabled={loading}
                        onClick={() => {
                          // TODO: 实现忘记密码功能
                          console.log('忘记密码')
                        }}
                      >
                        <HelpCircle className="h-3 w-3" />
                        忘记密码？
                      </AnimateUIButton>
                    </TooltipTrigger>
                    <TooltipContent>
                      <p>密码重置功能即将上线</p>
                    </TooltipContent>
                  </Tooltip>
                </div>
                <div className="relative">
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
                    initial={{ opacity: 0, y: -10, x: 0 }}
                    animate={{
                      opacity: 1,
                      y: 0,
                      x: [0, -10, 10, -10, 10, 0]
                    }}
                    exit={{ opacity: 0, y: -10 }}
                    transition={{
                      opacity: { type: 'spring', stiffness: 400, damping: 40 },
                      y: { type: 'spring', stiffness: 400, damping: 40 },
                      x: { duration: 0.5, ease: 'easeInOut' }
                    }}
                    className="text-sm text-destructive bg-destructive/10 border border-destructive/20 rounded-md p-3"
                  >
                    {error}
                  </motion.div>
                )}
              </AnimatePresence>

              <motion.div
                initial={{ opacity: 0, y: 10 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ delay: 0.3, duration: 0.5 }}
              >
                <AnimateUIButton
                  type="submit"
                  className="w-full"
                  disabled={loading}
                >
                  {loading ? (
                    <motion.span
                      className="inline-flex items-center gap-2"
                      animate={{ opacity: [0.5, 1] }}
                      transition={{ duration: 0.5, repeat: Infinity }}
                    >
                      <motion.span animate={{ rotate: 360 }} transition={{ duration: 1, repeat: Infinity, ease: 'linear' }}>
                        <Loader2 className="h-4 w-4" />
                      </motion.span>
                      登录中
                    </motion.span>
                  ) : (
                    '登录'
                  )}
                </AnimateUIButton>
              </motion.div>
            </form>
          </CardContent>
        </Card>
      </motion.div>
    </div>
  )
}
