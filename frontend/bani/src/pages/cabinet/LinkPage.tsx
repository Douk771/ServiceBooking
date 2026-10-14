import { useEffect, useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { Icon } from '@/components/ui/Icon'
import { InlineError } from '@/components/ui/InlineError'
import { Input } from '@/components/ui/Input'
import { Modal } from '@/components/ui/Modal'
import { SectionCard } from '@/components/slots/ui/formParts'
import { QrDialog } from '@/components/slots/ui/QrDialog'
import { CopyButton } from '@/components/slots/ui/CopyButton'
import { EmptyState, ErrorState, LoadingList } from '@/components/slots/ui/StatePanels'
import { useDebouncedValue } from '@/hooks/useDebouncedValue'
import { can } from '@/utils/slots/slotPermissions'
import { getStayErrorMessage, readConflict } from '@/utils/slots/slotError'
import { bathsCabinetApi } from '../../api/bathsCabinet'
import { bathsCompaniesApi } from '../../api/bathsCompanies'
import { useBathsCompany } from '../../cabinet/cabinetVertical'
import type { BathsConflictDto } from '../../cabinet/types'
import { bathsVertical } from '../../vertical'
import { normalizeSlugInput, slugLocalProblem } from '../../utils/baniSlug'

/**
 * `/cabinet/:companyId/link` (`ViewCabinet`) — the address and the QR code of the complex, the addresses of its resources, and (owner) the
 * change of the company address. A changed address does NOT redirect: old links and QR codes stop working — said before the change.
 */
export function LinkPage() {
  const { company, refresh } = useBathsCompany()
  const [qr, setQr] = useState<{ title: string; fileName: string; load: () => Promise<Blob> } | null>(null)
  const resources = useQuery({ queryKey: ['baths-link-resources', company.id], queryFn: () => bathsVertical.api.cabinet.list(company.id) })
  const manage = can(company.myPermissions, 'ManageCompany')
  const visible = (resources.data ?? []).filter((r) => !r.isArchived)

  return (
    <main className="mx-auto flex max-w-[760px] flex-col gap-6 px-4 pb-6 pt-8 sm:px-8">
      <SectionCard title="Страница комплекса" description="Эту ссылку и QR-код можно давать гостям и размещать в объявлениях и на входе.">
        <LinkRow url={company.publicUrl} label="ссылку на комплекс" onQr={() => setQr({ title: company.name, fileName: `${company.slug}.png`, load: () => bathsCabinetApi.qr(company.id) })} />
      </SectionCard>

      <SectionCard title="Бани" description="Ссылка на страницу конкретной бани. Гости откроют её, только если баня опубликована.">
        {resources.isLoading ? (
          <LoadingList rows={2} rowClass="h-16" />
        ) : resources.isError ? (
          <ErrorState message={getStayErrorMessage(resources.error, 'Не удалось загрузить список бань.')} onRetry={() => void resources.refetch()} />
        ) : visible.length === 0 ? (
          <EmptyState title="Бань пока нет" text="Добавьте баню на вкладке «Ресурсы» — здесь появится её ссылка." />
        ) : (
          <ul className="flex flex-col gap-3">
            {visible.map((r) => (
              <li key={r.id} className="rounded-2xl border border-line p-4">
                <p className="mb-2 text-sm font-semibold text-ink">
                  {r.name}
                  {!r.isPublished && <span className="ml-2 text-xs font-normal text-warning">не опубликована — гости ссылку не откроют</span>}
                </p>
                <LinkRow url={`${company.publicUrl}/${r.slug}`} label={`ссылку на баню «${r.name}»`} />
              </li>
            ))}
          </ul>
        )}
      </SectionCard>

      {manage && <SlugCard key={company.slug} companyId={company.id} slug={company.slug} onChanged={refresh} />}

      {qr && <QrDialog title={qr.title} fileName={qr.fileName} load={qr.load} onClose={() => setQr(null)} />}
    </main>
  )
}

function LinkRow({ url, label, onQr }: { url: string; label: string; onQr?: () => void }) {
  return (
    <div className="flex flex-wrap items-center gap-2">
      <a href={url} target="_blank" rel="noopener noreferrer" className="min-w-0 flex-1 truncate text-sm font-medium !text-ink underline decoration-line-strong underline-offset-2">
        {url}
        <span className="sr-only"> (откроется в новой вкладке)</span>
      </a>
      <CopyButton text={url} label={label} />
      {onQr && (
        <button
          type="button"
          onClick={onQr}
          className="inline-flex min-h-[44px] shrink-0 items-center gap-1.5 rounded-full border border-line bg-white px-4 text-xs font-semibold text-ink hover:border-line-strong"
          aria-label={`QR-код: ${label}`}
        >
          <Icon name="qr-code" size={14} strokeWidth={1.8} /> QR
        </button>
      )}
    </div>
  )
}

function SlugCard({ companyId, slug, onChanged }: { companyId: string; slug: string; onChanged: () => void }) {
  const [value, setValue] = useState(slug)
  const [confirm, setConfirm] = useState(false)
  const debounced = useDebouncedValue(value.trim(), 500)
  const changed = value.trim() !== slug
  const local = slugLocalProblem(value.trim())

  const availability = useQuery({
    queryKey: ['baths-slug-check', debounced, companyId],
    queryFn: () => bathsCompaniesApi.slugCheck({ slug: debounced, companyId }),
    enabled: debounced.length > 0 && debounced !== slug && slugLocalProblem(debounced) === null,
    retry: false,
  })

  const save = useMutation({
    mutationFn: () => bathsCabinetApi.updateSlug(companyId, value.trim()),
    onSuccess: () => {
      setConfirm(false)
      onChanged()
    },
    onError: () => setConfirm(false),
  })
  const conflict = save.isError ? readConflict<BathsConflictDto>(save.error) : null

  useEffect(() => {
    save.reset()
    // eslint-disable-next-line react-hooks/exhaustive-deps -- a new edit clears the previous refusal
  }, [value])

  const hint = local
    ? { ok: false, text: local }
    : changed && availability.data
      ? availability.data.available
        ? { ok: true, text: 'Адрес свободен' }
        : { ok: false, text: availability.data.conflict?.message ?? 'Этот адрес недоступен' }
      : null

  return (
    <SectionCard title="Адрес комплекса" description="Адрес на bani.ezbook.ru. Если вы его смените, старые ссылки и QR-коды перестанут работать — переадресации нет.">
      <div>
        <Input label="Адрес" value={value} maxLength={50} autoCapitalize="none" autoCorrect="off" spellCheck={false} onChange={(e) => setValue(normalizeSlugInput(e.target.value))} />
        <p className="mt-1.5 text-xs text-muted">
          bani.ezbook.ru/<span className="text-ink-soft">{value || 'адрес'}</span>
        </p>
        {hint && (
          <p role="status" className={`mt-1 text-xs font-medium ${hint.ok ? 'text-success' : 'text-danger'}`}>
            {hint.text}
          </p>
        )}
      </div>
      {save.isError && <InlineError>{conflict?.message ?? getStayErrorMessage(save.error, 'Не удалось сменить адрес.')}</InlineError>}
      <div>
        <Button
          className="min-h-[44px]"
          disabled={!changed || !!local || value.trim().length === 0 || (!!availability.data && !availability.data.available)}
          onClick={() => setConfirm(true)}
        >
          Сменить адрес
        </Button>
      </div>
      {confirm && (
        <Modal title="Сменить адрес комплекса?" onClose={() => setConfirm(false)} dismissible={!save.isPending}>
          <p className="text-sm text-ink-soft">
            Ссылки на комплекс и на его бани, QR-коды и объявления со старым адресом <span className="font-semibold text-ink">перестанут открываться</span>. Гости не будут
            переадресованы. Ссылки на уже оформленные брони продолжат работать.
          </p>
          <div className="mt-5 flex gap-3">
            <Button variant="secondary" className="min-h-[44px] flex-1" onClick={() => setConfirm(false)} disabled={save.isPending}>
              Отмена
            </Button>
            <Button className="min-h-[44px] flex-1" loading={save.isPending} onClick={() => save.mutate()}>
              Сменить адрес
            </Button>
          </div>
        </Modal>
      )}
    </SectionCard>
  )
}
