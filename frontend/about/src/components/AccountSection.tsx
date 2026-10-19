import { CONTAINER, H2 } from '@/components/landing/classes'
import type { PageCopy } from '../content/types'

export function AccountSection({ account }: { account: PageCopy['account'] }) {
  return (
    <section aria-labelledby="account-title" className={`${CONTAINER} mt-20 md:mt-28`}>
      <div className="grid gap-6 border-t border-line pt-12 lg:grid-cols-[1fr_1.4fr]">
        <h2 id="account-title" className={H2}>
          {account.title}
        </h2>
        <div className="space-y-4 text-[16px] leading-relaxed text-ink-soft">
          {account.paragraphs.map((t) => (
            <p key={t}>{t}</p>
          ))}
        </div>
      </div>
    </section>
  )
}
