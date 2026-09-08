import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { reviewsApi } from '../../api/reviews'
import { Button } from '../ui/Button'
import { Icon } from '../ui/Icon'

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
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-ink/45 backdrop-blur-sm px-4">
      <div className="bg-cream rounded-3xl shadow-modal w-full max-w-md p-7">
        <h2 className="font-serif text-xl font-medium text-ink mb-1">Оставить отзыв</h2>
        <p className="text-sm text-ink-soft mb-5">
          {serviceName} у мастера {masterName}
        </p>

        {/* Star rating */}
        <div className="flex gap-2 mb-5">
          {[1, 2, 3, 4, 5].map((star) => (
            <button
              key={star}
              type="button"
              className="transition-transform hover:scale-110"
              onMouseEnter={() => setHovered(star)}
              onMouseLeave={() => setHovered(0)}
              onClick={() => setRating(star)}
            >
              <Icon name="star" size={30} className={(hovered || rating) >= star ? 'text-[#B08A3E]' : 'text-line'} />
            </button>
          ))}
        </div>

        {/* Comment */}
        <textarea
          className="w-full border border-line bg-white rounded-xl px-4 py-3 text-sm text-ink resize-none outline-none focus:border-gold focus:ring-[3px] focus:ring-cream-deep mb-5"
          rows={4}
          placeholder="Расскажите о своём опыте (необязательно)"
          value={comment}
          onChange={(e) => setComment(e.target.value)}
        />

        {submit.isError && <p className="text-sm text-danger mb-3">Не удалось отправить отзыв. Попробуйте снова.</p>}

        <div className="flex gap-3">
          <Button variant="secondary" className="flex-1" onClick={onClose} disabled={submit.isPending}>
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
