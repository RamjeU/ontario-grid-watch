import type { DailySummary, GenerationPoint, GridSnapshot, RealtimePoint, Resolution } from './types'

export class ApiError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

async function get<T>(path: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(path, { signal, headers: { Accept: 'application/json' } })
  if (!response.ok) {
    // The API returns RFC 9457 problem details; surface its title when there is one.
    let message = `${response.status} ${response.statusText}`
    try {
      const problem = (await response.json()) as { title?: string; detail?: string }
      message = problem.detail ?? problem.title ?? message
    } catch {
      // Body was not JSON; keep the status text.
    }
    throw new ApiError(response.status, message)
  }
  return (await response.json()) as T
}

function range(from?: Date, to?: Date): string {
  const params = new URLSearchParams()
  if (from) params.set('from', from.toISOString())
  if (to) params.set('to', to.toISOString())
  return params.toString()
}

export const api = {
  snapshot: (signal?: AbortSignal) => get<GridSnapshot>('/api/grid/snapshot', signal),

  realtime: (from: Date, to: Date, signal?: AbortSignal) =>
    get<RealtimePoint[]>(`/api/grid/realtime?${range(from, to)}`, signal),

  generation: (from: Date, to: Date, resolution: Resolution, signal?: AbortSignal) =>
    get<GenerationPoint[]>(`/api/grid/generation?${range(from, to)}&resolution=${resolution}`, signal),

  daily: (from: Date, to: Date, signal?: AbortSignal) =>
    get<DailySummary[]>(`/api/grid/daily?${range(from, to)}`, signal),
}
