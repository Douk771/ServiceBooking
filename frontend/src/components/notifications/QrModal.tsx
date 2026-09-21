import { useEffect, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { notificationChannelsApi } from '../../api/notificationChannels'
import { Modal } from '../ui/Modal'
import { Icon } from '../ui/Icon'
import { getNotificationErrorMessage } from '../../utils/notificationError'

interface Props {
  channelId: string
  onClose: () => void
  onConnected: () => void
}

// US-53 п. 3, API_CONTRACT_CYCLE4.md §24.2: the server owns the polling cadence (refreshAfterSeconds),
// caching its own call to the provider for 2s. The frontend must not invent its own constant. The
// 15-minute cutoff mirrors how long an unauthorized instance survives on the server (US-53 п. 5).
const MAX_POLL_MS = 15 * 60 * 1000

export function QrModal({ channelId, onClose, onConnected }: Props) {
  const qc = useQueryClient()
  const [qrBase64, setQrBase64] = useState<string | null>(null)
  const [error, setError] = useState('')
  const [timedOut, setTimedOut] = useState(false)
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const startedAtRef = useRef(Date.now())
  const stoppedRef = useRef(false)

  useEffect(() => {
    stoppedRef.current = false

    const poll = async () => {
      if (stoppedRef.current) return
      if (Date.now() - startedAtRef.current > MAX_POLL_MS) {
        setTimedOut(true)
        return
      }
      try {
        const res = await notificationChannelsApi.getQr(channelId)
        if (stoppedRef.current) return
        if (res.state === 'Connected') {
          qc.invalidateQueries({ queryKey: ['notification-channels'] })
          onConnected()
          return
        }
        setQrBase64(res.qrBase64)
        setError('')
        timerRef.current = setTimeout(poll, Math.max(1, res.refreshAfterSeconds) * 1000)
      } catch (err) {
        if (stoppedRef.current) return
        setError(getNotificationErrorMessage(err, 'Не удалось получить QR-код. Попробуйте ещё раз.'))
        timerRef.current = setTimeout(poll, 5000)
      }
    }

    poll()

    return () => {
      stoppedRef.current = true
      if (timerRef.current) clearTimeout(timerRef.current)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [channelId])

  return (
    <Modal title="Подключение номера WhatsApp" onClose={onClose}>
      <div className="flex flex-col items-center gap-4 text-center">
        {timedOut ? (
          <div className="py-6">
            <Icon name="alert-circle" size={28} strokeWidth={1.7} className="text-warning mx-auto mb-2" />
            <p className="text-sm text-ink-soft">
              Время на подключение истекло. Начните привязку заново — раньше она уже занимала слишком много времени.
            </p>
          </div>
        ) : (
          <>
            <ol className="text-sm text-ink-soft text-left list-decimal pl-5 flex flex-col gap-1 self-stretch">
              <li>Откройте WhatsApp на телефоне с рабочим номером салона</li>
              <li>Настройки → Связанные устройства → Привязка устройства</li>
              <li>Отсканируйте QR-код ниже</li>
            </ol>
            <div className="w-56 h-56 rounded-2xl border border-line flex items-center justify-center bg-white overflow-hidden">
              {qrBase64 ? (
                <img
                  src={`data:image/png;base64,${qrBase64}`}
                  alt="QR-код для привязки WhatsApp"
                  className="w-full h-full object-contain"
                />
              ) : (
                <div className="w-8 h-8 border-2 border-gold border-t-transparent rounded-full animate-spin" />
              )}
            </div>
            <p className="text-xs text-muted">
              Код обновляется автоматически. Страница сама перейдёт дальше, как только вы отсканируете код.
            </p>
            {error && <p className="text-xs text-danger">{error}</p>}
          </>
        )}
      </div>
    </Modal>
  )
}
