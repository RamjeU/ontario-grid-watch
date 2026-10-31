import { formatAge, formatIntensity, formatMarketDate, formatMw, formatPercent, formatPrice, formatTime } from './format'

describe('number formatting', () => {
  it('formats megawatts with thousands separators', () => {
    expect(formatMw(15083.2)).toBe('15,083 MW')
  })

  it('formats prices as Canadian dollars, including negative prices', () => {
    expect(formatPrice(76.64)).toBe('$76.64')
    expect(formatPrice(-4.1)).toBe('-$4.10')
  })

  it('formats shares and intensity', () => {
    expect(formatPercent(0.7019)).toBe('70%')
    expect(formatIntensity(156.6)).toBe('157 g/kWh')
  })

  it('shows a dash for missing values', () => {
    expect(formatMw(null)).toBe('–')
    expect(formatPrice(undefined)).toBe('–')
    expect(formatPercent(null)).toBe('–')
  })
})

describe('time formatting', () => {
  it('shows Toronto local time, which follows daylight saving', () => {
    // 16:25 UTC is 12:25 in October (EDT) and 11:25 in January (EST).
    expect(formatTime('2026-10-31T16:25:00Z')).toMatch(/12:25/)
    expect(formatTime('2026-01-15T16:25:00Z')).toMatch(/11:25/)
  })

  it('shows a market date without shifting it across time zones', () => {
    expect(formatMarketDate('2026-10-30')).toMatch(/Oct 30/)
  })

  it('describes how old a reading is', () => {
    const now = new Date('2026-10-31T17:00:00Z')
    expect(formatAge('2026-10-31T16:55:00Z', now)).toBe('5 min ago')
    expect(formatAge('2026-10-31T05:00:00Z', now)).toBe('12 h ago')
    expect(formatAge('2026-10-28T17:00:00Z', now)).toBe('3 d ago')
  })
})
