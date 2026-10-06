import { useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { apiErrorMessage, passApi, visitorsApi } from '../lib/api'
import type { PassDto, VisitorListItemDto } from '../types/api'
import { Alert, EmptyState, PageHeader, Panel } from '../components/ui/Panel'
import { LoadingMask } from '../components/LoadingMask'
import { Button } from '../components/ui/Button'
import { TextInput, FieldLabel } from '../components/ui/Field'
import { Pagination } from '../components/ui/Pagination'

function canPrintPass(v: VisitorListItemDto) {
  const status = String(v.statusLabel).toLowerCase()
  return !!v.passCode || status.includes('inside') || status.includes('approved')
}

export function VisitorPassesPage() {
  const navigate = useNavigate()
  const [query, setQuery] = useState('')
  const [searchedQuery, setSearchedQuery] = useState<string | null>(null)
  const [results, setResults] = useState<VisitorListItemDto[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function fetchPage(term: string, nextPage: number, size: number) {
    setBusy(true)
    setError(null)
    try {
      const result = await visitorsApi.search({ query: term, page: nextPage, pageSize: size })
      setResults(result.items)
      setTotal(result.totalCount)
      setPage(nextPage)
      setSearchedQuery(term)
    } catch (err) {
      setError(apiErrorMessage(err))
    } finally {
      setBusy(false)
    }
  }

  function search(e: FormEvent) {
    e.preventDefault()
    void fetchPage(query.trim(), 1, pageSize)
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
      {busy ? <LoadingMask label="Working…" /> : null}
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
        <EmptyState title={searchedQuery === null ? 'Search for a visitor to print a pass' : 'No visits matched your search'} />
      ) : (
        <Panel className="p-0">
          <div className="divide-y divide-gray-100">
            {results.map((v) => (
              <div key={v.visitId} className="flex flex-col gap-3 px-4 py-4 sm:flex-row sm:items-center sm:justify-between sm:px-5">
                <div>
                  <Link to={`/visitors/${v.visitId}`} className="font-semibold text-steel hover:underline">
                    {v.visitorName}
                  </Link>
                  <div className="text-sm text-gray-500">
                    {v.visitNumber} · {v.companyName} · {v.statusLabel}
                  </div>
                </div>
                {canPrintPass(v) ? (
                  <Button variant="amber" disabled={busy} onClick={() => void print(v.visitId)}>
                    Print pass
                  </Button>
                ) : (
                  <span className="text-sm text-ink-muted">No pass for this status</span>
                )}
              </div>
            ))}
          </div>
          <Pagination
            page={page}
            pageSize={pageSize}
            total={total}
            onPageChange={(p) => searchedQuery !== null && void fetchPage(searchedQuery, p, pageSize)}
            onPageSizeChange={(size) => {
              setPageSize(size)
              if (searchedQuery !== null) void fetchPage(searchedQuery, 1, size)
            }}
          />
        </Panel>
      )}
    </div>
  )
}
