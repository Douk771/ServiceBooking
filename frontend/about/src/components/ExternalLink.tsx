import { BTN_PRIMARY, BTN_SECONDARY, FOCUS_RING } from '@/components/landing/classes'

interface ExternalLinkProps {
  href: string
  /** Видимый текст. */
  label: string
  /** Контекст для скринридера: название продукта. */
  context?: string
  variant?: 'primary' | 'secondary' | 'text'
  action?: 'client' | 'business' | 'demo'
}

const VARIANT = {
  primary: BTN_PRIMARY,
  secondary: BTN_SECONDARY,
  text: 'inline-flex items-center text-[14px] font-medium underline underline-offset-4',
} as const

/** Обычный <a href> с абсолютным адресом, без target (ARCHITECTURE_CYCLE44 §44.5). */
export function ExternalLink({ href, label, context, variant = 'text', action }: ExternalLinkProps) {
  return (
    <a href={href} data-action={action} className={`${VARIANT[variant]} ${FOCUS_RING} min-h-[44px]`}>
      {label}
      {context && <span className="sr-only"> — {context}</span>}
    </a>
  )
}
