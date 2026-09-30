import { useEffect, useMemo, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Icon } from '@/components/ui/Icon'
import { storefrontApi } from '../../api/storefront'
import { orderLegalTextsApi } from '../../api/legalNotice'
import { RadioChips } from '../pickup/RadioChips'
import { SlotList } from '../pickup/SlotList'
import { getGoodsErrorMessage } from '../../utils/orderError'
import type { PickupControl } from '../../hooks/usePickupChoice'
import type { PickupOptionsDto } from '../../types'

interface Props {
  slug: string
  options: PickupOptionsDto
  pickup: PickupControl
  /** A server message to show above the picker (e.g. «Это время уже недоступно — выберите другое»). */
  notice: string | null
  onNoticeChange: (text: string | null) => void
}

type Mode = 'asap' | 'scheduled'

/**
 * US-24-06/07 — «Когда заберёте»: «как можно скорее» or a date and a slot. Dates and slots come from the server
 * (`pickup.dates`, `GET …/pickup-slots`); the frontend never computes them (API_CONTRACT_CYCLE24.md §490). Both groups are
 * real radiogroups (keyboard + screen reader). Changing the date changes the assortment above (the page reacts to `pickup.date`).
 */
export function PickupPicker({ slug, options, pickup, notice, onNoticeChange }: Props) {
  const { choice, browseDate } = pickup
  const asapOffered = options.asapEnabled
  const scheduledOffered = options.scheduledEnabled && options.dates.length > 0
  const [scheduledMode, setScheduledMode] = useState<boolean>(() => choice?.kind === 'Slot' || (!asapOffered && scheduledOffered))
  const mode: Mode = choice?.kind === 'Asap' ? 'asap' : scheduledMode || choice?.kind === 'Slot' ? 'scheduled' : asapOffered ? 'asap' : 'scheduled'

  const firstDate = options.dates.find((d) => d.hasSlots)?.date ?? options.dates[0]?.date ?? null
  const activeDate = browseDate && options.dates.some((d) => d.date === browseDate) ? browseDate : choice?.kind === 'Slot' ? choice.date : null

  const slotsQuery = useQuery({
    queryKey: ['pickup-slots', slug, activeDate],
    queryFn: () => storefrontApi.pickupSlots(slug, activeDate!),
    enabled: mode === 'scheduled' && !!activeDate,
    staleTime: 20_000,
  })

  // A chosen slot the server no longer lists (time passed, hours changed) is dropped WITH the server's wording — never sent.
  const slotsData = slotsQuery.data
  const dropRef = useRef<string | null>(null)
  useEffect(() => {
    if (choice?.kind !== 'Slot' || !slotsData || slotsData.date !== choice.date || slotsQuery.isFetching) return
    if (!slotsData.slots.some((s) => s.startUtc === choice.slotStartUtc) && dropRef.current !== choice.slotStartUtc) {
      dropRef.current = choice.slotStartUtc
      pickup.choose(null)
      onNoticeChange('Это время уже недоступно — выберите другое')
    }
  }, [choice, slotsData, slotsQuery.isFetching, pickup, onNoticeChange])

  const preorderNotice = useQuery({
    queryKey: ['legal-text', 'OrderPreorderNotice'],
    queryFn: orderLegalTextsApi.preorderNotice,
    staleTime: 5 * 60 * 1000,
    retry: false, // 404 = «no text yet»: show nothing (§478.4)
    enabled: mode === 'scheduled',
  })
  const isFutureDate = !!activeDate && options.dates[0]?.date !== activeDate

  const modeOptions = useMemo(
    () => [
      ...(asapOffered ? [{ value: 'asap' as Mode, label: 'Как можно скорее', disabled: !options.asap?.available, hint: options.asap?.text ?? undefined }] : []),
      ...(scheduledOffered ? [{ value: 'scheduled' as Mode, label: 'К определённому времени' }] : []),
    ],
    [asapOffered, scheduledOffered, options.asap],
  )

  const chooseMode = (m: Mode) => {
    onNoticeChange(null)
    if (m === 'asap') {
      setScheduledMode(false)
      pickup.choose({ kind: 'Asap' })
    } else {
      setScheduledMode(true)
      if (choice?.kind === 'Asap') pickup.choose(null)
      if (!activeDate && firstDate) pickup.browse(firstDate)
    }
  }

  return (
    <section aria-labelledby="pickup-title" className="rounded-2xl border border-line bg-white p-3 sm:p-5 mb-6" id="pickup" data-testid="pickup-picker">
      <h2 id="pickup-title" className="font-serif text-xl text-ink flex items-center gap-2">
        <Icon name="clock" size={18} strokeWidth={1.7} /> Когда заберёте
      </h2>

      {notice && (
        <p role="alert" className="mt-3 rounded-xl bg-warning-bg text-warning text-sm font-medium px-4 py-2.5" data-testid="pickup-notice">
          {notice}
        </p>
      )}

      {modeOptions.length === 0 ? (
        <p className="mt-3 text-sm text-ink-soft">Сейчас выбрать время получения нельзя.</p>
      ) : (
        <div className="mt-3 flex flex-col gap-4">
          <div>
            <RadioChips<Mode> label="Способ получения" options={modeOptions} value={mode} onChange={chooseMode} />
            {mode === 'asap' && options.asap?.text && (
              <p className={`mt-2 text-sm ${options.asap.available ? 'text-ink-soft' : 'text-warning'}`} data-testid="asap-text">
                {options.asap.text}
              </p>
            )}
          </div>

          {mode === 'scheduled' && scheduledOffered && (
            <>
              <div>
                <p className="text-[13px] font-medium text-[#4A4038] mb-2" id="pickup-date-label">
                  Дата
                </p>
                <RadioChips
                  label="Дата получения"
                  value={activeDate}
                  onChange={(d) => {
                    onNoticeChange(null)
                    pickup.browse(d)
                  }}
                  options={options.dates.map((d) => ({ value: d.date, label: d.label, disabled: !d.hasSlots, hint: d.reasonText ?? undefined }))}
                />
                {options.dates.some((d) => !d.hasSlots && d.reasonText) && (
                  <ul className="mt-2 text-xs text-muted list-none flex flex-col gap-0.5">
                    {options.dates
                      .filter((d) => !d.hasSlots && d.reasonText)
                      .map((d) => (
                        <li key={d.date}>
                          {d.label}: {d.reasonText}
                        </li>
                      ))}
                  </ul>
                )}
              </div>

              {activeDate && (
                <div>
                  <p className="text-[13px] font-medium text-[#4A4038] mb-2">Время</p>
                  <SlotList
                    groupLabel="Время получения"
                    data={slotsData?.date === activeDate ? slotsData : undefined}
                    isLoading={slotsQuery.isLoading || (slotsQuery.isFetching && slotsData?.date !== activeDate)}
                    error={slotsQuery.isError ? getGoodsErrorMessage(slotsQuery.error, 'Не удалось загрузить время получения.') : null}
                    onRetry={() => void slotsQuery.refetch()}
                    selectedStartUtc={choice?.kind === 'Slot' ? choice.slotStartUtc : null}
                    onSelect={(s) => {
                      onNoticeChange(null)
                      dropRef.current = null
                      pickup.choose({ kind: 'Slot', date: activeDate, slotStartUtc: s.startUtc, dateLabel: options.dates.find((d) => d.date === activeDate)?.label, slotLabel: s.label })
                    }}
                  />
                </div>
              )}
              {isFutureDate && preorderNotice.data?.contentHtml && (
                <div className="legal-content text-xs text-ink-soft [&_a]:text-gold [&_p]:mb-0" data-testid="preorder-notice" dangerouslySetInnerHTML={{ __html: preorderNotice.data.contentHtml }} />
              )}
            </>
          )}
        </div>
      )}
    </section>
  )
}
