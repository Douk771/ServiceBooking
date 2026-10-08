import { useId, useState } from 'react'
import { Link } from 'react-router-dom'
import { Icon } from '../ui/Icon'
import { FOCUS_RING, H2, SECTION } from './classes'
import type { FaqItems } from './types'

/**
 * Секция 6: FAQ — WAI-ARIA «disclosure» (ARCHITECTURE_CYCLE38.md §38.6.1). Ответ выводится только текстовыми
 * узлами React; HTML не интерпретируется.
 */
export function FaqSection({ items }: { items: FaqItems }) {
  const uid = useId()
  const [open, setOpen] = useState<ReadonlySet<number>>(() => new Set())
  const toggle = (i: number) =>
    setOpen((prev) => {
      const next = new Set(prev)
      if (next.has(i)) next.delete(i)
      else next.add(i)
      return next
    })

  return (
    <section id="faq" aria-labelledby="faq-title" className={SECTION}>
      <h2 id="faq-title" className={H2}>
        Частые вопросы
      </h2>
      <ul role="list" className="mt-8 divide-y divide-line border-y border-line">
        {items.map((item, i) => {
          const isOpen = open.has(i)
          const qId = `${uid}-q${i}`
          const aId = `${uid}-a${i}`
          const paragraphs = typeof item.answer === 'string' ? [item.answer] : item.answer
          return (
            <li key={item.question}>
              <h3 className="m-0">
                <button
                  type="button"
                  id={qId}
                  aria-expanded={isOpen}
                  aria-controls={aId}
                  onClick={() => toggle(i)}
                  className={`w-full flex items-start justify-between gap-4 py-5 text-left font-serif text-[19px] text-ink ${FOCUS_RING}`}
                >
                  <span className="break-words min-w-0">{item.question}</span>
                  <Icon
                    name="chevron-down"
                    size={20}
                    aria-hidden
                    className={`shrink-0 mt-1 transition-transform ${isOpen ? 'rotate-180' : ''}`}
                  />
                </button>
              </h3>
              <div id={aId} hidden={!isOpen} className="pb-5 text-[15px] leading-[1.6] text-ink-soft max-w-[720px]">
                {paragraphs.map((p, pi) => (
                  <p key={pi} className={pi > 0 ? 'mt-3' : undefined}>
                    {p}
                  </p>
                ))}
                {item.link && (
                  <Link to={item.link.to} className="mt-3 inline-block font-semibold text-ink underline underline-offset-2">
                    {item.link.label}
                  </Link>
                )}
              </div>
            </li>
          )
        })}
      </ul>
    </section>
  )
}
