import { LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer } from 'recharts';
import type { HourlySummaryDto } from '@/types/logs';
import { format } from 'date-fns';

interface ResponseTimeChartProps {
  data: HourlySummaryDto[];
}

export default function ResponseTimeChart({ data }: ResponseTimeChartProps) {
  const chartData = data.map(item => ({
    time: format(new Date(item.hourStartTime), 'MM-dd HH:mm'),
    平均响应时间: Math.round(item.avgDurationMs),
    P95响应时间: item.p95DurationMs ? Math.round(item.p95DurationMs) : null,
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
            label={{ value: '响应时间 (ms)', angle: -90, position: 'insideLeft' }}
          />
          <Tooltip
            contentStyle={{
              backgroundColor: 'hsl(var(--card))',
              border: '1px solid hsl(var(--border))',
              borderRadius: '8px',
            }}
            labelStyle={{ color: 'hsl(var(--foreground))' }}
            formatter={(value: number | undefined) => [`${value ?? 0} ms`, '']}
          />
          <Legend />
          <Line
            type="monotone"
            dataKey="平均响应时间"
            stroke="hsl(var(--primary))"
            strokeWidth={2}
            dot={{ fill: 'hsl(var(--primary))' }}
          />
          <Line
            type="monotone"
            dataKey="P95响应时间"
            stroke="hsl(47 96% 53%)"
            strokeWidth={2}
            dot={{ fill: 'hsl(47 96% 53%)' }}
            connectNulls
          />
        </LineChart>
      </ResponsiveContainer>
    </div>
  );
}
