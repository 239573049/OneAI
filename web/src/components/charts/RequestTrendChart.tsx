import { AreaChart, Area, XAxis, YAxis, Tooltip, Legend, ResponsiveContainer, CartesianGrid } from 'recharts';
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
    总请求: item.successRequests + item.failedRequests,
  }));

  return (
    <div className="w-full h-[300px]">
      <ResponsiveContainer width="100%" height="100%">
        <AreaChart
          data={chartData}
          margin={{ top: 10, right: 10, left: 0, bottom: 20 }}
        >
          <defs>
            <linearGradient id="gradientSuccess" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stopColor="hsl(142 76% 50%)" stopOpacity={0.4}/>
              <stop offset="95%" stopColor="hsl(142 76% 50%)" stopOpacity={0.05}/>
            </linearGradient>
            <linearGradient id="gradientFailed" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stopColor="hsl(350 89% 60%)" stopOpacity={0.4}/>
              <stop offset="95%" stopColor="hsl(350 89% 60%)" stopOpacity={0.05}/>
            </linearGradient>
          </defs>
          <CartesianGrid
            strokeDasharray="3 3"
            stroke="hsl(var(--border))"
            opacity={0.5}
            vertical={false}
          />
          <XAxis
            dataKey="time"
            tick={{ fill: 'hsl(var(--muted-foreground))', fontSize: 11 }}
            axisLine={{ stroke: 'hsl(var(--border))', strokeWidth: 1 }}
            tickLine={false}
            angle={-20}
            textAnchor="end"
            height={60}
            interval="preserveStartEnd"
          />
          <YAxis
            tick={{ fill: 'hsl(var(--muted-foreground))', fontSize: 11 }}
            axisLine={false}
            tickLine={false}
            width={45}
            tickFormatter={(value) => value.toLocaleString()}
          />
          <Tooltip
            contentStyle={{
              backgroundColor: 'hsl(var(--popover))',
              border: '1px solid hsl(var(--border))',
              borderRadius: '10px',
              boxShadow: '0 8px 24px rgba(0,0,0,0.12)',
              padding: '12px 16px',
            }}
            labelStyle={{
              color: 'hsl(var(--foreground))',
              fontWeight: 600,
              marginBottom: '8px',
              fontSize: '13px',
            }}
            itemStyle={{
              color: 'hsl(var(--foreground))',
              fontSize: '12px',
              padding: '2px 0',
            }}
            cursor={{ stroke: 'hsl(var(--primary))', strokeWidth: 1, strokeDasharray: '4 4' }}
          />
          <Legend
            wrapperStyle={{
              paddingTop: '16px',
              fontSize: '12px',
            }}
            iconType="circle"
            iconSize={8}
          />
          <Area
            type="monotone"
            dataKey="成功请求"
            stackId="1"
            stroke="hsl(142 76% 45%)"
            strokeWidth={2}
            fill="url(#gradientSuccess)"
            dot={false}
            activeDot={{ r: 5, strokeWidth: 2, stroke: 'hsl(var(--background))' }}
          />
          <Area
            type="monotone"
            dataKey="失败请求"
            stackId="1"
            stroke="hsl(350 89% 55%)"
            strokeWidth={2}
            fill="url(#gradientFailed)"
            dot={false}
            activeDot={{ r: 5, strokeWidth: 2, stroke: 'hsl(var(--background))' }}
          />
        </AreaChart>
      </ResponsiveContainer>
    </div>
  );
}
