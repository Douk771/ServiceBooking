import { useEffect, useState } from 'react'
import { Icon } from '@/components/ui/Icon'

/** Copies `text` and says so for two seconds. The clipboard may be unavailable (http, old browser): then it says it could not. */
export function CopyButton({ text, label }: { text: string; label: string }) {
  const [state, setState] = useState<'idle' | 'copied' | 'failed'>('idle')

  useEffect(() => {
    if (state === 'idle') return
    const t = setTimeout(() => setState('idle'), 2000)
    return () => clearTimeout(t)
  }, [state])

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(text)
      setState('copied')
    } catch {
      setState('failed')
    }
  }

  return (
    <button
      type="button"
      onClick={() => void copy()}
      className="inline-flex min-h-[44px] shrink-0 items-center gap-1.5 rounded-full border border-line bg-white px-4 text-xs font-semibold text-ink hover:border-line-strong"
      aria-label={`Скопировать: ${label}`}
    >
      <Icon name={state === 'copied' ? 'check' : 'copy'} size={14} strokeWidth={1.8} />
      <span role="status">{state === 'copied' ? 'Скопировано' : state === 'failed' ? 'Не удалось' : 'Скопировать'}</span>
    </button>
  )
}
