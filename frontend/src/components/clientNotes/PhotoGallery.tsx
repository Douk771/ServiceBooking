import { useState } from 'react'
import { format, parseISO } from 'date-fns'
import { ru } from 'date-fns/locale'
import type { ClientNote } from '../../api/masters'
import { AuthedImage } from '../ui/AuthedImage'
import { PhotoViewerModal } from './PhotoViewerModal'

/** Builds the accessible `alt` text, e.g. "Фото работы, 12 марта, стрижка" (US-18 п. 5). */
export function photoAlt(note: Pick<ClientNote, 'createdAt' | 'bookingDate' | 'bookingServiceName'>): string {
  const dateSource = note.bookingDate ?? note.createdAt
  let dateLabel = ''
  try {
    dateLabel = format(parseISO(dateSource), 'd MMMM', { locale: ru })
  } catch {
    dateLabel = ''
  }
  const parts = ['Фото работы']
  if (dateLabel) parts.push(dateLabel)
  if (note.bookingServiceName) parts.push(note.bookingServiceName.toLowerCase())
  return parts.join(', ')
}

interface PhotoGalleryProps {
  note: ClientNote
  onDeletePhoto?: (photoId: string) => void
  deletingPhotoId?: string | null
}

/** Lazily-loaded thumbnail strip for one note's photos; click opens `PhotoViewerModal` (US-18). */
export function PhotoGallery({ note, onDeletePhoto, deletingPhotoId }: PhotoGalleryProps) {
  const [openIndex, setOpenIndex] = useState<number | null>(null)
  const photos = note.photos ?? []

  if (photos.length === 0) return null

  const alt = photoAlt(note)

  return (
    <>
      <div className="flex flex-wrap gap-2 mt-2">
        {photos.map((photo, i) => (
          <button
            key={photo.id}
            type="button"
            onClick={() => setOpenIndex(i)}
            aria-label={`Открыть фото: ${alt}`}
            className="w-16 h-16 rounded-xl overflow-hidden border border-line shrink-0 focus:outline-none focus:ring-2 focus:ring-gold"
          >
            <AuthedImage photoId={photo.id} variant="thumb" alt={alt} className="w-full h-full" />
          </button>
        ))}
      </div>
      {openIndex !== null && (
        <PhotoViewerModal
          photos={photos}
          startIndex={openIndex}
          alt={alt}
          bookingDate={note.bookingDate}
          bookingServiceName={note.bookingServiceName}
          onDelete={onDeletePhoto}
          deletingPhotoId={deletingPhotoId}
          onClose={() => setOpenIndex(null)}
        />
      )}
    </>
  )
}
