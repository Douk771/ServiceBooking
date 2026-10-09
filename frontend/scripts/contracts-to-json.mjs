// Конвертирует contracts/cycleNN/openapi.yaml в openapi.json для OpenApiContract.Load (ServiceBooking.Tests).
import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import yaml from 'js-yaml';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const cycles = ['cycle31', 'cycle32', 'cycle35', 'cycle37', 'cycle38', 'cycle39', 'cycle42'];
for (const cycle of cycles) {
  const dir = path.join(root, 'contracts', cycle);
  const doc = yaml.load(readFileSync(path.join(dir, 'openapi.yaml'), 'utf8'));
  writeFileSync(path.join(dir, 'openapi.json'), JSON.stringify(doc, null, 2) + '\n');
}
