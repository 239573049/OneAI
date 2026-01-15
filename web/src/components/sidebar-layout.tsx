'use client';

import { motion } from 'motion/react';
import { LayoutDashboard, User, FileText, Settings, Sliders } from 'lucide-react';
import { useState } from 'react';
import { useNavigate, useLocation } from 'react-router-dom';
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarGroup,
  SidebarGroupLabel,
  SidebarHeader,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarProvider,
  SidebarTrigger,
} from '@/components/animate-ui/components/radix/sidebar';
import { clearToken } from '@/services/api';
import { UserMenu } from '@/components/user-menu';

const menuItems = [
  {
    label: '仪表盘',
    icon: LayoutDashboard,
    id: 'home',
  },
  {
    label: '账户管理',
    icon: User,
    id: 'account',
  },
  {
    label: '请求日志',
    icon: FileText,
    id: 'logs',
  },
];

const settingsItems = [
  {
    label: '设置',
    icon: Settings,
    id: 'settings',
  },
  {
    label: '模型映射',
    icon: Sliders,
    id: 'model-mapping',
  },
];

export interface SidebarLayoutProps {
  children: React.ReactNode;
}

export function SidebarLayout({
  children
}: SidebarLayoutProps) {
  const navigate = useNavigate();
  const location = useLocation();
  const [isLoggingOut, setIsLoggingOut] = useState(false);

  // 根据当前路由确定活跃菜单
  const getActiveMenu = () => {
    const path = location.pathname;
    if (path.includes('/accounts')) return 'account';
    if (path.includes('/logs')) return 'logs';
    if (path.includes('/model-mapping')) return 'model-mapping';
    if (path.includes('/settings')) return 'settings';
    return 'home';
  };

  const currentActiveMenu = getActiveMenu();

  const handleLogout = async () => {
    setIsLoggingOut(true);
    await new Promise((resolve) => setTimeout(resolve, 300));
    clearToken();
    navigate('/login');
  };

  const handleMenuClick = (menuId: string) => {
    // 导航到对应的路由
    const pathMap: Record<string, string> = {
      'home': '/',
      'account': '/accounts',
      'logs': '/logs',
      'settings': '/settings',
      'model-mapping': '/model-mapping',
    };
    const path = pathMap[menuId] || '/';
    navigate(path);
  };

  return (
    <SidebarProvider>
      <div className="flex h-screen w-full bg-background">
        {/* Sidebar */}
        <Sidebar className="border-r">
          <SidebarHeader>
            <motion.div
              initial={{ opacity: 0, x: -20 }}
              animate={{ opacity: 1, x: 0 }}
              transition={{ duration: 0.5 }}
              className="flex items-center gap-3"
            >
              <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-primary text-primary-foreground font-bold text-sm">
                AI
              </div>
              <div>
                <span className="font-bold text-base">OneAI</span>
                <p className="text-xs text-muted-foreground">v1.0.0</p>
              </div>
            </motion.div>
          </SidebarHeader>

          <SidebarContent>
            {/* Main Menu Group */}
            <SidebarGroup>
              <SidebarGroupLabel>菜单</SidebarGroupLabel>
              <SidebarMenu>
                {menuItems.map((item) => (
                  <motion.div
                    key={item.id}
                    initial={{ opacity: 0, x: -10 }}
                    animate={{ opacity: 1, x: 0 }}
                    transition={{ duration: 0.3 }}
                  >
                    <SidebarMenuItem>
                      <SidebarMenuButton
                        onClick={() => handleMenuClick(item.id)}
                        isActive={currentActiveMenu === item.id}
                        className="hover:bg-accent"
                      >
                        <item.icon className="h-4 w-4" />
                        <span>{item.label}</span>
                      </SidebarMenuButton>
                    </SidebarMenuItem>
                  </motion.div>
                ))}
              </SidebarMenu>
            </SidebarGroup>

            {/* Settings Group */}
            <SidebarGroup>
              <SidebarGroupLabel>设置</SidebarGroupLabel>
              <SidebarMenu>
                {settingsItems.map((item) => (
                  <motion.div
                    key={item.id}
                    initial={{ opacity: 0, x: -10 }}
                    animate={{ opacity: 1, x: 0 }}
                    transition={{ duration: 0.3 }}
                  >
                    <SidebarMenuItem>
                      <SidebarMenuButton
                        onClick={() => handleMenuClick(item.id)}
                        isActive={currentActiveMenu === item.id}
                        className="hover:bg-accent"
                      >
                        <item.icon className="h-4 w-4" />
                        <span>{item.label}</span>
                      </SidebarMenuButton>
                    </SidebarMenuItem>
                  </motion.div>
                ))}
              </SidebarMenu>
            </SidebarGroup>
          </SidebarContent>

          <SidebarFooter>
            <motion.div
              initial={{ opacity: 0, y: 10 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ duration: 0.3 }}
            >
              <UserMenu
                userName="User"
                onLogout={handleLogout}
              />
            </motion.div>
          </SidebarFooter>
        </Sidebar>

        {/* Main Content */}
        <div className="flex-1 flex flex-col overflow-hidden">
          {/* Header */}
          <motion.div
            initial={{ opacity: 0, y: -10 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.3 }}
            className="flex items-center justify-between border-b bg-card px-4 py-4 sm:px-6"
          >
            <div className="flex items-center gap-2">
              <SidebarTrigger />
              <h1 className="text-2xl font-bold">OneAI</h1>
            </div>
          </motion.div>

          {/* Content Area */}
          <div className="flex-1 overflow-auto">
            <motion.div
              initial={{ opacity: 0, y: 20 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ duration: 0.3 }}
              className="h-full"
            >
              {children}
            </motion.div>
          </div>
        </div>
      </div>
    </SidebarProvider>
  );
}
