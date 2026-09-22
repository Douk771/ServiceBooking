import { useForm, Controller } from 'react-hook-form'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate } from 'react-router-dom'
import { authApi } from '../api/auth'
import { legalApi } from '../api/legal'
import { consentsApi } from '../api/consents'
import { useAuthStore } from '../store/authStore'
import { Button } from '../components/ui/Button'
import { Input } from '../components/ui/Input'
import { PhoneInput } from '../components/ui/PhoneInput'
import { Icon } from '../components/ui/Icon'
import { Card } from '../components/ui/Card'
import { getAuthErrorMessage } from '../utils/authError'
import { isRussianPhone } from '../utils/phone'
import { useState } from 'react'
import type { ConsentPurpose } from '../types'

interface FormData {
  firstName: string
  lastName: string
  phone: string
  password: string
  email?: string
  privacyAcknowledged: boolean
  termsAccepted: boolean
}

/**
 * API_CONTRACT_CYCLE5.md §40, §41; ARCHITECTURE_CYCLE5.md T5-F1. Registration is split into THREE
 * separate blocks and TWO server calls, not one checkbox:
 *   1. account fields;
 *   2. ознакомление с Privacy + акцепт TermsClient — both blocking, each its own labelled checkbox
 *      with its own link (ст. 9: a single "accept everything" checkbox is explicitly forbidden,
 *      US-65 п. 1);
 *   3. `PdnConsent` — a visually separate card with one checkbox per purpose, none required. Purposes
 *      come from the manifest (§39.1), never a hardcoded array, so a change to the purpose list on
 *      the server doesn't need a frontend release.
 * A second, non-blocking `POST /api/profile/consents` call fires only if at least one purpose was
 * checked (§40.4) — declining every purpose is a legitimate terminal state, not an error, and the
 * account is already fully registered before this call is even attempted.
 */
