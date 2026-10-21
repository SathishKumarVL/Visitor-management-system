import { useEffect, useRef, useState } from 'react'
import { apiErrorMessage, lookupsApi } from '../lib/api'
import type { LookupCheckResultDto, LookupMatchDto, LookupPick } from '../types/api'
import { cn } from '../lib/utils'
import { TextInput } from './ui/Field'

const SEARCH_DELAY_MS = 350

type SearchState =
  | { kind: 'idle' }
  | { kind: 'searching' }
  | { kind: 'results'; matches: LookupMatchDto[] }
  | { kind: 'error'; message: string }

/**
 * Type-ahead over a lookup source. Picking a result keeps the server's token so the visit stores the verified record;
 * editing the text afterwards drops it, and the typed value is saved as not verified.
 */
export function LookupFieldInput({
  id,
  sourceId,
  minChars,
  value,
  pick,
  telephone,
  email,
  context,
  onChange,
}: {
  id: string
  sourceId: string
  minChars: number
  value: string
  pick?: LookupPick
  telephone: string
  email: string
  /** The purpose's other extra fields by label, for sources whose request uses {field:Label}. */
  context: Record<string, string>
  onChange: (value: string, pick?: LookupPick) => void
}) {
  const [search, setSearch] = useState<SearchState>({ kind: 'idle' })
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState(-1)
  const [check, setCheck] = useState<LookupCheckResultDto | null>(null)
  const timer = useRef<number | undefined>(undefined)
  const abort = useRef<AbortController | null>(null)

  useEffect(() => () => {
    window.clearTimeout(timer.current)
    abort.current?.abort()
  }, [])

  function typed(text: string) {
    onChange(text, undefined)
    window.clearTimeout(timer.current)
    abort.current?.abort()
    const q = text.trim()
    if (q.length < minChars) {
      setSearch({ kind: 'idle' })
      setOpen(false)
      return
    }
    setSearch({ kind: 'searching' })
    setOpen(true)
    timer.current = window.setTimeout(() => {
      const controller = new AbortController()
      abort.current = controller
      lookupsApi
        .search(sourceId, q, context, controller.signal)
        .then((matches) => {
          if (controller.signal.aborted) return
          setSearch({ kind: 'results', matches })
          setActive(matches.length > 0 ? 0 : -1)
        })
        .catch((err) => {
          if (controller.signal.aborted) return
          setSearch({ kind: 'error', message: apiErrorMessage(err, 'Search is not available right now.') })
        })
    }, SEARCH_DELAY_MS)
  }

  function choose(m: LookupMatchDto) {
    onChange(m.display, { token: m.token, display: m.display, details: m.details, hasMatchCheck: m.hasMatchCheck })
    setOpen(false)
    setSearch({ kind: 'idle' })
  }

  const pickToken = pick?.hasMatchCheck ? pick.token : null
  useEffect(() => {
    setCheck(null)
    if (!pickToken) return
    const handle = window.setTimeout(() => {
      lookupsApi
        .check(pickToken, telephone, email)
        .then(setCheck)
        .catch(() => setCheck(null))
    }, 400)
    return () => window.clearTimeout(handle)
  }, [pickToken, telephone, email])

  const matches = search.kind === 'results' ? search.matches : []

  return (
    <div className="relative">
      <TextInput
        id={id}
        maxLength={500}
        autoComplete="off"
        role="combobox"
        aria-expanded={open}
        aria-controls={`${id}-list`}
        aria-autocomplete="list"
        value={value}
        placeholder={`Type at least ${minChars} letters to search`}
        onChange={(e) => typed(e.target.value)}
        onFocus={() => {
          if (!pick && matches.length > 0) setOpen(true)
        }}
        onBlur={() => window.setTimeout(() => setOpen(false), 150)}
        onKeyDown={(e) => {
          if (!open || matches.length === 0) return
          if (e.key === 'ArrowDown') {
            e.preventDefault()
            setActive((i) => Math.min(i + 1, matches.length - 1))
          } else if (e.key === 'ArrowUp') {
            e.preventDefault()
            setActive((i) => Math.max(i - 1, 0))
          } else if (e.key === 'Enter' && active >= 0) {
            e.preventDefault()
            choose(matches[active])
          } else if (e.key === 'Escape') {
            setOpen(false)
          }
        }}
      />

      {open && search.kind !== 'idle' ? (
        <div
          id={`${id}-list`}
          role="listbox"
          className="absolute left-0 right-0 z-30 mt-1 max-h-72 overflow-auto rounded-xl border border-border bg-white shadow-lg"
        >
          {search.kind === 'searching' ? (
            <p className="px-3 py-3 text-sm text-ink-muted">Searching…</p>
          ) : search.kind === 'error' ? (
            <p className="px-3 py-3 text-sm text-amber-700">{search.message}</p>
          ) : matches.length === 0 ? (
            <p className="px-3 py-3 text-sm text-ink-muted">No matches. You can type the value in.</p>
          ) : (
            matches.map((m, i) => (
              <button
                key={`${m.recordId}-${i}`}
                type="button"
                role="option"
                aria-selected={i === active}
                className={cn(
                  'block min-h-11 w-full px-3 py-2 text-left text-sm',
                  i === active ? 'bg-aqua-light' : 'hover:bg-aqua-light/60',
                )}
                onMouseDown={(e) => e.preventDefault()}
                onClick={() => choose(m)}
              >
                <span className="font-medium text-ink">{m.display}</span>
                {m.details.length > 0 ? (
                  <span className="block text-xs text-ink-muted">
                    {m.details.map((d) => `${d.label}: ${d.value}`).join(' · ')}
                  </span>
                ) : null}
              </button>
            ))
          )}
        </div>
      ) : null}

      {pick ? (
        <div className="mt-1 space-y-0.5 text-xs">
          <p className="font-medium text-emerald-700">✓ Picked from the list</p>
          {pick.hasMatchCheck ? <MatchLine check={check} /> : null}
        </div>
      ) : value.trim() ? (
        <p className="mt-1 text-xs text-amber-700">Typed by hand: will be saved as not verified.</p>
      ) : null}
    </div>
  )
}

function MatchLine({ check }: { check: LookupCheckResultDto | null }) {
  if (!check?.matchAgainst) return null
  const what = check.matchAgainst === 'mobile' ? 'mobile number' : 'email'
  if (check.matched == null) return <p className="text-ink-muted">Enter the visitor&apos;s {what} to check it against this record.</p>
  if (check.matched) return <p className="font-medium text-emerald-700">✓ Visitor&apos;s {what} is on this record</p>
  return (
    <p className="font-medium text-danger">
      Visitor&apos;s {what} is not on this record
      {check.blockOnMismatch ? '. Registration will be refused.' : '. The visit will be saved with a warning.'}
    </p>
  )
}
