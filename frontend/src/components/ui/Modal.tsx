import type { ReactNode } from 'react'
import { useOverlayDismiss } from '../../hooks/useOverlayDismiss'
import { Icon } from './Icon'

interface Props {
  title: string
  onClose: () => void
  children: ReactNode
}

export function Modal({ title, onClose, children }: Props) {
  const dismiss = useOverlayDismiss(onClose)
  return (
    <div className="fixed inset-0 bg-ink/45 backdrop-blur-sm z-50 flex items-center justify-center p-4" {...dismiss}>
      <div className="bg-cream rounded-3xl shadow-modal w-full max-w-lg max-h-[90vh] overflow-y-auto">
        <div className="flex items-center justify-between p-6 border-b border-line">
          <h2 className="text-lg font-serif font-medium text-ink">{title}</h2>
          <button onClick={onClose} className="text-muted hover:text-ink transition-colors">
            <Icon name="x" size={18} strokeWidth={1.8} />
          </button>
        </div>
        <div className="p-6">{children}</div>
      </div>
    </div>
  )
}
