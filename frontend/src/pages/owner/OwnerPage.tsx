import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { useForm } from 'react-hook-form'
import { companiesApi, type CreateCompanyPayload } from '../../api/companies'
import { Card } from '../../components/ui/Card'
import { Button } from '../../components/ui/Button'
import { Input } from '../../components/ui/Input'
import { Modal } from '../../components/ui/Modal'
import { getCreateCompanyErrorMessage } from '../../utils/companyError'

function slugify(str: string) {
  return str
    .toLowerCase()
    .replace(/[а-яё]/g, (c) => ({ а:'a',б:'b',в:'v',г:'g',д:'d',е:'e',ё:'yo',ж:'zh',з:'z',и:'i',й:'j',к:'k',л:'l',м:'m',н:'n',о:'o',п:'p',р:'r',с:'s',т:'t',у:'u',ф:'f',х:'h',ц:'ts',ч:'ch',ш:'sh',щ:'sch',ъ:'',ы:'y',ь:'',э:'e',ю:'yu',я:'ya' }[c] ?? c))
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
}

interface FormData {
  name: string
  slug: string
  description: string
  address: string
  phone: string
  email: string
  allowSelfBooking: boolean
  showInPublicListing: boolean
}

export function OwnerPage() {
  const [showCreate, setShowCreate] = useState(false)
  const qc = useQueryClient()

  const { data: companies, isLoading } = useQuery({
    queryKey: ['my-companies'],
    queryFn: companiesApi.getMy,
  })

  const { register, handleSubmit, setValue, watch, reset, formState: { errors } } = useForm<FormData>({
    defaultValues: { allowSelfBooking: true, showInPublicListing: true }
  })

  const nameValue = watch('name')

  const create = useMutation({
    mutationFn: (data: CreateCompanyPayload) => companiesApi.create(data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['my-companies'] })
      setShowCreate(false)
      reset()
    },
  })

  const onSubmit = (data: FormData) => {
    create.mutate({
      name: data.name,
      slug: data.slug || slugify(data.name),
      description: data.description || undefined,
      address: data.address || undefined,
      phone: data.phone || undefined,
      email: data.email || undefined,
      allowSelfBooking: data.allowSelfBooking,
      showInPublicListing: data.showInPublicListing,
    })
  }

  return (
    <div className="max-w-4xl mx-auto px-4 py-8">
      <div className="flex items-center justify-between mb-8">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Мои компании</h1>
          <p className="text-gray-500 mt-1">Управляйте компаниями, услугами и мастерами</p>
        </div>
        <Button onClick={() => setShowCreate(true)}>+ Создать компанию</Button>
      </div>

      {isLoading ? (
        <div className="grid gap-4">
          {Array.from({ length: 2 }).map((_, i) => (
            <div key={i} className="h-28 bg-gray-100 rounded-2xl animate-pulse" />
          ))}
        </div>
      ) : companies && companies.length > 0 ? (
        <div className="grid gap-4">
          {companies.map((c) => (
            <Card key={c.id} className="p-6 flex items-center justify-between gap-4">
              <div className="flex items-center gap-4">
                <div className="w-14 h-14 rounded-2xl bg-gradient-to-br from-primary-400 to-accent-500 flex items-center justify-center text-white text-xl font-bold shrink-0">
                  {c.name[0]}
                </div>
                <div>
                  <h3 className="font-semibold text-gray-900">{c.name}</h3>
                  <p className="text-sm text-gray-400">/{c.slug}</p>
                  {c.description && <p className="text-sm text-gray-500 mt-0.5 line-clamp-1">{c.description}</p>}
                  <div className="mt-1">
                    {c.allowSelfBooking
                      ? <span className="text-xs text-green-600 font-medium">✓ Онлайн-запись включена</span>
                      : <span className="text-xs text-amber-600 font-medium">✗ Только через мастера</span>}
                  </div>
                </div>
              </div>
              <div className="flex gap-2 shrink-0">
                <Link to={`/owner/company/${c.id}`}>
                  <Button variant="secondary" size="sm">Управление</Button>
                </Link>
                <Link to={`/company/${c.slug}`} target="_blank">
                  <Button variant="ghost" size="sm">Просмотр →</Button>
                </Link>
              </div>
            </Card>
          ))}
        </div>
      ) : (
        <Card className="p-16 text-center text-gray-400">
          <p className="text-4xl mb-3">🏪</p>
          <p className="text-lg font-medium text-gray-600">У вас ещё нет компаний</p>
          <p className="text-sm mt-1">Создайте первую компанию и начните принимать записи</p>
          <Button className="mt-6" onClick={() => setShowCreate(true)}>Создать компанию</Button>
        </Card>
      )}

      {showCreate && (
        <Modal title="Создать компанию" onClose={() => { setShowCreate(false); reset() }}>
          <form onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-4">
            <Input
              label="Название *"
              placeholder="Салон красоты «Розы»"
              error={errors.name?.message}
              {...register('name', {
                required: 'Введите название',
                onChange: (e) => setValue('slug', slugify(e.target.value))
              })}
            />
            <Input
              label="URL-адрес (slug) *"
              placeholder="rozy-salon"
              error={errors.slug?.message}
              {...register('slug', { required: 'Введите slug' })}
            />
            <div className="flex flex-col gap-1">
              <label className="text-sm font-medium text-gray-700">Описание</label>
              <textarea
                className="rounded-xl border border-gray-200 px-3 py-2 text-sm outline-none focus:border-primary-400 focus:ring-2 focus:ring-primary-100 resize-none"
                rows={3}
                placeholder="Расскажите о вашей компании..."
                {...register('description')}
              />
            </div>
            <Input label="Адрес" placeholder="ул. Пушкина, 10" {...register('address')} />
            <div className="grid grid-cols-2 gap-3">
              <Input label="Телефон" placeholder="+7 999 000 00 00" {...register('phone')} />
              <Input label="Email" type="email" placeholder="info@salon.ru" {...register('email')} />
            </div>
            <label className="flex items-center gap-3 cursor-pointer">
              <input
                type="checkbox"
                className="w-4 h-4 rounded accent-orange-500"
                {...register('allowSelfBooking')}
              />
              <span className="text-sm text-gray-700">Разрешить клиентам записываться самостоятельно</span>
            </label>
            <label className="flex items-center gap-3 cursor-pointer">
              <input
                type="checkbox"
                className="w-4 h-4 rounded accent-orange-500"
                {...register('showInPublicListing')}
              />
              <span className="text-sm text-gray-700">Показывать компанию в общем списке</span>
            </label>

            {create.isError && (
              <p className="text-sm text-red-500">{getCreateCompanyErrorMessage(create.error)}</p>
            )}

            <div className="flex gap-3 pt-2">
              <Button type="button" variant="secondary" className="flex-1" onClick={() => { setShowCreate(false); reset() }}>
                Отмена
              </Button>
              <Button type="submit" className="flex-1" loading={create.isPending}>
                Создать
              </Button>
            </div>
          </form>
        </Modal>
      )}
    </div>
  )
}
