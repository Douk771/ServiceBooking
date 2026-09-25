/**
 * ARCHITECTURE_CYCLE15.md §253.2/§283 — `PUT /api/companies/{id}` answers with ONE bare-text 400
 * when `yandexMapsUrl`/`twoGisUrl`/`clientRescheduleMinHours` fails validation (server checks
 * `yandexMapsUrl` first, then `twoGisUrl` — §283 "по порядку"). The contract requires the message to
 * be shown next to the field that caused it (accessibility: a programmatic association, not just
 * colour), so this routes the server's own sentence to the right form field by matching the fixed
 * texts from §283 — the mapper does not invent wording, it only decides *where* the server's own
 * sentence goes.
 *
 * A generic "Ссылка должна начинаться с https://" / "слишком длинная" message does not say which of
 * the two link fields failed. Since the server validates `yandexMapsUrl` first (§283), an ambiguous
 * link message is attributed to that field — the one place it's guaranteed to be correct when only
 * one link field is filled in, and the more likely guess otherwise.
 */
export type MapLinksErrorField = 'yandexMapsUrl' | 'twoGisUrl' | 'clientRescheduleMinHours' | null

export function mapLinksFieldError(serverText: string | undefined | null): MapLinksErrorField {
  if (!serverText) return null
  if (serverText.includes('2ГИС')) return 'twoGisUrl'
  if (serverText.includes('Яндекс')) return 'yandexMapsUrl'
  if (serverText.includes('Окно переноса')) return 'clientRescheduleMinHours'
  if (serverText.includes('Ссылка')) return 'yandexMapsUrl'
  return null
}
