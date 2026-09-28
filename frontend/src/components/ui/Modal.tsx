import { useId, type ReactNode } from 'react'
import { useOverlayDismiss } from '../../hooks/useOverlayDismiss'
import { Icon } from './Icon'

interface Props {
  title: string
  onClose: () => void
  children: ReactNode
  /** false — Esc, a backdrop click and the X do nothing (e.g. while a submit is in flight). Default true. */
  dismissible?: boolean
}

const noop = () => {}

export function Modal({ title, onClose, children, dismissible = true }: Props) {
  const dismiss = useOverlayDismiss(dismissible ? onClose : noop)
  const titleId = useId()
  return (
    <div className="fixed inset-0 bg-ink/45 backdrop-blur-sm z-50 flex items-center justify-center p-4" {...dismiss}>
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        className="bg-cream rounded-3xl shadow-modal w-full max-w-lg max-h-[90vh] overflow-y-auto"
      >
        <div className="flex items-center justify-between p-6 border-b border-line">
          <h2 id={titleId} className="text-lg font-serif font-medium text-ink">
            {title}
          </h2>
          <button
            onClick={onClose}
            disabled={!dismissible}
            aria-label="Закрыть"
            className="text-muted hover:text-ink transition-colors disabled:opacity-40 disabled:pointer-events-none"
          >
            <Icon name="x" size={18} strokeWidth={1.8} />
          </button>
        </div>
        <div className="p-6">{children}</div>
      </div>
    </div>
  )
}
