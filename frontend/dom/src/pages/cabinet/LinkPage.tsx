import { useEffect, useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { InlineError } from '@/components/ui/InlineError'
import { Input } from '@/components/ui/Input'
import { Modal } from '@/components/ui/Modal'
import { Icon } from '@/components/ui/Icon'
import { useDebouncedValue } from '@/hooks/useDebouncedValue'
import { staysCompaniesApi } from '../../api/staysCompanies'
import { staysHousesApi } from '../../api/staysHouses'
import { SectionCard } from '../../components/cabinet/formParts'
import { QrDialog } from '../../components/cabinet/QrDialog'
import { CopyButton } from '../../components/CopyButton'
import { EmptyState, ErrorState, LoadingList } from '../../components/StatePanels'
import { useStaysCompany } from '../../hooks/useStaysCompany'
import { can } from '../../utils/permissions'
import { COMPANY_SLUG_FORMAT_TEXT, isCompanySlugValid, normalizeSlugInput } from '../../utils/slug'
import { getStayErrorMessage, readConflict } from '../../utils/stayError'
import type { StaysConflictDto } from '../../types'

/**
 * `/cabinet/:companyId/link` (`ViewCabinet`, US-37-09) — the addresses and QR codes of the company and of every house, and (owner) the
 * change of the company address. A changed address does NOT redirect: old links and QR codes stop working — said before the change.
 */
export function LinkPage() {
  const { company, refresh } = useStaysCompany()
  const [qr, setQr] = useState<{ title: string; fileName: string; load: () => Promise<Blob> } | null>(null)
  const houses = useQuery({ queryKey: ['stays-houses', company.id], queryFn: () => staysHousesApi.list(company.id) })
  const manage = can(company.myPermissions, 'ManageCompany')

  return (
    <main className="mx-auto flex max-w-[760px] flex-col gap-6 px-4 pb-6 pt-8 sm:px-8">
      <SectionCard title="Страница компании" description="Эту ссылку и QR-код можно давать гостям и размещать в объявлениях.">
        <LinkRow url={company.publicUrl} label="ссылку на компанию" onQr={() => setQr({ title: company.name, fileName: `${company.slug}.png`, load: () => staysCompaniesApi.qr(company.id) })} />
      </SectionCard>

      <SectionCard title="Дома" description="Ссылка на страницу конкретного дома. Только опубликованные дома видны гостям.">
        {houses.isLoading ? (
          <LoadingList rows={2} rowClass="h-16" />
        ) : houses.isError ? (
          <ErrorState message={getStayErrorMessage(houses.error, 'Не удалось загрузить дома.')} onRetry={() => void houses.refetch()} />
        ) : !houses.data || houses.data.length === 0 ? (
          <EmptyState title="Домов пока нет" text="Добавьте дом на вкладке «Дома» — здесь появится его ссылка." />
        ) : (
          <ul className="flex flex-col gap-3">
            {houses.data.map((h) => (
              <li key={h.id} className="rounded-2xl border border-line p-4">
                <p className="mb-2 text-sm font-semibold text-ink">
                  {h.name}
                  {!h.isPublished && <span className="ml-2 text-xs font-normal text-warning">{h.isArchived ? 'в архиве' : 'не опубликован — гости ссылку не откроют'}</span>}
                </p>
                <LinkRow
                  url={h.publicUrl}
                  label={`ссылку на дом «${h.name}»`}
                  onQr={() => setQr({ title: h.name, fileName: `${company.slug}-${h.slug}.png`, load: () => staysHousesApi.qr(company.id, h.id) })}
                />
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

function LinkRow({ url, label, onQr }: { url: string; label: string; onQr: () => void }) {
  return (
    <div className="flex flex-wrap items-center gap-2">
      <a href={url} target="_blank" rel="noopener noreferrer" className="min-w-0 flex-1 truncate text-sm font-medium !text-ink underline decoration-line-strong underline-offset-2">
        {url}
        <span className="sr-only"> (откроется в новой вкладке)</span>
      </a>
      <CopyButton text={url} label={label} />
      <button
        type="button"
        onClick={onQr}
        className="inline-flex min-h-[44px] shrink-0 items-center gap-1.5 rounded-full border border-line bg-white px-4 text-xs font-semibold text-ink hover:border-line-strong"
        aria-label={`QR-код: ${label}`}
      >
        <Icon name="qr-code" size={14} strokeWidth={1.8} /> QR
      </button>
    </div>
  )
}

function SlugCard({ companyId, slug, onChanged }: { companyId: string; slug: string; onChanged: () => void }) {
  const [value, setValue] = useState(slug)
  const [confirm, setConfirm] = useState(false)
  const debounced = useDebouncedValue(value.trim(), 500)
  const changed = value.trim() !== slug
  const formatOk = value.trim() === '' || isCompanySlugValid(value.trim())

  const availability = useQuery({
    queryKey: ['stays-slug-check', debounced, companyId],
    queryFn: () => staysCompaniesApi.slugCheck({ slug: debounced, companyId }),
    enabled: debounced.length > 0 && debounced !== slug && isCompanySlugValid(debounced),
    retry: false,
  })

  const save = useMutation({
    mutationFn: () => staysCompaniesApi.updateSlug(companyId, value.trim()),
    onSuccess: () => {
      setConfirm(false)
      onChanged()
    },
    onError: () => setConfirm(false),
  })
  const conflict = save.isError ? readConflict<StaysConflictDto>(save.error) : null

  useEffect(() => {
    save.reset()
    // eslint-disable-next-line react-hooks/exhaustive-deps -- a new edit clears the previous refusal
  }, [value])

  const hint = !formatOk
    ? { ok: false, text: COMPANY_SLUG_FORMAT_TEXT }
    : changed && availability.data
      ? availability.data.available
        ? { ok: true, text: 'Адрес свободен' }
        : { ok: false, text: availability.data.conflict?.message ?? 'Этот адрес недоступен' }
      : null

  return (
    <SectionCard title="Адрес компании" description="Адрес на dom.ezbook.ru. Если вы его смените, старые ссылки и QR-коды перестанут работать — переадресации нет.">
      <div>
        <Input
          label="Адрес"
          value={value}
          maxLength={50}
          autoCapitalize="none"
          autoCorrect="off"
          spellCheck={false}
          onChange={(e) => setValue(normalizeSlugInput(e.target.value))}
        />
        <p className="mt-1.5 text-xs text-muted">
          dom.ezbook.ru/<span className="text-ink-soft">{value || 'адрес'}</span>
        </p>
        {hint && (
          <p role="status" className={`mt-1 text-xs font-medium ${hint.ok ? 'text-success' : 'text-danger'}`}>
            {hint.text}
          </p>
        )}
      </div>
      {save.isError && <InlineError>{conflict?.message ?? getStayErrorMessage(save.error, 'Не удалось сменить адрес.')}</InlineError>}
      <div>
        <Button className="min-h-[44px]" disabled={!changed || !formatOk || value.trim().length === 0 || (!!availability.data && !availability.data.available)} onClick={() => setConfirm(true)}>
          Сменить адрес
        </Button>
      </div>
      {confirm && (
        <Modal title="Сменить адрес компании?" onClose={() => setConfirm(false)} dismissible={!save.isPending}>
          <p className="text-sm text-ink-soft">
            Ссылки на компанию и на её дома, QR-коды и объявления со старым адресом <span className="font-semibold text-ink">перестанут открываться</span>.
            Гости не будут переадресованы. Ссылки на уже оформленные брони продолжат работать.
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
