import type { BathsCompanyManageDto, ChecklistItemDto } from './types'

type Code = ChecklistItemDto['code']

/** Where each unfinished step of the checklist is done. A path starting with `/` is absolute, others are relative to `/cabinet/:companyId/`. */
export const CHECKLIST_TARGETS: Record<Code, { to: string; label: string }> = {
  ProfileFilled: { to: 'settings', label: 'Заполнить профиль' },
  PaymentDetails: { to: 'settings#payment-details', label: 'Указать реквизиты' },
  ProviderInfo: { to: 'settings#provider', label: 'Указать исполнителя' },
  ResourcePublished: { to: 'resources', label: 'Опубликовать баню' },
  Plan: { to: '/cabinet/subscription', label: 'Выбрать тариф' },
}

/** The path to open for a step of the checklist. */
export function checklistPath(companyId: string, code: Code): string {
  const to = CHECKLIST_TARGETS[code].to
  return to.startsWith('/') ? to : `/cabinet/${companyId}/${to}`
}

export const pendingChecklist = (company: Pick<BathsCompanyManageDto, 'checklist'>): ChecklistItemDto[] => company.checklist.filter((c) => !c.done)

/** The heading over the unfinished steps: the contract's wording when guests really cannot book now, a neutral one otherwise. */
export function checklistTitle(gateAccepting: boolean): string {
  return gateAccepting ? 'Что осталось настроить' : 'Гости не могут бронировать, потому что…'
}

/** Red for the states in which the gate is shut by the plan, amber for the warnings that come before. */
export function planBannerTone(level: BathsCompanyManageDto['plan']['warningLevel']): 'danger' | 'warning' {
  return level === 'Expired' || level === 'NoPlan' || level === 'OverLimit' ? 'danger' : 'warning'
}

/**
 * What the owner's strip above the screens shows (gate, plan, checklist). Only a person who can manage the company sees it: a bather and an
 * administrator have nothing to fix, and the server hides the plan details from them anyway.
 */
export function ownerStrip(company: BathsCompanyManageDto, canManage: boolean) {
  const pending = canManage ? pendingChecklist(company) : []
  const gate = canManage && !company.gate.accepting && !!company.gate.reasonText ? company.gate.reasonText : null
  const plan = canManage && company.plan.warningLevel !== 'None' && company.plan.text ? { text: company.plan.text, tone: planBannerTone(company.plan.warningLevel) } : null
  return { gate, plan, pending, visible: gate !== null || plan !== null || pending.length > 0 }
}
