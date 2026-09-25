import { Icon } from '../ui/Icon'

interface Props {
  yandexUrl?: string | null
  twoGisUrl?: string | null
}

/**
 * ARCHITECTURE_CYCLE15.md §253/§285 (US-132/133/139, cycle 13; rewritten cycle 15). Two plain links
 * to third-party map services, now built entirely by the owner — the product no longer assembles
 * any URL itself (`utils/mapLinks.ts` is gone, §253.1).
 *
 * §285 — five licence conditions, NOT stylistic preferences, checked by `CompanyMapLinks.test.tsx`:
 *   1. No embedded map, no map screenshot, anywhere.
 *   2. `target="_blank"` + `rel="noopener noreferrer"` on both links.
 *   3. No service logos/branding — text labels only, accessible names EXACTLY
 *      "Открыть в Яндекс Картах" / "Открыть в 2ГИС".
 *   4. No "интеграция"/"работает на"/"партнёр" wording anywhere near this component.
 *   5. `href` equals the saved value byte for byte — we never append `utm`, ids, or metrics to the
 *      owner's own link (§253.4 п. 5, §285 п. 7 — the reformulated cycle-13 condition).
 *
 * Server-side validation (`MapLinkValidation.cs`) is the only gate on what URL can end up here; this
 * component trusts `company.yandexMapsUrl`/`twoGisUrl` completely and renders nothing when a field
 * is empty (§285 п. 1/2 — an unfilled field means no button at all, not a disabled one).
 */
export function CompanyMapLinks({ yandexUrl, twoGisUrl }: Props) {
  if (!yandexUrl && !twoGisUrl) return null

  return (
    <div className="flex flex-wrap items-center gap-1">
      {yandexUrl && (
        <a
          href={yandexUrl}
          target="_blank"
          rel="noopener noreferrer"
          className="min-h-[44px] px-3 inline-flex items-center gap-1.5 text-[13px] font-medium text-gold-dark hover:text-gold-darker rounded-full hover:bg-cream-deep transition-colors"
        >
          <Icon name="external-link" size={14} strokeWidth={1.7} />
          Открыть в Яндекс Картах
          <span className="sr-only"> (откроется в новой вкладке)</span>
        </a>
      )}
      {twoGisUrl && (
        <a
          href={twoGisUrl}
          target="_blank"
          rel="noopener noreferrer"
          className="min-h-[44px] px-3 inline-flex items-center gap-1.5 text-[13px] font-medium text-gold-dark hover:text-gold-darker rounded-full hover:bg-cream-deep transition-colors"
        >
          <Icon name="external-link" size={14} strokeWidth={1.7} />
          Открыть в 2ГИС
          <span className="sr-only"> (откроется в новой вкладке)</span>
        </a>
      )}
    </div>
  )
}
