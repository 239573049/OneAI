import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { clearToken } from '@/services/api'
import { useNavigate } from 'react-router-dom'

export default function Home() {
  const navigate = useNavigate()

  const handleLogout = () => {
    clearToken()
    navigate('/login')
  }

  return (
    <div className="min-h-screen bg-gradient-to-br from-gray-50 to-gray-100 dark:from-gray-900 dark:to-gray-800 p-6">
      <div className="max-w-7xl mx-auto">
        <header className="flex items-center justify-between mb-8">
          <h1 className="text-3xl font-bold">OneAI</h1>
          <Button variant="outline" onClick={handleLogout}>
            退出登录
          </Button>
        </header>

        <div className="grid gap-6 md:grid-cols-2 lg:grid-cols-3">
          <Card>
            <CardHeader>
              <CardTitle>欢迎使用 OneAI</CardTitle>
            </CardHeader>
            <CardContent>
              <p className="text-sm text-muted-foreground">
                这是一个基于 React + TypeScript + shadcn/ui 构建的现代化前端应用。
              </p>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>功能特性</CardTitle>
            </CardHeader>
            <CardContent>
              <ul className="text-sm text-muted-foreground space-y-2">
                <li>✓ 完整的认证系统</li>
                <li>✓ 统一的 API 封装</li>
                <li>✓ shadcn/ui 组件库</li>
                <li>✓ TypeScript 类型安全</li>
              </ul>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>快速开始</CardTitle>
            </CardHeader>
            <CardContent>
              <p className="text-sm text-muted-foreground">
                在这里开始构建你的应用功能...
              </p>
            </CardContent>
          </Card>
        </div>
      </div>
    </div>
  )
}
