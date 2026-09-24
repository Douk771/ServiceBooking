import { useEffect, useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { format, parseISO } from 'date-fns'
import { ru } from 'date-fns/locale'
import { mastersApi, type MasterClient } from '../api/masters'
import { Card } from '../components/ui/Card'
import { Button } from '../components/ui/Button'
import { Icon } from '../components/ui/Icon'
import { Pagination } from '../components/ui/Pagination'
import { NoteCard } from '../components/clientNotes/NoteCard'
import { PhoneVerifiedBadge } from '../components/phoneVerification/PhoneVerifiedBadge'
import { NotePhotoUploader } from '../components/clientNotes/NotePhotoUploader'
import { HealthNoteCard } from '../components/clientNotes/HealthNoteCard'
import { PhotoConsentBadge } from '../components/clientNotes/PhotoConsentBadge'
import { formatPhone } from '../utils/phone'
import { getClientKey } from '../utils/clientKey'
import { useDebouncedValue } from '../hooks/useDebouncedValue'
import { useLegalText } from '../hooks/useLegalText'
import { usePhotoUploadWithConsent } from '../hooks/usePhotoUploadWithConsent'
import { findSection, splitLegalSections } from '../utils/legalSections'
import { useAuthStore } from '../store/authStore'

const STATUS_LABELS: Record<string, string> = {
  Pending: 'Ожидает',
  Confirmed: 'Подтверждена',
  Completed: 'Выполнена',
  Cancelled: 'Отменена',
  NoShow: 'Не пришёл',
}

const STATUS_CLASSES: Record<string, string> = {
  Completed: 'bg-success-bg text-success',
  Cancelled: 'bg-danger-bg text-danger',
  NoShow: 'bg-danger-bg text-danger',
  Pending: 'bg-warning-bg text-warning',
  Confirmed: 'bg-info-bg text-info',
}

interface ClientCardProps {
  client: MasterClient
  companyId: string
}

function ClientCard({ client, companyId }: ClientCardProps) {
  const [expanded, setExpanded] = useState(false)
  const [newNote, setNewNote] = useState('')
  const [pendingPhotos, setPendingPhotos] = useState<File[]>([])
  const qc = useQueryClient()
  const hasRole = useAuthStore((s) => s.hasRole)
  const clientKey = getClientKey(client)
  // US-77 п. 4 — SuperAdmin gets no block at all, not an empty/403 one (checked at §53 checklist).
  const showHealthNote = !hasRole('SuperAdmin')

  const invalidate = () => qc.invalidateQueries({ queryKey: ['master-clients', companyId] })

  // §45.5, US-77 п. 2 — the warning next to the free-text note reuses D11's own "reminder to staff"
  // section, so the wording lives in one place (legal/11-health-data-consent.html) instead of being
  // duplicated here by hand.
  const { data: healthConsentText } = useLegalText('HealthDataConsent')
  const noteWarning = healthConsentText
    ? findSection(splitLegalSections(healthConsentText.contentHtml), 'Напоминание сотруднику')
    : null

  // Same reactive consent gate NotePhotoUploader uses for "attach to an existing note" — reused here
  // rather than duplicated, because the only actual difference is that the note (and so its id) is
  // created a moment earlier, inside this very mutation, instead of already existing (T5-F6 follow-up:
  // "add a note with photos already selected" surfaced a generic error instead of the consent form).
  const photoUpload = usePhotoUploadWithConsent({ companyId, clientKey, onUploaded: invalidate })

  const addNoteMut = useMutation({
    mutationFn: async () => {
      const created = await mastersApi.addNote({
        companyId,
        clientId: client.clientId ?? undefined,
        guestPhone: client.guestPhone ?? undefined,
        note: newNote,
      })
      // The files themselves now live inside `photoUpload`'s own state for the rest of this batch, so
      // clearing `pendingPhotos` right after this (in onSuccess, unconditionally) does not lose them —
      // see usePhotoUploadWithConsent's doc comment.
      await photoUpload.uploadSequentially(created.id, pendingPhotos)
      return created
    },
    onSuccess: () => {
      setNewNote('')
      setPendingPhotos([])
      invalidate()
    },
  })

  const lastVisit = (() => {
    try {
      return format(parseISO(client.lastVisitDate), 'd MMM yyyy', { locale: ru })
    } catch {
      return client.lastVisitDate
    }
  })()

  return (
    <Card className="p-4">
      <button
        onClick={() => setExpanded((v) => !v)}
        className="w-full text-left flex items-center justify-between gap-4"
      >
        <div className="flex items-center gap-3">
          <div className="w-10 h-10 rounded-full bg-cream-deep flex items-center justify-center text-gold-dark font-bold text-sm shrink-0">
            {client.name[0]?.toUpperCase() ?? '?'}
          </div>
          <div>
            <p className="font-semibold text-ink">{client.name}</p>
            <p className="text-xs text-muted">
              Последний визит: {lastVisit} · {client.totalVisits}{' '}
              {client.totalVisits === 1 ? 'визит' : client.totalVisits < 5 ? 'визита' : 'визитов'}
            </p>
          </div>
        </div>
        <div className="text-right shrink-0">
          {client.phone && (
            <a
              href={`tel:+${client.phone}`}
              onClick={(e) => e.stopPropagation()}
              className="text-sm text-gold hover:text-gold-dark"
            >
              {formatPhone(client.phone)}
            </a>
          )}
          {client.email && <p className="text-xs text-muted">{client.email}</p>}
          {/* API_CONTRACT_CYCLE12.md §170.2 (R15) — `true` only, positive signal only. `false` (has an
              account, just not verified) and `null` (guest, no account — "не применимо") both render
              nothing at all: no colour, no icon, no "not verified" wording. */}
          {client.phoneVerified === true && <PhoneVerifiedBadge label="Телефон подтверждён" className="mt-1" />}
        </div>
        <Icon
          name="chevron-down"
          size={16}
          strokeWidth={1.8}
          className={`text-muted shrink-0 transition-transform ${expanded ? 'rotate-180' : ''}`}
        />
      </button>

      {expanded && (
        <div className="mt-4 border-t border-line pt-4 flex flex-col gap-4">
          {/* Visit history */}
          <div>
            <p className="text-sm font-semibold text-ink mb-2">История визитов</p>
            {(client.bookingSummaries ?? []).length === 0 ? (
              <p className="text-sm text-muted">Нет записей</p>
            ) : (
              <div className="flex flex-col gap-1.5">
                {(client.bookingSummaries ?? []).map((b, i) => (
                  <div key={i} className="flex items-center gap-3 text-sm">
                    <span className="text-muted shrink-0">{b.date}</span>
                    <span className="text-ink flex-1">{b.serviceName}</span>
                    <span
                      className={`text-xs px-2.5 py-0.5 rounded-full shrink-0 font-medium ${STATUS_CLASSES[b.status] ?? 'bg-cream-deep text-ink-soft'}`}
                    >
                      {STATUS_LABELS[b.status] ?? b.status}
                    </span>
                  </div>
                ))}
              </div>
            )}
          </div>

          {/* Contraindications — US-77, visually separate from the free-text note, own component */}
          {showHealthNote && (
            <div>
              <HealthNoteCard companyId={companyId} clientKey={clientKey} />
            </div>
          )}

          {/* Notes */}
          <div>
            <div className="flex items-center justify-between gap-2 mb-2">
              <p className="text-sm font-semibold text-ink">Заметки</p>
              <PhotoConsentBadge companyId={companyId} clientKey={clientKey} />
            </div>
            {noteWarning && (
              <p className="text-xs text-muted mb-2">{noteWarning.html.replace(/<[^>]+>/g, '')}</p>
            )}
            {client.notes.length === 0 ? (
              <p className="text-sm text-muted mb-2">Нет заметок</p>
            ) : (
              <ul className="flex flex-col gap-1.5 mb-2">
                {client.notes.map((n) => (
                  <NoteCard key={n.id} note={n} companyId={companyId} clientKey={clientKey} />
                ))}
              </ul>
            )}
            <div className="flex flex-col gap-2 mt-2">
              <label className="sr-only" htmlFor={`new-note-${client.clientId ?? client.guestPhone}`}>
                Добавить заметку
              </label>
              <div className="flex gap-2">
                <input
                  id={`new-note-${client.clientId ?? client.guestPhone}`}
                  type="text"
                  value={newNote}
                  onChange={(e) => setNewNote(e.target.value)}
                  placeholder="Добавить заметку…"
                  className="flex-1 rounded-xl border border-line px-3.5 py-2.5 text-sm outline-none focus:border-gold focus:ring-[3px] focus:ring-cream-deep bg-white text-ink"
                  onKeyDown={(e) => {
                    if (e.key === 'Enter' && newNote.trim()) addNoteMut.mutate()
                  }}
                />
                <Button
                  size="sm"
                  onClick={() => addNoteMut.mutate()}
                  disabled={!newNote.trim()}
                  loading={addNoteMut.isPending}
                >
                  Добавить
                </Button>
              </div>
              <NotePhotoUploader
                remainingSlots={5 - pendingPhotos.length}
                value={pendingPhotos}
                onChange={setPendingPhotos}
                compact
              />
              {photoUpload.error && <p className="text-xs text-danger">{photoUpload.error}</p>}
              {photoUpload.consentModal}
            </div>
          </div>
        </div>
      )}
    </Card>
  )
}

interface Props {
  companyId: string
}

export function MasterClientsPage({ companyId }: Props) {
  const [search, setSearch] = useState('')
  const debouncedSearch = useDebouncedValue(search, 400)
  const [page, setPage] = useState(1)
  const pageSize = 20

  // Reset to page 1 whenever the (debounced) search term changes — a stale page number from a
  // previous search could otherwise land past `hasNext` and render as an empty result.
  useEffect(() => {
    setPage(1)
  }, [debouncedSearch])

  const { data, isLoading, isError } = useQuery({
    queryKey: ['master-clients', companyId, page, debouncedSearch],
    queryFn: () => mastersApi.getClients(companyId, page, pageSize, debouncedSearch),
    enabled: !!companyId,
  })
  const clients = data?.items ?? []

  if (isLoading) {
    return (
      <div className="flex flex-col gap-3">
        {Array.from({ length: 3 }).map((_, i) => (
          <div key={i} className="h-20 bg-cream-deep rounded-2xl animate-pulse" />
        ))}
      </div>
    )
  }

  if (isError) {
    return (
      <Card className="p-12 text-center text-muted">
        <Icon name="alert-circle" size={32} strokeWidth={1.4} className="mx-auto mb-2" />
        <p>Не удалось загрузить клиентов</p>
      </Card>
    )
  }

  return (
    <div>
      <div className="flex items-center justify-between mb-5">
        <h2 className="text-lg font-semibold text-ink">Мои клиенты</h2>
        <span className="text-sm text-muted">{data?.total ?? clients.length} клиентов</span>
      </div>

      <div className="mb-4 relative">
        <Icon
          name="search"
          size={16}
          strokeWidth={1.8}
          className="absolute left-3.5 top-1/2 -translate-y-1/2 text-muted"
        />
        <input
          type="text"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Поиск по имени или телефону…"
          className="w-full rounded-full border border-line pl-10 pr-4 py-2.5 text-sm outline-none focus:border-gold focus:ring-[3px] focus:ring-cream-deep bg-white text-ink"
        />
      </div>

      {clients.length === 0 ? (
        <Card className="p-12 text-center text-muted">
          <Icon name="users" size={32} strokeWidth={1.4} className="mx-auto mb-3" />
          <p className="text-lg font-medium text-ink-soft">
            {debouncedSearch ? 'Клиентов не найдено' : 'У вас пока нет клиентов'}
          </p>
          {!debouncedSearch && <p className="text-sm mt-1">Здесь появятся клиенты после первых записей</p>}
        </Card>
      ) : (
        <div className="flex flex-col gap-3">
          {clients.map((c) => (
            <ClientCard key={c.clientId ?? c.guestPhone ?? c.name} client={c} companyId={companyId} />
          ))}
        </div>
      )}

      {data && (
        <Pagination
          page={data.page}
          pageSize={data.pageSize}
          total={data.total}
          hasNext={data.hasNext}
          onPageChange={setPage}
        />
      )}
    </div>
  )
}
