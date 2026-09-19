import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react'
import en from './locales/en.json'
import ta from './locales/ta.json'

export type Lang = 'en' | 'ta'

const STORAGE_KEY = 'tiaano_vms_lang'

const dictionaries: Record<Lang, Record<string, unknown>> = { en, ta }

function readStoredLang(): Lang {
  try {
    const v = localStorage.getItem(STORAGE_KEY)
    if (v === 'ta' || v === 'en') return v
  } catch {
    /* ignore */
  }
  return 'en'
}

function lookup(dict: Record<string, unknown>, key: string): string | undefined {
  const parts = key.split('.')
  let cur: unknown = dict
  for (const p of parts) {
    if (cur == null || typeof cur !== 'object') return undefined
    cur = (cur as Record<string, unknown>)[p]
  }
  return typeof cur === 'string' ? cur : undefined
}

type I18nValue = {
  lang: Lang
  setLang: (lang: Lang) => void
  t: (key: string, fallback?: string) => string
}

const I18nContext = createContext<I18nValue | null>(null)

export function I18nProvider({ children }: { children: ReactNode }) {
  const [lang, setLangState] = useState<Lang>(() =>
    typeof window !== 'undefined' ? readStoredLang() : 'en',
  )

  const setLang = useCallback((next: Lang) => {
    setLangState(next)
    try {
      localStorage.setItem(STORAGE_KEY, next)
    } catch {
      /* ignore */
    }
  }, [])

  const t = useCallback(
    (key: string, fallback?: string) => {
      const primary = lookup(dictionaries[lang], key)
      if (primary) return primary
      const english = lookup(dictionaries.en, key)
      return english ?? fallback ?? key
    },
    [lang],
  )

  const value = useMemo(() => ({ lang, setLang, t }), [lang, setLang, t])

  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>
}

export function useI18n() {
  const ctx = useContext(I18nContext)
  if (!ctx) throw new Error('useI18n must be used within I18nProvider')
  return ctx
}

export function LanguageSwitcher({ className }: { className?: string }) {
  const { lang, setLang } = useI18n()
  return (
    <div className={className} role="group" aria-label="Language">
      <button
        type="button"
        className={`rounded-lg px-2 py-1 text-xs font-medium ${
          lang === 'en' ? 'bg-primary text-white' : 'text-ink-muted hover:bg-aqua-light'
        }`}
        onClick={() => setLang('en')}
      >
        English
      </button>
      <button
        type="button"
        className={`ml-1 rounded-lg px-2 py-1 text-xs font-medium ${
          lang === 'ta' ? 'bg-primary text-white' : 'text-ink-muted hover:bg-aqua-light'
        }`}
        onClick={() => setLang('ta')}
      >
        தமிழ்
      </button>
    </div>
  )
}

/** Flatten nested keys for parity checks (used by scripts/tests). */
export function flattenKeys(obj: Record<string, unknown>, prefix = ''): string[] {
  const keys: string[] = []
  for (const [k, v] of Object.entries(obj)) {
    const path = prefix ? `${prefix}.${k}` : k
    if (v && typeof v === 'object' && !Array.isArray(v)) {
      keys.push(...flattenKeys(v as Record<string, unknown>, path))
    } else {
      keys.push(path)
    }
  }
  return keys
}

export function localeKeyParity(): { missingInTa: string[]; missingInEn: string[] } {
  const enKeys = new Set(flattenKeys(en as Record<string, unknown>))
  const taKeys = new Set(flattenKeys(ta as Record<string, unknown>))
  return {
    missingInTa: [...enKeys].filter((k) => !taKeys.has(k)).sort(),
    missingInEn: [...taKeys].filter((k) => !enKeys.has(k)).sort(),
  }
}
