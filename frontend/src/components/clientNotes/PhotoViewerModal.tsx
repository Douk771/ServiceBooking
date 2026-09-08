import { useState } from 'react'
import { useOverlayDismiss } from '../../hooks/useOverlayDismiss'
import { AuthedImage } from '../ui/AuthedImage'
import { Button } from '../ui/Button'
import { Icon } from '../ui/Icon'
import type { ClientNotePhoto } from '../../api/masters'

interface PhotoViewerModalProps {
  photos: ClientNotePhoto[]
  startIndex: number
  alt: string
  bookingDate?: string | null
  bookingServiceName?: string | null
  onDelete?: (photoId: string) => void
  deletingPhotoId?: string | null
  onClose: () => void
}

/**
 * Full-size viewer for one note's photos, with navigation between them. Closes on Esc / backdrop click
 * via `useOverlayDismiss` (US-18 п. 5).
 */
export function PhotoViewerModal({
  photos,
  startIndex,
  alt,
  bookingDate,
  bookingServiceName,
  onDelete,
  deletingPhotoId,
  onClose,
}: PhotoViewerModalProps) {
  const [index, setIndex] = useState(startIndex)
  const dismiss = useOverlayDismiss(onClose)
  const photo = photos[index]
  if (!photo) return null

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-ink/80 backdrop-blur-sm px-4" {...dismiss}>
      <div className="relative w-full max-w-2xl">
        <button
          onClick={onClose}
          aria-label="Закрыть просмотр фото"
          className="absolute -top-11 right-0 text-cream hover:text-gold transition-colors"
        >
          <Icon name="x" size={22} strokeWidth={1.8} />
        </button>

        <div className="bg-cream rounded-2xl overflow-hidden">
          <div className="relative bg-ink/5">
            {photos.length > 1 && (
              <button
                onClick={() => setIndex((i) => (i - 1 + photos.length) % photos.length)}
                aria-label="Предыдущее фото"
                className="absolute left-2 top-1/2 -translate-y-1/2 w-9 h-9 rounded-full bg-white/90 flex items-center justify-center text-ink hover:bg-white transition-colors z-10"
              >
                <Icon name="chevron-left" size={18} strokeWidth={1.8} />
              </button>
            )}
            <AuthedImage photoId={photo.id} variant="full" alt={alt} fit="contain" className="h-[65vh] w-full" />
            {photos.length > 1 && (
              <button
                onClick={() => setIndex((i) => (i + 1) % photos.length)}
                aria-label="Следующее фото"
                className="absolute right-2 top-1/2 -translate-y-1/2 w-9 h-9 rounded-full bg-white/90 flex items-center justify-center text-ink hover:bg-white transition-colors z-10"
              >
                <Icon name="chevron-right" size={18} strokeWidth={1.8} />
              </button>
            )}
          </div>
          <div className="p-4 flex items-center justify-between gap-3 flex-wrap">
            <div className="text-sm text-ink-soft">
              {bookingDate && (
                <p>
                  {bookingServiceName ? `${bookingServiceName} · ` : ''}
                  {bookingDate}
                </p>
              )}
              <p className="text-xs text-muted mt-0.5">
                Загрузил(а): {photo.uploadedByName ?? 'аккаунт удалён'}
                {photos.length > 1 && ` · ${index + 1} из ${photos.length}`}
              </p>
            </div>
            {photo.canDelete && onDelete && (
              <Button
                variant="danger"
                size="sm"
                loading={deletingPhotoId === photo.id}
                onClick={() => onDelete(photo.id)}
              >
                <Icon name="trash" size={13} strokeWidth={1.8} /> Удалить фото
              </Button>
            )}
          </div>
        </div>
      </div>
    </div>
  )
}
