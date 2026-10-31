import { CartesianGrid, ComposedChart, Legend, Line, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import type { RealtimePoint } from '../api/types'
import { formatDateTime, formatMw, formatPrice, formatTime } from '../lib/format'

/** Last 24 hours of 5-minute Ontario demand (left axis) and price (right axis). */
export function RealtimeChart({ points }: { points: RealtimePoint[] }) {
  const data = points.map((p) => ({ t: Date.parse(p.intervalStartUtc), demand: p.ontarioDemandMw, price: p.pricePerMwh }))

  return (
    <ResponsiveContainer width="100%" height={300}>
      <ComposedChart data={data} margin={{ top: 8, right: 8, bottom: 0, left: 8 }}>
        <CartesianGrid stroke="var(--grid)" vertical={false} />
        <XAxis
          dataKey="t"
          type="number"
          scale="time"
          domain={['dataMin', 'dataMax']}
          tickFormatter={(t: number) => formatTime(new Date(t).toISOString())}
          stroke="var(--muted)"
          fontSize={12}
          minTickGap={40}
        />
        <YAxis
          yAxisId="mw"
          stroke="var(--muted)"
          fontSize={12}
          width={56}
          domain={['auto', 'auto']}
          tickFormatter={(v: number) => `${(v / 1000).toFixed(1)}k`}
        />
        <YAxis
          yAxisId="price"
          orientation="right"
          stroke="var(--muted)"
          fontSize={12}
          width={48}
          tickFormatter={(v: number) => `$${v}`}
        />
        <Tooltip
          labelFormatter={(t) => formatDateTime(new Date(Number(t)).toISOString())}
          formatter={(value, name) => [String(name).startsWith('Price') ? formatPrice(Number(value)) : formatMw(Number(value)), name]}
          contentStyle={{ background: 'var(--surface)', border: '1px solid var(--border)', borderRadius: 8 }}
        />
        <Legend verticalAlign="top" height={28} iconType="plainline" />
        <Line yAxisId="mw" dataKey="demand" name="Ontario demand (MW)" stroke="var(--accent)" strokeWidth={2} dot={false} isAnimationActive={false} connectNulls />
        <Line yAxisId="price" dataKey="price" name="Price ($/MWh)" type="stepAfter" stroke="var(--price)" strokeWidth={1.5} dot={false} isAnimationActive={false} connectNulls />
      </ComposedChart>
    </ResponsiveContainer>
  )
}
