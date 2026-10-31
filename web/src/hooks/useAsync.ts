import { useCallback, useEffect, useRef, useState } from 'react'

export interface AsyncState<T> {
  data: T | undefined
  error: Error | undefined
  loading: boolean
  reload: () => void
}

interface Settled<T> {
  request: string
  data?: T
  error?: Error
}

/**
 * Runs an async loader whenever `key` changes or reload() is called. Stale requests are aborted,
 * and the previous data stays on screen while a refresh is in flight so charts do not flash empty
 * on every live update. `loading` is derived (the latest request has not settled yet) rather than
 * stored, so the effect only sets state from the async callbacks.
 */
export function useAsync<T>(load: (signal: AbortSignal) => Promise<T>, key: string): AsyncState<T> {
  const [version, setVersion] = useState(0)
  const [settled, setSettled] = useState<Settled<T>>({ request: '' })
  const loader = useRef(load)
  const request = `${key}#${version}`

  useEffect(() => {
    loader.current = load
  }, [load])

  useEffect(() => {
    const controller = new AbortController()
    loader.current(controller.signal).then(
      (data) => setSettled({ request, data }),
      (e: unknown) => {
        if (controller.signal.aborted) return
        setSettled((prev) => ({ request, data: prev.data, error: e instanceof Error ? e : new Error(String(e)) }))
      },
    )
    return () => controller.abort()
  }, [request])

  const reload = useCallback(() => setVersion((v) => v + 1), [])
  return { data: settled.data, error: settled.error, loading: settled.request !== request, reload }
}
