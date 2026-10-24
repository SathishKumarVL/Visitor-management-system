import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiErrorMessage, visitorsApi } from '../lib/api'
import type { VisitorListItemDto } from '../types/api'
import { Alert, Badge, EmptyState, PageHeader, Panel, Spinner } from '../components/ui/Panel'
import { LoadingMask } from '../components/LoadingMask'
import { Button } from '../components/ui/Button'
import { FieldLabel, TextInput, TextSelect } from '../components/ui/Field'
import { Pagination } from '../components/ui/Pagination'
import { usePagination } from '../lib/usePagination'
import { formatDateTime, formatDuration, hasAnyRole, statusBadgeClass } from '../lib/utils'
import { useAuthStore } from '../store/authStore'
import { useI18n } from '../i18n'

type SortKey = 'longest' | 'recent' | 'name' | 'company' | 'host'

export function CurrentlyInsidePage() {
  const { t } = useI18n()
  const roles = useAuthStore((s) => s.user?.roles ?? [])
  const canUseCheckout = hasAnyRole(roles, ['SuperAdmin', 'Admin', 'Reception', 'Security'])
  const canManualCheckout = hasAnyRole(roles, ['SuperAdmin'])
  const canEditMom = hasAnyRole(roles, ['SuperAdmin', 'Admin', 'Host'])
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
  const [message, setMessage] = useState<string | null>(null)
  const [query, setQuery] = useState('')
  const [sort, setSort] = useState<SortKey>('longest')
  const [checkingOutId, setCheckingOutId] = useState<string | null>(null)
  const [momFlagId, setMomFlagId] = useState<string | null>(null)
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
          return (a.durationMinutes ?? 0) - (b.durationMinutes ?? 0)
        default:
          return (b.durationMinutes ?? 0) - (a.durationMinutes ?? 0)
      }
    })
    return sorted
  }, [items, query, sort])

  const { pageItems, pagination } = usePagination(filtered, 10, `${query}|${sort}`)

  async function setMomNotRequired(v: VisitorListItemDto, notRequired: boolean) {
    if (notRequired && !window.confirm(t('inside.momNotRequiredConfirm').replace('{name}', v.visitorName))) return
    setMomFlagId(v.visitId)
    setError(null)
    setMessage(null)
    try {
      await visitorsApi.setMomNotRequired(v.visitId, notRequired)
      setMessage(
        (notRequired ? t('inside.momNotRequiredOk') : t('inside.momRequiredAgainOk')).replace('{name}', v.visitorName),
      )
      await load()
    } catch (e) {
      setError(apiErrorMessage(e, t('inside.momNotRequiredError')))
    } finally {
      setMomFlagId(null)
    }
  }

  async function manualCheckOut(v: VisitorListItemDto) {
    if (!canManualCheckout) return
    const ok = window.confirm(
      t('inside.manualCheckOutConfirmNamed').replace('{name}', v.visitorName),
    )
    if (!ok) return
    setCheckingOutId(v.visitId)
    setError(null)
    setMessage(null)
    try {
      await visitorsApi.checkOut(v.visitId, { skipFeedback: true })
      setMessage(t('inside.checkedOutOk').replace('{name}', v.visitorName))
      await load()
    } catch (e) {
      setError(apiErrorMessage(e, t('inside.checkOutError')))
    } finally {
      setCheckingOutId(null)
    }
  }

  return (
    <div>
      {checkingOutId ? <LoadingMask label={t('inside.checkingOut')} /> : null}
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

      {error ? <Alert tone="error">{error}</Alert> : null}
      {message ? <Alert tone="success">{message}</Alert> : null}
      {loading ? <Spinner label="Loading currently inside…" /> : null}
      {!loading && filtered.length === 0 ? (
        <EmptyState
          title={t('inside.empty')}
          description={query ? 'No matches for your search.' : 'Checked-in visitors will appear here.'}
        />
      ) : null}

      <div className="space-y-3">
        {pageItems.map((v) => (
          <Panel key={v.visitId} className={v.isLongStay ? 'border-danger/40 bg-red-50/40' : undefined}>
            <div className="flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
              <div>
                <div className="flex flex-wrap items-center gap-2">
                  <Link to={`/visitors/${v.visitId}`} className="text-lg font-semibold text-ink hover:text-primary">
                    {v.visitorName}
                  </Link>
                  <Badge className={statusBadgeClass(v.statusLabel)}>{v.statusLabel}</Badge>
                  {v.isLongStay ? <Badge className="bg-red-100 text-danger ring-1 ring-red-200">Long stay</Badge> : null}
                  {v.momEmailSent ? (
                    <Badge className="bg-emerald-100 text-emerald-800 ring-1 ring-emerald-200">MoM sent</Badge>
                  ) : v.hasMeetingMinutes ? (
                    <Badge className="bg-sky-50 text-sky-800 ring-1 ring-sky-200">MoM saved, not sent</Badge>
                  ) : v.momNotRequired ? (
                    <Badge className="bg-slate-100 text-slate-700 ring-1 ring-slate-200">{t('inside.momNotRequired')}</Badge>
                  ) : (
                    <Badge className="bg-amber-100 text-amber-900 ring-1 ring-amber-200">MoM pending</Badge>
                  )}
                </div>
                <p className="mt-1 text-sm text-ink-muted">
                  {v.companyName} · Person to meet {v.hostName}
                  {v.locations.length ? ` · ${v.locations.join(', ')}` : ''}
                  {v.meetingHallName ? ` · Hall ${v.meetingHallName}` : ''}
                </p>
                <p className="mt-0.5 text-sm text-ink-muted">
                  {v.visitNumber} · In since {formatDateTime(v.checkInAt)} · {formatDuration(v.durationMinutes)}
                </p>
              </div>
              <div className="flex flex-wrap gap-2">
                {canEditMom && !v.momEmailSent ? (
                  <Link to={`/visitors/${v.visitId}/minutes`}>
                    <Button variant="secondary">
                      {v.hasMeetingMinutes ? t('inside.editMom') : t('inside.addMom')}
                    </Button>
                  </Link>
                ) : null}
                {!v.hasMeetingMinutes ? (
                  <Button
                    variant="secondary"
                    disabled={momFlagId === v.visitId}
                    onClick={() => void setMomNotRequired(v, !v.momNotRequired)}
                  >
                    {v.momNotRequired ? t('inside.momRequiredAgain') : t('inside.momNotRequired')}
                  </Button>
                ) : null}
                <Link to={`/visitors/${v.visitId}`}>
                  <Button variant="secondary">View</Button>
                </Link>
                {canUseCheckout ? (
                  <Link to="/checkout/face">
                    <Button variant="secondary">{t('inside.checkOutFace')}</Button>
                  </Link>
                ) : null}
                {canManualCheckout ? (
                  <Button
                    variant="amber"
                    disabled={checkingOutId === v.visitId}
                    onClick={() => void manualCheckOut(v)}
                  >
                    {t('inside.checkOutManual')}
                  </Button>
                ) : null}
              </div>
            </div>
          </Panel>
        ))}
      </div>
      {!loading && filtered.length > 0 ? (
        <Panel className="mt-3 p-0">
          <Pagination {...pagination} bordered={false} />
        </Panel>
      ) : null}
    </div>
  )
}
