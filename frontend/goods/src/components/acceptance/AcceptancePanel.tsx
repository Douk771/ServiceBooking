import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { Icon } from '@/components/ui/Icon'
import { scheduleApi } from '../../api/schedule'
import { InlineError } from '../StatePanels'
import { pauseRemainingText, PAUSE_OPTIONS } from '../../utils/acceptance'
import { getGoodsErrorMessage } from '../../utils/orderError'
import type { PauseDuration, ShopAcceptanceDto } from '../../types'

interface Props {
  shopId: string
  acceptance: ShopAcceptanceDto
  /** Server clock in ms (poll answer + offset), for «осталось N мин». */
  serverNowMs: number
  /** Called with the answer of a successful `PUT …/acceptance` so the screen reflects it before the next poll. */
  onChanged: (a: ShopAcceptanceDto) => void
}

const BTN =
  'min-h-[44px] min-w-[44px] inline-flex items-center justify-center rounded-full px-4 text-sm font-semibold border transition-colors disabled:opacity-50 disabled:cursor-not-allowed'

/**
 * US-24-03 — the acceptance panel on the orders screen: pause for 15/30/60 min or until the end of the day, stop, resume.
 * The status line is the server's `statusText` (mode is in words, colour is only an accent); every button is at least
 * 44×44 px (API_CONTRACT_CYCLE24.md §490). «Кто и когда» (P1) is printed only when the server sends `changedText`.
 */
export function AcceptancePanel({ shopId, acceptance, serverNowMs, onChanged }: Props) {
  const [error, setError] = useState<string | null>(null)
  const mutation = useMutation({
    mutationFn: (v: { mode: 'Accepting' | 'Paused' | 'Stopped'; pause?: PauseDuration }) => scheduleApi.putAcceptance(shopId, v),
    onSuccess: (a) => {
      setError(null)
      onChanged(a)
    },
    onError: (err) => setError(getGoodsErrorMessage(err, 'Не удалось изменить режим приёма.')),
  })
  const busy = mutation.isPending
  const remaining = pauseRemainingText(acceptance, serverNowMs)
  const accepting = acceptance.mode === 'Accepting'

  return (
    <section aria-label="Приём заказов" className="rounded-2xl border border-line bg-white p-4 mb-4" data-testid="acceptance-panel" data-mode={acceptance.mode}>
      <div className="flex items-center gap-3 flex-wrap">
        <span
          className={`inline-flex items-center gap-1.5 rounded-full px-3 py-1.5 text-sm font-semibold ${accepting ? 'bg-success-bg text-success' : acceptance.mode === 'Paused' ? 'bg-warning-bg text-warning' : 'bg-danger-bg text-danger'}`}
          role="status"
        >
          <Icon name={accepting ? 'check-circle' : 'alert-circle'} size={15} strokeWidth={1.8} />
          {acceptance.statusText}
        </span>
        {remaining && <span className="text-sm text-ink-soft">{remaining}</span>}
        {acceptance.changedText && <span className="text-xs text-muted ml-auto">{acceptance.changedText}</span>}
      </div>

      <div className="mt-3 flex flex-wrap gap-2 items-center">
        {accepting ? (
          <>
            <span className="text-sm text-ink-soft mr-1">Пауза:</span>
            {PAUSE_OPTIONS.map((o) => (
              <button key={o.value} type="button" disabled={busy} onClick={() => mutation.mutate({ mode: 'Paused', pause: o.value })} className={`${BTN} bg-white text-ink border-line hover:bg-cream-deep`}>
                {o.label}
              </button>
            ))}
            <button type="button" disabled={busy} onClick={() => mutation.mutate({ mode: 'Stopped' })} className={`${BTN} bg-danger-bg text-danger border-transparent hover:bg-[#F5DAD1] sm:ml-auto`}>
              Не принимать заказы
            </button>
          </>
        ) : (
          <button type="button" disabled={busy} onClick={() => mutation.mutate({ mode: 'Accepting' })} className={`${BTN} bg-ink text-cream border-transparent hover:bg-ink/90 px-6`}>
            {busy ? 'Включаем…' : 'Возобновить приём'}
          </button>
        )}
      </div>
      {error && (
        <div className="mt-3">
          <InlineError>{error}</InlineError>
        </div>
      )}
    </section>
  )
}
