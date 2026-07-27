import { useState } from 'react'
import { useQuery, useMutation } from '@tanstack/react-query'
import { format, parseISO } from 'date-fns'
import { ru } from 'date-fns/locale'
import { mailingApi } from '../../api/mailing'
import { Card } from '../../components/ui/Card'
import { Button } from '../../components/ui/Button'
import { Input } from '../../components/ui/Input'

export function MailingTab({ companyId }: { companyId: string }) {
  const [subject, setSubject] = useState('')
  const [message, setMessage] = useState('')
  const [successMsg, setSuccessMsg] = useState('')

  const { data: history, isLoading, refetch } = useQuery({
    queryKey: ['mailing-history', companyId],
    queryFn: () => mailingApi.history(companyId),
  })

  const sendMut = useMutation({
    mutationFn: () => mailingApi.send(companyId, { subject: subject.trim(), message: message.trim() }),
    onSuccess: (data) => {
      setSuccessMsg(`Рассылка отправлена ${data.recipientCount} получателям`)
      setSubject('')
      setMessage('')
      refetch()
    },
    onError: () => setSuccessMsg(''),
  })

  const canSend = subject.trim().length > 0 && message.trim().length > 0

  return (
    <div>
      <Card className="p-6 mb-6">
        <h2 className="text-base font-semibold text-gray-900 mb-4">Новая рассылка</h2>
        <div className="flex flex-col gap-4">
          <Input
            label="Тема"
            placeholder="Акция — скидка 20% в июле"
            value={subject}
            onChange={e => { setSubject(e.target.value); setSuccessMsg('') }}
          />
          <div className="flex flex-col gap-1">
            <label className="text-sm font-medium text-gray-700">Сообщение</label>
            <textarea
              rows={4}
              value={message}
              onChange={e => { setMessage(e.target.value); setSuccessMsg('') }}
              placeholder="Уважаемые клиенты! Рады сообщить..."
              className="rounded-xl border border-gray-200 px-3 py-2 text-sm outline-none focus:border-primary-400 focus:ring-2 focus:ring-primary-100 resize-none"
            />
          </div>
          {successMsg && (
            <div className="flex items-center gap-2 text-sm text-green-700 bg-green-50 border border-green-200 rounded-xl px-3 py-2">
              <span>✓</span>
              <span>{successMsg}</span>
            </div>
          )}
          {sendMut.isError && (
            <p className="text-sm text-red-500">Ошибка при отправке рассылки</p>
          )}
          <Button
            loading={sendMut.isPending}
            disabled={!canSend}
            onClick={() => sendMut.mutate()}
          >
            Отправить рассылку
          </Button>
        </div>
      </Card>

      <div>
        <h2 className="text-base font-semibold text-gray-900 mb-3">История рассылок</h2>
        {isLoading ? (
          <div className="grid gap-3">
            {Array.from({ length: 3 }).map((_, i) => (
              <div key={i} className="h-14 bg-gray-100 rounded-2xl animate-pulse" />
            ))}
          </div>
        ) : history && history.length > 0 ? (
          <div className="grid gap-3">
            {history.map(log => (
              <Card key={log.id} className="p-4">
                <div className="flex items-start justify-between gap-4 flex-wrap">
                  <div>
                    <p className="font-medium text-gray-900">{log.subject}</p>
                    <p className="text-sm text-gray-500 mt-0.5 line-clamp-2">{log.message}</p>
                  </div>
                  <div className="text-right shrink-0">
                    <p className="text-sm text-gray-500">{log.recipientCount} получателей</p>
                    <p className="text-xs text-gray-400 mt-0.5">
                      {format(parseISO(log.sentAt), 'd MMM yyyy, HH:mm', { locale: ru })}
                    </p>
                  </div>
                </div>
              </Card>
            ))}
          </div>
        ) : (
          <Card className="p-10 text-center text-gray-400">
            <p className="text-3xl mb-2">📣</p>
            <p>Рассылок ещё не было</p>
          </Card>
        )}
      </div>
    </div>
  )
}
