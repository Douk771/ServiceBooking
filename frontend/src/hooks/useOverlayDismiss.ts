import { useRef, type MouseEvent } from 'react'

/**
 * Returns handlers for a modal backdrop that closes only on a genuine backdrop click —
 * i.e. when both the press and the release land on the overlay itself.
 *
 * This prevents an accidental close when a text selection drag that *starts inside* the
 * modal ends on the backdrop (e.g. selecting an input's contents right-to-left on a
 * trackpad). In that case the browser dispatches the `click` on the overlay, which a
 * naive `onClick={onClose}` would treat as a dismiss.
 *
 * Spread the result onto the overlay element:
 *   const dismiss = useOverlayDismiss(onClose)
 *   <div className="fixed inset-0 ..." {...dismiss}>...</div>
 *
 * Inner content does NOT need `stopPropagation` — clicks there have `target !== currentTarget`.
 */
export function useOverlayDismiss(onClose: () => void) {
  const pressStartedOnOverlay = useRef(false)

  return {
    onMouseDown: (e: MouseEvent<HTMLElement>) => {
      pressStartedOnOverlay.current = e.target === e.currentTarget
    },
    onClick: (e: MouseEvent<HTMLElement>) => {
      if (pressStartedOnOverlay.current && e.target === e.currentTarget) onClose()
      pressStartedOnOverlay.current = false
    },
  }
}
