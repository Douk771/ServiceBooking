import { useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { companyPhotosApi } from '../../api/companyPhotos'
import { getUploadErrorMessage } from '../../utils/uploadError'
import { Card } from '../../components/ui/Card'
import { Button } from '../../components/ui/Button'
import { Icon } from '../../components/ui/Icon'
import { Modal } from '../../components/ui/Modal'
import { useLegalText } from '../../hooks/useLegalText'
import { findSection, splitLegalSections } from '../../utils/legalSections'
import { useAuthStore } from '../../store/authStore'

const MAX_PHOTOS = 10

/**
 * Т20-07 п. 1 (D2 цикла 10, US-20-06) — the lawyer's uiText `CompanyPhotoPeopleNotice` shown right
 * above the picker/drop-zone, BEFORE a file is chosen (not after, not as a checkbox — no gate on the
 * upload itself, per ARCHITECTURE_CYCLE20.md §408: "Галочки нет, загрузку подсказка не блокирует").
 * Falls back to the whole document if the "Текст" section isn't found (§411), same rule as every
 * other `findSection` call site — never leave the screen silently empty while text is missing.
 */
function CompanyPhotoPeopleNotice({ id }: { id: string }) {
  const { data: text, isLoading } = useLegalText('CompanyPhotoPeopleNotice')
  if (isLoading || !text?.contentHtml) return null
  const section = findSection(splitLegalSections(text.contentHtml), 'Текст')
  return (
    <div id={id} className="text-xs text-ink-soft bg-cream-deep rounded-xl px-3 py-2.5 mb-3 flex items-start gap-2">
      <Icon name="alert-circle" size={13} strokeWidth={1.8} className="shrink-0 mt-0.5 text-gold-dark" />
      <div
        className="[&_p]:mb-1 [&_p:last-child]:mb-0 [&_a]:underline"
        dangerouslySetInnerHTML={{ __html: (section ?? { html: text.contentHtml }).html }}
      />
    </div>
  )
}

/**
 * Т20-07 п. 2 (§404.7) — SuperAdmin-only confirmation before removing a photo, so the reason (which
 * decides whether the owner gets a `PhotoRemoved` platform notice) is a deliberate choice, not a side
 * effect of the same click an owner would use. The owner's own delete button is untouched — no dialog,
 * no reason, exactly as before this cycle.
 */
function SuperAdminRemovePhotoDialog({
  loading,
  onConfirm,
  onClose,
}: {
  loading: boolean
  onConfirm: (reason?: 'DepictedPersonRequest') => void
  onClose: () => void
}) {
  const [reason, setReason] = useState<'DepictedPersonRequest' | 'other'>('other')
  return (
    <Modal title="Удалить фотографию" onClose={onClose}>
      <div className="flex flex-col gap-4">
        <p className="text-sm text-ink-soft">Причина удаления:</p>
        <div className="flex flex-col gap-2">
          <label className="flex items-start gap-2.5 cursor-pointer">
            <input
              type="radio"
              name="photo-removal-reason"
              checked={reason === 'DepictedPersonRequest'}
              onChange={() => setReason('DepictedPersonRequest')}
              className="mt-0.5 accent-gold"
            />
            <span className="text-sm text-ink">По обращению изображённого человека (владельцу придёт уведомление)</span>
          </label>
          <label className="flex items-start gap-2.5 cursor-pointer">
            <input
              type="radio"
              name="photo-removal-reason"
              checked={reason === 'other'}
              onChange={() => setReason('other')}
              className="mt-0.5 accent-gold"
            />
            <span className="text-sm text-ink">Другая причина</span>
          </label>
        </div>
        <div className="flex gap-3 pt-1">
          <Button variant="secondary" className="flex-1" onClick={onClose}>
            Отмена
          </Button>
          <Button
            variant="danger"
            className="flex-1"
            loading={loading}
            onClick={() => onConfirm(reason === 'DepictedPersonRequest' ? 'DepictedPersonRequest' : undefined)}
          >
            Удалить
          </Button>
        </div>
      </div>
    </Modal>
  )
}

/**
 * ARCHITECTURE_CYCLE10.md §109.2 (US-125). Deliberately its OWN card, separate from the logo block
 * above it in `CompanyManagePage.tsx` — the logo and the gallery are different things (a company
 * without a single photo can still have a logo). No consent screen/checkbox here (П7) — company
 * photos aren't personal data of an identifiable person the way client-note photos can be.
 */
export function CompanyPhotosSection({ companyId }: { companyId: string }) {
  const qc = useQueryClient()
  const isSuperAdmin = useAuthStore((s) => s.hasRole('SuperAdmin'))
  const fileInputRef = useRef<HTMLInputElement>(null)
  const [error, setError] = useState('')
  const [reordering, setReordering] = useState(false)
  const [dragOver, setDragOver] = useState(false)
  const [confirmingRemoval, setConfirmingRemoval] = useState<string | null>(null)

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
    mutationFn: ({ photoId, reason }: { photoId: string; reason?: 'DepictedPersonRequest' }) =>
      companyPhotosApi.remove(companyId, photoId, reason),
    onMutate: () => setError(''),
    onSuccess: () => {
      setConfirmingRemoval(null)
      invalidate()
    },
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
                  // Т20-07 п. 2 — SuperAdmin gets the reason dialog (deletion is unchanged for the
                  // owner, who never sees `reason` at all: the server ignores it for non-SuperAdmin
                  // callers, but not sending it keeps the owner's request byte-for-byte what it was).
                  onClick={() => (isSuperAdmin ? setConfirmingRemoval(p.id) : removeMut.mutate({ photoId: p.id }))}
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

      {/* Т20-07 п. 1 — shown BEFORE the picker/drop-zone below, unconditionally (not gated by
          `atLimit`): the point is to inform before a file is chosen, not to react to one. */}
      <CompanyPhotoPeopleNotice id="company-photo-people-notice" />

      <input
        ref={fileInputRef}
        type="file"
        accept="image/jpeg,image/png,image/webp"
        aria-describedby="company-photo-people-notice"
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

      {confirmingRemoval && (
        <SuperAdminRemovePhotoDialog
          loading={removeMut.isPending}
          onConfirm={(reason) => removeMut.mutate({ photoId: confirmingRemoval, reason })}
          onClose={() => setConfirmingRemoval(null)}
        />
      )}
    </Card>
  )
}
