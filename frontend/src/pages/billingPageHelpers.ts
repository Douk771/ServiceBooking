// Вынесено из BillingPage.tsx: react-refresh/only-export-components — файл, экспортирующий рядом с
// компонентом ещё и объект, лишается горячей подмены целиком. Тот же приём, что у соседнего
// admin/billingAccountsHelpers.ts.

// N23: status badge colour must reflect the actual subscription state — a green "Active" badge on
// an expired subscription is misleading. Free is neutral, Active is success, Expired is danger.
export const STATUS_BADGE_CLASS: Record<string, string> = {
  Free: 'bg-cream-deep text-muted',
  Active: 'bg-success-bg text-success',
  Expired: 'bg-danger-bg text-danger',
}
