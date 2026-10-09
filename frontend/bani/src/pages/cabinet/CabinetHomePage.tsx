import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { describeDevice, useWebPush } from '@/hooks/useWebPush'
import { Icon } from '@/components/ui/Icon'
import { CompanyLogoMark } from '@/components/company/CompanyLogoMark'
import { LinkButton } from '@/components/slots/ui/LinkButton'
import { EmptyState, ErrorState, LoadingList } from '@/components/slots/ui/StatePanels'
import { getStayErrorMessage } from '@/utils/slots/slotError'
import { likelySameBrowserOnOtherSite } from '@/utils/staffPushTexts'
import { bathsCompaniesApi } from '../../api/bathsCompanies'
import { roleLabel } from '../../cabinet/cabinetTabs'

/** «Кабинет» — my «Бани» companies (owner or staff) plus pointers to the salons and shops of the same person on the other sites (no word of houses on bani, LEGAL_REVIEW_CYCLE42.md §10). */
export function CabinetHomePage() {
  const companies = useQuery({ queryKey: ['baths-my-companies'], queryFn: bathsCompaniesApi.my })
  const summary = useQuery({ queryKey: ['kinds-summary'], queryFn: bathsCompaniesApi.kindsSummary, retry: false })
  // A nudge for staff whose device has no booking notifications yet; also refreshes the worker's sibling origin.
  const push = useWebPush({ site: 'Baths', keepBrowserSubscription: true })
  const showPushNudge =
    push.isStaff === true &&
    push.hasBaths &&
    !push.isLoading &&
    !push.devicesError &&
    push.reason === null &&
    !push.isSubscribedOnThisDevice &&
    !likelySameBrowserOnOtherSite(push.devices, 'Baths', describeDevice())

  const list = companies.data ?? []
  const isOwnerSomewhere = list.some((c) => c.myRole === 'Owner' || c.myRole === 'SuperAdmin')
  const others = [
    { key: 'services', what: 'салоны', item: summary.data?.services },
    { key: 'orders', what: 'магазины', item: summary.data?.orders },
  ].filter((o) => o.item && o.item.count > 0)

  return (
    <main className="mx-auto max-w-[900px] px-4 pt-10 sm:px-8">
      <div className="mb-7 flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="font-serif text-[32px] text-ink">Мои компании</h1>
          <p className="mt-1 text-sm text-ink-soft">Компании «Бани», которыми вы владеете или где вы работаете сотрудником.</p>
        </div>
        <LinkButton to="/cabinet/new">
          <Icon name="plus" size={16} /> Подключить баню
        </LinkButton>
      </div>

      {companies.isLoading ? (
        <LoadingList rows={2} />
      ) : companies.isError ? (
        <ErrorState message={getStayErrorMessage(companies.error, 'Не удалось загрузить компании.')} onRetry={() => void companies.refetch()} />
      ) : list.length === 0 ? (
        <EmptyState
          title="У вас пока нет компаний"
          text="Создайте компанию — название, город, телефон для гостей и адрес на bani.ezbook.ru. Бани, цены и реквизиты — следующими шагами."
          action={<LinkButton to="/cabinet/new">Подключить баню</LinkButton>}
        />
      ) : (
        <ul className="grid gap-4 sm:grid-cols-2">
          {list.map((c) => (
            <li key={c.id}>
              <Link
                to={`/cabinet/${c.id}`}
                className="flex h-full items-center gap-4 rounded-2xl border border-line bg-white p-5 !text-ink transition-all hover:border-line-strong hover:shadow-soft"
              >
                <CompanyLogoMark name={c.name} logoUrl={c.logoUrl} size="catalog" />
                <div className="min-w-0 flex-1">
                  <p className="truncate font-semibold">{c.name}</p>
                  <p className="truncate text-xs text-muted">bani.ezbook.ru/{c.slug}</p>
                  <p className="mt-1.5 text-xs text-ink-soft">{roleLabel(c.myRole)}</p>
                  {!c.acceptingBookings && <p className="mt-0.5 text-xs font-medium text-warning">Гости пока не могут бронировать</p>}
                </div>
                {!!c.awaitingPaymentCount && c.awaitingPaymentCount > 0 && (
                  <span
                    className="inline-flex min-w-[24px] items-center justify-center rounded-full bg-gold px-2 text-xs font-bold leading-6 text-white"
                    aria-label={`ожидают проверки оплаты: ${c.awaitingPaymentCount}`}
                  >
                    {c.awaitingPaymentCount}
                  </span>
                )}
              </Link>
            </li>
          ))}
        </ul>
      )}

      {showPushNudge && (
        <p className="mt-8 text-sm text-ink-soft" data-testid="push-nudge">
          Уведомления о новых бронях на этом устройстве не включены —{' '}
          <Link to="/profile#devices" className="font-medium text-gold hover:text-gold-dark">
            включить в профиле
          </Link>
          .
        </p>
      )}

      {isOwnerSomewhere && (
        <nav aria-label="Аккаунт" className="mt-8 flex flex-wrap gap-3">
          <Link
            to="/cabinet/subscription"
            className="inline-flex min-h-[44px] items-center gap-2 rounded-full border border-line bg-white px-5 text-sm font-semibold !text-ink hover:border-line-strong"
          >
            <Icon name="credit-card" size={15} strokeWidth={1.8} /> Подписка
          </Link>
        </nav>
      )}

      {others.length > 0 && (
        <div className="mt-8 flex flex-col gap-1 text-sm text-ink-soft">
          {others.map((o) => (
            <p key={o.key}>
              Ваши {o.what} — на{' '}
              <a href={o.item!.siteUrl} className="font-medium text-gold hover:text-gold-dark">
                {o.item!.siteUrl.replace(/^https?:\/\//, '')}
              </a>
              .
            </p>
          ))}
        </div>
      )}
    </main>
  )
}
