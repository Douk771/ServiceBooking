import { useEffect, useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { Icon } from '@/components/ui/Icon'
import { formatPhone, telHref } from '@/utils/phone'
import { formatRub } from '@/utils/money'
import { ProviderBlock } from '@/components/slots/ui/ProviderBlock'
import { ErrorState, LoadingList } from '@/components/slots/ui/StatePanels'
import { ServiceOrderPanel } from '@/components/slots/services/ServiceOrderPanel'
import { ServiceTermsModal } from '@/components/slots/services/ServiceTermsModal'
import { getStayErrorMessage, isNotFound } from '@/utils/slots/slotError'
import { isIsoDate } from '@/utils/slots/slotDates'
import { useGuestWords, useSlotVertical } from '@/components/slots/SlotVerticalContext'

/**
 * `/:slug/uslugi/:serviceSlug` — the page of a service (US-39-07/08, API_CONTRACT_CYCLE39.md §39.22.1). Gallery, the owner's
 * description (the visiting rules are the owner's text there), the price table in the guest wording, the minimum of hours, the
 * positions, the cancellation rule (with a prepayment), the preparation time (only if the owner shows it), then the order form — or
 * «Можно добавить к брони дома» when orders without a stay are off. No tourist tax here (Т39-15), no occupied time, no names.
 */
export function ServiceView() {
  const { api, words, features, NotFound } = useSlotVertical()
  const guestWords = useGuestWords()
  const publicServicesApi = api.publicServices
  const params = useParams()
  const slug = params.slug ?? ''
  // dom's address is /:slug/uslugi/:serviceSlug, bani's is /:slug/:resourceSlug.
  const serviceSlug = params.serviceSlug ?? params.resourceSlug ?? ''
  const [sp] = useSearchParams()
  const dateParam = sp.get('date')
  const initialDate = dateParam && isIsoDate(dateParam) ? dateParam : null
  const [termsOpen, setTermsOpen] = useState(false)

  const query = useQuery({
    queryKey: ['stays-service-page', slug, serviceSlug],
    queryFn: () => publicServicesApi.page(slug, serviceSlug),
    retry: (count, err) => !isNotFound(err) && count < 1,
  })
  const service = query.data

  useEffect(() => {
    if (service) document.title = `${service.name} — ${service.company.name} · ${words.brandName}`
    return () => {
      document.title = words.brandTitle
    }
  }, [service, words])

  if (query.isLoading) {
    return (
      <main className="mx-auto max-w-[1000px] px-4 py-10 sm:px-8">
        <LoadingList rows={3} rowClass="h-40" />
      </main>
    )
  }
  if (query.isError && isNotFound(query.error)) return <NotFound title="Услуга не найдена" />
  if (query.isError || !service) {
    return (
      <main className="mx-auto max-w-[760px] px-4 py-10 sm:px-8">
        <ErrorState message={getStayErrorMessage(query.error, 'Не удалось загрузить страницу услуги.')} onRetry={() => void query.refetch()} />
      </main>
    )
  }
  if (!service.available) {
    return (
      <main className="mx-auto max-w-[560px] px-4 py-24 text-center">
        <h1 className="font-serif text-2xl text-ink">{service.notAvailableText ?? 'Услуга недоступна для бронирования'}</h1>
        <Link to={service.company.url} className="mt-6 inline-block text-sm font-semibold text-gold hover:text-gold-dark">
          К компании «{service.company.name}»
        </Link>
      </main>
    )
  }

  const phone = service.company.phone
  const standalone = service.standalone

  return (
    <main className="mx-auto max-w-[1000px] px-4 pb-4 pt-8 sm:px-8">
      <p className="text-sm">
        <Link to={service.company.url} className="font-medium text-ink-soft hover:text-gold-dark">
          ← {service.company.name}
        </Link>
      </p>
      <h1 className="mt-2 font-serif text-[32px] leading-tight text-ink sm:text-[40px]">{service.name}</h1>

      {service.photos.length > 0 && (
        <ul className="mt-5 flex snap-x gap-3 overflow-x-auto pb-2" aria-label="Фотографии услуги">
          {service.photos.map((p, i) => (
            <li key={p.id} className="shrink-0 snap-start">
              <img
                src={p.url}
                alt={`${service.name}, фото ${i + 1}`}
                loading={i === 0 ? 'eager' : 'lazy'}
                className="h-56 w-auto max-w-[85vw] rounded-2xl object-cover sm:h-72"
              />
            </li>
          ))}
        </ul>
      )}

      <div className="mt-6 grid gap-8 lg:grid-cols-[1fr_420px]">
        <div className="flex min-w-0 flex-col gap-6">
          {features.capacity && service.capacity != null && (
            <p className="text-sm font-medium text-ink" data-testid="service-capacity">
              до {service.capacity} человек
            </p>
          )}
          {service.description && <p className="whitespace-pre-line text-[15px] leading-relaxed text-ink-soft">{service.description}</p>}

          <section aria-labelledby="price-table" className="rounded-2xl border border-line bg-white p-5">
            <h2 id="price-table" className="mb-3 text-[15px] font-semibold text-ink">
              Цена за час
            </h2>
            {service.priceTable.length === 0 ? (
              <p className="text-sm text-ink-soft">Цены пока не заданы.</p>
            ) : (
              <dl className="text-sm">
                {service.priceTable.map((r, i) => (
                  <div key={i} className="flex items-baseline justify-between gap-4 border-b border-line/70 py-2 last:border-0">
                    <dt className="text-ink-soft">{r.label}</dt>
                    <dd className="shrink-0 font-medium tabular-nums text-ink">{formatRub(r.priceRub)}/ч</dd>
                  </div>
                ))}
              </dl>
            )}
            <p className="mt-3 text-xs text-ink-soft">
              Минимум {service.minHours} ч{service.maxHours > service.minHours ? `, не больше ${service.maxHours} ч` : ''}.
              {service.bufferMinutes != null && service.bufferMinutes > 0 && <> Время на подготовку после сеанса — {service.bufferMinutes} мин.</>}
            </p>
          </section>

          {service.items.length > 0 && (
            <section aria-labelledby="items" className="rounded-2xl border border-line bg-white p-5">
              <h2 id="items" className="mb-3 text-[15px] font-semibold text-ink">
                Дополнительно
              </h2>
              <dl className="text-sm">
                {service.items.map((it) => (
                  <div key={it.id} className="flex items-baseline justify-between gap-4 border-b border-line/70 py-2 last:border-0">
                    <dt className="text-ink-soft">{it.name}</dt>
                    <dd className="shrink-0 font-medium tabular-nums text-ink">{it.priceRub === 0 ? 'бесплатно' : formatRub(it.priceRub)}</dd>
                  </div>
                ))}
              </dl>
            </section>
          )}

          {standalone.ordering && standalone.cancellationSummary && (
            <section aria-labelledby="cancel-rule" className="rounded-2xl border border-line bg-white p-5">
              <h2 id="cancel-rule" className="mb-1 text-[15px] font-semibold text-ink">
                Отмена
              </h2>
              <p className="text-sm text-ink-soft">{standalone.cancellationSummary}</p>
              <button type="button" onClick={() => setTermsOpen(true)} className="mt-2 min-h-[44px] text-sm font-semibold text-gold-dark underline">
                Условия оказания услуги
              </button>
            </section>
          )}

          <ProviderBlock provider={service.provider} />

          <section className="rounded-2xl border border-line bg-white p-5" aria-labelledby="company">
            <h2 id="company" className="mb-2 text-[15px] font-semibold text-ink">
              Компания
            </h2>
            <p className="text-sm text-ink">{service.company.name}</p>
            {phone && (
              <a href={telHref(phone) || undefined} className="mt-1 inline-flex min-h-[44px] items-center gap-2 text-sm font-semibold !text-ink">
                <Icon name="phone" size={15} strokeWidth={1.7} className="text-gold-dark" />
                {formatPhone(phone)}
              </a>
            )}
          </section>
        </div>

        <aside className="min-w-0">
          {!service.acceptingBookings ? (
            <p role="status" className="rounded-2xl bg-warning-bg px-5 py-4 text-sm text-warning">
              {service.notAcceptingText ?? 'Бронирование временно недоступно'}
            </p>
          ) : standalone.ordering ? (
            <ServiceOrderPanel service={service} initialDate={initialDate} onOpenTerms={() => setTermsOpen(true)} />
          ) : (
            <section className="rounded-3xl border border-line bg-white p-6" aria-label={guestWords.panelLabel}>
              <p className="text-base font-medium text-ink" data-testid="not-ordering">
                {standalone.notOrderingText ?? words.notOrderingFallback}
              </p>
              <Link to={service.company.url} className="mt-3 inline-flex min-h-[44px] items-center text-sm font-semibold text-gold-dark underline">
                {words.chooseResource}
              </Link>
            </section>
          )}
        </aside>
      </div>

      {termsOpen && <ServiceTermsModal companyName={service.company.name} onClose={() => setTermsOpen(false)} />}
    </main>
  )
}
