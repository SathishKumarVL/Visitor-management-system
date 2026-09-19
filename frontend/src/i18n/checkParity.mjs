/**
 * Asserts English and Tamil locale dictionaries have matching keys.
 * Run: node src/i18n/checkParity.mjs
 */
import { readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

function flattenKeys(obj, prefix = '') {
  const keys = []
  for (const [k, v] of Object.entries(obj)) {
    const path = prefix ? `${prefix}.${k}` : k
    if (v && typeof v === 'object' && !Array.isArray(v)) {
      keys.push(...flattenKeys(v, path))
    } else {
      keys.push(path)
    }
  }
  return keys
}

const dir = dirname(fileURLToPath(import.meta.url))
const en = JSON.parse(readFileSync(join(dir, 'locales/en.json'), 'utf8'))
const ta = JSON.parse(readFileSync(join(dir, 'locales/ta.json'), 'utf8'))

const enKeys = new Set(flattenKeys(en))
const taKeys = new Set(flattenKeys(ta))
const missingInTa = [...enKeys].filter((k) => !taKeys.has(k)).sort()
const missingInEn = [...taKeys].filter((k) => !enKeys.has(k)).sort()

if (missingInTa.length || missingInEn.length) {
  console.error('i18n key parity failed')
  if (missingInTa.length) console.error('Missing in ta:', missingInTa.join(', '))
  if (missingInEn.length) console.error('Missing in en:', missingInEn.join(', '))
  process.exit(1)
}

console.log(`i18n key parity OK (${enKeys.size} keys)`)
