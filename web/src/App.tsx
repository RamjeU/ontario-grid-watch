import { useCallback, useEffect, useState } from 'react'
import { api } from './api/client'
import type { Resolution } from './api/types'
import { DailyTable } from './components/DailyTable'
import { GenerationChart } from './components/GenerationChart'
import { KpiCard } from './components/KpiCard'
import { MixBars } from './components/MixBars'
import { RealtimeChart } from './components/RealtimeChart'
import { useAsync } from './hooks/useAsync'
import { useGridHub, type HubStatus } from './hooks/useGridHub'
import { formatAge, formatDateTime, formatIntensity, formatMw, formatPercent, formatPrice } from './lib/format'

const HOUR = 3_600_000
const FALLBACK_REFRESH_MS = 5 * 60_000

const RANGES: Record<'7d' | '30d', { days: number; resolution: Resolution; label: string }> = {
  '7d': { days: 7, resolution: 'Hour', label: '7 days, hourly' },
  '30d': { days: 30, resolution: 'Day', label: '30 days, daily average' },
}

export default function App() {
  const [refresh, setRefresh] = useState(0)
  const bump = useCallback(() => setRefresh((n) => n + 1), [])
  const status = useGridHub(bump)
  const [rangeKey, setRangeKey] = useState<keyof typeof RANGES>('7d')

  // A slow timer as a safety net in case the live connection is blocked by a proxy.
  useEffect(() => {
    const id = window.setInterval(bump, FALLBACK_REFRESH_MS)
    return () => window.clearInterval(id)
  }, [bump])

  // The clock used for "5 min ago" labels; ticks so the ages stay honest between updates.
  const [now, setNow] = useState(() => new Date())
  useEffect(() => {
    const id = window.setInterval(() => setNow(new Date()), 30_000)
    return () => window.clearInterval(id)
  }, [])

  const snapshot = useAsync((signal) => api.snapshot(signal), `snapshot-${refresh}`)
  const snap = snapshot.data

  // Each IESO report is published on a different lag, so each chart ends at the latest data it has
  // rather than at "now". This also keeps the dashboard useful when running on bundled sample data.
  const realtimeEnd = snap?.liveDemand?.intervalStartUtc ?? snap?.price?.intervalStartUtc
  const fuelEnd = snap?.mix?.hourStartUtc
  const range = RANGES[rangeKey]

  const realtime = useAsync(
    (signal) => {
      if (!realtimeEnd) return Promise.resolve([])
      const end = new Date(Date.parse(realtimeEnd) + 5 * 60_000)
      return api.realtime(new Date(end.getTime() - 24 * HOUR), end, signal)
    },
    `rt-${realtimeEnd}-${refresh}`,
  )

  const generation = useAsync(
    (signal) => {
      if (!fuelEnd) return Promise.resolve([])
      const end = new Date(Date.parse(fuelEnd) + HOUR)
      return api.generation(new Date(end.getTime() - range.days * 24 * HOUR), end, range.resolution, signal)
    },
    `gen-${fuelEnd}-${rangeKey}-${refresh}`,
  )

  const daily = useAsync(
    (signal) => {
      const latest = realtimeEnd ?? fuelEnd
      if (!latest) return Promise.resolve([])
      const end = new Date(Date.parse(latest) + HOUR)
      // Start on a whole market day (midnight EST = 05:00 UTC) so the oldest row is not cut off.
      const start = new Date(end.getTime() - 14 * 24 * HOUR)
      start.setUTCHours(5, 0, 0, 0)
      return api.daily(start, end, signal)
    },
    `daily-${realtimeEnd}-${fuelEnd}-${refresh}`,
  )

  const mix = snap?.mix?.mix

  return (
    <div className="page">
      <header className="header">
        <div>
          <h1>Ontario Grid Watch</h1>
          <p className="subtitle">Electricity demand, price and supply mix from the IESO's public reports</p>
        </div>
        <StatusPill status={status} />
      </header>

      {snapshot.error && !snap && <p className="error" role="alert">Could not reach the API: {snapshot.error.message}</p>}

      <div className="kpis">
        <KpiCard
          label="Ontario demand"
          value={formatMw(snap?.liveDemand?.ontarioDemandMw)}
          asOf={snap?.liveDemand ? `5-min, ${formatDateTime(snap.liveDemand.intervalStartUtc)} · ${formatAge(snap.liveDemand.intervalStartUtc, now)}` : undefined}
          stale={isStale(snap?.liveDemand?.intervalStartUtc, now, 1)}
        />
        <KpiCard
          label="Ontario zonal price"
          value={formatPrice(snap?.price?.pricePerMwh)}
          detail="per MWh"
          asOf={snap?.price ? `5-min, ${formatDateTime(snap.price.intervalStartUtc)} · ${formatAge(snap.price.intervalStartUtc, now)}` : undefined}
          stale={isStale(snap?.price?.intervalStartUtc, now, 1)}
        />
        <KpiCard
          label="Clean share of supply"
          value={formatPercent(mix?.nonEmittingShare)}
          detail="nuclear, hydro, wind and solar"
          asOf={snap?.mix ? `Hour from ${formatDateTime(snap.mix.hourStartUtc)} · ${formatAge(snap.mix.hourStartUtc, now)}` : undefined}
          stale={isStale(snap?.mix?.hourStartUtc, now, 48)}
        />
        <KpiCard
          label="Carbon intensity (est.)"
          value={formatIntensity(mix?.carbonIntensity)}
          detail="life-cycle CO₂e, IPCC median factors"
          asOf={snap?.mix ? `Same hour as the mix${mix && !mix.complete ? ' · some meters missing' : ''}` : undefined}
        />
      </div>

      <section className="card">
        <div className="card-head">
          <h2>Last 24 hours</h2>
          <p className="muted">Ontario demand and real-time price, every 5 minutes</p>
        </div>
        {realtime.data && realtime.data.length > 0 ? <RealtimeChart points={realtime.data} /> : <Placeholder state={realtime} waiting={snapshot.loading} />}
      </section>

      <div className="split">
        <section className="card">
          <div className="card-head">
            <h2>Generation by fuel</h2>
            <div className="segmented" role="group" aria-label="Range">
              {(Object.keys(RANGES) as (keyof typeof RANGES)[]).map((key) => (
                <button key={key} type="button" aria-pressed={rangeKey === key} onClick={() => setRangeKey(key)}>
                  {key}
                </button>
              ))}
            </div>
          </div>
          <p className="muted small">{range.label}. The IESO publishes fuel output once a day, for the previous day.</p>
          {generation.data && generation.data.length > 0 ? (
            <GenerationChart points={generation.data} resolution={range.resolution} />
          ) : (
            <Placeholder state={generation} waiting={snapshot.loading} />
          )}
        </section>

        <section className="card">
          <div className="card-head">
            <h2>Supply mix</h2>
            {snap?.mix && <p className="muted small">{formatDateTime(snap.mix.hourStartUtc)}</p>}
          </div>
          {mix ? (
            <>
              <MixBars mix={mix} />
              <p className="muted small total">Total output {formatMw(mix.totalMw)}</p>
            </>
          ) : (
            <Placeholder state={snapshot} />
          )}
        </section>
      </div>

      <section className="card">
        <div className="card-head">
          <h2>Daily summary</h2>
          <p className="muted">Market days run midnight to midnight Eastern Standard Time</p>
        </div>
        {daily.data && daily.data.length > 0 ? <DailyTable days={daily.data} /> : <Placeholder state={daily} waiting={snapshot.loading} />}
      </section>

      <footer className="footer">
        <p>
          Data: <a href="https://reports-public.ieso.ca/public/">IESO public reports</a> (generator output by fuel,
          hourly demand, real-time totals and Ontario zonal price). Carbon intensity is an estimate using median
          life-cycle factors from IPCC AR5 WGIII Annex III (2014), not measured emissions. Times are Toronto local time.
        </p>
        <p>
          <a href="/docs">API reference</a> · <a href="https://github.com/RamjeU/ontario-grid-watch">Source on GitHub</a>
        </p>
      </footer>
    </div>
  )
}

function isStale(iso: string | undefined, now: Date, hours: number): boolean {
  return iso !== undefined && now.getTime() - Date.parse(iso) > hours * HOUR
}

function StatusPill({ status }: { status: HubStatus }) {
  const labels: Record<HubStatus, string> = {
    connecting: 'Connecting…',
    live: 'Live',
    reconnecting: 'Reconnecting…',
    offline: 'Offline · refreshing every 5 min',
  }
  return (
    <span className={`status status-${status}`} role="status">
      <span className="dot" aria-hidden />
      {labels[status]}
    </span>
  )
}

function Placeholder({ state, waiting = false }: { state: { loading: boolean; error?: Error }; waiting?: boolean }) {
  if (state.error) return <p className="placeholder error">Could not load: {state.error.message}</p>
  return <p className="placeholder">{state.loading || waiting ? 'Loading…' : 'No data for this range yet.'}</p>
}
