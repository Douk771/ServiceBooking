import { useEffect, useState } from 'react'
import { Button } from '@/components/ui/Button'
import { useDebouncedValue } from '@/hooks/useDebouncedValue'
import { useCatalogFilters } from '../../hooks/useCatalogFilters'
import { DEFAULT_GUESTS, MAX_GUESTS, hasActiveFilters, type CatalogFilters } from '../../utils/catalogQuery'
import { addDays } from '../../utils/stayDates'

/** Local calendar date of this device — only the `min` of the date inputs; the server decides what is bookable. */
function deviceToday(): string {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

const LABEL = 'flex flex-col gap-1.5 text-[13px] font-medium text-[#4A4038]'
const INPUT = 'min-h-[44px] rounded-xl border border-line bg-white px-3 text-sm text-ink outline-none focus:border-gold focus:ring-[3px] focus:ring-cream-deep'

/**
 * «Подбор дома» — the panel slot of the landing header (ARCHITECTURE_CYCLE41.md §41.4.2). The frame (white card) comes from the
 * template. The only part of the page that writes the filters to the URL from an effect (debounced guests and price).
 */
export function StaySearchPanel() {
  const { filters, setFilters, resetFilters } = useCatalogFilters()

  const [checkIn, setCheckIn] = useState(filters.checkIn ?? '')
  const [checkOut, setCheckOut] = useState(filters.checkOut ?? '')
  const [guests, setGuests] = useState(String(filters.guests))
  const [maxPrice, setMaxPrice] = useState(filters.maxPrice ? String(filters.maxPrice) : '')
  const [dateHint, setDateHint] = useState<string | null>(null)

  // The URL is the source of truth (back/forward, shared links): the form follows it.
  useEffect(() => {
    setCheckIn(filters.checkIn ?? '')
    setCheckOut(filters.checkOut ?? '')
    setGuests(String(filters.guests))
    setMaxPrice(filters.maxPrice ? String(filters.maxPrice) : '')
  }, [filters.checkIn, filters.checkOut, filters.guests, filters.maxPrice])

  const apply = (next: Partial<CatalogFilters>) => setFilters({ ...filters, page: 1, ...next })

  // Dates apply as soon as both are valid; guests and price after a pause in typing.
  const typedGuests = useDebouncedValue(guests)
  const typedPrice = useDebouncedValue(maxPrice)
  useEffect(() => {
    const g = /^\d+$/.test(typedGuests) ? Math.min(Math.max(Number(typedGuests), 1), MAX_GUESTS) : DEFAULT_GUESTS
    const p = /^\d+$/.test(typedPrice) && Number(typedPrice) > 0 ? Number(typedPrice) : null
    if (g !== filters.guests || p !== filters.maxPrice) setFilters({ ...filters, guests: g, maxPrice: p, page: 1 })
    // eslint-disable-next-line react-hooks/exhaustive-deps -- only a settled change of the two text fields writes the URL
  }, [typedGuests, typedPrice])

  const onDates = (nextIn: string, nextOut: string) => {
    setCheckIn(nextIn)
    setCheckOut(nextOut)
    if (nextIn && nextOut) {
      if (nextOut <= nextIn) {
        setDateHint('Дата выезда должна быть позже даты заезда')
        return
      }
      setDateHint(null)
      apply({ checkIn: nextIn, checkOut: nextOut })
    } else if (!nextIn && !nextOut) {
      setDateHint(null)
      apply({ checkIn: null, checkOut: null })
    } else {
      setDateHint('Укажите обе даты: заезд и выезд')
    }
  }

  const today = deviceToday()

  return (
    <form
      aria-label="Подбор дома"
      onSubmit={(e) => {
        e.preventDefault()
        onDates(checkIn, checkOut)
      }}
    >
      <div className="grid grid-cols-2 gap-3">
        <label className={LABEL}>
          Заезд
          <input
            type="date"
            value={checkIn}
            min={today}
            onChange={(e) => onDates(e.target.value, checkOut && e.target.value && checkOut <= e.target.value ? '' : checkOut)}
            className={INPUT}
          />
        </label>
        <label className={LABEL}>
          Выезд
          <input
            type="date"
            value={checkOut}
            min={checkIn ? addDays(checkIn, 1) : today}
            onChange={(e) => onDates(checkIn, e.target.value)}
            className={INPUT}
          />
        </label>
        <label className={LABEL}>
          Гостей
          <input
            type="number"
            inputMode="numeric"
            min={1}
            max={MAX_GUESTS}
            value={guests}
            onChange={(e) => setGuests(e.target.value)}
            className={INPUT}
          />
        </label>
        <label className={LABEL}>
          Цена за ночь до, ₽
          <input
            type="number"
            inputMode="numeric"
            min={1}
            placeholder="любая"
            value={maxPrice}
            onChange={(e) => setMaxPrice(e.target.value)}
            className={`${INPUT} placeholder:text-muted`}
          />
        </label>
      </div>
      <p className="mt-2 min-h-[18px] text-xs text-danger" role="status">
        {dateHint}
      </p>
      {hasActiveFilters(filters) && (
        <Button
          type="button"
          variant="ghost"
          size="sm"
          className="mt-1"
          onClick={() => {
            resetFilters()
            setDateHint(null)
          }}
        >
          Сбросить фильтры
        </Button>
      )}
    </form>
  )
}