export function RegisterPage() {
  const {
    register,
    handleSubmit,
    watch,
    control,
    formState: { errors },
  } = useForm<FormData>({
    defaultValues: { privacyAcknowledged: false, termsAccepted: false, phone: '' },
  })
  const { setAuth } = useAuthStore()
  const navigate = useNavigate()
  const qc = useQueryClient()
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [purposes, setPurposes] = useState<ConsentPurpose[]>([])
  const privacyAcknowledged = watch('privacyAcknowledged')
  const termsAccepted = watch('termsAccepted')

  const { data: manifest, isLoading: manifestLoading, isError: manifestError, refetch } = useQuery({
    queryKey: ['legal-documents'],
    queryFn: legalApi.getManifest,
  })

  const privacy = manifest?.documents.find((d) => d.type === 'Privacy')
  const terms = manifest?.documents.find((d) => d.type === 'TermsClient')
  const pdnConsent = manifest?.documents.find((d) => d.type === 'PdnConsent')
  const documentsReady = !!privacy && !!terms

  const togglePurpose = (key: ConsentPurpose, checked: boolean) => {
    setPurposes((prev) => (checked ? [...prev, key] : prev.filter((p) => p !== key)))
  }

  const onSubmit = async (data: FormData) => {
    if (!privacy || !terms) return
    setLoading(true)
    setError('')
    try {
      const res = await authApi.register({
        firstName: data.firstName,
        lastName: data.lastName,
        phone: data.phone,
        password: data.password,
        email: data.email || undefined,
        legal: { privacyAcknowledgedVersion: privacy.version, termsAcceptedVersion: terms.version },
      })
      // Signing in over a live session (a direct /login link, or registering a second account without
      // logging out) would otherwise leave the previous user's cached queries in place, and the new
      // user gets a first frame of someone else's data. Navbar's logout clears for the same reason.
      qc.clear()
      setAuth(
        {
          id: res.userId,
          phone: res.phone,
          email: res.email,
          firstName: res.firstName,
          lastName: res.lastName,
          roles: res.roles,
        },
        res.token,
      )

      // §40.4 — a second, separate, non-blocking call: only made if the person opted into at least
      // one purpose, and its failure must never undo the registration that already succeeded above.
      if (purposes.length > 0 && pdnConsent) {
        try {
          await consentsApi.grant('PdnConsent', pdnConsent.version, purposes)
        } catch {
          // Best-effort — the account exists either way; the purpose can still be granted later from
          // "Мои согласия".
        }
      }

      navigate('/')
    } catch (e: unknown) {
      setError(getAuthErrorMessage(e))
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="min-h-[calc(100vh-76px)] flex items-center justify-center px-4 py-16">
      <div className="w-full max-w-[440px]">
        <Link to="/" className="flex items-center justify-center gap-3 mb-10">
          <span className="w-[38px] h-[38px] rounded-full bg-ink flex items-center justify-center shrink-0">
            <Icon name="calendar" size={18} className="text-cream" strokeWidth={1.6} />
          </span>
          <span className="font-serif text-xl text-ink">EZBOOK</span>
        </Link>

        <div className="bg-white border border-line rounded-3xl p-11 shadow-soft">
          <div className="text-center mb-8">
            <h1 className="font-serif text-[28px] font-medium text-ink mb-2">Создать аккаунт</h1>
            <p className="text-sm text-ink-soft">Присоединяйтесь — это бесплатно</p>
          </div>

          {manifestLoading ? (
            <div className="h-64 bg-cream-deep rounded-2xl animate-pulse" />
          ) : manifestError || !documentsReady ? (
            <div className="text-center py-8">
              <Icon name="alert-circle" size={28} strokeWidth={1.6} className="mx-auto mb-2 text-muted" />
              <p className="text-sm text-ink-soft mb-4">Не удалось загрузить правовые документы. Без них форма недоступна.</p>
              <Button variant="secondary" onClick={() => refetch()}>
                Попробовать снова
              </Button>
            </div>
          ) : (
            <form onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-[18px]" noValidate>
              {/* Block 1 — account fields */}
              <fieldset className="flex flex-col gap-[18px]">
                <legend className="sr-only">Личные данные</legend>
                <div className="grid grid-cols-2 gap-3.5">
                  <Input
                    label="Имя"
                    placeholder="Иван"
                    error={errors.firstName?.message}
                    {...register('firstName', { required: 'Введите имя' })}
                  />
                  <Input
                    label="Фамилия"
                    placeholder="Иванов"
                    error={errors.lastName?.message}
                    {...register('lastName', { required: 'Введите фамилию' })}
                  />
                </div>
                <Controller
                  name="phone"
                  control={control}
                  rules={{
                    required: 'Введите телефон',
                    validate: (v) =>
                      isRussianPhone(v) || 'Пока принимаем только российские номера, в формате +7 (900) 000-00-00',
                  }}
                  render={({ field }) => (
                    <PhoneInput
                      label="Телефон"
                      error={errors.phone?.message}
                      value={field.value}
                      onChange={field.onChange}
                    />
                  )}
                />
                <Input label="Email (необязательно)" type="email" placeholder="your@email.com" {...register('email')} />
                <div className="flex flex-col gap-1.5">
                  <Input
                    label="Пароль"
                    type="password"
                    placeholder="••••••••"
                    error={errors.password?.message}
                    {...register('password', {
                      required: 'Введите пароль',
                      minLength: { value: 8, message: 'Пароль должен быть не короче 8 символов' },
                      validate: {
                        hasLower: (v) => /[a-z]/.test(v) || 'Добавьте строчную букву',
                        hasUpper: (v) => /[A-Z]/.test(v) || 'Добавьте заглавную букву',
                        hasDigit: (v) => /\d/.test(v) || 'Добавьте цифру',
                      },
                    })}
                  />
                  {/* US-60/Q6 (ARCHITECTURE_CYCLE6.md §42.3, API_CONTRACT_CYCLE6.md §39.6): the password
                      policy itself is not changed by the customer's decision — only made visible up
                      front, so the user doesn't have to guess it from a rejected-array error after the
                      fact. */}
                  <p className="text-xs text-muted">
                    Не менее 8 символов, хотя бы одна строчная и одна заглавная буква, хотя бы одна цифра
                  </p>
                </div>
              </fieldset>

              {/* Block 2 — Privacy acknowledgement + TermsClient acceptance: two SEPARATE actions,
                  each blocking, never merged into a single "accept everything" checkbox (US-65 п. 1). */}
              <fieldset className="flex flex-col gap-2.5 rounded-2xl border border-line bg-cream-deep/40 p-4">
                <legend className="text-[13px] font-medium text-[#4A4038] px-0.5">Правовые документы</legend>

                <label htmlFor="privacyAcknowledged" className="flex items-start gap-2.5 cursor-pointer">
                  <input
                    id="privacyAcknowledged"
                    type="checkbox"
                    className="w-4 h-4 mt-0.5 rounded accent-gold"
                    aria-describedby="privacyAcknowledged-desc"
                    {...register('privacyAcknowledged', { required: true })}
                  />
                  <span id="privacyAcknowledged-desc" className="text-[13px] text-ink-soft leading-snug">
                    Я ознакомлен(а) с{' '}
                    <Link to="/privacy" target="_blank" className="text-gold hover:text-gold-dark">
                      Политикой обработки персональных данных
                    </Link>
                  </span>
                </label>

                <label htmlFor="termsAccepted" className="flex items-start gap-2.5 cursor-pointer">
                  <input
                    id="termsAccepted"
                    type="checkbox"
                    className="w-4 h-4 mt-0.5 rounded accent-gold"
                    aria-describedby="termsAccepted-desc"
                    {...register('termsAccepted', { required: true })}
                  />
                  <span id="termsAccepted-desc" className="text-[13px] text-ink-soft leading-snug">
                    Я принимаю{' '}
                    <Link to="/terms" target="_blank" className="text-gold hover:text-gold-dark">
                      Пользовательское соглашение
                    </Link>
                  </span>
                </label>
              </fieldset>

              {/* Block 3 — PdnConsent: a visually separate card, purposes come from the manifest, and
                  NONE of it is required (§41.2, US-67 п. 3–4) — declining changes nothing about
                  registration or later booking. */}
              {pdnConsent && pdnConsent.purposes && pdnConsent.purposes.length > 0 && (
                <Card className="p-4 border-line">
                  <p className="text-[13px] font-medium text-[#4A4038] mb-1">
                    Согласие на обработку персональных данных (необязательно)
                  </p>
                  <p className="text-xs text-muted mb-3">
                    Отдельный документ. Можно не отмечать ни одного пункта — это не помешает зарегистрироваться и
                    записаться к мастеру.{' '}
                    <Link to="/pdn-consent" target="_blank" className="text-gold hover:text-gold-dark">
                      Читать полный текст
                    </Link>
                  </p>
                  <div className="flex flex-col gap-2">
                    {pdnConsent.purposes.map((p) => (
                      <label key={p.key} htmlFor={`purpose-${p.key}`} className="flex items-start gap-2.5 cursor-pointer">
                        <input
                          id={`purpose-${p.key}`}
                          type="checkbox"
                          className="w-4 h-4 mt-0.5 rounded accent-gold"
                          checked={purposes.includes(p.key)}
                          onChange={(e) => togglePurpose(p.key, e.target.checked)}
                        />
                        <span className="text-[13px] text-ink-soft leading-snug">{p.title}</span>
                      </label>
                    ))}
                  </div>
                </Card>
              )}

              {/* US-33 п. 1 */}
              <p className="text-[13px] text-muted -mt-1">
                Оставляя номер телефона, вы получите сервисные сообщения о своих записях в WhatsApp от салонов.
              </p>

              {error && <div className="bg-danger-bg text-danger text-sm px-4 py-2 rounded-xl">{error}</div>}

              <Button
                type="submit"
                size="lg"
                loading={loading}
                disabled={!privacyAcknowledged || !termsAccepted}
                className="mt-1 w-full"
              >
                Зарегистрироваться
              </Button>
            </form>
          )}

          <p className="text-center text-sm text-ink-soft mt-7">
            Уже есть аккаунт?{' '}
            <Link to="/login" className="text-gold font-semibold hover:text-gold-dark">
              Войти
            </Link>
          </p>
        </div>
      </div>
    </div>
  )
}
