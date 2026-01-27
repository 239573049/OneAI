import { useState, useEffect } from 'react';
import { motion } from 'motion/react';
import {
  TrendingUp,
  Activity,
  Zap,
  AlertCircle,
  Loader2,
  RefreshCw,
  Calendar,
} from 'lucide-react';
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
  CardDescription,
} from '@/components/animate-ui/components/card';
import { logsApi } from '@/services/logs';
import type { LogStatistics, HourlyByModelDto } from '@/types/logs';
import RequestTrendChart from '@/components/charts/RequestTrendChart';
import ModelDistributionChart from '@/components/charts/ModelDistributionChart';
import ModelTokenChart from '@/components/charts/ModelTokenChart';

type TimeRange = '1d' | '7d' | '15d';

const TIME_RANGE_OPTIONS: { value: TimeRange; label: string }[] = [
  { value: '1d', label: '最近 1 天' },
  { value: '7d', label: '最近 7 天' },
  { value: '15d', label: '最近 15 天' },
];

const STAT_COLORS = [
  { bg: 'from-blue-500/10 to-blue-600/5', icon: 'text-blue-500', border: 'border-blue-500/20' },
  { bg: 'from-emerald-500/10 to-emerald-600/5', icon: 'text-emerald-500', border: 'border-emerald-500/20' },
  { bg: 'from-amber-500/10 to-amber-600/5', icon: 'text-amber-500', border: 'border-amber-500/20' },
];

