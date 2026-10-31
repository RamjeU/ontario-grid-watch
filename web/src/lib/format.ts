// Times are shown in Toronto local time, which is what people in Ontario read on a clock.
// (The IESO's market hours are in EST year-round; the API converts those to UTC.)
export const TIME_ZONE = 'America/Toronto'

const number = new Intl.NumberFormat('en-CA', { maximumFractionDigits: 0 })
const money = new Intl.NumberFormat('en-CA', { style: 'currency', currency: 'CAD', minimumFractionDigits: 2, maximumFractionDigits: 2 })
const percent = new Intl.NumberFormat('en-CA', { style: 'percent', maximumFractionDigits: 0 })

export function formatMw(value: number | null | undefined): string {
  return value == null ? '–' : `${number.format(value)} MW`
}

export function formatPrice(value: number | null | undefined): string {
  return value == null ? '–' : money.format(value)
}

export function formatPercent(value: number | null | undefined): string {
  return value == null ? '–' : percent.format(value)
}

export function formatIntensity(value: number | null | undefined): string {
  return value == null ? '–' : `${number.format(value)} g/kWh`
}

export function formatTime(iso: string): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone: TIME_ZONE, hour: 'numeric', minute: '2-digit' }).format(new Date(iso))
}

export function formatDateTime(iso: string): string {
  return new Intl.DateTimeFormat('en-CA', {
    timeZone: TIME_ZONE,
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  }).format(new Date(iso))
}

export function formatDay(iso: string): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone: TIME_ZONE, month: 'short', day: 'numeric' }).format(new Date(iso))
}

/** A market date ("2026-10-30") shown without converting time zones. */
export function formatMarketDate(date: string): string {
  const [y, m, d] = date.split('-').map(Number)
  return new Intl.DateTimeFormat('en-CA', { timeZone: 'UTC', weekday: 'short', month: 'short', day: 'numeric' }).format(
    new Date(Date.UTC(y, m - 1, d)),
  )
}

/** "5 min ago", "3 h ago", "2 d ago": how stale a reading is. */
export function formatAge(iso: string, now: Date = new Date()): string {
  const minutes = Math.max(0, Math.round((now.getTime() - new Date(iso).getTime()) / 60000))
  if (minutes < 1) return 'just now'
  if (minutes < 60) return `${minutes} min ago`
  const hours = Math.round(minutes / 60)
  if (hours < 48) return `${hours} h ago`
  return `${Math.round(hours / 24)} d ago`
}
