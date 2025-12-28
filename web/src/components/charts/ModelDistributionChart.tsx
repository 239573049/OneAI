import { PieChart, Pie, Cell, ResponsiveContainer, Legend, Tooltip } from 'recharts';
import type { LogStatistics } from '@/types/logs';

interface ModelDistributionChartProps {
  data: LogStatistics['modelStats'];
}

const COLORS = [
  'hsl(var(--primary))',
  'hsl(142 76% 36%)',
  'hsl(221 83% 53%)',
  'hsl(262 83% 58%)',
  'hsl(47 96% 53%)',
  'hsl(0 84% 60%)',
  'hsl(173 58% 39%)',
  'hsl(12 76% 61%)',
];

export default function ModelDistributionChart({ data }: ModelDistributionChartProps) {
  const chartData = data.map(item => ({
    name: item.model,
    value: item.count,
    tokens: item.totalTokens,
  }));

  const renderLabel = (entry: any) => {
    const percent = ((entry.value / chartData.reduce((sum, item) => sum + item.value, 0)) * 100).toFixed(1);
    return `${entry.name}: ${percent}%`;
  };

  return (
    <div className="w-full h-[300px]">
      <ResponsiveContainer width="100%" height="100%">
        <PieChart>
          <Pie
            data={chartData}
            cx="50%"
            cy="50%"
            labelLine={false}
            label={renderLabel}
            outerRadius={80}
            fill="#8884d8"
            dataKey="value"
          >
            {chartData.map((entry, index) => (
              <Cell key={`cell-${index}`} fill={COLORS[index % COLORS.length]} />
            ))}
          </Pie>
          <Tooltip
            contentStyle={{
              backgroundColor: 'hsl(var(--card))',
              border: '1px solid hsl(var(--border))',
              borderRadius: '8px',
            }}
            formatter={(value: number | undefined, name: string | undefined, props: any) => [
              `${value ?? 0} 次请求 | ${props.payload.tokens.toLocaleString()} tokens`,
              name ?? '',
            ]}
          />
          <Legend />
        </PieChart>
      </ResponsiveContainer>
    </div>
  );
}
