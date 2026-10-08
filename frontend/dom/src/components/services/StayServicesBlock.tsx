import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { Icon } from '@/components/ui/Icon'
import { publicServicesApi } from '../../api/publicServices'
import { publicStaysApi } from '../../api/publicStays'
import type { StayQuoteServiceDto, StayServiceSelectionInput } from '../../types'
import { toSelectionInput } from '../../utils/serviceSelection'
import { getStayErrorMessage } from '../../utils/stayError'
import { ServicePickDialog, type PickableService } from './ServicePickDialog'

/** A service chosen in the booking form: the request part plus the captions the form shows before the answer arrives. */
export interface ChosenStayService {
  selection: StayServiceSelectionInput
  name: string
  timeLabel: string
}

export const MAX_STAY_SERVICES = 3

/**
 * «Добавить к проживанию» in the booking form (US-39-10, P1). Collapsed; NOTHING is chosen by default (ЮР39-6, 69-ФЗ): the guest
 * opens it, picks a service and a time inside the stay, and the choice goes into the quote and the booking request (≤ 3). Nothing is
 * created here — the booking and the sessions are created together, or not at all. Paid on the spot, outside the prepayment.
 */
export function StayServicesBlock({
  companySlug,
  houseId,
  checkIn,
  checkOut,
  chosen,
  quoteServices,
  onChange,
  companyName,
}: {
  companySlug: string
  houseId: string
  checkIn: string | null
  checkOut: string | null
  chosen: readonly ChosenStayService[]
  quoteServices?: readonly StayQuoteServiceDto[]
  onChange: (next: ChosenStayService[]) => void
  companyName: string
}) {
  const [open, setOpen] = useState(false)
  const [adding, setAdding] = useState(false)
  const company = useQuery({
    queryKey: ['stays-company', companySlug, null],
    queryFn: () => publicStaysApi.company(companySlug),
    enabled: open,
  })
  const services: PickableService[] = (company.data?.services ?? []).filter((s) => s.availableForHouseBookings)
  const range = { houseId, checkIn: checkIn ?? undefined, checkOut: checkOut ?? undefined }
  const hasRange = !!checkIn && !!checkOut

  return (
    <details className="mt-5 border-t border-line pt-4" open={open} onToggle={(e) => setOpen((e.currentTarget as HTMLDetailsElement).open)} data-testid="stay-services">
      <summary className="flex min-h-[44px] cursor-pointer items-center text-[15px] font-semibold text-ink">
        Добавить к проживанию <span className="ml-2 text-xs font-normal text-muted">(необязательно)</span>
      </summary>
      <div className="mt-2 flex flex-col gap-3">
        <p className="text-xs text-ink-soft">Баня, чан и другие услуги компании на время проживания. Ничего не добавлено, пока вы сами не выберете. Оплачиваются на месте.</p>

        {chosen.length > 0 && (
          <ul className="flex flex-col gap-2">
            {chosen.map((c, i) => {
              const qs = quoteServices?.find((s) => s.index === i)
              return (
                <li key={`${c.selection.serviceId}-${c.selection.businessDate}-${c.selection.startMinute}`} className="flex items-start justify-between gap-3 rounded-2xl border border-line bg-cream/40 px-4 py-3">
                  <div className="min-w-0 text-sm">
                    <p className="font-medium text-ink">{c.name}</p>
                    <p className="text-ink-soft">{c.timeLabel}</p>
                    {qs && !qs.ok && (
                      <ul role="alert" className="mt-1 text-xs text-danger">
                        {qs.quote.problems.map((p) => (
                          <li key={p.code}>{p.message}</li>
                        ))}
                      </ul>
                    )}
                  </div>
                  <button
                    type="button"
                    onClick={() => onChange(chosen.filter((_, j) => j !== i))}
                    aria-label={`Убрать услугу «${c.name}»`}
                    className="flex h-11 w-11 shrink-0 items-center justify-center rounded-full border border-line bg-white text-ink-soft hover:border-line-strong"
                  >
                    <Icon name="x" size={16} strokeWidth={1.8} />
                  </button>
                </li>
              )
            })}
          </ul>
        )}

        {company.isError && <p role="alert" className="text-sm text-danger">{getStayErrorMessage(company.error, 'Не удалось загрузить услуги.')}</p>}
        {open && !company.isLoading && !company.isError && services.length === 0 && <p className="text-sm text-ink-soft">У компании нет услуг, которые можно добавить к проживанию.</p>}

        {services.length > 0 && chosen.length < MAX_STAY_SERVICES && (
          <div>
            <Button type="button" variant="secondary" className="min-h-[44px]" disabled={!hasRange} onClick={() => setAdding(true)}>
              <Icon name="plus" size={15} strokeWidth={1.8} /> Добавить услугу
            </Button>
            {!hasRange && <p className="mt-1 text-xs text-muted">Сначала выберите даты проживания</p>}
          </div>
        )}
      </div>

      {adding && hasRange && (
        <ServicePickDialog
          title="Добавить услугу к проживанию"
          services={services}
          loadAvailability={(id, from, days) => publicServicesApi.availability(id, { from, days, ...range })}
          loadStarts={(id, date) => publicServicesApi.starts(id, { date, ...range })}
          loadQuote={(id, sel) => publicServicesApi.quote(id, { ...sel, ...range })}
          confirmLabel="Добавить в заявку"
          pending={false}
          error=""
          companyName={companyName}
          onClose={() => setAdding(false)}
          onConfirm={(serviceId, pick, quote, order) => {
            onChange([
              ...chosen,
              {
                selection: { serviceId, ...toSelectionInput(pick, order) },
                name: services.find((s) => s.id === serviceId)?.name ?? 'Услуга',
                timeLabel: quote.time?.label ?? '',
              },
            ])
            setAdding(false)
          }}
        />
      )}
    </details>
  )
}
