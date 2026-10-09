import { useCallback, useEffect, useRef, useState } from 'react'
import { notificationChannelsApi } from '../../api/notificationChannels'
import { Button } from '../ui/Button'
import { getNotificationErrorMessage } from '../../utils/notificationError'
import * as T from './numbersTexts'

interface Props {
  channelId: string
  displayName: string
  /** `qrInstruction` и `connectionNotice` приходят с сервера по транспорту (§40.33.5) — выводятся дословно. */
  instruction: string[]
  connectionNotice?: string | null
  onConnected: () => void
}

// Каденцию опроса задаёт сервер (refreshAfterSeconds); 15 минут — сколько живёт неавторизованный экземпляр (US-53 п. 5).
const MAX_POLL_MS = 15 * 60 * 1000

/** Шаг «QR»: `connect` (идемпотентен — Connecting даёт 202 без нового экземпляра), затем опрос `qr` до Connected. */
export function QrStep({ channelId, displayName, instruction, connectionNotice, onConnected }: Props) {
  const [qr, setQr] = useState<string | null>(null)
  const [error, setError] = useState('')
  const [fatal, setFatal] = useState(false)
  const [timedOut, setTimedOut] = useState(false)
  const [attempt, setAttempt] = useState(0)
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null)
  const onConnectedRef = useRef(onConnected)
  onConnectedRef.current = onConnected

  useEffect(() => {
    let stopped = false
    const startedAt = Date.now()

    const poll = async () => {
      if (stopped) return
      if (Date.now() - startedAt > MAX_POLL_MS) {
        setTimedOut(true)
        return
      }
      try {
        const res = await notificationChannelsApi.getQr(channelId)
        if (stopped) return
        if (res.state === 'Connected') {
          onConnectedRef.current()
          return
        }
        setQr(res.qrBase64)
        setError('')
        timer.current = setTimeout(poll, Math.max(1, res.refreshAfterSeconds) * 1000)
      } catch (err) {
        if (stopped) return
        setError(getNotificationErrorMessage(err, T.QR_ERROR))
        timer.current = setTimeout(poll, 5000)
      }
    }

    const start = async () => {
      setError('')
      setFatal(false)
      setTimedOut(false)
      setQr(null)
      try {
        await notificationChannelsApi.connect(channelId)
      } catch (err) {
        if (stopped) return
        // 402/409/503: текст отказа пишет сервер; продолжать опрос бессмысленно.
        setError(getNotificationErrorMessage(err, T.QR_ERROR))
        setFatal(true)
        return
      }
      if (!stopped) void poll()
    }

    void start()
    return () => {
      stopped = true
      if (timer.current) clearTimeout(timer.current)
    }
  }, [channelId, attempt])

  const restart = useCallback(() => setAttempt((a) => a + 1), [])

  if (fatal || timedOut) {
    return (
      <div className="flex flex-col gap-3 items-start">
        <p role="alert" className="text-sm text-danger">
          {timedOut ? T.QR_TIMEOUT : error}
        </p>
        <Button variant="secondary" onClick={restart}>
          {T.QR_RETRY}
        </Button>
      </div>
    )
  }

  return (
    <div className="flex flex-col items-center gap-4 text-center">
      {connectionNotice && <p className="text-xs text-ink-soft bg-cream-deep rounded-xl px-3.5 py-2.5 self-stretch text-left">{connectionNotice}</p>}
      <ol className="text-sm text-ink-soft text-left list-decimal pl-5 flex flex-col gap-1 self-stretch">
        {instruction.map((line) => (
          <li key={line}>{line}</li>
        ))}
      </ol>
      <div className="w-56 h-56 rounded-2xl border border-line flex items-center justify-center bg-white overflow-hidden">
        {qr ? (
          <img src={`data:image/png;base64,${qr}`} alt={T.QR_SCAN_ALT(displayName)} className="w-full h-full object-contain" />
        ) : (
          <div role="status" aria-label="Загрузка QR-кода" className="w-8 h-8 border-2 border-gold border-t-transparent rounded-full animate-spin" />
        )}
      </div>
      <p className="text-xs text-muted">{T.QR_AUTO_HINT}</p>
      {error && <p className="text-xs text-danger">{error}</p>}
    </div>
  )
}
