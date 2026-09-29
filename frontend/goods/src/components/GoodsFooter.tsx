import { Link } from 'react-router-dom'

/**
 * Legal links stay on this domain (SPEC US-23-06, Р7): real <Link>s to the goods routes of the shared
 * legal pages, not to ezbook.ru.
 */
export function GoodsFooter() {
  return (
    <footer className="border-t border-line mt-16">
      <div className="max-w-[1180px] mx-auto px-4 sm:px-8 py-6 flex flex-wrap items-center justify-center gap-x-6 gap-y-2 text-xs text-muted">
        <span>© {new Date().getFullYear()} ezbook · Заказы</span>
        <Link to="/privacy" className="hover:text-ink-soft transition-colors">
          Политика обработки персональных данных
        </Link>
        <Link to="/terms" className="hover:text-ink-soft transition-colors">
          Пользовательское соглашение
        </Link>
        <Link to="/terms-owner" className="hover:text-ink-soft transition-colors">
          Соглашение с компанией
        </Link>
        <Link to="/pdn-consent" className="hover:text-ink-soft transition-colors">
          Согласие на обработку ПДн
        </Link>
        <Link to="/data-request" className="hover:text-ink-soft transition-colors">
          Обращение по своим данным
        </Link>
      </div>
    </footer>
  )
}
