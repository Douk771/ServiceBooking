import { ownerWarningTexts } from '@/utils/slots/ownerWarnings'

/** Soft warnings about the owner's text (Т42-12): saved anyway, the owner decides. Renders nothing when there is none. */
export function ContentWarnings({ codes, className = '' }: { codes: readonly string[] | null | undefined; className?: string }) {
  const texts = ownerWarningTexts(codes)
  if (texts.length === 0) return null
  return (
    <ul role="status" aria-label="Предупреждения о тексте" className={`flex flex-col gap-1.5 rounded-xl bg-warning-bg px-3 py-2.5 text-xs leading-relaxed text-warning ${className}`}>
      {texts.map((t) => (
        <li key={t}>{t}</li>
      ))}
    </ul>
  )
}
