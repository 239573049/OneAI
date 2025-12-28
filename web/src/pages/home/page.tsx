import { useState, useEffect } from 'react';
import { motion } from 'motion/react';
import {
  TrendingUp,
  Activity,
  Zap,
  Clock,
  AlertCircle,
  Loader2,
} from 'lucide-react';
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
  CardDescription,
} from '@/components/animate-ui/components/card';
import { logsApi } from '@/services/logs';
import type { LogStatistics, HourlySummaryDto } from '@/types/logs';
import RequestTrendChart from '@/components/charts/RequestTrendChart';
import ModelDistributionChart from '@/components/charts/ModelDistributionChart';
import ResponseTimeChart from '@/components/charts/ResponseTimeChart';

export default function HomeView() {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [statistics, setStatistics] = useState<LogStatistics | null>(null);
  const [hourlySummary, setHourlySummary] = useState<HourlySummaryDto[]>([]);

  useEffect(() => {
    loadData();
  }, []);

  const loadData = async () => {
    try {
      setLoading(true);
      setError(null);

      // 计算最近7天的时间范围
      const endTime = new Date().toISOString();
      const startTime = new Date(Date.now() - 7 * 24 * 60 * 60 * 1000).toISOString();

      // 并行加载统计数据和趋势数据
      const [statsData, summaryData] = await Promise.all([
        logsApi.getStatistics({ startTime, endTime }),
        logsApi.getHourlySummary({ startTime, endTime }),
      ]);

      setStatistics(statsData);
      setHourlySummary(summaryData);
    } catch (err) {
      console.error('加载统计数据失败:', err);
      setError(err instanceof Error ? err.message : '加载数据失败');
    } finally {
      setLoading(false);
    }
  };

  const formatNumber = (num: number): string => {
    return num.toLocaleString('zh-CN');
  };

  const formatPercentage = (num: number): string => {
    return `${(num * 100).toFixed(1)}%`;
  };

  const formatDuration = (ms: number): string => {
    if (ms < 1000) return `${Math.round(ms)}ms`;
    return `${(ms / 1000).toFixed(2)}s`;
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center h-[calc(100vh-200px)]">
        <div className="text-center space-y-4">
          <Loader2 className="h-8 w-8 animate-spin mx-auto text-primary" />
          <p className="text-muted-foreground">加载统计数据中...</p>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex items-center justify-center h-[calc(100vh-200px)]">
        <div className="text-center space-y-4">
          <AlertCircle className="h-12 w-12 mx-auto text-destructive" />
          <div>
            <p className="text-lg font-semibold">加载失败</p>
            <p className="text-sm text-muted-foreground">{error}</p>
          </div>
          <button
            onClick={loadData}
            className="px-4 py-2 bg-primary text-primary-foreground rounded-md hover:bg-primary/90"
          >
            重试
          </button>
        </div>
      </div>
    );
  }

  if (!statistics) {
    return null;
  }

  const stats = [
    {
      label: '总请求数',
      value: formatNumber(statistics.totalRequests),
      icon: Activity,
      description: `成功 ${formatNumber(statistics.successRequests)} | 失败 ${formatNumber(statistics.failedRequests)}`,
    },
    {
      label: '成功率',
      value: formatPercentage(statistics.successRate),
      icon: TrendingUp,
      description: `已完成 ${formatNumber(statistics.completedRequests)} 个请求`,
    },
    {
      label: 'Token 消耗',
      value: formatNumber(statistics.totalTokens),
      icon: Zap,
      description: '总计消耗的 Token 数量',
    },
    {
      label: '平均响应时间',
      value: formatDuration(statistics.avgDurationMs),
      icon: Clock,
      description: '所有请求的平均处理时间',
    },
  ];

  return (
    <div className="p-6 space-y-6">
      {/* Welcome Section */}
      <motion.div
        initial={{ opacity: 0, y: 20 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.5 }}
        className="space-y-2"
      >
        <h2 className="text-3xl font-bold">统计概览</h2>
        <p className="text-muted-foreground">
          最近 7 天的 AI 请求统计数据
        </p>
      </motion.div>

      {/* Stats Grid */}
      <motion.div
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        transition={{ staggerChildren: 0.1, delayChildren: 0.2 }}
        className="grid gap-4 md:grid-cols-2 lg:grid-cols-4"
      >
        {stats.map((stat, index) => (
          <motion.div
            key={index}
            initial={{ opacity: 0, y: 20 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ delay: 0.1 * index, duration: 0.5 }}
          >
            <Card variant="elevated">
              <CardContent className="pt-6">
                <div className="flex items-center justify-between">
                  <div className="flex-1">
                    <p className="text-sm text-muted-foreground">{stat.label}</p>
                    <p className="text-2xl font-bold mt-2">{stat.value}</p>
                    <p className="text-xs text-muted-foreground mt-1">
                      {stat.description}
                    </p>
                  </div>
                  <div className="text-primary opacity-20">
                    <stat.icon className="h-8 w-8" />
                  </div>
                </div>
              </CardContent>
            </Card>
          </motion.div>
        ))}
      </motion.div>

      {/* Charts Grid */}
      <motion.div
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        transition={{ delay: 0.4 }}
        className="grid gap-6 md:grid-cols-2"
      >
        {/* Request Trend Chart */}
        <Card variant="elevated">
          <CardHeader>
            <CardTitle>请求趋势</CardTitle>
            <CardDescription>每小时请求数量变化</CardDescription>
          </CardHeader>
          <CardContent>
            {hourlySummary.length > 0 ? (
              <RequestTrendChart data={hourlySummary} />
            ) : (
              <div className="h-[300px] flex items-center justify-center text-muted-foreground">
                暂无数据
              </div>
            )}
          </CardContent>
        </Card>

        {/* Response Time Chart */}
        <Card variant="elevated">
          <CardHeader>
            <CardTitle>响应时间趋势</CardTitle>
            <CardDescription>平均响应时间和 P95 响应时间</CardDescription>
          </CardHeader>
          <CardContent>
            {hourlySummary.length > 0 ? (
              <ResponseTimeChart data={hourlySummary} />
            ) : (
              <div className="h-[300px] flex items-center justify-center text-muted-foreground">
                暂无数据
              </div>
            )}
          </CardContent>
        </Card>
      </motion.div>

      {/* Model Distribution */}
      <motion.div
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        transition={{ delay: 0.5 }}
      >
        <Card variant="elevated">
          <CardHeader>
            <CardTitle>模型使用分布</CardTitle>
            <CardDescription>各 AI 模型的请求占比</CardDescription>
          </CardHeader>
          <CardContent>
            {statistics.modelStats.length > 0 ? (
              <ModelDistributionChart data={statistics.modelStats} />
            ) : (
              <div className="h-[300px] flex items-center justify-center text-muted-foreground">
                暂无数据
              </div>
            )}
          </CardContent>
        </Card>
      </motion.div>

      {/* Model Stats Table */}
      {statistics.modelStats.length > 0 && (
        <motion.div
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          transition={{ delay: 0.6 }}
        >
          <Card variant="elevated">
            <CardHeader>
              <CardTitle>模型详细统计</CardTitle>
              <CardDescription>各模型的请求量和 Token 消耗</CardDescription>
            </CardHeader>
            <CardContent>
              <div className="overflow-x-auto">
                <table className="w-full text-sm">
                  <thead>
                    <tr className="border-b">
                      <th className="text-left py-3 px-4 font-medium">模型</th>
                      <th className="text-right py-3 px-4 font-medium">请求数</th>
                      <th className="text-right py-3 px-4 font-medium">Token 消耗</th>
                      <th className="text-right py-3 px-4 font-medium">平均 Token/请求</th>
                    </tr>
                  </thead>
                  <tbody>
                    {statistics.modelStats.map((model, index) => (
                      <tr key={index} className="border-b last:border-0 hover:bg-muted/50">
                        <td className="py-3 px-4 font-medium">{model.model}</td>
                        <td className="text-right py-3 px-4">
                          {formatNumber(model.count)}
                        </td>
                        <td className="text-right py-3 px-4">
                          {formatNumber(model.totalTokens)}
                        </td>
                        <td className="text-right py-3 px-4">
                          {formatNumber(Math.round(model.totalTokens / model.count))}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </CardContent>
          </Card>
        </motion.div>
      )}
    </div>
  );
}
