import type { FaqItems, LandingConfig, ThreeSteps } from './types'

/** Конфиг для тестов шаблона (не конфиг сервиса). */
export const steps = (p: string): ThreeSteps => [
  { icon: 'user', title: `${p} 1`, text: `${p} text 1` },
  { icon: 'calendar', title: `${p} 2`, text: `${p} text 2` },
  { icon: 'check-circle', title: `${p} 3`, text: `${p} text 3` },
]

export const faqItems: FaqItems = [
  { question: 'Q1', answer: 'A1' },
  { question: 'Q2', answer: 'A2' },
  { question: 'Q3', answer: 'A3' },
  { question: 'Q4', answer: 'A4' },
  { question: 'Q5', answer: 'A5' },
  { question: 'Q6', answer: ['A6a', 'A6b'], link: { to: '/pricing', label: 'Тарифы и цены' } },
]

export const shot = { src: '/x.webp', width: 320, height: 640, alt: 'Скрин', caption: 'Подпись' }

export function makeConfig(over: Partial<LandingConfig> = {}): LandingConfig {
  return {
    hero: {
      eyebrow: 'Эйбро',
      title: 'Заголовок',
      intro: 'Интро',
      howTo: { kind: 'anchor', href: '#c-title', label: 'Как' },
    },
    catalog: { id: 'cat', ariaLabel: 'Каталог' },
    clients: {
      eyebrow: 'Клиентам',
      title: 'Клиенты',
      titleId: 'c-title',
      text: 'Текст',
      actions: [{ kind: 'anchor', href: '#cat', label: 'Выбрать' }],
      stepsPanel: { title: 'Шаги клиента', titleId: 'c-steps', steps: steps('c') },
      list: { title: 'Список', titleId: 'c-list', items: ['один', 'два'], note: 'Примечание' },
    },
    business: {
      eyebrow: 'Бизнесу',
      title: 'Бизнес',
      titleId: 'b-title',
      text: 'Текст',
      actions: [
        { kind: 'auth-route', guestTo: '/register', authedTo: '/cabinet', label: 'Подключить' },
        { kind: 'route', to: '/cabinet', label: 'Войти' },
      ],
      benefits: ['плюс'],
      stepsPanel: { title: 'Шаги бизнеса', titleId: 'b-steps', steps: steps('b') },
    },
    faq: { items: faqItems },
    ...over,
  }
}
