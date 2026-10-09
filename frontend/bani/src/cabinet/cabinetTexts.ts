import type { SlotText } from '@/utils/slots/slotTexts'

/**
 * The only owner text of the cabinet that `utils/baniTexts.ts` (FE-42-3, §11.0–§11.5) does not carry: the «no people in the frame» line
 * under the photo upload (Т42-13). The server's `CompanyPhotoPeopleNotice` wins when it exists; FE-42-8 may move this into baniTexts.
 */
export const PHOTO_PEOPLE_FALLBACK: Record<'CompanyPhotoPeopleNotice', SlotText> = {
  CompanyPhotoPeopleNotice: { short: '<p>Загружайте фото, на которых нет людей. Если люди в кадре есть, нужно их согласие на публикацию изображения.</p>', full: null },
}

/** R42-1: the database refuses a double booking only inside one resource, so one bath must live in one company and one card. */
export const RESOURCE_ONE_PLACE_HINT =
  'Одну баню заведите один раз — в одной компании и в одной карточке. Сервис не даёт забронировать уже занятое время только внутри карточки: если та же баня есть в двух компаниях или брони принимают ещё где-то, пересечения возможны, и отвечаете за них вы.'

/** US-42-01: the city is not in the reference list and an admin cannot add one — the owner is told so honestly. */
export const CITY_MISSING_TEXT = 'Вашего города пока нет в списке — напишите в поддержку.'

/** The support contact of the platform. The repository has none yet; while it is null the line above is shown without a link (open question to architect). */
export const SUPPORT_MAILTO: string | null = null
