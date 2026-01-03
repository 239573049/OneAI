import { BarChart, Bar, XAxis, YAxis, Tooltip, Legend, ResponsiveContainer } from 'recharts';
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
  }));

  return (
    <div className="w-full h-[300px]">
      <ResponsiveContainer width="100%" height="100%">
        <BarChart
          data={chartData}
          margin={{ top: 10, right: 10, left: 0, bottom: 20 }}
          barGap={2}
        >
          <defs>
            <linearGradient id="colorSuccess" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stopColor="hsl(142 80% 55%)" stopOpacity={0.95}/>
              <stop offset="100%" stopColor="hsl(142 80% 45%)" stopOpacity={0.85}/>
            </linearGradient>
            <linearGradient id="colorFailed" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stopColor="hsl(350 90% 65%)" stopOpacity={0.95}/>
              <stop offset="100%" stopColor="hsl(350 90% 55%)" stopOpacity={0.85}/>
            </linearGradient>
          </defs>
          <XAxis
            dataKey="time"
            tick={{ fill: 'hsl(var(--muted-foreground))', fontSize: 11 }}
            axisLine={{ stroke: 'hsl(var(--border))' }}
            tickLine={false}
            angle={-15}
            textAnchor="end"
            height={60}
          />
          <YAxis
            tick={{ fill: 'hsl(var(--muted-foreground))', fontSize: 11 }}
            axisLine={false}
            tickLine={false}
            width={40}
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
              marginBottom: '4px',
            }}
            cursor={{ fill: 'hsl(var(--muted))', opacity: 0.1 }}
          />
          <Legend
            wrapperStyle={{
              paddingTop: '10px',
              fontSize: '13px',
            }}
            iconType="circle"
          />
          <Bar
            dataKey="成功请求"
            stackId="a"
            fill="url(#colorSuccess)"
            maxBarSize={60}
          />
          <Bar
            dataKey="失败请求"
            stackId="a"
            fill="url(#colorFailed)"
            maxBarSize={60}
          />
        </BarChart>
      </ResponsiveContainer>
    </div>
  );
}
