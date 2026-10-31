import type { DailySummary } from '../api/types'
import { formatIntensity, formatMarketDate, formatMw, formatPercent, formatPrice, formatTime } from '../lib/format'

export function DailyTable({ days }: { days: DailySummary[] }) {
  return (
    <div className="table-wrap">
      <table>
        <thead>
          <tr>
            <th scope="col">Market day</th>
            <th scope="col" className="num">Peak demand</th>
            <th scope="col" className="num">Avg demand</th>
            <th scope="col" className="num">Avg price</th>
            <th scope="col" className="num">Clean share</th>
            <th scope="col" className="num">CO₂ intensity</th>
          </tr>
        </thead>
        <tbody>
          {days.map((d) => (
            <tr key={d.marketDate}>
              <th scope="row">
                {formatMarketDate(d.marketDate)}
                {!d.complete && <span className="tag partial" title="Some hours are not published yet">partial</span>}
              </th>
              <td className="num">
                {formatMw(d.peakOntarioDemandMw)}
                {d.peakHourStartUtc && <span className="muted"> at {formatTime(d.peakHourStartUtc)}</span>}
              </td>
              <td className="num">{formatMw(d.averageOntarioDemandMw)}</td>
              <td className="num">{formatPrice(d.averagePrice)}</td>
              <td className="num">{formatPercent(d.nonEmittingShare)}</td>
              <td className="num">{formatIntensity(d.carbonIntensity)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
