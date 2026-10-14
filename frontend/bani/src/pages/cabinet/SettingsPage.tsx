import { useEffect } from 'react'
import { useLocation } from 'react-router-dom'
import { can } from '@/utils/slots/slotPermissions'
import { useBathsCompany } from '../../cabinet/cabinetVertical'
import type { TrialOutcomeDto } from '../../cabinet/types'
import { BathsProfileCard } from '../../components/cabinet/BathsProfileCard'
import { BookingRulesCard } from '../../components/cabinet/BookingRulesCard'
import { PaymentCard } from '../../components/cabinet/PaymentCard'
import { PhotosCard } from '../../components/cabinet/PhotosCard'
import { ProviderCard } from '../../components/cabinet/ProviderCard'
import { NotFoundPage } from '../NotFoundPage'

/**
 * `/cabinet/:companyId/settings` (`ManageCompany`): profile, photos of the complex, requisites, the provider, booking and the reminder.
 * After creating a company the page shows the result of the trial request that went with it. The checklist of the layout links here with an
 * anchor (`#payment-details`, `#provider`).
 */
export function SettingsPage() {
  const { company, refresh } = useBathsCompany()
  const { state, hash } = useLocation()
  const trial = (state as { trial?: TrialOutcomeDto | null } | null)?.trial ?? null

  useEffect(() => {
    if (hash) document.getElementById(hash.slice(1))?.scrollIntoView({ block: 'start' })
  }, [hash, company.id])

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
      <BathsProfileCard key={`profile-${company.id}`} company={company} onSaved={refresh} />
      <PhotosCard key={`photos-${company.id}`} companyId={company.id} onChanged={refresh} />
      <PaymentCard key={`pay-${company.id}`} company={company} onSaved={refresh} />
      <ProviderCard key={`provider-${company.id}`} company={company} onSaved={refresh} />
      <BookingRulesCard key={`rules-${company.id}`} company={company} settings={company.settings} onSaved={refresh} />
    </main>
  )
}
