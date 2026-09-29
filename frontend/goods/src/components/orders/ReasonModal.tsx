import { useState } from 'react'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'

interface Props {
  title: string
  description?: string
  confirmLabel: string
  busy?: boolean
  /** Reject/cancel reason is optional and ≤ 300 characters (§416). */
  onConfirm: (reason: string | undefined) => void
  onClose: () => void
}

export function ReasonModal({ title, description, confirmLabel, busy, onConfirm, onClose }: Props) {
  const [reason, setReason] = useState('')
  return (
    <Modal title={title} onClose={onClose} dismissible={!busy}>
      {description && <p className="text-sm text-ink-soft mb-4">{description}</p>}
      <label htmlFor="order-reason" className="text-[13px] font-medium text-[#4A4038]">
        Причина (необязательно)
      </label>
      <textarea
        id="order-reason"
        rows={3}
        maxLength={300}
        value={reason}
        onChange={(e) => setReason(e.target.value)}
        className="mt-1.5 w-full rounded-xl border border-line px-4 py-3 text-sm resize-none bg-white text-ink outline-none focus:border-gold focus:ring-[3px] focus:ring-cream-deep"
        placeholder="Покупатель увидит её на странице заказа"
      />
      <p className="text-[11px] text-muted text-right mt-1">{reason.length}/300</p>
      <div className="flex gap-3 mt-4">
        <Button variant="secondary" className="flex-1" onClick={onClose} disabled={busy}>
          Назад
        </Button>
        <Button variant="danger" className="flex-1" loading={busy} onClick={() => onConfirm(reason.trim() || undefined)}>
          {confirmLabel}
        </Button>
      </div>
    </Modal>
  )
}

export function ConfirmModal({ title, text, confirmLabel, busy, onConfirm, onClose }: { title: string; text: string; confirmLabel: string; busy?: boolean; onConfirm: () => void; onClose: () => void }) {
  return (
    <Modal title={title} onClose={onClose} dismissible={!busy}>
      <p className="text-sm text-ink-soft">{text}</p>
      <div className="flex gap-3 mt-5">
        <Button variant="secondary" className="flex-1" onClick={onClose} disabled={busy}>
          Назад
        </Button>
        <Button className="flex-1" loading={busy} onClick={onConfirm}>
          {confirmLabel}
        </Button>
      </div>
    </Modal>
  )
}
