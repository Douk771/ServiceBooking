import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { InlineError } from '@/components/ui/InlineError'
import { formatRub } from '@/utils/money'
import type { ServiceItemDto, StaysServiceConflictDto } from '@/types/slots'
import { restrictedItemPrompt, type RestrictedItemPrompt } from '@/utils/slots/ownerWarnings'
import { getStayErrorMessage, readConflict } from '@/utils/slots/slotError'
import { moveId } from '@/utils/slots/serviceForms'
import { ConfirmDialog } from '@/components/slots/services/ConfirmDialog'
import { ContentWarnings } from '@/components/slots/ui/ContentWarnings'
import { SlotNotice } from '@/components/slots/ui/SlotNotice'
import { SectionCard, SwitchRow } from '@/components/slots/ui/formParts'
import { ErrorState, Skeleton } from '@/components/slots/ui/StatePanels'
import { useServiceTab } from '@/components/slots/services/cabinet/serviceContext'
import { useSlotVertical } from '@/components/slots/SlotVerticalContext'

interface Draft {
  id: string | null
  name: string
  priceRub: string
  maxPerSession: string
  isActive: boolean
}
const EMPTY: Draft = { id: null, name: '', priceRub: '0', maxPerSession: '1', isActive: true }

/** «Позиции»: веники, чай, простыни — what a guest can add to a session. At the guest they start at 0 (nothing is added for them). */
export function ServiceItemsTab() {
  const { api, legal } = useSlotVertical()
  const staysServicesApi = api.cabinet
  const { companyId, service, canManage } = useServiceTab()
  const qc = useQueryClient()
  const key = ['stays-service-items', companyId, service.id]
  const query = useQuery({ queryKey: key, queryFn: () => staysServicesApi.items(companyId, service.id), staleTime: 0 })
  const [draft, setDraft] = useState<Draft | null>(null)
  const [error, setError] = useState('')
  const [deleting, setDeleting] = useState<ServiceItemDto | null>(null)
  /** The position looks like alcohol or tobacco: the server wants the owner's word before it saves (Т42-05). */
  const [restricted, setRestricted] = useState<(RestrictedItemPrompt & { draft: Draft }) | null>(null)

  const refresh = () => {
    void qc.invalidateQueries({ queryKey: key })
    setDraft(null)
    setError('')
  }
  const save = useMutation({
    mutationFn: ({ draft: d, confirmRestricted }: { draft: Draft; confirmRestricted?: boolean }) => {
      const body = { name: d.name.trim(), priceRub: Number(d.priceRub), maxPerSession: Number(d.maxPerSession), isActive: d.isActive, ...(confirmRestricted ? { confirmRestricted: true } : {}) }
      return d.id ? staysServicesApi.updateItem(companyId, service.id, d.id, body) : staysServicesApi.addItem(companyId, service.id, body)
    },
    onSuccess: () => {
      setRestricted(null)
      refresh()
    },
    onError: (err, vars) => {
      const conflict = readConflict<StaysServiceConflictDto>(err)
      const prompt = restrictedItemPrompt(conflict)
      if (prompt) {
        setRestricted({ ...prompt, draft: vars.draft })
        return
      }
      setRestricted(null)
      setError(conflict?.message ?? getStayErrorMessage(err, 'Не удалось сохранить позицию.'))
    },
  })
  const remove = useMutation({
    mutationFn: (id: string) => staysServicesApi.deleteItem(companyId, service.id, id),
    onSuccess: () => {
      setDeleting(null)
      refresh()
    },
    onError: (err) => {
      setDeleting(null)
      setError(getStayErrorMessage(err, 'Не удалось удалить позицию.'))
    },
  })
  const reorder = useMutation({
    mutationFn: (ids: string[]) => staysServicesApi.reorderItems(companyId, service.id, ids),
    onSuccess: (list) => qc.setQueryData(key, list),
    onError: (err) => setError(getStayErrorMessage(err, 'Не удалось изменить порядок.')),
  })

  if (query.isLoading) return <Skeleton className="h-48" />
  if (query.isError || !query.data) return <ErrorState message={getStayErrorMessage(query.error, 'Не удалось загрузить позиции.')} onRetry={() => void query.refetch()} />
  const items = [...query.data].sort((a, b) => a.position - b.position)

  return (
    <SectionCard title="Позиции" description="Дополнения к сеансу. Гость выбирает их сам: по умолчанию ничего не добавлено, бесплатные позиции — тоже.">
      {legal.keys.positionsOwnerNotice && <SlotNotice textKey={legal.keys.positionsOwnerNotice} />}
      {items.length === 0 ? (
        <p className="text-sm text-ink-soft">Позиций пока нет. Услугу можно публиковать и без них.</p>
      ) : (
        <ul className="flex flex-col gap-2" aria-label="Позиции услуги">
          {items.map((it, i) => (
            <li key={it.id} className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-line px-4 py-3">
              <p className="text-sm text-ink">
                <span className="font-medium">{it.name}</span> — {it.priceRub === 0 ? 'бесплатно' : formatRub(it.priceRub)}, не больше {it.maxPerSession}
                {!it.isActive && <span className="ml-2 rounded-full bg-cream-deep px-2 py-0.5 text-[11px] font-medium text-ink-soft">скрыта</span>}
              </p>
              <ContentWarnings codes={it.warnings} className="basis-full" />
              {canManage && (
                <div className="flex flex-wrap gap-2">
                  <Button type="button" variant="ghost" size="sm" className="min-h-[44px]" disabled={i === 0 || reorder.isPending} aria-label={`Поднять «${it.name}»`} onClick={() => reorder.mutate(moveId(items.map((x) => x.id), it.id, -1))}>
                    Выше
                  </Button>
                  <Button type="button" variant="ghost" size="sm" className="min-h-[44px]" disabled={i === items.length - 1 || reorder.isPending} aria-label={`Опустить «${it.name}»`} onClick={() => reorder.mutate(moveId(items.map((x) => x.id), it.id, 1))}>
                    Ниже
                  </Button>
                  <Button type="button" variant="secondary" size="sm" className="min-h-[44px]" onClick={() => setDraft({ id: it.id, name: it.name, priceRub: String(it.priceRub), maxPerSession: String(it.maxPerSession), isActive: it.isActive })}>
                    Изменить
                  </Button>
                  <Button type="button" variant="danger" size="sm" className="min-h-[44px]" onClick={() => setDeleting(it)}>
                    Удалить
                  </Button>
                </div>
              )}
            </li>
          ))}
        </ul>
      )}

      {canManage && !draft && (
        <Button type="button" variant="secondary" className="min-h-[44px] self-start" onClick={() => setDraft({ ...EMPTY })}>
          Добавить позицию
        </Button>
      )}
      {canManage && draft && (
        <form
          noValidate
          className="flex flex-col gap-4 rounded-2xl border border-line-strong bg-cream/40 p-4"
          aria-label={draft.id ? 'Изменить позицию' : 'Новая позиция'}
          onSubmit={(e) => {
            e.preventDefault()
            setError('')
            save.mutate({ draft })
          }}
        >
          <Input label="Название" maxLength={100} value={draft.name} onChange={(e) => setDraft({ ...draft, name: e.target.value })} />
          <div className="grid gap-3 sm:grid-cols-2">
            <Input label="Цена, ₽" type="number" inputMode="numeric" min={0} max={100000} value={draft.priceRub} onChange={(e) => setDraft({ ...draft, priceRub: e.target.value })} />
            <Input label="Не больше на сеанс" type="number" inputMode="numeric" min={1} max={50} value={draft.maxPerSession} onChange={(e) => setDraft({ ...draft, maxPerSession: e.target.value })} />
          </div>
          <SwitchRow label="Показывать гостям" checked={draft.isActive} onChange={(v) => setDraft({ ...draft, isActive: v })} />
          {error && <InlineError>{error}</InlineError>}
          <div className="flex gap-3">
            <Button type="submit" loading={save.isPending} className="min-h-[44px]">
              Сохранить позицию
            </Button>
            <Button type="button" variant="secondary" className="min-h-[44px]" onClick={() => { setDraft(null); setError('') }}>
              Отмена
            </Button>
          </div>
        </form>
      )}
      {!draft && error && <InlineError>{error}</InlineError>}

      {restricted && (
        <ConfirmDialog
          title="Это не алкоголь и не табак?"
          text={[restricted.markers.length > 0 ? `Совпало: ${restricted.markers.join(', ')}.` : '', restricted.text].filter(Boolean).join(' ')}
          confirmLabel="Это не алкоголь и не табак"
          pending={save.isPending}
          onConfirm={() => save.mutate({ draft: restricted.draft, confirmRestricted: true })}
          onClose={() => setRestricted(null)}
        />
      )}

      {deleting && (
        <ConfirmDialog
          title="Удалить позицию?"
          text={`«${deleting.name}» исчезнет из выбора. В уже оформленных сеансах она сохранится.`}
          confirmLabel="Удалить"
          pending={remove.isPending}
          onConfirm={() => remove.mutate(deleting.id)}
          onClose={() => setDeleting(null)}
        />
      )}
    </SectionCard>
  )
}
