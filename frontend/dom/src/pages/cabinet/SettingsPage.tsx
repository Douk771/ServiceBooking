import { useLocation } from 'react-router-dom'
import { ArrivalReminderCard } from '../../components/cabinet/ArrivalReminderCard'
import { PaymentCard } from '../../components/cabinet/PaymentCard'
import { ProviderCard } from '../../components/cabinet/ProviderCard'
import { RulesCard } from '../../components/cabinet/RulesCard'
import { StaysProfileCard } from '../../components/cabinet/StaysProfileCard'
import { useStaysCompany } from '../../hooks/useStaysCompany'
import type { StaysTrialOutcomeDto } from '../../types'
import { can } from '../../utils/permissions'
import { NotFoundPage } from '../NotFoundPage'

/**
 * `/cabinet/:companyId/settings` (`ManageCompany`): profile, rules, prepayment, cancellation, check-in information, requisites,
 * the provider. After creating a company the page shows the result of the trial request that went with it.
 */
export function SettingsPage() {
  const { company, refresh } = useStaysCompany()
  const trial = (useLocation().state as { trial?: StaysTrialOutcomeDto | null } | null)?.trial ?? null

  if (!can(company.myPermissions, 'ManageCompany') || !company.settings) {
    return <NotFoundPage title="Раздел недоступен" hint="Настройки компании открыты владельцу." />
  }

  return (
    <main className="mx-auto flex max-w-[760px] flex-col gap-6 px-4 pb-6 pt-8 sm:px-8">
      {trial && (
        <p role="status" className={`rounded-2xl px-5 py-3 text-sm ${trial.granted ? 'bg-success-bg text-success' : 'bg-warning-bg text-warning'}`} data-testid="trial-note">
          {trial.granted ? 'Пробный период начат.' : (trial.message ?? 'Пробный период не начат.')}
        </p>
      )}
      <StaysProfileCard key={`profile-${company.id}`} company={company} onSaved={refresh} />
      <RulesCard key={`rules-${company.id}`} company={company} onSaved={refresh} />
      <ArrivalReminderCard key={`reminder-${company.id}`} companyId={company.id} />
      <PaymentCard key={`pay-${company.id}`} company={company} onSaved={refresh} />
      <ProviderCard key={`provider-${company.id}`} company={company} onSaved={refresh} />
    </main>
  )
}
