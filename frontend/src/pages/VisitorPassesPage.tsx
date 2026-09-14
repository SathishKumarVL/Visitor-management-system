import { useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { apiErrorMessage, passApi, visitorsApi } from '../lib/api'
import type { PassDto, VisitorListItemDto } from '../types/api'
import { Alert, EmptyState, PageHeader, Panel } from '../components/ui/Panel'
import { Button } from '../components/ui/Button'
import { TextInput, FieldLabel } from '../components/ui/Field'

export function VisitorPassesPage() {
  const navigate = useNavigate()
  const [query, setQuery] = useState('')
  const [results, setResults] = useState<VisitorListItemDto[]>([])
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function search(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      const page = await visitorsApi.search({ query: query.trim(), page: 1, pageSize: 20 })
      setResults(page.items.filter((v) => v.passCode || String(v.statusLabel).toLowerCase().includes('inside') || String(v.statusLabel).toLowerCase().includes('approved')))
    } catch (err) {
      setError(apiErrorMessage(err))
    } finally {
      setBusy(false)
    }
  }

  async function print(visitId: string) {
    setBusy(true)
    setMessage(null)
    setError(null)
    try {
      const pass: PassDto = await passApi.generate(visitId)
      setMessage(`Pass ready for ${pass.visitorName}`)
      navigate(`/passes/${visitId}/print`)
    } catch (err) {
      setError(apiErrorMessage(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div>
      <PageHeader title="Visitor Passes" subtitle="Search a visit and print or reprint the pass" />
      <Panel className="mb-4">
        <form onSubmit={search} className="flex flex-col gap-3 sm:flex-row sm:items-end">
          <div className="flex-1">
            <FieldLabel htmlFor="q">Search visitor / visit / pass</FieldLabel>
            <TextInput id="q" value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Name, visit number, phone…" required />
          </div>
          <Button type="submit" disabled={busy}>{busy ? 'Searching…' : 'Search'}</Button>
        </form>
      </Panel>

      {error ? <div className="mb-4"><Alert tone="error">{error}</Alert></div> : null}
      {message ? <div className="mb-4"><Alert tone="success">{message}</Alert></div> : null}

      {results.length === 0 ? (
        <EmptyState title="Search for a visitor to print a pass" />
      ) : (
        <div className="space-y-3">
          {results.map((v) => (
            <Panel key={v.visitId} className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
              <div>
                <Link to={`/visitors/${v.visitId}`} className="font-semibold text-steel hover:underline">
                  {v.visitorName}
                </Link>
                <div className="text-sm text-gray-500">
                  {v.visitNumber} · {v.companyName} · {v.statusLabel}
                  {v.passCode ? ` · Pass ${v.passCode}` : ''}
                </div>
              </div>
              <Button variant="amber" disabled={busy} onClick={() => void print(v.visitId)}>
                Print pass
              </Button>
            </Panel>
          ))}
        </div>
      )}
    </div>
  )
}
