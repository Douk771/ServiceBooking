import { useParams, Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { clientConsentsApi } from '../api/clientConsents'
import { useLegalText } from '../hooks/useLegalText'
import { findSection, splitLegalSections } from '../utils/legalSections'
import { applyLegalRuntimeValues } from '../utils/legalRuntimeValues'
import { blankLineForPrint, saveLastPrintedHealthForm } from '../utils/healthConsentForm'
import { useAuthStore } from '../store/authStore'
import { Button } from '../components/ui/Button'
import { Icon } from '../components/ui/Icon'

/**
 * `/companies/:companyId/clients/:clientKey/health-consent-form` — API_CONTRACT_CYCLE20.md §432.4
 * (US-20-01, Т20-04). Printed from the browser (`window.print()`), never generated/stored server-side
 * (§443: "ни одного маршрута, сохраняющего сгенерированный бланк"). Every reload/refetch of the values
 * gets a BRAND NEW `formId` from the server — that's by design (§432.4), which is why this page remembers
 * the one it last *printed* (`saveLastPrintedHealthForm`, in-memory only — see §441 item 2 note there)
 * for the mark-consent dialog back on the client card to read, rather than the server ever being asked
 * "what was the last one". Recorded at the moment "Печать" is clicked, not on every fetch/refetch of
 * this page — "last printed" must mean printed, not merely opened/reloaded.
 */
export function HealthConsentFormPrintPage() {
  const { companyId = '', clientKey: rawClientKey = '' } = useParams<{ companyId: string; clientKey: string }>()
  const clientKey = decodeURIComponent(rawClientKey)
  const isOwner = useAuthStore((s) => s.hasRole('CompanyOwner'))

  const { data, isLoading, isError, refetch, isFetching } = useQuery({
    queryKey: ['health-consent-form', companyId, clientKey],
    queryFn: () => clientConsentsApi.getHealthConsentForm(companyId, clientKey),
    staleTime: 0,
    refetchOnWindowFocus: false,
    retry: false,
  })

  const { data: text, isLoading: textLoading } = useLegalText('HealthDataWrittenConsentForm')

  const handlePrint = () => {
    if (data) saveLastPrintedHealthForm(companyId, clientKey, { formId: data.formId, textVersion: data.textVersion })
    window.print()
  }

  const section = text ? findSection(splitLegalSections(text.contentHtml), 'Бланк') : null
  const html = section ? section.html : text?.contentHtml
  const printableHtml = html && data ? applyLegalRuntimeValues(html, blankLineForPrint(data.runtimeValues)) : null

  return (
    <div className="max-w-[820px] mx-auto px-4 py-8">
      <div className="flex items-center justify-between mb-6 print:hidden">
        <h1 className="text-xl font-serif font-medium text-ink">Бланк письменного согласия</h1>
        <div className="flex gap-2">
          <Button variant="secondary" size="sm" loading={isFetching} onClick={() => refetch()}>
            Получить новый бланк
          </Button>
          <Button size="sm" disabled={!printableHtml} onClick={handlePrint}>
            <Icon name="printer" size={14} strokeWidth={1.8} />
            Печать
          </Button>
        </div>
      </div>

      {(isLoading || textLoading) && <div className="h-64 bg-cream-deep rounded-2xl animate-pulse print:hidden" />}

      {isError && (
        <p className="text-sm text-danger print:hidden">Не удалось получить бланк. Попробуйте ещё раз.</p>
      )}

      {data?.operatorDetailsMissing && (
        <div className="mb-4 text-sm text-warning bg-warning-bg rounded-xl px-4 py-3 print:hidden">
          В бланке не заполнены реквизиты оператора персональных данных — соответствующие строки будут напечатаны линией
          для заполнения от руки.{' '}
          {isOwner ? (
            <Link to="/billing" className="underline font-medium">
              Заполнить в разделе «Ваша подписка»
            </Link>
          ) : (
            'Попросите владельца компании заполнить их в разделе «Ваша подписка».'
          )}
        </div>
      )}

      {printableHtml ? (
        // A4, ч/б — печать браузером (US-20-01, ARCHITECTURE_CYCLE20.md §401). No passport-data
        // inputs anywhere on this page (§441 item 2) — the HTML comes verbatim from the manifest,
        // which the contract itself guarantees contains none (§443).
        <div
          className="legal-content bg-white text-ink text-sm leading-relaxed print:text-black [&_h2]:hidden [&_p]:mb-2 [&_table]:w-full"
          style={{ colorAdjust: 'exact' } as React.CSSProperties}
          dangerouslySetInnerHTML={{ __html: printableHtml }}
        />
      ) : (
        !isLoading && !textLoading && !isError && <p className="text-sm text-danger print:hidden">Текст бланка недоступен.</p>
      )}
    </div>
  )
}
