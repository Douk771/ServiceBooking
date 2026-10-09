import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import type {
  AvailabilityDayDto,
  ServiceItemPublicDto,
  ServiceQuoteDto,
  ServiceSelectionInput,
  ServiceStartsDto,
} from '@/types/slots'
import { isQuoteBookable, toSelectionInput, type SessionPick } from '@/utils/slots/serviceSelection'
import { parseServiceUrl } from '@/utils/slots/serviceUrl'
import { getStayErrorMessage } from '@/utils/slots/slotError'
import { ErrorState, InlineError, Skeleton } from '@/components/slots/ui/StatePanels'
import { SlotNotice } from '@/components/slots/ui/SlotNotice'
import { ServiceQuoteSummary } from '@/components/slots/services/ServiceQuoteSummary'
import { ServiceTimePicker } from '@/components/slots/services/ServiceTimePicker'
import { useSlotVertical, type SlotApi } from '@/components/slots/SlotVerticalContext'

export interface PickableService {
  id: string
  name: string
  /** `/<company>/uslugi/<service>`: the positions of the service are read from its page (absent for the staff, who pass `loadItems`). */
  url?: string
  priceFromRub?: number | null
  minHours?: number
}

interface Props {
  title: string
  services: readonly PickableService[]
  /** The days of the stay (booking and staff modes); absent → the picker pages through `loadAvailability`. */
  staticDays?: (serviceId: string) => readonly AvailabilityDayDto[]
  loadAvailability?: (
    serviceId: string,
    from: string | undefined,
    days: number,
  ) => ReturnType<SlotApi['publicServices']['availability']>
  loadStarts: (serviceId: string, date: string) => Promise<ServiceStartsDto>
  loadQuote: (serviceId: string, selection: ServiceSelectionInput) => Promise<ServiceQuoteDto>
  /** Positions of a service when its public page is not the source (the staff: an unpublished service has no page). */
  loadItems?: (serviceId: string) => Promise<ServiceItemPublicDto[]>
  confirmLabel: string
  /** `legal.keys.serviceAddNotice` under the button. */
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
  loadItems,
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
  const { api, legal } = useSlotVertical()
  const publicServicesApi = api.publicServices
  const [serviceId, setServiceId] = useState<string | null>(null)
  const service = services.find((s) => s.id === serviceId) ?? null
  const [pick, setPick] = useState<SessionPick | null>(null)

  useEffect(() => setPick(null), [serviceId])

  const address = service?.url ? parseServiceUrl(service.url) : null
  const page = useQuery({
    queryKey: loadItems
      ? ['stays-service-items-pick', serviceId]
      : ['stays-service-page', address?.companySlug, address?.serviceSlug],
    queryFn: async () =>
      loadItems
        ? { items: await loadItems(serviceId!) }
        : { items: (await publicServicesApi.page(address!.companySlug, address!.serviceSlug)).items },
    enabled: loadItems ? !!serviceId : !!address,
  })
  const items = useMemo(() => page.data?.items ?? [], [page.data])
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
        {services.length > 0 && (
          <fieldset>
            <legend className="mb-2 text-[15px] font-semibold text-ink">Услуга</legend>
            <ul className="flex flex-col gap-2">
              {services.map((s) => (
                <li key={s.id}>
                  <label className="flex min-h-[44px] cursor-pointer items-center gap-3 rounded-2xl border border-line bg-white px-4 py-2 text-sm text-ink has-[:checked]:border-ink">
                    <input
                      type="radio"
                      name="service"
                      checked={serviceId === s.id}
                      onChange={() => setServiceId(s.id)}
                      className="h-5 w-5 accent-gold"
                    />
                    <span className="font-medium">{s.name}</span>
                  </label>
                </li>
              ))}
            </ul>
          </fieldset>
        )}

        {service && (address || loadItems) && page.isLoading && <Skeleton className="h-32" />}
        {service && (address || loadItems) && page.isError && (
          <ErrorState
            message={getStayErrorMessage(page.error, 'Не удалось загрузить услугу.')}
            onRetry={() => void page.refetch()}
          />
        )}
        {service && (!(address || loadItems) || page.data) && (
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
                  <ErrorState
                    message={getStayErrorMessage(quote.error, 'Не удалось рассчитать стоимость.')}
                    onRetry={() => void quote.refetch()}
                  />
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
          {showAddNotice && <SlotNotice textKey={legal.keys.serviceAddNotice} variant="plain" companyName={companyName} />}
        </div>
      </div>
    </Modal>
  )
}
