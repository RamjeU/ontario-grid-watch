import type { Fuel } from '../api/types'

// Stacking order puts steady baseload at the bottom and variable sources on top.
export const FUEL_ORDER: Fuel[] = ['Nuclear', 'Hydro', 'Gas', 'Wind', 'Solar', 'Biofuel', 'Other']

export const FUEL_COLOURS: Record<Fuel, string> = {
  Nuclear: 'var(--fuel-nuclear)',
  Hydro: 'var(--fuel-hydro)',
  Gas: 'var(--fuel-gas)',
  Wind: 'var(--fuel-wind)',
  Solar: 'var(--fuel-solar)',
  Biofuel: 'var(--fuel-biofuel)',
  Other: 'var(--fuel-other)',
}

export const NON_EMITTING: ReadonlySet<Fuel> = new Set<Fuel>(['Nuclear', 'Hydro', 'Wind', 'Solar'])
