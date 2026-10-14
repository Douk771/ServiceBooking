import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'

/** A small yes/no dialog of the cabinet (destructive actions are never one click). */
export function ConfirmDialog({
  title,
  text,
  confirmLabel,
  pending,
  onConfirm,
  onClose,
}: {
  title: string
  text: string
  confirmLabel: string
  pending: boolean
  onConfirm: () => void
  onClose: () => void
}) {
  return (
    <Modal title={title} onClose={onClose} dismissible={!pending}>
      <p className="text-sm text-ink-soft">{text}</p>
      <div className="mt-5 flex gap-3">
        <Button variant="secondary" className="min-h-[44px] flex-1" onClick={onClose} disabled={pending}>
          Отмена
        </Button>
        <Button variant="danger" className="min-h-[44px] flex-1" loading={pending} onClick={onConfirm}>
          {confirmLabel}
        </Button>
      </div>
    </Modal>
  )
}
