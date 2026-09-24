import { buildTwoGisUrl, buildYandexMapsUrl } from '../../utils/mapLinks'
import { Icon } from '../ui/Icon'

interface Props {
  address?: string | null
  cityName?: string | null
  point?: { latitude: number; longitude: number } | null
}

/**
 * ARCHITECTURE_CYCLE13.md §205/§239 (US-132/133/139). Two plain links to third-party map services.
 *
 * §205.1 / §239 — five licence conditions, NOT stylistic preferences, checked by `CompanyMapLinks.test.tsx`:
 *   1. No embedded map, no map screenshot, anywhere.
 *   2. `target="_blank"` + `rel="noopener noreferrer"` on both links.
 *   3. No service logos/branding — text labels only, accessible names EXACTLY
 *      "Открыть в Яндекс Картах" / "Открыть в 2ГИС".
 *   4. No "интеграция"/"работает на"/"партнёр" wording anywhere near this component.
 *   5. The URL carries only the city and the address (enforced by `utils/mapLinks.ts`).
 */
export function CompanyMapLinks({ address, cityName, point }: Props) {
  if (!address) return null

  const target = { address, cityName, point }

  return (
    <div className="flex flex-wrap items-center gap-1">
      <a
        href={buildYandexMapsUrl(target)}
        target="_blank"
        rel="noopener noreferrer"
        className="min-h-[44px] px-3 inline-flex items-center gap-1.5 text-[13px] font-medium text-gold-dark hover:text-gold-darker rounded-full hover:bg-cream-deep transition-colors"
      >
        <Icon name="external-link" size={14} strokeWidth={1.7} />
        Открыть в Яндекс Картах
        <span className="sr-only"> (откроется в новой вкладке)</span>
      </a>
      <a
        href={buildTwoGisUrl(target)}
        target="_blank"
        rel="noopener noreferrer"
        className="min-h-[44px] px-3 inline-flex items-center gap-1.5 text-[13px] font-medium text-gold-dark hover:text-gold-darker rounded-full hover:bg-cream-deep transition-colors"
      >
        <Icon name="external-link" size={14} strokeWidth={1.7} />
        Открыть в 2ГИС
        <span className="sr-only"> (откроется в новой вкладке)</span>
      </a>
    </div>
  )
}
