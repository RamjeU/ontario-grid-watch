interface KpiCardProps {
  label: string
  value: string
  detail?: string
  asOf?: string
  stale?: boolean
}

export function KpiCard({ label, value, detail, asOf, stale }: KpiCardProps) {
  return (
    <section className="card kpi" aria-label={label}>
      <h2 className="kpi-label">{label}</h2>
      <p className="kpi-value">{value}</p>
      {detail && <p className="kpi-detail">{detail}</p>}
      {asOf && <p className={stale ? 'kpi-asof stale' : 'kpi-asof'}>{asOf}</p>}
    </section>
  )
}
