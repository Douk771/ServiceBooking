import { useEffect, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { companiesApi } from '../api/companies'
import { CityCombobox } from '../components/ui/CityCombobox'
import { Icon } from '../components/ui/Icon'
import { PricingTeaser } from '../components/pricing/PricingTeaser'
import type { City, Company } from '../types'
import salonHero from '../assets/salon-hero.jpg'

// US-115: the visitor's city choice persists across visits (SPEC §115 п. 4).
const HOME_CITY_KEY = 'ezbook_home_city'

function loadStoredCity(): City | null {
  try {
    const raw = localStorage.getItem(HOME_CITY_KEY)
    return raw ? (JSON.parse(raw) as City) : null
  } catch {
    return null
  }
}

function CompanyCard({ company }: { company: Company }) {
  return (
    <Link
      to={`/company/${company.slug}`}
      className="block bg-white border border-line rounded-[20px] p-[26px] transition-all duration-200 hover:shadow-card hover:-translate-y-[3px] hover:border-line-strong"
    >
      <div className="flex items-start gap-4 mb-4">
        {company.logoUrl ? (
          <img src={company.logoUrl} alt={company.name} className="w-14 h-14 rounded-2xl object-cover shrink-0" />
        ) : (
          <div className="w-14 h-14 rounded-2xl bg-cream-deep flex items-center justify-center shrink-0">
            <Icon name="store" size={24} className="text-gold-dark" />
          </div>
        )}
        <div className="min-w-0">
          <h3 className="font-serif text-[19px] font-medium text-ink truncate">{company.name}</h3>
        </div>
      </div>
      {company.description && (
        <p className="text-sm leading-[1.55] text-ink-soft mb-4 line-clamp-2">{company.description}</p>
      )}
      <div className="flex items-center justify-between gap-3 pt-3.5 border-t border-cream-deep">
        <div className="flex items-center gap-1.5 text-[13px] text-ink-soft min-w-0">
          {company.address && (
            <>
              <Icon name="map-pin" size={14} strokeWidth={1.6} className="shrink-0" />
              <span className="truncate">{company.address}</span>
            </>
          )}
        </div>
        {company.onlineBookingEnabled && (
          <span className="inline-flex items-center gap-1 text-xs font-semibold text-success bg-success-bg px-2.5 py-1 rounded-full shrink-0">
            <Icon name="check" size={11} strokeWidth={2.2} />
            Онлайн-запись
          </span>
        )}
      </div>
    </Link>
  )
}

const howItWorks = [
  {
    icon: 'user',
    title: 'Выберите специалиста',
    text: 'Смотрите профили мастеров, их специализацию и отзывы клиентов.',
  },
  {
    icon: 'calendar',
    title: 'Выберите удобное время',
    text: 'Актуальные свободные слоты — записывайтесь на сегодня или на неделю вперёд.',
  },
  {
    icon: 'check-circle',
    title: 'Получите подтверждение',
    text: 'Мгновенное подтверждение записи и напоминание накануне визита.',
  },
] as const

export function HomePage() {
  const [city, setCity] = useState<City | null>(() => loadStoredCity())
  const [search, setSearch] = useState('')
  // Debounce so the search-by-name/address query (server-side, per SPEC §115) isn't re-fired per
  // keystroke.
  const [debouncedSearch, setDebouncedSearch] = useState('')

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSearch(search.trim()), 300)
    return () => clearTimeout(timer)
  }, [search])

  useEffect(() => {
    if (city) {
      localStorage.setItem(HOME_CITY_KEY, JSON.stringify(city))
    } else {
      localStorage.removeItem(HOME_CITY_KEY)
    }
  }, [city])

  const {
    data: page,
    isLoading,
    isError,
  } = useQuery({
    queryKey: ['companies', 'public', city?.id ?? null, debouncedSearch],
    queryFn: () =>
      companiesApi.getPublic({
        cityId: city?.id,
        search: debouncedSearch || undefined,
        pageSize: 100,
      }),
  })
  const companies = page?.items

  return (
    <div>
      {/* Hero */}
      <section className="max-w-[1180px] mx-auto px-8 pt-[88px] pb-24 grid md:grid-cols-[1.05fr_0.95fr] gap-16 items-center">
        <div>
          <div className="inline-flex items-center gap-2 border border-line-strong text-gold-dark px-4 py-[7px] rounded-full text-[13px] font-semibold mb-7">
            <Icon name="check-circle" size={14} strokeWidth={1.8} />
            Онлайн-запись за пару минут
          </div>
          <h1 className="font-serif text-[44px] md:text-[60px] leading-[1.08] font-medium mb-6 -tracking-[0.01em] text-ink">
            Красота и уход,
            <br />
            <em className="text-gold-dark not-italic italic">подобранные под вас</em>
          </h1>
          <p className="text-lg leading-[1.6] text-ink-soft max-w-[460px] mb-9">
            Найдите проверенного мастера, выберите удобное время и получите подтверждение записи мгновенно — без звонков
            и ожидания.
          </p>
          <div className="flex items-center gap-7 flex-wrap">
            <a
              href="#companies"
              className="inline-flex items-center gap-2.5 bg-ink hover:bg-ink/90 text-cream px-7 py-[15px] rounded-full text-[15px] font-semibold transition-colors"
            >
              Найти специалиста
              <Icon name="arrow-right" size={16} strokeWidth={1.8} />
            </a>
            <a href="#how" className="text-[15px] font-medium text-ink border-b border-ink">
              Как это работает
            </a>
          </div>
        </div>
        <div className="hidden md:block w-full aspect-[4/5] rounded-[28px] border border-line overflow-hidden">
          <img src={salonHero} alt="Интерьер салона" className="w-full h-full object-cover" />
        </div>
      </section>

      {/* How it works */}
      <section id="how" className="bg-cream-deep py-[72px] px-8">
        <div className="max-w-[1180px] mx-auto grid md:grid-cols-3 gap-10">
          {howItWorks.map((item) => (
            <div key={item.title}>
              <div className="w-[46px] h-[46px] rounded-full bg-cream border border-line-strong flex items-center justify-center mb-5">
                <Icon name={item.icon} size={20} strokeWidth={1.6} className="text-gold-dark" />
              </div>
              <h3 className="font-serif text-[21px] font-medium mb-2.5 text-ink">{item.title}</h3>
              <p className="text-[15px] leading-[1.6] text-ink-soft">{item.text}</p>
            </div>
          ))}
        </div>
      </section>

      <PricingTeaser />

      {/* Companies */}
      <section id="companies" className="max-w-[1180px] mx-auto px-8 pt-[88px] pb-[100px]">
        <div className="flex items-baseline justify-between mb-6 flex-wrap gap-3">
          <div>
            <h2 className="font-serif text-[32px] font-medium mb-2 text-ink">
              {city ? `Компании и салоны — ${city.name}` : 'Компании и салоны'}
            </h2>
            <p className="text-[15px] text-ink-soft">
              {city ? `Показаны салоны в городе «${city.label}»` : 'Проверенные специалисты на платформе, все города'}
            </p>
          </div>
          <div className="relative">
            <Icon
              name="search"
              size={17}
              strokeWidth={1.8}
              className="absolute left-4 top-1/2 -translate-y-1/2 text-gold-dark"
            />
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Поиск по названию или адресу"
              className="w-[280px] pl-[42px] pr-4 py-3 rounded-full border border-line bg-white text-sm outline-none text-ink focus:border-gold focus:ring-[3px] focus:ring-cream-deep"
            />
          </div>
        </div>

        <div className="flex items-end gap-3 mb-10 flex-wrap">
          <div className="w-[280px]">
            <CityCombobox label="" value={city} onChange={setCity} placeholder="Выберите город" />
          </div>
          {city && (
            <button
              type="button"
              onClick={() => setCity(null)}
              className="text-[13px] font-medium text-ink-soft border-b border-line-strong pb-[3px] hover:text-ink"
            >
              Все города
            </button>
          )}
        </div>

        {isLoading ? (
          <div className="grid gap-6" style={{ gridTemplateColumns: 'repeat(auto-fill, minmax(320px, 1fr))' }}>
            {Array.from({ length: 6 }).map((_, i) => (
              <div key={i} className="h-40 bg-cream-deep rounded-[20px] animate-pulse" />
            ))}
          </div>
        ) : isError ? (
          <div className="text-center py-16 text-muted">
            <Icon name="alert-circle" size={36} strokeWidth={1.4} className="mx-auto mb-3" />
            <p className="text-lg">Не удалось загрузить список компаний. Попробуйте обновить страницу.</p>
          </div>
        ) : companies && companies.length > 0 ? (
          <div className="grid gap-6" style={{ gridTemplateColumns: 'repeat(auto-fill, minmax(320px, 1fr))' }}>
            {companies.map((c) => (
              <CompanyCard key={c.id} company={c} />
            ))}
          </div>
        ) : city ? (
          <div className="text-center py-16 text-muted">
            <Icon name="store" size={36} strokeWidth={1.4} className="mx-auto mb-3" />
            <p className="text-lg">В городе «{city.name}» пока нет компаний на платформе.</p>
            <button
              type="button"
              onClick={() => setCity(null)}
              className="mt-4 text-[14px] font-medium text-gold-dark border-b border-gold-dark"
            >
              Показать все города
            </button>
          </div>
        ) : (
          <div className="text-center py-16 text-muted">
            <Icon name="store" size={36} strokeWidth={1.4} className="mx-auto mb-3" />
            <p className="text-lg">Компании пока не добавлены</p>
          </div>
        )}
      </section>

      {/* Footer */}
      <footer className="bg-cream-deep border-t border-line">
        <div className="max-w-[1180px] mx-auto px-8 py-10 flex items-center justify-between flex-wrap gap-4">
          <span className="font-serif text-[17px] text-ink">EZBOOK</span>
          <span className="text-[13px] text-muted">© 2026 EZBOOK. Все права защищены.</span>
        </div>
      </footer>
    </div>
  )
}
