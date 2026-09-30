/**
 * ARCHITECTURE_CYCLE31.md §31.11.2 — the ONE class string of an action link in the company card header: the phone
 * and both map links share the height (44 px touch target) and the look. The map-link classes are moved here
 * verbatim (cycle 15, §285) and gain the focus outline the phone link already had.
 */
export const COMPANY_ACTION_LINK_CLASS =
  'min-h-[44px] px-3 inline-flex items-center gap-1.5 text-[13px] font-medium text-gold-dark hover:text-gold-darker rounded-full hover:bg-cream-deep transition-colors focus-visible:outline focus-visible:outline-2 focus-visible:outline-gold focus-visible:outline-offset-2'

/** Non-interactive variant (a phone that cannot be dialled): same look, no hover/focus affordances. */
export const COMPANY_ACTION_STATIC_CLASS =
  'min-h-[44px] px-3 inline-flex items-center gap-1.5 text-[13px] font-medium text-gold-dark rounded-full'
