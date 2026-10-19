// Демо-набор для съёмки главной «Записи». Всё выдуманное: салон, мастера, клиенты, телефоны.
// Существует только в локальной базе стека sb-shots (stack.sh), в бой не попадает.

export const OWNER = {
  firstName: 'Алина',
  lastName: 'Воронцова',
  phone: '+79000000100',
  bio: 'Парикмахер-стилист: стрижки, окрашивание, укладки.',
}

export const SALON = {
  name: 'Студия красоты «Лаванда»',
  slug: 'lavanda-studio',
  address: 'ул. Липовая, 7',
  phone: '+79000000199',
  description: 'Стрижки, окрашивание, маникюр и брови в уютной студии рядом с парком. Работаем без выходных.',
}

// Логотип и картинки услуг — нарисованные иллюстрации витрины (ServiceBooking.API/ShowcaseAssets, без фото и людей).
export const LOGO_IMAGE = 'logos/beauty.jpg'

// Услуги: длительность в минутах, цена в рублях, image — файл из ShowcaseAssets/services.
export const SERVICES = [
  { key: 'cut', image: 'haircut.jpg', name: 'Женская стрижка', durationMinutes: 60, price: 1800, description: 'Мытьё, стрижка и укладка феном.' },
  { key: 'color', image: 'coloring.jpg', name: 'Окрашивание в один тон', durationMinutes: 120, price: 3500, description: 'Краска салона, уход после окрашивания.' },
  { key: 'styling', image: 'styling.jpg', name: 'Укладка', durationMinutes: 45, price: 1200, description: null },
  { key: 'manicure', image: 'manicure.jpg', name: 'Маникюр с покрытием', durationMinutes: 90, price: 2200, description: 'Аппаратный маникюр и гель-лак.' },
  { key: 'pedicure', image: 'pedicure.jpg', name: 'Педикюр', durationMinutes: 90, price: 2500, description: null },
  { key: 'brows', image: 'brows.jpg', name: 'Коррекция и окрашивание бровей', durationMinutes: 45, price: 1100, description: null },
  { key: 'lashes', image: 'lashes.jpg', name: 'Ламинирование ресниц', durationMinutes: 60, price: 2000, description: null },
]

// Владелец — тоже мастер (key: 'owner'). Второго мастера владелец добавляет по телефону. Мастеров двое: столько мест
// у тарифа «Бесплатный» (владелец + 1), а засев ходит только через обычный API, без смены тарифа.
export const MASTERS = [
  { key: 'owner', services: ['cut', 'color', 'styling'] },
  { key: 'marina', firstName: 'Марина', lastName: 'Ковалёва', phone: '+79000000101', bio: 'Маникюр, педикюр, брови и ресницы.', services: ['manicure', 'pedicure', 'brows', 'lashes'] },
]

export const WORK_START = '10:00'
export const WORK_END = '20:00'

// Записи гостей на «день съёмки» (dayOffset дней от сегодня по времени салона). Время — местное.
export const BOOKINGS = [
  { master: 'owner', service: 'cut', time: '10:00', name: 'Ольга Смирнова', phone: '+79000000111', notes: null },
  { master: 'owner', service: 'color', time: '11:30', name: 'Виктория Лебедева', phone: '+79000000112', notes: 'Хочу оттенок чуть светлее, чем сейчас' },
  { master: 'owner', service: 'styling', time: '14:00', name: 'Дарья Морозова', phone: '+79000000113', notes: null },
  { master: 'owner', service: 'cut', time: '16:00', name: 'Екатерина Орлова', phone: '+79000000114', notes: null },
  { master: 'marina', service: 'manicure', time: '10:00', name: 'Наталья Волкова', phone: '+79000000115', notes: null },
  { master: 'marina', service: 'pedicure', time: '12:00', name: 'Светлана Зайцева', phone: '+79000000116', notes: null },
  { master: 'marina', service: 'brows', time: '15:00', name: 'Полина Соколова', phone: '+79000000117', notes: null },
]

// Кадр клиента: запись к этому мастеру и на эту услугу — шаг выбора времени.
export const CLIENT_SHOT = { master: 'owner', service: 'cut' }
