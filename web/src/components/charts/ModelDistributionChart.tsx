import { BarChart, Bar, XAxis, YAxis, Tooltip, ResponsiveContainer, Cell } from 'recharts';
import type { LogStatistics } from '@/types/logs';

interface ModelDistributionChartProps {
  data: LogStatistics['modelStats'];
}

const COLORS = [
  'hsl(262 90% 65%)', // 鲜艳紫色
  'hsl(221 90% 65%)', // 鲜艳蓝色
  'hsl(142 85% 55%)', // 鲜艳绿色
  'hsl(350 90% 65%)', // 鲜艳红色
  'hsl(280 85% 65%)', // 鲜艳粉紫
  'hsl(173 75% 55%)', // 鲜艳青色
  'hsl(47 95% 60%)',  // 鲜艳黄色
  'hsl(30 95% 65%)',  // 鲜艳橙色
];

export default function ModelDistributionChart({ data }: ModelDistributionChartProps) {
  // 按请求数量降序排列
  const chartData = [...data]
    .sort((a, b) => b.count - a.count)
    .map((item, index) => ({
      name: item.model,
      value: item.count,
      tokens: item.totalTokens,
      color: COLORS[index % COLORS.length],
    }));

  const totalRequests = chartData.reduce((sum, item) => sum + item.value, 0);

  return (
    <div className="w-full h-[300px]">
      <ResponsiveContainer width="100%" height="100%">
        <BarChart
          data={chartData}
          layout="vertical"
          margin={{ top: 10, right: 30, left: 10, bottom: 10 }}
          barSize={32}
        >
          <defs>
            {chartData.map((item, index) => (
              <linearGradient key={`gradient-${index}`} id={`colorGrad-${index}`} x1="0" y1="0" x2="1" y2="0">
                <stop offset="0%" stopColor={item.color} stopOpacity={0.8}/>
                <stop offset="100%" stopColor={item.color} stopOpacity={0.5}/>
              </linearGradient>
            ))}
          </defs>
          <XAxis
            type="number"
            tick={{ fill: 'hsl(var(--muted-foreground))', fontSize: 11 }}
            axisLine={false}
            tickLine={false}
          />
          <YAxis
            type="category"
            dataKey="name"
            tick={{ fill: 'hsl(var(--foreground))', fontSize: 12 }}
            axisLine={false}
            tickLine={false}
            width={120}
          />
          <Tooltip
            contentStyle={{
              backgroundColor: 'hsl(var(--popover))',
              border: 'none',
              borderRadius: '12px',
              boxShadow: '0 4px 12px rgba(0,0,0,0.1)',
              padding: '12px',
            }}
            labelStyle={{
              color: 'hsl(var(--foreground))',
              fontWeight: 600,
              marginBottom: '6px',
            }}
            formatter={(value: number | undefined, name: string | undefined, props: any) => {
              const actualValue = value ?? 0;
              const percent = ((actualValue / totalRequests) * 100).toFixed(1);
              return [
                <div key="tooltip" className="space-y-1">
                  <div className="text-sm">{actualValue.toLocaleString()} 次请求 ({percent}%)</div>
                  <div className="text-xs text-muted-foreground">
                    {props.payload.tokens.toLocaleString()} tokens
                  </div>
                </div>,
                ''
              ];
            }}
            cursor={{ fill: 'hsl(var(--muted))', opacity: 0.1 }}
          />
          <Bar
            dataKey="value"
            radius={[0, 8, 8, 0]}
          >
            {chartData.map((entry, index) => (
              <Cell key={`cell-${index}`} fill={`url(#colorGrad-${index})`} />
            ))}
          </Bar>
        </BarChart>
      </ResponsiveContainer>
    </div>
  );
}
