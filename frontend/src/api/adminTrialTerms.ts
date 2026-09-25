import { api } from './client'
import type { components } from '../types/api-cycle18.generated'

type Schemas = components['schemas']

export type TrialTermsDto = Schemas['TrialTermsDto']

/**
 * GET /api/admin/trial-terms/{version} — API_CONTRACT_CYCLE18.md §376. Archival edition of the
 * activation-terms text by version: what proves "the owner was informed" months after a dispute
 * (the grant journal stores the version + sha256, this route resolves them back into the actual
 * template text). SuperAdmin-only; 404 when the version isn't in the registry.
 */
export const adminTrialTermsApi = {
  getVersion: (version: string): Promise<TrialTermsDto> =>
    api.get<TrialTermsDto>(`/admin/trial-terms/${encodeURIComponent(version)}`).then((r) => r.data),
}
