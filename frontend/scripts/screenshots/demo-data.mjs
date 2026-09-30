// Демо-набор для съёмки goods (ARCHITECTURE_CYCLE30.md §30.7.3). Всё выдуманное.
// Существует только в локальной базе стека sb-shots.

export const OWNER = {
  firstName: 'Елена',
  lastName: 'Демидова',
  phone: '+79000000000',
}

export const SHOP = {
  name: 'Пекарня на Садовой',
  slug: 'pekarnya-na-sadovoy',
  address: 'ул. Садовая, 12',
  phone: '+79000000099',
  description: 'Свежий хлеб, выпечка и домашняя кулинария каждый день.',
}

const piece = (name, price, portionText = null) => ({ name, unit: 'Piece', price, portionText })
const weight = (name, price) => ({
  name,
  unit: 'Weight',
  price,
  weightStepGrams: 50,
  minQuantityGrams: 200,
})

export const CATEGORIES = [
  {
    name: 'Хлеб и выпечка',
    products: [piece('Хлеб ржаной', 85), piece('Багет', 95), piece('Круассан', 120)],
  },
  {
    name: 'Кулинария',
    products: [piece('Сырники', 180, '2 шт'), weight('Пирог с капустой', 890), weight('Салат оливье', 760)],
  },
  {
    name: 'Напитки',
    products: [piece('Морс клюквенный', 150, '0,5 л'), piece('Капучино', 190, '300 мл')],
  },
]

// scheduleMinutes: null — «как можно скорее», число — первый слот не раньше «сейчас + N мин».
// lines.quantity: штуки, для весовых — граммы.
export const ORDERS = {
  A: { name: 'Мария С.', phone: '+79000000001', scheduleMinutes: null, comment: null, lines: [['Багет', 1], ['Круассан', 2]], finalStatus: 'Ready' },
  B: { name: 'Олег П.', phone: '+79000000002', scheduleMinutes: null, comment: null, lines: [['Пирог с капустой', 450], ['Морс клюквенный', 1]], finalStatus: 'Ready' },
  C: { name: 'Ирина Д.', phone: '+79000000003', scheduleMinutes: 90, comment: null, lines: [['Хлеб ржаной', 1], ['Сырники', 2]], finalStatus: 'Accepted' },
  D: { name: 'Сергей К.', phone: '+79000000004', scheduleMinutes: null, comment: 'Капучино без сахара', lines: [['Салат оливье', 300], ['Капучино', 2]], finalStatus: 'Accepted' },
  E: { name: 'Анна Л.', phone: '+79000000005', scheduleMinutes: 120, comment: 'Упакуйте, пожалуйста, отдельно', lines: [['Круассан', 4], ['Пирог с капустой', 600]], finalStatus: 'New' },
  F: { name: 'Дмитрий В.', phone: '+79000000006', scheduleMinutes: null, comment: null, lines: [['Багет', 2]], finalStatus: 'New' },
}

// Порядок создания задаёт номера заказов дня.
export const ORDER_SEQUENCE = ['A', 'B', 'C', 'D', 'E', 'F']
export const ACCEPT_SEQUENCE = ['B', 'C', 'D', 'A']
export const READY_SEQUENCE = ['A', 'B']
export const ORDER_PAGE_KEY = 'C'
