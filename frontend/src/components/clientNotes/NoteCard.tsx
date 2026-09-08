import { useState } from 'react'
import { format, parseISO } from 'date-fns'
import { ru } from 'date-fns/locale'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { mastersApi, type ClientNote } from '../../api/masters'
import { clientNotesApi } from '../../api/clientNotes'
import { getUploadErrorMessage } from '../../utils/uploadError'
import { Button } from '../ui/Button'
import { Icon } from '../ui/Icon'
import { PhotoGallery } from './PhotoGallery'
import { NotePhotoUploader } from './NotePhotoUploader'

interface NoteCardProps {
  note: ClientNote
  companyId: string
}

/**
 * A single `ClientNote` — author, date, text, its photos, and (when `canDelete`) removal with an
 * inline confirmation. Shared between `MasterClientsPage` and `MyBookingsPage` (US-07 п. 3).
 */
export function NoteCard({ note, companyId }: NoteCardProps) {
  const qc = useQueryClient()
  const [confirmingDelete, setConfirmingDelete] = useState(false)
  const [photoError, setPhotoError] = useState('')
  const [deletingPhotoId, setDeletingPhotoId] = useState<string | null>(null)

  const invalidate = () => qc.invalidateQueries({ queryKey: ['master-clients', companyId] })

  const deleteNoteMut = useMutation({
    mutationFn: () => mastersApi.deleteNote(note.id),
    onSuccess: invalidate,
  })

  const deletePhotoMut = useMutation({
    mutationFn: (photoId: string) => clientNotesApi.deletePhoto(photoId),
    onMutate: (photoId: string) => {
      setDeletingPhotoId(photoId)
      setPhotoError('')
    },
    onSuccess: () => {
      setDeletingPhotoId(null)
      invalidate()
    },
    onError: (e: unknown) => {
      setDeletingPhotoId(null)
      setPhotoError(getUploadErrorMessage(e))
    },
  })

  const dateLabel = (() => {
    try {
      return format(parseISO(note.createdAt), 'd MMM yyyy, HH:mm', { locale: ru })
    } catch {
      return note.createdAt
    }
  })()

  return (
    <li className="text-sm text-ink-soft bg-cream-deep rounded-xl px-3 py-2.5">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="whitespace-pre-wrap break-words">{note.note}</p>
          <p className="text-xs text-muted mt-1">
            {note.authorName} · {dateLabel}
            {note.bookingServiceName && ` · ${note.bookingServiceName}`}
          </p>
        </div>
        {note.canDelete &&
          (confirmingDelete ? (
            <div className="flex items-center gap-1.5 shrink-0">
              <span className="text-xs text-danger">Удалить?</span>
              <Button
                size="sm"
                variant="danger"
                loading={deleteNoteMut.isPending}
                onClick={() => deleteNoteMut.mutate()}
              >
                Да
              </Button>
              <Button size="sm" variant="ghost" onClick={() => setConfirmingDelete(false)}>
                Нет
              </Button>
            </div>
          ) : (
            <button
              type="button"
              onClick={() => setConfirmingDelete(true)}
              aria-label="Удалить заметку"
              className="text-muted hover:text-danger transition-colors shrink-0 p-0.5"
            >
              <Icon name="trash" size={14} strokeWidth={1.8} />
            </button>
          ))}
      </div>

      <PhotoGallery note={note} onDeletePhoto={(id) => deletePhotoMut.mutate(id)} deletingPhotoId={deletingPhotoId} />

      {note.photos.length < 5 && (
        <div className="mt-2">
          <NotePhotoUploader noteId={note.id} remainingSlots={5 - note.photos.length} compact onUploaded={invalidate} />
        </div>
      )}
      {photoError && <p className="text-xs text-danger mt-1">{photoError}</p>}
      {deleteNoteMut.isError && (
        <p className="text-xs text-danger mt-1">Не удалось удалить заметку. Попробуйте снова.</p>
      )}
    </li>
  )
}
