// @vitest-environment node
import goodsWorker from '../../public/sw.js?raw'
import { describeWorkerRouting } from '@/test/workerRouting'

// ARCHITECTURE_CYCLE33.md §33.5.2 / §33.13.2 — the goods worker's routing table (same table as ezbook's).
describeWorkerRouting({ name: 'goods', source: goodsWorker, origin: 'https://goods.ezbook.ru', peer: 'https://ezbook.ru', fallback: '/cabinet' })
