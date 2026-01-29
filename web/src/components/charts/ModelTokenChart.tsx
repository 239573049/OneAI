import { useMemo } from 'react';
import {
  AreaChart,
  Area,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ResponsiveContainer,
  Legend,
} from 'recharts';
import type { HourlyByModelDto } from '@/types/logs';

interface ModelTokenChartProps {
  data: HourlyByModelDto[];
  timeRange: '1d' | '7d' | '15d';
}

const MODEL_COLORS = [
  { input: 'hsl(221 83% 53%)', output: 'hsl(221 83% 73%)' },  // 蓝色
  { input: 'hsl(142 71% 45%)', output: 'hsl(142 71% 65%)' },  // 绿色
  { input: 'hsl(262 83% 58%)', output: 'hsl(262 83% 78%)' },  // 紫色
  { input: 'hsl(38 92% 50%)', output: 'hsl(38 92% 70%)' },    // 橙色
  { input: 'hsl(350 89% 60%)', output: 'hsl(350 89% 80%)' },  // 红色
  { input: 'hsl(173 80% 40%)', output: 'hsl(173 80% 60%)' },  // 青色
];

export default function ModelTokenChart({ data, timeRange }: ModelTokenChartProps) {
  const { chartData, models } = useMemo(() => {
    // 获取所有唯一模型
    const modelSet = new Set<string>();
    data.forEach(item => modelSet.add(item.model));
    const models = Array.from(modelSet);

    // 按时间聚合数据
    const timeMap = new Map<string, Record<string, { input: number; output: number }>>();

    data.forEach(item => {
      const date = new Date(item.hourStartTime);
      // 根据时间范围决定聚合粒度
      const key = timeRange === '1d'
        ? date.toISOString().slice(0, 13) // 按小时
        : date.toISOString().slice(0, 10); // 按天

      if (!timeMap.has(key)) {
        timeMap.set(key, {});
      }
      const timeData = timeMap.get(key)!;
      if (!timeData[item.model]) {
        timeData[item.model] = { input: 0, output: 0 };
      }
      timeData[item.model].input += item.promptTokens;
      timeData[item.model].output += item.completionTokens;
    });

    // 转换为图表数据格式
    const chartData = Array.from(timeMap.entries())
      .sort((a, b) => a[0].localeCompare(b[0]))
      .map(([time, modelData]) => {
        const result: Record<string, any> = { time };
        models.forEach(model => {
          result[`${model}_input`] = modelData[model]?.input || 0;
          result[`${model}_output`] = modelData[model]?.output || 0;
        });
        return result;
      });

    return { chartData, models };
  }, [data, timeRange]);

  const formatXAxis = (value: string) => {
    const date = new Date(value);
    if (timeRange === '1d') {
      return `${date.getHours()}:00`;
    }
    return `${date.getMonth() + 1}/${date.getDate()}`;
  };

  const formatNumber = (num: number): string => {
    if (num >= 1000000) return `${(num / 1000000).toFixed(1)}M`;
    if (num >= 1000) return `${(num / 1000).toFixed(1)}K`;
    return num.toString();
  };

  const CustomTooltip = ({ active, payload, label }: any) => {
    if (!active || !payload?.length) return null;

    const date = new Date(label);
    const timeLabel = timeRange === '1d'
      ? `${date.getMonth() + 1}/${date.getDate()} ${date.getHours()}:00`
      : `${date.getFullYear()}/${date.getMonth() + 1}/${date.getDate()}`;

    return (
      <div
        style={{
          backgroundColor: 'hsl(var(--popover))',
          border: '1px solid hsl(var(--border))',
          borderRadius: '10px',
          boxShadow: '0 8px 24px rgba(0,0,0,0.12)',
          padding: '12px 16px',
          maxWidth: '300px',
        }}
      >
        <p style={{ fontWeight: 600, marginBottom: '8px', fontSize: '13px', color: 'hsl(var(--foreground))' }}>
          {timeLabel}
        </p>
        <div style={{ fontSize: '12px' }}>
          {models.map((model, idx) => {
            const inputKey = `${model}_input`;
            const outputKey = `${model}_output`;
            const inputValue = payload.find((p: any) => p.dataKey === inputKey)?.value || 0;
            const outputValue = payload.find((p: any) => p.dataKey === outputKey)?.value || 0;
            if (inputValue === 0 && outputValue === 0) return null;
            const color = MODEL_COLORS[idx % MODEL_COLORS.length];
            return (
              <div key={model} style={{ marginBottom: '6px' }}>
                <p style={{ fontWeight: 500, color: 'hsl(var(--foreground))', marginBottom: '2px' }}>{model}</p>
                <p style={{ color: 'hsl(var(--muted-foreground))' }}>
                  <span style={{ color: color.input }}>Input: {formatNumber(inputValue)}</span>
                  {' | '}
                  <span style={{ color: color.output }}>Output: {formatNumber(outputValue)}</span>
                </p>
              </div>
            );
          })}
        </div>
      </div>
    );
  };

  return (
    <div className="w-full h-[350px]">
      <ResponsiveContainer width="100%" height="100%">
        <AreaChart data={chartData} margin={{ top: 10, right: 10, left: 0, bottom: 0 }}>
          <defs>
            {models.map((model, idx) => {
              const color = MODEL_COLORS[idx % MODEL_COLORS.length];
              return (
                <linearGradient key={`gradient-${model}`} id={`gradient-${model}`} x1="0" y1="0" x2="0" y2="1">
                  <stop offset="5%" stopColor={color.input} stopOpacity={0.3} />
                  <stop offset="95%" stopColor={color.input} stopOpacity={0} />
                </linearGradient>
              );
            })}
          </defs>
          <CartesianGrid strokeDasharray="3 3" stroke="hsl(var(--border))" opacity={0.5} />
          <XAxis
            dataKey="time"
            tickFormatter={formatXAxis}
            stroke="hsl(var(--muted-foreground))"
            fontSize={11}
            tickLine={false}
            axisLine={false}
          />
          <YAxis
            tickFormatter={formatNumber}
            stroke="hsl(var(--muted-foreground))"
            fontSize={11}
            tickLine={false}
            axisLine={false}
            width={50}
          />
          <Tooltip content={<CustomTooltip />} />
          <Legend
            formatter={(value: string) => {
              const [model, type] = value.split('_');
              return `${model} (${type === 'input' ? 'In' : 'Out'})`;
            }}
            wrapperStyle={{ fontSize: '11px' }}
          />
          {models.map((model, idx) => {
            const color = MODEL_COLORS[idx % MODEL_COLORS.length];
            return (
              <Area
                key={`${model}_input`}
                type="monotone"
                dataKey={`${model}_input`}
                stackId={model}
                stroke={color.input}
                fill={`url(#gradient-${model})`}
                strokeWidth={2}
              />
            );
          })}
          {models.map((model, idx) => {
            const color = MODEL_COLORS[idx % MODEL_COLORS.length];
            return (
              <Area
                key={`${model}_output`}
                type="monotone"
                dataKey={`${model}_output`}
                stackId={model}
                stroke={color.output}
                fill="transparent"
                strokeWidth={1.5}
                strokeDasharray="4 2"
              />
            );
          })}
        </AreaChart>
      </ResponsiveContainer>
    </div>
  );
}
