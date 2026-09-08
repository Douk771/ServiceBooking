import { useEffect, useRef, useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { clientNotesApi } from '../../api/clientNotes'
import { getUploadErrorMessage } from '../../utils/uploadError'
import { Button } from '../ui/Button'
import { Icon } from '../ui/Icon'

const MAX_PHOTOS = 5

interface NotePhotoUploaderProps {
  /**
   * When present, files are uploaded immediately to this existing note (US-17 п. 2 — attaching to an
   * already-created note). When absent, selected files are staged via `value`/`onChange` for the
   * caller to upload right after the note itself is created (US-17 п. 1).
   */
  noteId?: string
  /** How many more photos the note can take (5 minus however many it already has). */
  remainingSlots: number
  value?: File[]
  onChange?: (files: File[]) => void
  onUploaded?: () => void
  compact?: boolean
}

/**
 * Up to 5 photos per note (Q7), from the camera OR the gallery (US-17 п. 1) — that needs **two**
 * separate `<input type="file">` elements, not one. A single input with both `capture` and `multiple`
 * doesn't give a real choice on mobile: on Android Chrome, `capture` present at all opens the camera
 * directly and `multiple` is then ignored, so the gallery becomes unreachable through that input. The
 * gallery input has no `capture` attribute (and allows `multiple`); the camera input has `capture` and
 * takes one photo at a time, matching how the native camera UI hands files back.
 */
export function NotePhotoUploader({
  noteId,
  remainingSlots,
  value,
  onChange,
  onUploaded,
  compact,
}: NotePhotoUploaderProps) {
  const galleryInputRef = useRef<HTMLInputElement>(null)
  const cameraInputRef = useRef<HTMLInputElement>(null)
  const [error, setError] = useState('')
  const [uploading, setUploading] = useState(false)
  const [previewUrls, setPreviewUrls] = useState<string[]>([])

  useEffect(() => {
    const urls = (value ?? []).map((f) => URL.createObjectURL(f))
    setPreviewUrls(urls)
    return () => urls.forEach((u) => URL.revokeObjectURL(u))
  }, [value])

  const uploadMut = useMutation({
    mutationFn: (file: File) => clientNotesApi.uploadPhoto(noteId!, file),
  })

  const disabled = remainingSlots <= 0

  const handleFiles = async (files: FileList | null) => {
    if (!files || files.length === 0) return
    setError('')
    const selected = Array.from(files).slice(0, Math.max(0, remainingSlots))
    if (files.length > selected.length) {
      setError(`К одной заметке можно приложить не больше ${MAX_PHOTOS} фото.`)
    }
    if (selected.length === 0) return

    if (noteId) {
      setUploading(true)
      for (const file of selected) {
        try {
          await uploadMut.mutateAsync(file)
          onUploaded?.()
        } catch (e) {
          setError(getUploadErrorMessage(e))
          break
        }
      }
      setUploading(false)
    } else {
      onChange?.([...(value ?? []), ...selected].slice(0, MAX_PHOTOS))
    }
  }

  return (
    <div className="flex flex-col gap-2">
      {previewUrls.length > 0 && (
        <div className="flex flex-wrap gap-2">
          {previewUrls.map((url, i) => (
            <div key={url} className="relative w-14 h-14 rounded-lg overflow-hidden border border-line shrink-0">
              <img src={url} alt={`Фото для загрузки ${i + 1}`} className="w-full h-full object-cover" />
              <button
                type="button"
                onClick={() => onChange?.((value ?? []).filter((_, idx) => idx !== i))}
                aria-label="Убрать фото из списка на загрузку"
                className="absolute top-0.5 right-0.5 w-4.5 h-4.5 rounded-full bg-ink/70 text-cream flex items-center justify-center"
              >
                <Icon name="x" size={10} strokeWidth={2.2} />
              </button>
            </div>
          ))}
        </div>
      )}

      <div className="flex flex-wrap items-center gap-2">
        <label className="sr-only" htmlFor={`note-photo-gallery-${noteId ?? 'new'}`}>
          Выбрать фото из галереи
        </label>
        <input
          id={`note-photo-gallery-${noteId ?? 'new'}`}
          ref={galleryInputRef}
          type="file"
          accept="image/*"
          multiple
          className="hidden"
          onChange={(e) => {
            handleFiles(e.target.files)
            e.target.value = ''
          }}
        />
        <label className="sr-only" htmlFor={`note-photo-camera-${noteId ?? 'new'}`}>
          Сделать фото камерой
        </label>
        <input
          id={`note-photo-camera-${noteId ?? 'new'}`}
          ref={cameraInputRef}
          type="file"
          accept="image/*"
          capture="environment"
          className="hidden"
          onChange={(e) => {
            handleFiles(e.target.files)
            e.target.value = ''
          }}
        />
        <Button
          type="button"
          size="sm"
          variant="secondary"
          disabled={disabled}
          loading={uploading}
          onClick={() => galleryInputRef.current?.click()}
        >
          <Icon name="image" size={13} strokeWidth={1.8} />
          {compact ? 'Фото' : 'Из галереи'}
          {!disabled && !compact && ` (до ${remainingSlots})`}
        </Button>
        <Button
          type="button"
          size="sm"
          variant="secondary"
          disabled={disabled}
          loading={uploading}
          onClick={() => cameraInputRef.current?.click()}
        >
          <Icon name="image" size={13} strokeWidth={1.8} />
          Камера
        </Button>
      </div>
      {error && <p className="text-xs text-danger">{error}</p>}
    </div>
  )
}
