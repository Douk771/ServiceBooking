import type { ProviderFullDto, ProviderPublicDto } from '@/types/slots'

/**
 * «Об исполнителе» (ЮР-3, Т37-03, API_CONTRACT_CYCLE37.md §37.22.5). The same block serves the public pages (`ProviderPublicDto`:
 * for a sole proprietor / company — everything; for a self-employed person or an individual — status and INN only) and the
 * booking page (`ProviderFullDto`: the full name and the claims address too). Fields the server did not send are not drawn, so
 * the page never prints an empty label.
 */
export function ProviderBlock({
  provider,
  className = '',
}: {
  provider: ProviderPublicDto | ProviderFullDto | undefined
  className?: string
}) {
  if (!provider) return null
  const ogrnLabel = provider.status === 'IndividualEntrepreneur' ? 'ОГРНИП' : 'ОГРН'
  return (
    <section aria-labelledby="provider-title" className={`rounded-2xl border border-line bg-white p-5 ${className}`}>
      <h2 id="provider-title" className="mb-3 text-[15px] font-semibold text-ink">
        Об исполнителе
      </h2>
      <dl className="grid gap-x-6 gap-y-2 text-sm sm:grid-cols-[170px_1fr]">
        <dt className="text-muted">Статус</dt>
        <dd className="text-ink">{provider.statusLabel}</dd>
        {provider.name && (
          <>
            <dt className="text-muted">Наименование</dt>
            <dd className="text-ink">{provider.name}</dd>
          </>
        )}
        <dt className="text-muted">ИНН</dt>
        <dd className="text-ink">{provider.inn}</dd>
        {provider.ogrn && (
          <>
            <dt className="text-muted">{ogrnLabel}</dt>
            <dd className="text-ink">{provider.ogrn}</dd>
          </>
        )}
        {provider.claimsAddress && (
          <>
            <dt className="text-muted">Адрес для претензий</dt>
            <dd className="text-ink">{provider.claimsAddress}</dd>
          </>
        )}
      </dl>
    </section>
  )
}
