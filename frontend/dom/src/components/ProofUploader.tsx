import { useRef, useState } from 'react'
import { Button } from '@/components/ui/Button'
import { Icon } from '@/components/ui/Icon'
import { guestBookingsApi } from '../api/guestBookings'
import type { ProofRulesDto } from '../types'
import { PROOF_ACCEPT, proofFileProblem, splitBySlots } from '../utils/paymentProof'
import { getStayErrorMessage, readConflict } from '../utils/stayError'
import { InlineError } from './StatePanels'

/** What the uploader needs of a booking OR of a separate session: the files already attached and the rules. */
interface ProofHolder {
  paymentProofs: readonly unknown[]
  proofs: ProofRulesDto
}

interface Props<T extends ProofHolder> {
  token: string
  booking: T
  /** The server's answer after every file (it carries the current status and the list of files). */
  onBooking: (booking: T) => void
  /** Sends one file; defaults to the booking route. A session passes its own route. */
  upload?: (token: string, file: File, onProgress: (percent: number) => void) => Promise<T>
  /** A refusal that came with the current booking (e.g. `HoldExpired`): the screen changes under the uploader, so the page keeps the text. */
  onRefusal?: (message: string) => void
}

/**
 * «Приложить подтверждение оплаты» (квитанция или скриншот перевода), up to `proofs.maxCount` files, one after another with progress.
 * `accept` has no HEIC on purpose: iOS Safari then converts a photo to JPEG itself (ARCHITECTURE_CYCLE37.md §37.18 п. 2). The first
 * file moves the booking to «ожидает проверки оплаты»; a hold that expired meanwhile comes back as a 409 `HoldExpired` with the
 * current booking, which replaces the screen — nothing is lost silently.
 */
export function ProofUploader<T extends ProofHolder>({ token, booking, onBooking, onRefusal, upload: send }: Props<T>) {
  const inputRef = useRef<HTMLInputElement>(null)
  const [busy, setBusy] = useState(false)
  const [progress, setProgress] = useState<{ index: number; total: number; percent: number } | null>(null)
  const [error, setError] = useState('')

  const rules = booking.proofs
  const attached = booking.paymentProofs.length
  const freeSlots = Math.max(0, rules.maxCount - attached)

  const uploadAll = async (picked: File[]) => {
    setError('')
    const { take, skipped } = splitBySlots(picked, attached, rules.maxCount)
    if (take.length === 0) {
      setError(`Можно приложить не больше ${rules.maxCount} файлов`)
      return
    }
    setBusy(true)
    try {
      for (let i = 0; i < take.length; i++) {
        const problem = proofFileProblem(take[i], rules)
        if (problem) {
          setError(problem)
          return
        }
        setProgress({ index: i + 1, total: take.length, percent: 0 })
        try {
          const onPercent = (percent: number) => setProgress({ index: i + 1, total: take.length, percent })
          const next = send ? await send(token, take[i], onPercent) : ((await guestBookingsApi.uploadProof(token, take[i], onPercent)) as unknown as T)
          onBooking(next)
        } catch (err) {
          // A booking refusal carries `booking`, a session refusal carries `order`: both are the current state of the holder.
          const conflict = readConflict<{ code: string; message: string; booking?: T; order?: T }>(err)
          const message = getStayErrorMessage(err, 'Не удалось загрузить файл.')
          const current = conflict?.booking ?? conflict?.order
          if (current) {
            onBooking(current)
            onRefusal?.(message)
          }
          setError(message)
          return
        }
      }
      if (skipped > 0) setError(`Приложено ${take.length}: больше ${rules.maxCount} файлов на бронь нельзя`)
    } finally {
      setBusy(false)
      setProgress(null)
      if (inputRef.current) inputRef.current.value = ''
    }
  }

  if (!rules.canAttach) return null

  return (
    <div>
      <input
        ref={inputRef}
        type="file"
        accept={PROOF_ACCEPT}
        multiple={freeSlots > 1}
        className="sr-only"
        id="proof-input"
        onChange={(e) => {
          const files = Array.from(e.target.files ?? [])
          if (files.length > 0) void uploadAll(files)
        }}
      />
      <Button type="button" size="lg" loading={busy} onClick={() => inputRef.current?.click()} className="w-full sm:w-auto">
        <Icon name="plus" size={16} strokeWidth={1.8} />
        {attached === 0 ? 'Приложить подтверждение оплаты' : 'Приложить ещё файл'}
      </Button>
      <p className="mt-1.5 text-xs text-muted">
        Квитанция или скриншот перевода: PDF, JPEG, PNG или WebP до 10 МБ, не больше {rules.maxCount} файлов (осталось {freeSlots}).
      </p>
      {progress && (
        <div className="mt-2" role="status" aria-live="polite">
          <p className="text-xs text-ink-soft">
            Загрузка {progress.index} из {progress.total}: {progress.percent} %
          </p>
          <div className="mt-1 h-1.5 overflow-hidden rounded-full bg-cream-deep">
            <div className="h-full bg-gold transition-all" style={{ width: `${progress.percent}%` }} />
          </div>
        </div>
      )}
      {error && (
        <div className="mt-2">
          <InlineError>{error}</InlineError>
        </div>
      )}
    </div>
  )
}
