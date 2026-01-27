import { PieChart, Pie, Cell, ResponsiveContainer, Tooltip, Legend } from 'recharts';
import type { LogStatistics } from '@/types/logs';

interface ModelDistributionChartProps {
  data: LogStatistics['modelStats'];
}

const COLORS = [
  { main: 'hsl(262 83% 58%)', light: 'hsl(262 83% 68%)' },  // 紫色
  { main: 'hsl(221 83% 53%)', light: 'hsl(221 83% 63%)' },  // 蓝色
  { main: 'hsl(142 71% 45%)', light: 'hsl(142 71% 55%)' },  // 绿色
  { main: 'hsl(38 92% 50%)', light: 'hsl(38 92% 60%)' },    // 橙色
  { main: 'hsl(350 89% 60%)', light: 'hsl(350 89% 70%)' },  // 红色
  { main: 'hsl(173 80% 40%)', light: 'hsl(173 80% 50%)' },  // 青色
  { main: 'hsl(280 85% 60%)', light: 'hsl(280 85% 70%)' },  // 粉紫
  { main: 'hsl(199 89% 48%)', light: 'hsl(199 89% 58%)' },  // 天蓝
];

const RADIAN = Math.PI / 180;

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

  const renderCustomizedLabel = (props: any) => {
    const { cx, cy, midAngle, innerRadius, outerRadius, percent } = props;
    if (percent < 0.05) return null; // 小于5%不显示标签

    const radius = innerRadius + (outerRadius - innerRadius) * 0.5;
    const x = cx + radius * Math.cos(-midAngle * RADIAN);
    const y = cy + radius * Math.sin(-midAngle * RADIAN);

    return (
      <text
        x={x}
        y={y}
        fill="white"
        textAnchor="middle"
        dominantBaseline="central"
        fontSize={12}
        fontWeight={600}
        style={{ textShadow: '0 1px 2px rgba(0,0,0,0.3)' }}
      >
        {`${(percent * 100).toFixed(0)}%`}
      </text>
    );
  };

  const CustomTooltip = ({ active, payload }: any) => {
    if (active && payload && payload.length) {
      const item = payload[0].payload;
      const percent = ((item.value / totalRequests) * 100).toFixed(1);
      return (
        <div
          style={{
            backgroundColor: 'hsl(var(--popover))',
            border: '1px solid hsl(var(--border))',
            borderRadius: '10px',
            boxShadow: '0 8px 24px rgba(0,0,0,0.12)',
            padding: '12px 16px',
          }}
        >
          <p style={{
            color: 'hsl(var(--foreground))',
            fontWeight: 600,
            marginBottom: '8px',
            fontSize: '13px'
          }}>
            {item.name}
          </p>
          <div style={{ fontSize: '12px', color: 'hsl(var(--muted-foreground))' }}>
            <p style={{ marginBottom: '4px' }}>
              <span style={{ color: item.color.main, fontWeight: 500 }}>
                {item.value.toLocaleString()}
              </span>
              {' '}次请求 ({percent}%)
            </p>
            <p>
              <span style={{ fontWeight: 500 }}>
                {item.tokens.toLocaleString()}
              </span>
              {' '}tokens
            </p>
          </div>
        </div>
      );
    }
    return null;
  };

  const CustomLegend = ({ payload }: any) => {
    return (
      <div className="flex flex-wrap justify-center gap-x-4 gap-y-2 mt-4">
        {payload.map((entry: any, index: number) => (
          <div key={`legend-${index}`} className="flex items-center gap-2">
            <div
              className="w-3 h-3 rounded-full"
              style={{ backgroundColor: entry.color }}
            />
            <span className="text-xs text-muted-foreground">
              {entry.value}
            </span>
          </div>
        ))}
      </div>
    );
  };

  return (
    <div className="w-full h-[350px]">
      <ResponsiveContainer width="100%" height="100%">
        <PieChart>
          <defs>
            {chartData.map((item, index) => (
              <linearGradient
                key={`gradient-${index}`}
                id={`pieGradient-${index}`}
                x1="0"
                y1="0"
                x2="1"
                y2="1"
              >
                <stop offset="0%" stopColor={item.color.light} />
                <stop offset="100%" stopColor={item.color.main} />
              </linearGradient>
            ))}
          </defs>
          <Pie
            data={chartData}
            cx="50%"
            cy="45%"
            labelLine={false}
            label={renderCustomizedLabel}
            innerRadius={60}
            outerRadius={110}
            paddingAngle={2}
            dataKey="value"
            animationBegin={0}
            animationDuration={800}
            animationEasing="ease-out"
          >
            {chartData.map((_, index) => (
              <Cell
                key={`cell-${index}`}
                fill={`url(#pieGradient-${index})`}
                stroke="hsl(var(--background))"
                strokeWidth={2}
                style={{ filter: 'drop-shadow(0 2px 4px rgba(0,0,0,0.1))' }}
              />
            ))}
          </Pie>
          <Tooltip content={<CustomTooltip />} />
          <Legend content={<CustomLegend />} />
          {/* Center text */}
          <text
            x="50%"
            y="42%"
            textAnchor="middle"
            dominantBaseline="middle"
            style={{ fill: 'hsl(var(--foreground))', fontSize: '24px', fontWeight: 700 }}
          >
            {totalRequests.toLocaleString()}
          </text>
          <text
            x="50%"
            y="50%"
            textAnchor="middle"
            dominantBaseline="middle"
            style={{ fill: 'hsl(var(--muted-foreground))', fontSize: '12px' }}
          >
            总请求数
          </text>
        </PieChart>
      </ResponsiveContainer>
    </div>
  );
}
