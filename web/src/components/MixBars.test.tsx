import { render, screen, within } from '@testing-library/react'
import type { FuelMix } from '../api/types'
import { MixBars } from './MixBars'

const mix: FuelMix = {
  totalMw: 10000,
  nonEmittingShare: 0.7,
  carbonIntensity: 160,
  complete: true,
  fuels: [
    { fuel: 'Gas', outputMw: 3000, share: 0.3 },
    { fuel: 'Nuclear', outputMw: 6000, share: 0.6 },
    { fuel: 'Wind', outputMw: 1000, share: 0.1 },
  ],
}

describe('MixBars', () => {
  it('lists fuels from largest to smallest share', () => {
    render(<MixBars mix={mix} />)

    const rows = within(screen.getByRole('list', { name: 'Generation mix' })).getAllByRole('listitem')
    expect(rows.map((r) => r.textContent)).toEqual([
      expect.stringContaining('Nuclear'),
      expect.stringContaining('Gas'),
      expect.stringContaining('Wind'),
    ])
    expect(rows[0].textContent).toContain('60%')
  })

  it('marks only non-emitting fuels as clean', () => {
    render(<MixBars mix={mix} />)

    expect(screen.getAllByText('clean')).toHaveLength(2) // Nuclear and Wind, not Gas
  })
})