export default function HomeView() {
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [statistics, setStatistics] = useState<LogStatistics | null>(null);
  const [hourlyByModel, setHourlyByModel] = useState<HourlyByModelDto[]>([]);
  const [timeRange, setTimeRange] = useState<TimeRange>('7d');

  useEffect(() => {
    loadData();
  }, [timeRange]);

  const getTimeRange = () => {
    const endTime = new Date();
    let startTime: Date;

    switch (timeRange) {
      case '1d':
        startTime = new Date(Date.now() - 24 * 60 * 60 * 1000);
        break;
      case '7d':
        startTime = new Date(Date.now() - 7 * 24 * 60 * 60 * 1000);
        break;
      case '15d':
        startTime = new Date(Date.now() - 15 * 24 * 60 * 60 * 1000);
        break;
    }

    return {
      startTime: startTime.toISOString(),
      endTime: endTime.toISOString(),
    };
  };

  const loadData = async (isRefresh = false) => {
    try {
      if (isRefresh) {
        setRefreshing(true);
      } else {
        setLoading(true);
      }
      setError(null);

      const { startTime, endTime } = getTimeRange();

      // 并行加载统计数据和按模型的小时数据
      const [statsData, modelData] = await Promise.all([
        logsApi.getStatistics({ startTime, endTime }),
        logsApi.getHourlyByModel({ startTime, endTime }),
      ]);

      setStatistics(statsData);
      setHourlyByModel(modelData);
    } catch (err) {
      console.error('加载统计数据失败:', err);
      setError(err instanceof Error ? err.message : '加载数据失败');
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  };

  const formatNumber = (num: number): string => {
    if (num >= 1000000) {
      return `${(num / 1000000).toFixed(1)}M`;
    }
    if (num >= 1000) {
      return `${(num / 1000).toFixed(1)}K`;
    }
    return num.toLocaleString('zh-CN');
  };

  const formatFullNumber = (num: number): string => {
    return num.toLocaleString('zh-CN');
  };

  const formatPercentage = (num: number): string => {
    return `${(num * 100).toFixed(1)}%`;
  };

  // 计算总的 input/output tokens
  const getTotalTokens = () => {
    let totalInput = 0;
    let totalOutput = 0;
    hourlyByModel.forEach(item => {
      totalInput += item.promptTokens;
      totalOutput += item.completionTokens;
    });
    return { totalInput, totalOutput };
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center h-[calc(100vh-200px)]">
        <motion.div
          initial={{ opacity: 0, scale: 0.9 }}
          animate={{ opacity: 1, scale: 1 }}
          className="text-center space-y-4"
        >
          <div className="relative">
            <div className="w-16 h-16 rounded-full bg-primary/10 flex items-center justify-center mx-auto">
              <Loader2 className="h-8 w-8 animate-spin text-primary" />
            </div>
          </div>
          <p className="text-muted-foreground">加载统计数据中...</p>
        </motion.div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex items-center justify-center h-[calc(100vh-200px)]">
        <motion.div
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          className="text-center space-y-4"
        >
          <div className="w-16 h-16 rounded-full bg-destructive/10 flex items-center justify-center mx-auto">
            <AlertCircle className="h-8 w-8 text-destructive" />
          </div>
          <div>
            <p className="text-lg font-semibold">加载失败</p>
            <p className="text-sm text-muted-foreground mt-1">{error}</p>
          </div>
          <button
            onClick={() => loadData()}
            className="px-5 py-2.5 bg-primary text-primary-foreground rounded-lg hover:bg-primary/90 transition-colors font-medium"
          >
            重试
          </button>
        </motion.div>
      </div>
    );
  }

  if (!statistics) {
    return null;
  }

  const { totalInput, totalOutput } = getTotalTokens();

  const stats = [
    {
      label: '总请求数',
      value: formatNumber(statistics.totalRequests),
      fullValue: formatFullNumber(statistics.totalRequests),
      icon: Activity,
      description: `成功 ${formatFullNumber(statistics.successRequests)} | 失败 ${formatFullNumber(statistics.failedRequests)}`,
    },
    {
      label: '成功率',
      value: formatPercentage(statistics.successRate),
      icon: TrendingUp,
      description: `已完成 ${formatFullNumber(statistics.completedRequests)} 个请求`,
    },
    {
      label: 'Token 消耗',
      value: formatNumber(statistics.totalTokens),
      fullValue: formatFullNumber(statistics.totalTokens),
      icon: Zap,
      description: `Input ${formatNumber(totalInput)} | Output ${formatNumber(totalOutput)}`,
    },
  ];

  const timeRangeLabel = TIME_RANGE_OPTIONS.find(o => o.value === timeRange)?.label || '';

  return (
    <div className="p-6 space-y-6">
      {/* Header Section */}
      <motion.div
        initial={{ opacity: 0, y: 20 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.5 }}
        className="flex flex-col sm:flex-row sm:items-center justify-between gap-4"
      >
        <div className="space-y-1">
          <h2 className="text-3xl font-bold tracking-tight">统计概览</h2>
          <p className="text-muted-foreground">
            {timeRangeLabel}的 AI 请求统计数据
          </p>
        </div>
        <div className="flex items-center gap-3">
          {/* Time Range Selector */}
          <div className="flex items-center bg-muted/50 rounded-lg p-1">
            {TIME_RANGE_OPTIONS.map((option) => (
              <button
                key={option.value}
                onClick={() => setTimeRange(option.value)}
                className={`px-3 py-1.5 text-sm font-medium rounded-md transition-all ${
                  timeRange === option.value
                    ? 'bg-background text-foreground shadow-sm'
                    : 'text-muted-foreground hover:text-foreground'
                }`}
              >
                {option.label.replace('最近 ', '')}
              </button>
            ))}
          </div>
          {/* Refresh Button */}
          <motion.button
            onClick={() => loadData(true)}
            disabled={refreshing}
            className="flex items-center gap-2 px-3 py-2 text-sm font-medium text-muted-foreground hover:text-foreground bg-muted/50 hover:bg-muted rounded-lg transition-colors disabled:opacity-50"
            whileHover={{ scale: 1.02 }}
            whileTap={{ scale: 0.98 }}
          >
            <RefreshCw className={`h-4 w-4 ${refreshing ? 'animate-spin' : ''}`} />
          </motion.button>
        </div>
      </motion.div>

      {/* Stats Grid */}
      <div className="grid gap-4 md:grid-cols-3">
        {stats.map((stat, index) => (
          <motion.div
            key={index}
            initial={{ opacity: 0, y: 20 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ delay: 0.1 * index, duration: 0.5 }}
          >
            <Card
              variant="elevated"
              className={`relative overflow-hidden border ${STAT_COLORS[index].border}`}
            >
              <div className={`absolute inset-0 bg-gradient-to-br ${STAT_COLORS[index].bg}`} />
              <CardContent className="pt-6 relative">
                <div className="flex items-start justify-between">
                  <div className="flex-1 space-y-2">
                    <p className="text-sm font-medium text-muted-foreground">{stat.label}</p>
                    <p className="text-3xl font-bold tracking-tight" title={stat.fullValue}>
                      {stat.value}
                    </p>
                    <p className="text-xs text-muted-foreground">
                      {stat.description}
                    </p>
                  </div>
                  <div className={`p-3 rounded-xl bg-background/80 backdrop-blur-sm ${STAT_COLORS[index].icon}`}>
                    <stat.icon className="h-5 w-5" />
                  </div>
                </div>
              </CardContent>
            </Card>
          </motion.div>
        ))}
      </div>

      {/* Model Token Usage Chart - Main Focus */}
      <motion.div
        initial={{ opacity: 0, y: 20 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ delay: 0.4 }}
      >
        <Card variant="elevated">
          <CardHeader className="pb-2">
            <div className="flex items-center justify-between">
              <div>
                <CardTitle className="text-lg">模型 Token 消耗趋势</CardTitle>
                <CardDescription>
                  各模型的 Input/Output Token 使用量
                  {timeRange === '1d' ? '（按小时）' : '（按天）'}
                </CardDescription>
              </div>
              <div className="flex items-center gap-2 text-xs text-muted-foreground">
                <Calendar className="h-4 w-4" />
                {timeRangeLabel}
              </div>
            </div>
          </CardHeader>
          <CardContent>
            {hourlyByModel.length > 0 ? (
              <ModelTokenChart data={hourlyByModel} timeRange={timeRange} />
            ) : (
              <div className="h-[350px] flex items-center justify-center text-muted-foreground">
                <div className="text-center">
                  <Zap className="h-12 w-12 mx-auto mb-3 opacity-20" />
                  <p>暂无数据</p>
                </div>
              </div>
            )}
          </CardContent>
        </Card>
      </motion.div>

      {/* Charts Section */}
      <div className="grid gap-6 lg:grid-cols-2">
        {/* Request Trend Chart */}
        <motion.div
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ delay: 0.5 }}
        >
          <Card variant="elevated" className="h-full">
            <CardHeader className="pb-2">
              <CardTitle className="text-lg">请求趋势</CardTitle>
              <CardDescription>成功/失败请求数量变化</CardDescription>
            </CardHeader>
            <CardContent>
              {hourlyByModel.length > 0 ? (
                <RequestTrendChart
                  data={hourlyByModel.reduce((acc, item) => {
                    const existing = acc.find(a => a.hourStartTime === item.hourStartTime);
                    if (existing) {
                      existing.successRequests += item.successRequests;
                      existing.failedRequests += item.totalRequests - item.successRequests;
                    } else {
                      acc.push({
                        hourStartTime: item.hourStartTime,
                        totalRequests: item.totalRequests,
                        successRequests: item.successRequests,
                        failedRequests: item.totalRequests - item.successRequests,
                        successRate: item.successRate,
                        totalTokens: item.totalTokens,
                        avgDurationMs: item.avgDurationMs,
                      });
                    }
                    return acc;
                  }, [] as any[])}
                />
              ) : (
                <div className="h-[300px] flex items-center justify-center text-muted-foreground">
                  <div className="text-center">
                    <TrendingUp className="h-12 w-12 mx-auto mb-3 opacity-20" />
                    <p>暂无数据</p>
                  </div>
                </div>
              )}
            </CardContent>
          </Card>
        </motion.div>

        {/* Model Distribution */}
        <motion.div
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ delay: 0.55 }}
        >
          <Card variant="elevated" className="h-full">
            <CardHeader className="pb-2">
              <CardTitle className="text-lg">模型使用分布</CardTitle>
              <CardDescription>各 AI 模型的请求占比</CardDescription>
            </CardHeader>
            <CardContent>
              {statistics.modelStats.length > 0 ? (
                <ModelDistributionChart data={statistics.modelStats} />
              ) : (
                <div className="h-[350px] flex items-center justify-center text-muted-foreground">
                  <div className="text-center">
                    <Activity className="h-12 w-12 mx-auto mb-3 opacity-20" />
                    <p>暂无数据</p>
                  </div>
                </div>
              )}
            </CardContent>
          </Card>
        </motion.div>
      </div>

      {/* Model Stats Table */}
      {statistics.modelStats.length > 0 && (
        <motion.div
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ delay: 0.6 }}
        >
          <Card variant="elevated">
            <CardHeader className="pb-2">
              <CardTitle className="text-lg">模型详细统计</CardTitle>
              <CardDescription>各模型的请求量和 Token 消耗明细</CardDescription>
            </CardHeader>
            <CardContent>
              <div className="overflow-x-auto">
                <table className="w-full text-sm">
                  <thead>
                    <tr className="border-b border-border/50">
                      <th className="text-left py-3 px-4 font-semibold text-muted-foreground">模型</th>
                      <th className="text-right py-3 px-4 font-semibold text-muted-foreground">请求数</th>
                      <th className="text-right py-3 px-4 font-semibold text-muted-foreground">Input Tokens</th>
                      <th className="text-right py-3 px-4 font-semibold text-muted-foreground">Output Tokens</th>
                      <th className="text-right py-3 px-4 font-semibold text-muted-foreground">总 Tokens</th>
                      <th className="text-right py-3 px-4 font-semibold text-muted-foreground">占比</th>
                    </tr>
                  </thead>
                  <tbody>
                    {(() => {
                      // 按模型聚合 hourlyByModel 数据
                      const modelAggregated = new Map<string, { requests: number; input: number; output: number; total: number }>();
                      hourlyByModel.forEach(item => {
                        const existing = modelAggregated.get(item.model) || { requests: 0, input: 0, output: 0, total: 0 };
                        modelAggregated.set(item.model, {
                          requests: existing.requests + item.totalRequests,
                          input: existing.input + item.promptTokens,
                          output: existing.output + item.completionTokens,
                          total: existing.total + item.totalTokens,
                        });
                      });

                      const totalRequests = Array.from(modelAggregated.values()).reduce((sum, m) => sum + m.requests, 0);

                      return Array.from(modelAggregated.entries())
                        .sort((a, b) => b[1].requests - a[1].requests)
                        .map(([model, data], index) => {
                          const percentage = totalRequests > 0 ? ((data.requests / totalRequests) * 100).toFixed(1) : '0';
                          return (
                            <tr
                              key={index}
                              className="border-b border-border/30 last:border-0 hover:bg-muted/30 transition-colors"
                            >
                              <td className="py-3 px-4">
                                <span className="font-medium">{model}</span>
                              </td>
                              <td className="text-right py-3 px-4 tabular-nums">
                                {formatFullNumber(data.requests)}
                              </td>
                              <td className="text-right py-3 px-4 tabular-nums text-blue-600 dark:text-blue-400">
                                {formatFullNumber(data.input)}
                              </td>
                              <td className="text-right py-3 px-4 tabular-nums text-emerald-600 dark:text-emerald-400">
                                {formatFullNumber(data.output)}
                              </td>
                              <td className="text-right py-3 px-4 tabular-nums font-medium">
                                {formatFullNumber(data.total)}
                              </td>
                              <td className="text-right py-3 px-4">
                                <div className="flex items-center justify-end gap-2">
                                  <div className="w-16 h-1.5 bg-muted rounded-full overflow-hidden">
                                    <div
                                      className="h-full bg-primary rounded-full"
                                      style={{ width: `${percentage}%` }}
                                    />
                                  </div>
                                  <span className="text-muted-foreground tabular-nums w-12 text-right">
                                    {percentage}%
                                  </span>
                                </div>
                              </td>
                            </tr>
                          );
                        });
                    })()}
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
