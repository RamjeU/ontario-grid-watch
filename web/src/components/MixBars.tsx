import type { FuelMix } from '../api/types'
import { formatMw, formatPercent } from '../lib/format'
import { FUEL_COLOURS, NON_EMITTING } from '../lib/fuels'

/** Generation mix for one hour as labelled horizontal bars, largest first. */
export function MixBars({ mix }: { mix: FuelMix }) {
  const fuels = [...mix.fuels].sort((a, b) => b.outputMw - a.outputMw)

  return (
    <ul className="mix" aria-label="Generation mix">
      {fuels.map((f) => (
        <li key={f.fuel} className="mix-row">
          <span className="mix-name">
            <span className="swatch" style={{ background: FUEL_COLOURS[f.fuel] }} aria-hidden />
            {f.fuel}
            {NON_EMITTING.has(f.fuel) && <span className="tag" title="Non-emitting at the point of generation">clean</span>}
          </span>
          <span className="mix-bar" aria-hidden>
            <span style={{ width: `${Math.max(f.share * 100, f.outputMw > 0 ? 0.5 : 0)}%`, background: FUEL_COLOURS[f.fuel] }} />
          </span>
          <span className="mix-value">
            <strong>{formatPercent(f.share)}</strong> <span className="muted">{formatMw(f.outputMw)}</span>
          </span>
        </li>
      ))}
    </ul>
  )
}
