import { useNavigate } from 'react-router-dom'
import { CreateCompanyDialog } from '../components/company/CreateCompanyDialog'

/**
 * `/cabinet/new` — «Подключить салон» для любого вошедшего (в том числе клиента без роли).
 * Форма — общий `CreateCompanyDialog` из кабинета; после создания роль CompanyOwner и новый токен
 * сохраняются сразу, поэтому кабинет открывается без перелогина.
 */
export function CreateCompanyPage() {
  const navigate = useNavigate()
  return (
    <main className="max-w-[640px] mx-auto px-4 sm:px-8 pt-10">
      <h1 className="font-serif text-[32px] text-ink mb-1">Подключить салон</h1>
      <p className="text-sm text-ink-soft mb-8">Несколько минут — и клиенты смогут записываться онлайн.</p>
      <CreateCompanyDialog onClose={() => navigate('/', { replace: true })} onCreated={() => navigate('/cabinet', { replace: true })} />
    </main>
  )
}
