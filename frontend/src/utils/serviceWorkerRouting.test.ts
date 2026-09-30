import ezbookWorker from '../../public/sw.js?raw'
import { describeWorkerRouting } from '../test/workerRouting'

// ARCHITECTURE_CYCLE33.md §33.5.2 / §33.13.2 — the ezbook worker's routing table (the goods one is in goods/src/utils).
describeWorkerRouting({ name: 'ezbook', source: ezbookWorker, origin: 'https://ezbook.ru', peer: 'https://goods.ezbook.ru', fallback: '/my-bookings' })
