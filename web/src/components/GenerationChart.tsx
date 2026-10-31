import { Area, AreaChart, CartesianGrid, Legend, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import type { Fuel, GenerationPoint, Resolution } from '../api/types'
import { formatDateTime, formatDay, formatMw } from '../lib/format'
import { FUEL_COLOURS, FUEL_ORDER } from '../lib/fuels'

/** Stacked output by fuel. Hourly for short ranges, daily averages for longer ones. */
export function GenerationChart({ points, resolution }: { points: GenerationPoint[]; resolution: Resolution }) {
  const data = points.map((p) => ({ t: Date.parse(p.startUtc), ...p.outputMw }))
  const present = FUEL_ORDER.filter((f) => points.some((p) => (p.outputMw[f] ?? 0) > 0))

  // Market days start at 05:00 UTC (midnight EST). Tick once per day so labels never repeat.
  const ticks = data.filter((d) => new Date(d.t).getUTCHours() === 5).map((d) => d.t)
  const rank = (name: unknown) => FUEL_ORDER.indexOf(name as Fuel)

  return (
    <ResponsiveContainer width="100%" height={320}>
      <AreaChart data={data} margin={{ top: 8, right: 8, bottom: 0, left: 8 }}>
        <CartesianGrid stroke="var(--grid)" vertical={false} />
        <XAxis
          dataKey="t"
          type="number"
          scale="time"
          domain={['dataMin', 'dataMax']}
          ticks={ticks}
          interval="preserveStartEnd"
          tickFormatter={(t: number) => formatDay(new Date(t).toISOString())}
          stroke="var(--muted)"
          fontSize={12}
          minTickGap={30}
        />
        <YAxis
          stroke="var(--muted)"
          fontSize={12}
          width={56}
          tickFormatter={(v: number) => `${(v / 1000).toFixed(0)}k`}
          label={{ value: 'MW', angle: -90, position: 'insideLeft', fill: 'var(--muted)', fontSize: 12 }}
        />
        <Tooltip
          labelFormatter={(t) => {
            const iso = new Date(Number(t)).toISOString()
            return resolution === 'Day' ? `${formatDay(iso)} (daily average)` : formatDateTime(iso)
          }}
          formatter={(value, name) => [formatMw(Number(value)), name]}
          itemSorter={(item) => -rank(item.name)}
          itemStyle={{ color: 'var(--text)' }}
          contentStyle={{ background: 'var(--surface)', border: '1px solid var(--border)', borderRadius: 8 }}
        />
        <Legend verticalAlign="top" height={28} iconType="square" itemSorter={(item) => rank(item.value)} />
        {present.map((fuel) => (
          <Area
            key={fuel}
            dataKey={fuel}
            name={fuel}
            stackId="mix"
            type={resolution === 'Day' ? 'monotone' : 'linear'}
            stroke={FUEL_COLOURS[fuel]}
            fill={FUEL_COLOURS[fuel]}
            fillOpacity={0.85}
            strokeWidth={0}
            isAnimationActive={false}
          />
        ))}
      </AreaChart>
    </ResponsiveContainer>
  )
}
