import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate } from 'react-router-dom'
import { legalApi } from '../../api/legal'
import { useAuthStore } from '../../store/authStore'
import { useLegalStore } from '../../store/legalStore'
import { Button } from '../ui/Button'
import { Icon } from '../ui/Icon'
import { getLegalErrorMessage } from '../../utils/legalError'
import type { ConsentStatus } from '../../types'

interface Props {
  status: ConsentStatus
}

/**
 * Full-screen blocking state for a "Material" legal-document change (US-37 п. 4, API_CONTRACT.md §0.3).
 * Rendered whenever `requiresAcceptance` is true — from either the 451 branch in client.ts or the
 * consent-status query on app entry. While it's up, the only things a signed-in user can do are read
 * the documents, sign out, or delete their account (T-F2 done-criterion) — nothing else is reachable
 * because everything else keeps returning 451.
 */
export function ConsentGate({ status }: Props) {
  const qc = useQueryClient()
  const navigate = useNavigate()
  const { user, setAuth, logout } = useAuthStore()
  const setConsentRequired = useLegalStore((s) => s.setConsentRequired)

  const privacy = status.documents.find((d) => d.type === 'Privacy')
  const terms = status.documents.find((d) => d.type === 'Terms')

  const mut = useMutation({
    mutationFn: () => {
      if (!privacy || !terms) throw new Error('missing document versions')
      return legalApi.accept(privacy.version, terms.version)
    },
    onSuccess: (res) => {
      if (user) setAuth(user, res.token)
      setConsentRequired(false)
      qc.invalidateQueries({ queryKey: ['legal-consent-status'] })
    },
  })

  const handleLogout = () => {
    logout()
    qc.clear()
    navigate('/login')
  }

  return (
    <div className="fixed inset-0 z-[100] bg-cream flex items-center justify-center px-4 py-10 overflow-y-auto">
      <div className="w-full max-w-[520px] bg-white border border-line rounded-3xl p-9 shadow-soft">
        <div className="w-12 h-12 rounded-full bg-warning-bg flex items-center justify-center mb-5">
          <Icon name="alert-circle" size={22} strokeWidth={1.8} className="text-warning" />
        </div>
        <h1 className="font-serif text-2xl font-medium text-ink mb-2.5">Документы обновились</h1>
        <p className="text-sm text-ink-soft leading-[1.6] mb-6">
          Мы изменили политику обработки персональных данных и/или пользовательское соглашение.
          Чтобы продолжить пользоваться сервисом, ознакомьтесь с новой редакцией и примите её.
        </p>

        <div className="flex flex-col gap-2 mb-6">
          <Link to="/privacy" target="_blank" className="text-sm text-gold hover:text-gold-dark flex items-center gap-1.5">
            <Icon name="chevron-right" size={13} strokeWidth={1.8} /> Политика обработки персональных данных
          </Link>
          <Link to="/terms" target="_blank" className="text-sm text-gold hover:text-gold-dark flex items-center gap-1.5">
            <Icon name="chevron-right" size={13} strokeWidth={1.8} /> Пользовательское соглашение
          </Link>
        </div>

        {mut.isError && <p className="text-sm text-danger mb-4">{getLegalErrorMessage(mut.error)}</p>}

        <Button size="lg" className="w-full mb-3" loading={mut.isPending} onClick={() => mut.mutate()}>
          Принимаю новую редакцию
        </Button>

        <div className="flex items-center justify-center gap-4 text-xs text-muted mt-2">
          <button onClick={handleLogout} className="hover:text-ink-soft transition-colors">Выйти</button>
          <span>·</span>
          <Link to="/profile/delete" className="hover:text-ink-soft transition-colors">Удалить аккаунт</Link>
        </div>
      </div>
    </div>
  )
}
