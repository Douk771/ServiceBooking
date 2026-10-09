import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { BillingPage } from '@/pages/BillingPage'
import { Button } from '@/components/ui/Button'
import { InlineError } from '@/components/ui/InlineError'
import { fmtDate } from '@/utils/dateFormat'
import { bathsCompaniesApi } from '../../api/bathsCompanies'
import { isTrialTermsMismatch, trialErrorMessage } from '../../cabinet/trialOutcome'

/**
 * `/cabinet/subscription` (owner) — «Ваша подписка» of the «Бани» line. The screen is ezbook's own component (ARCHITECTURE_CYCLE42.md §42.12.3):
 * same texts and request flow, `line="Baths"` switches it to resources and the plans from `availablePlans`. Above it, the trial of the line:
 * the conditions first, then the button.
 */
export function SubscriptionPage() {
  return (
    <>
      <BathsTrialSection />
      <BillingPage line="Baths" />
    </>
  )
}

function BathsTrialSection() {
  const qc = useQueryClient()
  const trial = useQuery({ queryKey: ['baths-trial'], queryFn: bathsCompaniesApi.trialState, retry: false })
  const start = useMutation({
    mutationFn: (version: string) => bathsCompaniesApi.activateTrial(version),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['baths-trial'] })
      void qc.invalidateQueries({ queryKey: ['owner-subscription'] })
      void qc.invalidateQueries({ queryKey: ['baths-company'] })
    },
    onError: (err) => {
      // The terms moved on: show the CURRENT text, never resend the stale version.
      if (isTrialTermsMismatch(err)) void qc.invalidateQueries({ queryKey: ['baths-trial'] })
    },
  })

  const t = trial.data
  if (!t || !t.offered) return null

  return (
    <div className="mx-auto max-w-[860px] px-4 pt-16 sm:px-8" data-testid="baths-trial">
      <section className="rounded-3xl border border-line bg-white p-6" aria-labelledby="trial-title">
        <h2 id="trial-title" className="font-serif text-[22px] text-ink">
          Пробный период
        </h2>
        {t.eligible && t.termsVersion ? (
          <>
            <p className="mt-1 text-sm text-ink-soft">Длительность пробного периода — {t.durationDays} дней. Условия:</p>
            {t.termsText && <p className="mt-3 whitespace-pre-line rounded-xl bg-cream-deep px-4 py-3 text-xs text-ink-soft">{t.termsText}</p>}
            {start.isError && (
              <div className="mt-3">
                <InlineError>{trialErrorMessage(start.error)}</InlineError>
              </div>
            )}
            <Button className="mt-4 min-h-[44px]" loading={start.isPending} onClick={() => start.mutate(t.termsVersion!)}>
              Начать пробный период
            </Button>
          </>
        ) : (
          <p className="mt-1 text-sm text-ink-soft">
            {t.message ?? 'Пробный период для вашего аккаунта недоступен.'}
            {t.endsAtUtc && ` Действует до ${fmtDate(t.endsAtUtc)}.`}
          </p>
        )}
      </section>
    </div>
  )
}
