import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import { useEffect, useRef, useState } from 'react'

export type HubStatus = 'connecting' | 'live' | 'reconnecting' | 'offline'

/**
 * Connects to the server's SignalR hub and calls onUpdate whenever new IESO data lands.
 * Reconnects automatically; if the socket cannot be held open the page still refreshes on a timer.
 */
export function useGridHub(onUpdate: () => void): HubStatus {
  const [status, setStatus] = useState<HubStatus>('connecting')
  const callback = useRef(onUpdate)

  useEffect(() => {
    callback.current = onUpdate
  }, [onUpdate])

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/grid')
      .withAutomaticReconnect([0, 2000, 10000, 30000, 60000])
      .configureLogging(LogLevel.Warning)
      .build()

    connection.on('gridUpdated', () => callback.current())
    connection.onreconnecting(() => setStatus('reconnecting'))
    connection.onreconnected(() => {
      setStatus('live')
      callback.current() // catch up on anything missed while disconnected
    })
    connection.onclose(() => setStatus('offline'))

    let cancelled = false
    connection
      .start()
      .then(() => !cancelled && setStatus('live'))
      .catch(() => !cancelled && setStatus('offline'))

    return () => {
      cancelled = true
      if (connection.state !== HubConnectionState.Disconnected) void connection.stop()
    }
  }, [])

  return status
}
