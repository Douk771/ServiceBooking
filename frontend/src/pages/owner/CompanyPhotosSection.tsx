import { useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { companyPhotosApi } from '../../api/companyPhotos'
import { getUploadErrorMessage } from '../../utils/uploadError'
import { Card } from '../../components/ui/Card'
import { Button } from '../../components/ui/Button'
import { Icon } from '../../components/ui/Icon'

const MAX_PHOTOS = 10

/**
 * ARCHITECTURE_CYCLE10.md §109.2 (US-125). Deliberately its OWN card, separate from the logo block
 * above it in `CompanyManagePage.tsx` — the logo and the gallery are different things (a company
 * without a single photo can still have a logo). No consent screen/checkbox here (П7) — company
 * photos aren't personal data of an identifiable person the way client-note photos can be.
 */
export function CompanyPhotosSection({ companyId }: { companyId: string }) {
  const qc = useQueryClient()
  const fileInputRef = useRef<HTMLInputElement>(null)
  const [error, setError] = useState('')
  const [reordering, setReordering] = useState(false)
  const [dragOver, setDragOver] = useState(false)

  const { data: photos, isLoading } = useQuery({
    queryKey: ['company-photos', companyId],
    queryFn: () => companyPhotosApi.list(companyId),
  })

  const invalidate = () => {
    qc.invalidateQueries({ queryKey: ['company-photos', companyId] })
    // Review finding — `CompanyPage.tsx` reads `photos`/`coverPhotoUrl` off `['company', slug]`
    // (§109.3, `CompanyDto`), a DIFFERENT cache entry from this section's own `company-photos`
    // list. Without this the public company card kept showing the gallery as it was before the
    // edit until a full page reload. There's no `slug` in scope here, so match by prefix — a plain
    // `['company']` key matches every `['company', slug]` entry (react-query's default).
    qc.invalidateQueries({ queryKey: ['company'] })
  }

  const uploadMut = useMutation({
    mutationFn: (file: File) => companyPhotosApi.upload(companyId, file),
    onMutate: () => setError(''),
    onSuccess: invalidate,
    onError: (err) => setError(getUploadErrorMessage(err)),
  })

  const removeMut = useMutation({
    mutationFn: (photoId: string) => companyPhotosApi.remove(companyId, photoId),
    onMutate: () => setError(''),
    onSuccess: invalidate,
    onError: (err) => setError(getUploadErrorMessage(err)),
  })

  const reorderMut = useMutation({
    mutationFn: (photoIds: string[]) => companyPhotosApi.reorder(companyId, photoIds),
    onMutate: () => {
      setError('')
      setReordering(true)
    },
    onSettled: () => setReordering(false),
    onSuccess: invalidate,
    onError: (err) => setError(getUploadErrorMessage(err)),
  })

  const move = (index: number, direction: -1 | 1) => {
    if (!photos) return
    const target = index + direction
    if (target < 0 || target >= photos.length) return
    const ids = photos.map((p) => p.id)
    ;[ids[index], ids[target]] = [ids[target], ids[index]]
    reorderMut.mutate(ids)
  }

  const makeCover = (index: number) => {
    if (!photos || index === 0) return
    const ids = photos.map((p) => p.id)
    const [id] = ids.splice(index, 1)
    ids.unshift(id)
    reorderMut.mutate(ids)
  }

  const atLimit = (photos?.length ?? 0) >= MAX_PHOTOS
  const busy = uploadMut.isPending || removeMut.isPending || reordering

  return (
    <Card className="p-6">
      <div className="flex items-center justify-between mb-4">
        <h3 className="text-lg font-semibold text-ink">Фотографии салона</h3>
        <span className="text-xs text-muted">{photos?.length ?? 0} / {MAX_PHOTOS}</span>
      </div>

      {isLoading ? (
        <div className="grid grid-cols-3 gap-3">
          {[1, 2, 3].map((i) => (
            <div key={i} className="aspect-square bg-cream-deep rounded-xl animate-pulse" />
          ))}
        </div>
      ) : photos && photos.length > 0 ? (
        <div className="grid grid-cols-3 gap-3 mb-4">
          {photos.map((p, i) => (
            <div key={p.id} className="relative group">
              <img
                src={p.thumbnailUrl}
                alt=""
                className="w-full aspect-square object-cover rounded-xl border border-line"
              />
              {i === 0 && (
                <span className="absolute top-1.5 left-1.5 text-[10px] font-medium bg-ink text-cream px-2 py-0.5 rounded-full">
                  Обложка
                </span>
              )}
              <div className="absolute inset-x-0 bottom-0 flex items-center justify-center gap-1 p-1.5 bg-gradient-to-t from-ink/70 to-transparent rounded-b-xl opacity-0 group-hover:opacity-100 transition-opacity">
                <button
                  type="button"
                  aria-label="Переместить левее"
                  disabled={i === 0 || busy}
                  onClick={() => move(i, -1)}
                  className="w-6 h-6 rounded-full bg-white/90 flex items-center justify-center disabled:opacity-40"
                >
                  <Icon name="chevron-left" size={12} strokeWidth={2} />
                </button>
                {i !== 0 && (
                  <button
                    type="button"
                    disabled={busy}
                    onClick={() => makeCover(i)}
                    className="text-[9px] font-medium bg-white/90 rounded-full px-1.5 h-6 disabled:opacity-40"
                  >
                    Сделать обложкой
                  </button>
                )}
                <button
                  type="button"
                  aria-label="Переместить правее"
                  disabled={i === photos.length - 1 || busy}
                  onClick={() => move(i, 1)}
                  className="w-6 h-6 rounded-full bg-white/90 flex items-center justify-center disabled:opacity-40"
                >
                  <Icon name="chevron-right" size={12} strokeWidth={2} />
                </button>
                <button
                  type="button"
                  aria-label="Удалить фото"
                  disabled={busy}
                  onClick={() => removeMut.mutate(p.id)}
                  className="w-6 h-6 rounded-full bg-white/90 flex items-center justify-center text-danger disabled:opacity-40"
                >
                  <Icon name="x" size={12} strokeWidth={2} />
                </button>
              </div>
            </div>
          ))}
        </div>
      ) : (
        <div className="text-center py-8 mb-4 bg-cream-deep rounded-xl">
          <Icon name="store" size={22} strokeWidth={1.6} className="text-gold-dark mx-auto mb-2" />
          <p className="text-sm text-ink-soft">В галерее пока нет фотографий</p>
        </div>
      )}

      <input
        ref={fileInputRef}
        type="file"
        accept="image/jpeg,image/png,image/webp"
        className="hidden"
        onChange={(e) => {
          const f = e.target.files?.[0]
          if (f) uploadMut.mutate(f)
          e.target.value = ''
        }}
      />
      {/* Review finding — §109.2 asks for a drop zone, not just the file picker button. This is a
          plain <div> with no role/tabIndex/keyboard handler: drag-and-drop has no keyboard
          equivalent, so making the whole zone a fake "button" only doubled up on the real
          <Button> below it (a button nested inside a button, and a click handler that only worked
          because of stopPropagation). The <Button> stays the sole interactive element and the sole
          keyboard/click path to the file picker. */}
      <div
        onDragOver={(e) => {
          e.preventDefault()
          if (!atLimit) setDragOver(true)
        }}
        onDragLeave={() => setDragOver(false)}
        onDrop={(e) => {
          e.preventDefault()
          setDragOver(false)
          if (atLimit) return
          const f = e.dataTransfer.files?.[0]
          if (f) uploadMut.mutate(f)
        }}
        className={`rounded-xl border-2 border-dashed px-4 py-5 text-center transition-colors mb-1 ${
          atLimit
            ? 'border-line bg-cream-deep opacity-60'
            : dragOver
              ? 'border-gold bg-cream-deep'
              : 'border-line'
        }`}
      >
        <p className="text-sm text-ink-soft mb-2">
          {atLimit ? `Достигнут лимит в ${MAX_PHOTOS} фото` : 'Перетащите фото сюда или'}
        </p>
        {!atLimit && (
          <Button
            size="sm"
            variant="secondary"
            loading={uploadMut.isPending}
            onClick={() => fileInputRef.current?.click()}
          >
            Выбрать файл
          </Button>
        )}
      </div>
      <p className="text-xs text-muted mt-1">JPEG, PNG или WEBP, до 5 МБ, не больше {MAX_PHOTOS} фото</p>
      {error && <p className="text-xs text-danger mt-1">{error}</p>}
    </Card>
  )
}
