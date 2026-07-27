import { Link, useNavigate } from 'react-router-dom'
import { useAuthStore } from '../../store/authStore'
import { Icon } from '../ui/Icon'

export function Navbar() {
  const { user, logout, isAuthenticated, hasRole } = useAuthStore()
  const navigate = useNavigate()

  const handleLogout = () => { logout(); navigate('/') }

  const isMasterOrOwner = hasRole('Master') || hasRole('CompanyOwner') || hasRole('SuperAdmin')
  const isAdmin = hasRole('SuperAdmin')
  const isClient = !isMasterOrOwner && isAuthenticated()

  const navLinkClass = 'text-sm font-medium text-ink-soft hover:text-gold-dark transition-colors'

  return (
    <nav className="sticky top-0 z-50 bg-cream/86 backdrop-blur-md border-b border-line">
      <div className="max-w-[1180px] mx-auto px-8 h-[76px] flex items-center justify-between">
        <Link to="/" className="flex items-center gap-3">
          <span className="w-[38px] h-[38px] rounded-full bg-ink flex items-center justify-center shrink-0">
            <Icon name="scissors" size={18} className="text-cream" strokeWidth={1.6} />
          </span>
          <span className="font-serif text-xl text-ink">EZBOOK</span>
        </Link>

        <div className="flex items-center gap-7">
          {isAuthenticated() ? (
            <>
              {isClient && (
                <Link to="/my-visits" className={navLinkClass}>Мои визиты</Link>
              )}
              {isMasterOrOwner && (
                <Link to="/my-bookings" className={navLinkClass}>Мои записи</Link>
              )}
              {isMasterOrOwner && (
                <Link to="/cabinet" className={navLinkClass}>Кабинет</Link>
              )}
              {isAdmin && (
                <Link to="/admin" className={navLinkClass}>Админ</Link>
              )}
              <button onClick={handleLogout} className="flex items-center gap-1.5 text-sm font-medium text-ink-soft hover:text-gold-dark transition-colors">
                <Icon name="log-out" size={15} strokeWidth={1.7} />
                Выйти
              </button>
              <Link to="/profile" className="w-9 h-9 rounded-full bg-cream-deep flex items-center justify-center text-gold-dark font-bold text-xs hover:bg-line transition-colors shrink-0">
                {user?.firstName[0]}{user?.lastName[0]}
              </Link>
            </>
          ) : (
            <>
              <Link to="/login" className={navLinkClass}>Войти</Link>
              <Link
                to="/register"
                className="inline-flex items-center bg-ink hover:bg-ink/90 text-cream text-sm font-semibold px-[22px] py-2.5 rounded-full transition-colors"
              >
                Регистрация
              </Link>
            </>
          )}
        </div>
      </div>
    </nav>
  )
}
