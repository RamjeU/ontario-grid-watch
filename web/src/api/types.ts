// Mirrors the C# read models in GridWatch.Core/Analytics/ReadModels.cs. All times are UTC ISO strings.

export type Fuel = 'Nuclear' | 'Gas' | 'Hydro' | 'Wind' | 'Solar' | 'Biofuel' | 'Other'

export interface FuelShare {
  fuel: Fuel
  outputMw: number
  share: number
}

export interface FuelMix {
  totalMw: number
  fuels: FuelShare[]
  nonEmittingShare: number
  carbonIntensity: number | null
  complete: boolean
}

export interface GridSnapshot {
  liveDemand: { intervalStartUtc: string; ontarioDemandMw: number } | null
  demand: { hourStartUtc: string; ontarioDemandMw: number; marketDemandMw: number } | null
  price: { intervalStartUtc: string; pricePerMwh: number } | null
  mix: { hourStartUtc: string; mix: FuelMix } | null
  generatedAtUtc: string
}

export interface RealtimePoint {
  intervalStartUtc: string
  ontarioDemandMw: number | null
  pricePerMwh: number | null
}

export interface GenerationPoint {
  startUtc: string
  outputMw: Partial<Record<Fuel, number | null>>
  complete: boolean
}

export interface DailySummary {
  marketDate: string
  complete: boolean
  peakOntarioDemandMw: number | null
  peakHourStartUtc: string | null
  averageOntarioDemandMw: number | null
  averagePrice: number | null
  nonEmittingShare: number | null
  carbonIntensity: number | null
}

export type Resolution = 'Hour' | 'Day'
