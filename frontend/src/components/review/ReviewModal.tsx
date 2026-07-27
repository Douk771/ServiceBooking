import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { reviewsApi } from '../../api/reviews'
import { Button } from '../ui/Button'

interface ReviewModalProps {
  bookingId: string
  serviceName: string
  masterName: string
  companyId?: string
  onClose: () => void
  onSuccess: () => void
}

export function ReviewModal({ bookingId, serviceName, masterName, companyId, onClose, onSuccess }: ReviewModalProps) {
  const [rating, setRating] = useState(0)
  const [hovered, setHovered] = useState(0)
  const [comment, setComment] = useState('')
  const qc = useQueryClient()

  const submit = useMutation({
    mutationFn: () => reviewsApi.submit(bookingId, rating, comment),
    onSuccess: () => {
      if (companyId) qc.invalidateQueries({ queryKey: ['company-reviews', companyId] })
      qc.invalidateQueries({ queryKey: ['can-review'] })
      onSuccess()
      onClose()
    },
  })

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4">
      <div className="bg-white rounded-2xl shadow-xl w-full max-w-md p-6">
        <h2 className="text-xl font-bold text-gray-900 mb-1">Оставить отзыв</h2>
        <p className="text-sm text-gray-500 mb-5">
          {serviceName} у мастера {masterName}
        </p>

        {/* Star rating */}
        <div className="flex gap-2 mb-5">
          {[1, 2, 3, 4, 5].map(star => (
            <button
              key={star}
              type="button"
              className="text-3xl transition-transform hover:scale-110"
              onMouseEnter={() => setHovered(star)}
              onMouseLeave={() => setHovered(0)}
              onClick={() => setRating(star)}
            >
              <span className={(hovered || rating) >= star ? 'text-yellow-400' : 'text-gray-300'}>
                ★
              </span>
            </button>
          ))}
        </div>

        {/* Comment */}
        <textarea
          className="w-full border border-gray-200 rounded-xl px-3 py-2 text-sm resize-none focus:outline-none focus:ring-2 focus:ring-primary-300 mb-5"
          rows={4}
          placeholder="Расскажите о своём опыте (необязательно)"
          value={comment}
          onChange={e => setComment(e.target.value)}
        />

        {submit.isError && (
          <p className="text-sm text-red-500 mb-3">Не удалось отправить отзыв. Попробуйте снова.</p>
        )}

        <div className="flex gap-3">
          <Button
            variant="secondary"
            className="flex-1"
            onClick={onClose}
            disabled={submit.isPending}
          >
            Отмена
          </Button>
          <Button
            className="flex-1"
            disabled={rating === 0 || submit.isPending}
            loading={submit.isPending}
            onClick={() => submit.mutate()}
          >
            Отправить отзыв
          </Button>
        </div>
      </div>
    </div>
  )
}
