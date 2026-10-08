import { useEffect, useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { Icon } from '@/components/ui/Icon'
import { Button } from '@/components/ui/Button'
import { Pagination } from '@/components/ui/Pagination'
import { useAuthStore } from '@/store/authStore'
import { useDebouncedValue } from '@/hooks/useDebouncedValue'
import { publicStaysApi } from '../api/publicStays'
import { HouseCard } from '../components/HouseCard'
import { EmptyState, ErrorState, LoadingList } from '../components/StatePanels'
import {
  CATALOG_PAGE_SIZE,
  DEFAULT_GUESTS,
  MAX_GUESTS,
  hasActiveFilters,
  parseCatalogFilters,
  toApiQuery,
  toSearchParams,
  type CatalogFilters,
} from '../utils/catalogQuery'
import { addDays } from '../utils/stayDates'
import { getStayErrorMessage } from '../utils/stayError'

/** Local calendar date of this device — only the `min` of the date inputs; the server decides what is bookable. */
function deviceToday(): string {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

/**
 * `/` — catalog of houses of Sheregesh (US-37-05). Filters live in the URL (shareable): `checkIn`, `checkOut`, `guests`, `maxPrice`,
 * `page`. With dates the list shows only houses that are free for them, with the price for those nights. Plus «Для владельцев».
 */
export function CatalogPage() {
  const [sp, setSp] = useSearchParams()
  const filters = useMemo(() => parseCatalogFilters(sp), [sp])
  const authed = useAuthStore((s) => s.isAuthenticated())

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

  const apply = (next: Partial<CatalogFilters>) => setSp(toSearchParams({ ...filters, page: 1, ...next }), { replace: false })

  // Dates apply as soon as both are valid; guests and price after a pause in typing.
  const typedGuests = useDebouncedValue(guests)
  const typedPrice = useDebouncedValue(maxPrice)
  useEffect(() => {
    const g = /^\d+$/.test(typedGuests) ? Math.min(Math.max(Number(typedGuests), 1), MAX_GUESTS) : DEFAULT_GUESTS
    const p = /^\d+$/.test(typedPrice) && Number(typedPrice) > 0 ? Number(typedPrice) : null
    if (g !== filters.guests || p !== filters.maxPrice) setSp(toSearchParams({ ...filters, guests: g, maxPrice: p, page: 1 }))
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

  const query = useQuery({
    queryKey: ['stays-catalog', toApiQuery(filters)],
    queryFn: () => publicStaysApi.catalog(toApiQuery(filters)),
    placeholderData: (prev) => prev,
  })

  const today = deviceToday()
  const list = query.data

  return (
    <main>
      <section className="border-b border-line bg-gradient-to-b from-cream-deep/70 to-cream">
        <div className="mx-auto grid max-w-[1180px] gap-8 px-4 pb-10 pt-10 sm:px-8 lg:grid-cols-[1.05fr_1fr] lg:items-end lg:pt-14">
          <div>
            <p className="mb-3 text-xs font-semibold uppercase tracking-[0.14em] text-gold-dark">Шерегеш · посуточно</p>
            <h1 className="font-serif text-[38px] leading-[1.08] text-ink sm:text-[52px]">
              Дом на&nbsp;склоне,
              <br />
              <span className="italic text-gold-dark">а не номер в отеле</span>
            </h1>
            <p className="mt-4 max-w-[460px] text-[15px] leading-relaxed text-ink-soft">
              Свободные даты видны сразу. Бронь держится за вами, пока вы оплачиваете её напрямую владельцу — без комиссии с гостя.
            </p>
          </div>

          <form
            className="rounded-3xl border border-line bg-white p-5 shadow-soft sm:p-6"
            aria-label="Подбор дома"
            onSubmit={(e) => {
              e.preventDefault()
              onDates(checkIn, checkOut)
            }}
          >
            <div className="grid grid-cols-2 gap-3">
              <label className="flex flex-col gap-1.5 text-[13px] font-medium text-[#4A4038]">
                Заезд
                <input
                  type="date"
                  value={checkIn}
                  min={today}
                  onChange={(e) => onDates(e.target.value, checkOut && e.target.value && checkOut <= e.target.value ? '' : checkOut)}
                  className="min-h-[44px] rounded-xl border border-line bg-white px-3 text-sm text-ink outline-none focus:border-gold focus:ring-[3px] focus:ring-cream-deep"
                />
              </label>
              <label className="flex flex-col gap-1.5 text-[13px] font-medium text-[#4A4038]">
                Выезд
                <input
                  type="date"
                  value={checkOut}
                  min={checkIn ? addDays(checkIn, 1) : today}
                  onChange={(e) => onDates(checkIn, e.target.value)}
                  className="min-h-[44px] rounded-xl border border-line bg-white px-3 text-sm text-ink outline-none focus:border-gold focus:ring-[3px] focus:ring-cream-deep"
                />
              </label>
              <label className="flex flex-col gap-1.5 text-[13px] font-medium text-[#4A4038]">
                Гостей
                <input
                  type="number"
                  inputMode="numeric"
                  min={1}
                  max={MAX_GUESTS}
                  value={guests}
                  onChange={(e) => setGuests(e.target.value)}
                  className="min-h-[44px] rounded-xl border border-line bg-white px-3 text-sm text-ink outline-none focus:border-gold focus:ring-[3px] focus:ring-cream-deep"
                />
              </label>
              <label className="flex flex-col gap-1.5 text-[13px] font-medium text-[#4A4038]">
                Цена за ночь до, ₽
                <input
                  type="number"
                  inputMode="numeric"
                  min={1}
                  placeholder="любая"
                  value={maxPrice}
                  onChange={(e) => setMaxPrice(e.target.value)}
                  className="min-h-[44px] rounded-xl border border-line bg-white px-3 text-sm text-ink outline-none placeholder:text-muted focus:border-gold focus:ring-[3px] focus:ring-cream-deep"
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
                  setSp(new URLSearchParams())
                  setDateHint(null)
                }}
              >
                Сбросить фильтры
              </Button>
            )}
          </form>
        </div>
      </section>

      <section className="mx-auto max-w-[1180px] px-4 py-10 sm:px-8" aria-live="polite">
        {query.isLoading ? (
          <div className="grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
            <LoadingList rows={1} rowClass="h-[360px]" />
            <div className="hidden sm:block">
              <LoadingList rows={1} rowClass="h-[360px]" />
            </div>
            <div className="hidden lg:block">
              <LoadingList rows={1} rowClass="h-[360px]" />
            </div>
          </div>
        ) : query.isError && !list ? (
          <ErrorState message={getStayErrorMessage(query.error, 'Не удалось загрузить каталог.')} onRetry={() => void query.refetch()} />
        ) : !list || list.items.length === 0 ? (
          <EmptyState
            title={hasActiveFilters(filters) ? 'По этим условиям домов нет' : 'Пока нет домов в каталоге'}
            text={
              hasActiveFilters(filters)
                ? 'Попробуйте другие даты, меньше гостей или более высокую цену — или сбросьте фильтры.'
                : 'Как только владельцы опубликуют дома, они появятся здесь.'
            }
            action={
              hasActiveFilters(filters) ? (
                <Button variant="secondary" onClick={() => setSp(new URLSearchParams())}>
                  Сбросить фильтры
                </Button>
              ) : undefined
            }
          />
        ) : (
          <>
            <p className="mb-5 text-sm text-ink-soft">
              {filters.checkIn && filters.checkOut ? 'Свободно на ваши даты' : 'Все дома'}: {list.totalCount}
            </p>
            <ul className="grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
              {list.items.map((item) => (
                <li key={item.houseId}>
                  <HouseCard item={item} filters={filters} />
                </li>
              ))}
            </ul>
            <Pagination
              page={list.page}
              pageSize={list.pageSize || CATALOG_PAGE_SIZE}
              total={list.totalCount}
              hasNext={list.page * (list.pageSize || CATALOG_PAGE_SIZE) < list.totalCount}
              onPageChange={(page) => setSp(toSearchParams({ ...filters, page }))}
            />
          </>
        )}
      </section>

      <section className="mx-auto max-w-[1180px] px-4 pb-4 sm:px-8">
        <div className="grid gap-6 rounded-3xl bg-ink px-6 py-8 text-cream sm:grid-cols-[1.2fr_1fr] sm:items-center sm:px-10">
          <div>
            <h2 className="font-serif text-2xl sm:text-[28px]">Для владельцев</h2>
            <p className="mt-2 max-w-[460px] text-sm leading-relaxed text-cream/75">
              Шахматка, подтверждение оплаты по реквизитам, график уборок для горничной. Гости бронируют сами, деньги приходят
              вам напрямую.
            </p>
          </div>
          <div className="flex flex-wrap gap-3 sm:justify-end">
            <Link
              to={authed ? '/cabinet/new' : '/register?returnTo=%2Fcabinet%2Fnew'}
              className="inline-flex min-h-[44px] items-center gap-2 rounded-full bg-cream px-6 text-sm font-semibold !text-ink hover:bg-white"
            >
              <Icon name="plus" size={15} /> Подключить дома
            </Link>
            {authed && (
              <Link to="/cabinet" className="inline-flex min-h-[44px] items-center rounded-full border border-cream/30 px-6 text-sm font-semibold !text-cream hover:bg-white/10">
                Мой кабинет
              </Link>
            )}
          </div>
        </div>
      </section>
    </main>
  )
}
