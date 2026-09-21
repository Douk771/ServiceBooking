import { Link } from 'react-router-dom'

/**
 * Site-wide footer. Legal links required by US-36 п. 4 and, from cycle 5, API_CONTRACT_CYCLE5.md §43
 * (the four new document routes) and §48.4 (`/data-request`) — real `<a>`/`<Link>` elements, not
 * onClick handlers, so crawlers and users without JS both see them (ARCHITECTURE.md §4.5).
 */
export function Footer() {
  return (
    <footer className="border-t border-line mt-16">
      <div className="max-w-[1180px] mx-auto px-4 sm:px-8 py-6 flex flex-wrap items-center justify-center gap-x-6 gap-y-2 text-xs text-muted">
        <span>© {new Date().getFullYear()} EZBOOK</span>
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
        <Link to="/channel-risk" className="hover:text-ink-soft transition-colors">
          Риски канала уведомлений
        </Link>
        <Link to="/offer-channel" className="hover:text-ink-soft transition-colors">
          Оферта на подключение канала
        </Link>
        <Link to="/data-request" className="hover:text-ink-soft transition-colors">
          Обращение по своим данным
        </Link>
      </div>
    </footer>
  )
}
