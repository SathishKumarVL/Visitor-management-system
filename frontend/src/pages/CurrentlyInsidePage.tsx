import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiErrorMessage, visitorsApi } from '../lib/api'
import type { VisitorListItemDto } from '../types/api'
import { Alert, Badge, EmptyState, PageHeader, Panel, Spinner } from '../components/ui/Panel'
import { Button } from '../components/ui/Button'
import { FieldLabel, TextInput, TextSelect } from '../components/ui/Field'
import { formatDateTime, formatDuration, statusBadgeClass } from '../lib/utils'
import { useAuthStore } from '../store/authStore'
import { hasAnyRole } from '../lib/utils'
import { useI18n } from '../i18n'

type SortKey = 'longest' | 'recent' | 'name' | 'company' | 'host'

export function CurrentlyInsidePage() {
  const { t } = useI18n()
  const roles = useAuthStore((s) => s.user?.roles ?? [])
  const canUseCheckout = hasAnyRole(roles, ['SuperAdmin', 'Admin', 'Reception', 'Security'])
  const sortOptions: { value: SortKey; label: string }[] = [
    { value: 'longest', label: t('inside.longest') },
    { value: 'recent', label: t('inside.recent') },
    { value: 'name', label: t('inside.visitorName') },
    { value: 'company', label: t('common.company') },
    { value: 'host', label: t('common.host') },
  ]

  const [items, setItems] = useState<VisitorListItemDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [query, setQuery] = useState('')
  const [sort, setSort] = useState<SortKey>('longest')

  const load = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      setItems(await visitorsApi.inside())
    } catch (e) {
      setError(apiErrorMessage(e, t('inside.loadError')))
    } finally {
      setLoading(false)
    }
  }, [t])

  useEffect(() => {
    void load()
  }, [load])

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase()
    const matches = q
      ? items.filter(
          (v) =>
            v.visitorName.toLowerCase().includes(q) ||
            (v.companyName ?? '').toLowerCase().includes(q) ||
            (v.hostName ?? '').toLowerCase().includes(q) ||
            v.visitNumber.toLowerCase().includes(q) ||
            v.locations.some((l) => l.toLowerCase().includes(q)),
        )
      : items

    const sorted = [...matches]
    sorted.sort((a, b) => {
      switch (sort) {
        case 'name':
          return a.visitorName.localeCompare(b.visitorName)
        case 'company':
          return (a.companyName ?? '').localeCompare(b.companyName ?? '')
        case 'host':
          return (a.hostName ?? '').localeCompare(b.hostName ?? '')
        case 'recent':
          // Shortest time on site first, i.e. most recent arrival.
          return (a.durationMinutes ?? 0) - (b.durationMinutes ?? 0)
        default:
          return (b.durationMinutes ?? 0) - (a.durationMinutes ?? 0)
      }
    })
    return sorted
  }, [items, query, sort])

  return (
    <div>
      <PageHeader
        eyebrow={t('inside.eyebrow')}
        title={t('inside.title')}
        subtitle={t('inside.subtitle')}
        actions={<Button variant="secondary" onClick={() => void load()}>{t('common.retry')}</Button>}
      />

      <div className="mb-6 grid gap-3 sm:grid-cols-[auto_1fr] sm:items-end">
        <Panel className="bg-brand-soft px-6 py-5 sm:min-w-[180px]">
          <p className="text-xs font-semibold uppercase tracking-[0.14em] text-ink-muted">{t('inside.title')}</p>
          <p className="mt-1 text-4xl font-semibold text-ink">{items.length}</p>
        </Panel>
        <Panel className="grid gap-3 sm:grid-cols-[2fr_1fr]">
          <div>
            <FieldLabel htmlFor="insideSearch">{t('common.search')}</FieldLabel>
            <TextInput
              id="insideSearch"
              type="search"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder={t('inside.searchPlaceholder')}
            />
          </div>
          <div>
            <FieldLabel htmlFor="insideSort">{t('inside.sortBy')}</FieldLabel>
            <TextSelect id="insideSort" value={sort} onChange={(e) => setSort(e.target.value as SortKey)}>
              {sortOptions.map((o) => (
                <option key={o.value} value={o.value}>
                  {o.label}
                </option>
              ))}
            </TextSelect>
          </div>
        </Panel>
      </div>

      <div aria-live="polite" aria-atomic="true">
        {error ? (
          <div className="mb-4">
            <Alert tone="error">{error}</Alert>
          </div>
        ) : null}
      </div>
      {loading ? <Spinner /> : null}
      {!loading && filtered.length === 0 ? (
        <EmptyState
          title={t('inside.empty')}
          description={query ? 'No matches for your search.' : 'Checked-in visitors will appear here.'}
        />
      ) : null}

      <div className="space-y-3">
        {filtered.map((v) => (
          <Panel key={v.visitId} className={v.isLongStay ? 'border-danger/40 bg-red-50/40' : undefined}>
            <div className="flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
              <div>
                <div className="flex flex-wrap items-center gap-2">
                  <Link to={`/visitors/${v.visitId}`} className="text-lg font-semibold text-ink hover:text-primary">
                    {v.visitorName}
                  </Link>
                  <Badge className={statusBadgeClass(v.statusLabel)}>{v.statusLabel}</Badge>
                  {v.isLongStay ? <Badge className="bg-red-100 text-danger ring-1 ring-red-200">Long stay</Badge> : null}
                </div>
                <p className="mt-1 text-sm text-ink-muted">
                  {v.companyName} · Person to meet {v.hostName}
                  {v.locations.length ? ` · ${v.locations.join(', ')}` : ''}
                </p>
                <p className="mt-0.5 text-sm text-ink-muted">
                  {v.visitNumber} · In since {formatDateTime(v.checkInAt)} · {formatDuration(v.durationMinutes)}
                </p>
              </div>
              <div className="flex flex-wrap gap-2">
                <Link to={`/visitors/${v.visitId}`}>
                  <Button variant="secondary">View</Button>
                </Link>
                {canUseCheckout ? (
                  <Link to="/checkout/face">
                    <Button variant="secondary">{t('inside.checkOutFace')}</Button>
                  </Link>
                ) : null}
              </div>
            </div>
          </Panel>
        ))}
      </div>
    </div>
  )
}
