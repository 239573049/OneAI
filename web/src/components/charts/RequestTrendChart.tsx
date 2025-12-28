import { LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer } from 'recharts';
import type { HourlySummaryDto } from '@/types/logs';
import { format } from 'date-fns';

interface RequestTrendChartProps {
  data: HourlySummaryDto[];
}

export default function RequestTrendChart({ data }: RequestTrendChartProps) {
  const chartData = data.map(item => ({
    time: format(new Date(item.hourStartTime), 'MM-dd HH:mm'),
    成功请求: item.successRequests,
    失败请求: item.failedRequests,
    总请求: item.totalRequests,
  }));

  return (
    <div className="w-full h-[300px]">
      <ResponsiveContainer width="100%" height="100%">
        <LineChart data={chartData} margin={{ top: 5, right: 30, left: 20, bottom: 5 }}>
          <CartesianGrid strokeDasharray="3 3" className="stroke-muted" />
          <XAxis
            dataKey="time"
            className="text-xs text-muted-foreground"
            tick={{ fill: 'currentColor' }}
          />
          <YAxis
            className="text-xs text-muted-foreground"
            tick={{ fill: 'currentColor' }}
          />
          <Tooltip
            contentStyle={{
              backgroundColor: 'hsl(var(--card))',
              border: '1px solid hsl(var(--border))',
              borderRadius: '8px',
            }}
            labelStyle={{ color: 'hsl(var(--foreground))' }}
          />
          <Legend />
          <Line
            type="monotone"
            dataKey="总请求"
            stroke="hsl(var(--primary))"
            strokeWidth={2}
            dot={{ fill: 'hsl(var(--primary))' }}
          />
          <Line
            type="monotone"
            dataKey="成功请求"
            stroke="hsl(142 76% 36%)"
            strokeWidth={2}
            dot={{ fill: 'hsl(142 76% 36%)' }}
          />
          <Line
            type="monotone"
            dataKey="失败请求"
            stroke="hsl(0 84% 60%)"
            strokeWidth={2}
            dot={{ fill: 'hsl(0 84% 60%)' }}
          />
        </LineChart>
      </ResponsiveContainer>
    </div>
  );
}
