import { Link } from 'react-router-dom'
import { Icon } from '../components/ui/Icon'

/**
 * US-28-08 (audit of the public pages, «404»): an address the app doesn't know used to render the navbar and an
 * empty gap above the footer. A visitor — or a reviewer — gets a plain sentence and a way back instead.
 */
export function NotFoundPage() {
  return (
    <div className="max-w-[760px] mx-auto px-8 pt-24 pb-24 text-center">
      <Icon name="search" size={36} strokeWidth={1.4} className="mx-auto mb-4 text-muted" />
      <h1 className="font-serif text-[30px] font-medium text-ink mb-3">Страница не найдена</h1>
      <p className="text-[15px] text-ink-soft mb-8">Возможно, ссылка устарела или в адресе опечатка.</p>
      <Link to="/" className="inline-flex items-center gap-2 text-[15px] font-semibold text-ink border-b border-ink">
        На главную
        <Icon name="arrow-right" size={15} strokeWidth={1.8} />
      </Link>
    </div>
  )
}
