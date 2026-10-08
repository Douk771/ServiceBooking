import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import type { AvailabilityDayDto, ServiceQuoteDto, ServiceSelectionInput, ServiceStartsDto } from '../../types'
import { publicServicesApi } from '../../api/publicServices'
import { isQuoteBookable, toSelectionInput, type SessionPick } from '../../utils/serviceSelection'
import { parseServiceUrl } from '../../utils/serviceUrl'
import { getStayErrorMessage } from '../../utils/stayError'
import { ErrorState, InlineError, Skeleton } from '../StatePanels'
import { StayNotice } from '../StayNotice'
import { ServiceQuoteSummary } from './ServiceQuoteSummary'
import { ServiceTimePicker } from './ServiceTimePicker'

export interface PickableService {
  id: string
  name: string
  /** `/<company>/uslugi/<service>`: the positions of the service are read from its page. */
  url: string
  priceFromRub?: number | null
  minHours?: number
}

interface Props {
  title: string
  services: readonly PickableService[]
  /** The days of the stay (booking and staff modes); absent → the picker pages through `loadAvailability`. */
  staticDays?: (serviceId: string) => readonly AvailabilityDayDto[]
  loadAvailability?: (serviceId: string, from: string | undefined, days: number) => ReturnType<typeof publicServicesApi.availability>
  loadStarts: (serviceId: string, date: string) => Promise<ServiceStartsDto>
  loadQuote: (serviceId: string, selection: ServiceSelectionInput) => Promise<ServiceQuoteDto>
  confirmLabel: string
  /** `StayServiceAddNotice` under the button. */
  showAddNotice?: boolean
  hint?: string | null
  /** Extra fields between the quote and the button (the staff «основание»). */
  extra?: ReactNode
  extraReady?: boolean
  pending: boolean
  error: string
  companyName?: string | null
  /** Bumped when the server said «время занято»: the picker starts over. */
  resetSignal?: number
  onConfirm: (serviceId: string, pick: SessionPick, quote: ServiceQuoteDto, order: readonly string[]) => void
  onClose: () => void
}

/**
 * «Добавить услугу»: service → time → server quote → confirm. Used on the booking page of the guest, in the booking form (nothing is
 * created there, the pick goes into the booking request) and by the staff. Nothing is pre-selected and positions start at 0.
 */
export function ServicePickDialog({
  title,
  services,
  staticDays,
  loadAvailability,
  loadStarts,
  loadQuote,
  confirmLabel,
  showAddNotice = true,
  hint,
  extra,
  extraReady = true,
  pending,
  error,
  companyName,
  resetSignal = 0,
  onConfirm,
  onClose,
}: Props) {
  const [serviceId, setServiceId] = useState<string | null>(services.length === 1 ? services[0].id : null)
  const service = services.find((s) => s.id === serviceId) ?? null
  const [pick, setPick] = useState<SessionPick | null>(null)

  useEffect(() => setPick(null), [serviceId])

  const address = service ? parseServiceUrl(service.url) : null
  const page = useQuery({
    queryKey: ['stays-service-page', address?.companySlug, address?.serviceSlug],
    queryFn: () => publicServicesApi.page(address!.companySlug, address!.serviceSlug),
    enabled: !!address,
  })
  const items = page.data?.items ?? []
  const order = useMemo(() => items.map((i) => i.id), [items])

  const selection = pick && service ? toSelectionInput(pick, order) : null
  const quote = useQuery({
    queryKey: ['stays-session-quote', serviceId, selection],
    queryFn: () => loadQuote(serviceId!, selection!),
    enabled: !!selection && !!serviceId,
    staleTime: 0,
    placeholderData: (prev) => prev,
  })
  const q = quote.data
  const fresh = !!q && !quote.isPlaceholderData && !quote.isFetching
  const canConfirm = !!pick && !!service && fresh && isQuoteBookable(q) && extraReady && !pending

  return (
    <Modal title={title} onClose={onClose} dismissible={!pending}>
      <div className="flex flex-col gap-5">
        {services.length > 1 && (
          <fieldset>
            <legend className="mb-2 text-[15px] font-semibold text-ink">Услуга</legend>
            <ul className="flex flex-col gap-2">
              {services.map((s) => (
                <li key={s.id}>
                  <label className="flex min-h-[44px] cursor-pointer items-center gap-3 rounded-2xl border border-line bg-white px-4 py-2 text-sm text-ink has-[:checked]:border-ink">
                    <input type="radio" name="service" checked={serviceId === s.id} onChange={() => setServiceId(s.id)} className="h-5 w-5 accent-gold" />
                    <span className="font-medium">{s.name}</span>
                  </label>
                </li>
              ))}
            </ul>
          </fieldset>
        )}

        {service && address && page.isLoading && <Skeleton className="h-32" />}
        {service && address && page.isError && (
          <ErrorState message={getStayErrorMessage(page.error, 'Не удалось загрузить услугу.')} onRetry={() => void page.refetch()} />
        )}
        {service && (!address || page.data) && (
          <>
            <ServiceTimePicker
              key={service.id}
              scope={`pick-${service.id}`}
              items={items}
              staticDays={staticDays?.(service.id)}
              loadAvailability={loadAvailability ? (from, days) => loadAvailability(service.id, from, days) : undefined}
              loadStarts={(date) => loadStarts(service.id, date)}
              onChange={setPick}
              resetSignal={resetSignal}
            />
            {pick && (
              <div className="border-t border-line pt-4">
                <h3 className="mb-2 text-[15px] font-semibold text-ink">Стоимость</h3>
                {quote.isError && !q ? (
                  <ErrorState message={getStayErrorMessage(quote.error, 'Не удалось рассчитать стоимость.')} onRetry={() => void quote.refetch()} />
                ) : (
                  <ServiceQuoteSummary quote={q} loading={quote.isLoading} stale={quote.isFetching} />
                )}
              </div>
            )}
          </>
        )}
        {!service && <p className="text-sm text-ink-soft">Выберите услугу.</p>}

        {extra}
        {hint && (
          <p role="note" className="rounded-xl bg-warning-bg px-4 py-2.5 text-sm text-warning">
            {hint}
          </p>
        )}
        {error && <InlineError>{error}</InlineError>}

        <div className="flex flex-col gap-2.5">
          <Button
            type="button"
            size="lg"
            loading={pending}
            disabled={!canConfirm}
            className="w-full"
            onClick={() => pick && q && service && onConfirm(service.id, pick, q, order)}
          >
            {confirmLabel}
          </Button>
          {showAddNotice && <StayNotice textKey="StayServiceAddNotice" variant="plain" companyName={companyName} />}
        </div>
      </div>
    </Modal>
  )
}
