import { BTN_PRIMARY, CONTAINER, FOCUS_RING } from '@/components/landing/classes'
import type { PageCopy } from '../content/types'

export function NotFoundPage({ copy }: { copy: PageCopy }) {
  return (
    <main id="main" className={`${CONTAINER} py-24`}>
      <p className="font-serif text-[22px] font-semibold tracking-[0.08em] text-ink">{copy.brand}</p>
      <h1 className="mt-6 font-serif text-[40px] font-medium leading-tight text-ink">{copy.notFound.title}</h1>
      <p className="mt-4 max-w-[480px] text-[16px] leading-relaxed text-ink-soft">{copy.notFound.text}</p>
      <p className="mt-8">
        <a href="/" className={`${BTN_PRIMARY} ${FOCUS_RING} min-h-[44px]`}>
          {copy.notFound.backLabel}
        </a>
      </p>
    </main>
  )
}
