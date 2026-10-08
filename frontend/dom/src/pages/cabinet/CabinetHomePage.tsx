import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { describeDevice, useWebPush } from '@/hooks/useWebPush'
import { Icon } from '@/components/ui/Icon'
import { CompanyLogoMark } from '@/components/company/CompanyLogoMark'
import { likelySameBrowserOnOtherSite } from '@/utils/staffPushTexts'
import { staysCompaniesApi } from '../../api/staysCompanies'
import { LinkButton } from '../../components/LinkButton'
import { EmptyState, ErrorState, LoadingList } from '../../components/StatePanels'
import { roleLabel } from '../../utils/permissions'
import { getStayErrorMessage } from '../../utils/stayError'

/** «Кабинет» — my «Дома» companies (owner or staff) plus pointers to the salons on ezbook and the shops on goods (§37.3.3 п. 3). */
export function CabinetHomePage() {
  const companies = useQuery({ queryKey: ['stays-my-companies'], queryFn: staysCompaniesApi.my })
  const summary = useQuery({ queryKey: ['kinds-summary'], queryFn: staysCompaniesApi.kindsSummary, retry: false })
  // A nudge for staff whose device has no booking notifications yet; also refreshes the worker's sibling origin.
  const push = useWebPush({ site: 'Stays', keepBrowserSubscription: true })
  const showPushNudge =
    push.isStaff === true &&
    push.hasStays &&
    !push.isLoading &&
    !push.devicesError &&
    push.reason === null &&
    !push.isSubscribedOnThisDevice &&
    !likelySameBrowserOnOtherSite(push.devices, 'Stays', describeDevice())

  const list = companies.data ?? []
  const isOwnerSomewhere = list.some((c) => c.myRole === 'Owner' || c.myRole === 'SuperAdmin')

  return (
    <main className="mx-auto max-w-[900px] px-4 pt-10 sm:px-8">
      <div className="mb-7 flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="font-serif text-[32px] text-ink">Мои компании</h1>
          <p className="mt-1 text-sm text-ink-soft">Компании «Дома», которыми вы владеете или где вы работаете сотрудником.</p>
        </div>
        <LinkButton to="/cabinet/new">
          <Icon name="plus" size={16} /> Подключить дома
        </LinkButton>
      </div>

      {companies.isLoading ? (
        <LoadingList rows={2} />
      ) : companies.isError ? (
        <ErrorState message={getStayErrorMessage(companies.error, 'Не удалось загрузить компании.')} onRetry={() => void companies.refetch()} />
      ) : list.length === 0 ? (
        <EmptyState
          title="У вас пока нет компаний"
          text="Создайте компанию — название, телефон для гостей и адрес на dom.ezbook.ru. Дома, цены и реквизиты — следующими шагами."
          action={<LinkButton to="/cabinet/new">Подключить дома</LinkButton>}
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
                  <p className="truncate text-xs text-muted">dom.ezbook.ru/{c.slug}</p>
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

      {summary.data && (summary.data.services.count > 0 || summary.data.orders.count > 0) && (
        <div className="mt-8 flex flex-col gap-1 text-sm text-ink-soft">
          {summary.data.services.count > 0 && (
            <p>
              Ваши салоны — на{' '}
              <a href={summary.data.services.siteUrl} className="font-medium text-gold hover:text-gold-dark">
                ezbook.ru
              </a>
              .
            </p>
          )}
          {summary.data.orders.count > 0 && (
            <p>
              Ваши магазины — на{' '}
              <a href={summary.data.orders.siteUrl} className="font-medium text-gold hover:text-gold-dark">
                goods.ezbook.ru
              </a>
              .
            </p>
          )}
        </div>
      )}
    </main>
  )
}
