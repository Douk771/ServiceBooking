import { Link, useNavigate } from 'react-router-dom'
import { useAuthStore } from '../../store/authStore'
import { Button } from '../ui/Button'

export function Navbar() {
  const { user, logout, isAuthenticated, hasRole } = useAuthStore()
  const navigate = useNavigate()

  const handleLogout = () => { logout(); navigate('/') }

  const isMasterOrOwner = hasRole('Master') || hasRole('CompanyOwner') || hasRole('SuperAdmin')
  const isAdmin = hasRole('SuperAdmin')
  const isClient = !isMasterOrOwner && isAuthenticated()

  return (
    <nav className="sticky top-0 z-50 bg-white/80 backdrop-blur-md border-b border-orange-100">
      <div className="max-w-6xl mx-auto px-4 h-16 flex items-center justify-between">
        <Link to="/" className="flex items-center gap-2 font-bold text-xl text-primary-600">
          <span className="text-2xl">📅</span>
          <span>ServiceBooking</span>
        </Link>

        <div className="flex items-center gap-3">
          {isAuthenticated() ? (
            <>
              {isClient && (
                <Link to="/my-visits" className="text-sm font-medium text-gray-600 hover:text-primary-600 transition-colors">
                  Мои визиты
                </Link>
              )}
              {isMasterOrOwner && (
                <Link to="/my-bookings" className="text-sm font-medium text-gray-600 hover:text-primary-600 transition-colors">
                  Мои записи
                </Link>
              )}
              {isMasterOrOwner && (
                <Link to="/cabinet" className="text-sm font-medium text-gray-600 hover:text-primary-600 transition-colors">
                  Кабинет
                </Link>
              )}
              {isAdmin && (
                <Link to="/admin" className="text-sm font-medium text-gray-600 hover:text-primary-600 transition-colors">
                  Админ
                </Link>
              )}
              <div className="flex items-center gap-2 ml-1">
                <Link to="/profile" className="w-8 h-8 rounded-full bg-primary-100 flex items-center justify-center text-primary-700 font-semibold text-sm hover:bg-primary-200 transition-colors">
                  {user?.firstName[0]}{user?.lastName[0]}
                </Link>
                <Button variant="ghost" size="sm" onClick={handleLogout}>Выйти</Button>
              </div>
            </>
          ) : (
            <>
              <Link to="/login"><Button variant="ghost" size="sm">Войти</Button></Link>
              <Link to="/register"><Button size="sm">Регистрация</Button></Link>
            </>
          )}
        </div>
      </div>
    </nav>
  )
}
