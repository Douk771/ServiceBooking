import { api } from './client'
import type { components } from '../types/api-cycle7.generated'
import type { components as Cycle18Schemas } from '../types/api-cycle18.generated'

type Schemas = components['schemas']
type Cycle18 = Cycle18Schemas['schemas']

/** 152-ФЗ ч. 7 ст. 5 prohibits indefinite retention of personal data; `Forever` was removed from
 *  the contract (`contracts/cycle7/openapi.yaml`) to match `RemovePhotoRetentionForever`, which
 *  migrated existing `Forever` plans to `TwelveMonths` server-side. */
export type PhotoRetention = Schemas['PhotoRetention']
export type OptionAvailability = Schemas['OptionAvailability']

/** Full admin-facing plan, including the highlight bullets and the plan→option availability
 *  matrix (B4) — kept in sync with the generated AdminPlanDto rather than hand-duplicated. */
/** Cycle 7 shape + `isSystemTrial`/`optionCoverage` (cycle 18, additive — API_CONTRACT_CYCLE18.md
 *  §366). Intersection rather than a second hand-written DTO copy, same convention as elsewhere. */
export type PlanConfig = Schemas['AdminPlanDto'] & Cycle18['AdminPlanDtoTrialPatch']
export type PlanOptionRuleDto = Schemas['PlanOptionRuleDto']
export type AdminPlanInput = Schemas['AdminPlanInput']
export type AdminOptionDto = Schemas['AdminOptionDto']

export const plansApi = {
  list: () => api.get<{ plans: PlanConfig[] }>('/admin/plans').then((r) => r.data.plans),
  create: (data: AdminPlanInput) => api.post<PlanConfig>('/admin/plans', data).then((r) => r.data),
  update: (id: string, data: AdminPlanInput) =>
    api.put<PlanConfig>(`/admin/plans/${id}`, data).then((r) => r.data),
  deactivate: (id: string) => api.delete(`/admin/plans/${id}`),
  /** PUT /api/admin/plans/{id}/system-free — separate from the ordinary field-edit PUT on purpose
   *  (contract note on AdminPlanInput: no `isSystemFree` field there at all), so this flag can only
   *  ever change here, never as an accidental side effect of an unrelated plan edit. */
  setSystemFree: (id: string, isSystemFree: boolean) =>
    api.put<PlanConfig>(`/admin/plans/${id}/system-free`, { isSystemFree }).then((r) => r.data),
  /** GET /api/admin/options — catalog used to build the plan↔option availability matrix (US-66). */
  listOptions: () => api.get<{ options: AdminOptionDto[] }>('/admin/options').then((r) => r.data.options),
  /** PUT /api/admin/plans/{id}/system-trial (§366) — same isolation as setSystemFree above:
   *  `AdminPlanInput` deliberately has no `isSystemTrial` field, so an ordinary field edit can never
   *  move this flag by accident. 400 (price != 0), 409 (another plan already flagged / this plan is
   *  the system-free one / index race), 404 (no such plan). */
  setSystemTrial: (id: string, isSystemTrial: boolean) =>
    api.put<PlanConfig>(`/admin/plans/${id}/system-trial`, { isSystemTrial }).then((r) => r.data),
}
