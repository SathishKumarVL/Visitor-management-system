import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
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

  const [momVisit, setMomVisit] = useState<VisitorListItemDto | null>(null)
  const [momText, setMomText] = useState('')
  const [momLoading, setMomLoading] = useState(false)
  const [momSaving, setMomSaving] = useState(false)
  const [momError, setMomError] = useState<string | null>(null)
  const noticeRef = useRef<HTMLDivElement | null>(null)

  useEffect(() => {
    if (!message && !error) return
    noticeRef.current?.scrollIntoView({ behavior: 'smooth', block: 'center' })
  }, [message, error])

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

  async function openMom(v: VisitorListItemDto) {
    setMomVisit(v)
    setMomText('')
    setMomError(null)
    setMomLoading(true)
    try {
      const detail = await visitorsApi.get(v.visitId)
      setMomText(detail.meetingMinutes || '')
    } catch (e) {
      setMomError(apiErrorMessage(e, t('inside.momLoadError')))
    } finally {
      setMomLoading(false)
    }
  }

  function closeMom() {
    if (momSaving) return
    setMomVisit(null)
    setMomText('')
    setMomError(null)
  }

  async function saveMom() {
    if (!momVisit) return
    const text = momText.trim()
    if (!text) {
      setMomError(t('inside.momRequired'))
      return
    }
    setMomSaving(true)
    setMomError(null)
    setError(null)
    setMessage(null)
    try {
      await visitorsApi.saveMeetingMinutes(momVisit.visitId, text)
      const name = momVisit.visitorName
      setMomVisit(null)
      setMomText('')
      setMessage(t('inside.momSavedOk').replace('{name}', name))
      await load()
    } catch (e) {
      setMomError(apiErrorMessage(e, t('inside.momSaveError')))
    } finally {
      setMomSaving(false)
    }
  }

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
      {momSaving || checkingOutId ? (
        <LoadingMask label={checkingOutId ? t('inside.checkingOut') : t('inside.momSaving')} />
      ) : null}
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

      <div ref={noticeRef} aria-live="polite" aria-atomic="true" className="scroll-mt-24">
        {error ? (
          <div className="mb-4">
            <Alert tone="error">{error}</Alert>
          </div>
        ) : null}
        {message ? (
          <div className="mb-4">
            <Alert tone="success">{message}</Alert>
          </div>
        ) : null}
      </div>
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
                  {v.hasMeetingMinutes ? (
                    <Badge className="bg-emerald-100 text-emerald-800 ring-1 ring-emerald-200">MoM saved</Badge>
                  ) : v.momNotRequired ? (
                    <Badge className="bg-slate-100 text-slate-700 ring-1 ring-slate-200">{t('inside.momNotRequired')}</Badge>
                  ) : (
                    <Badge className="bg-amber-100 text-amber-900 ring-1 ring-amber-200">MoM pending</Badge>
                  )}
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
                {canEditMom ? (
                  <Button variant="secondary" onClick={() => void openMom(v)}>
                    {v.hasMeetingMinutes ? t('inside.editMom') : t('inside.addMom')}
                  </Button>
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

      {momVisit ? (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center bg-ink/40 p-4 backdrop-blur-[1px]"
          role="dialog"
          aria-modal="true"
          aria-labelledby="mom-dialog-title"
          onClick={closeMom}
        >
          <div className="w-full max-w-lg" onClick={(e) => e.stopPropagation()}>
            <Panel className="shadow-xl">
              <h2 id="mom-dialog-title" className="text-lg font-semibold text-ink">
                {momVisit.hasMeetingMinutes ? t('inside.editMom') : t('inside.addMom')}
              </h2>
              <p className="mt-1 text-sm text-ink-muted">
                {momVisit.visitorName} · {momVisit.visitNumber}
              </p>
              <p className="mt-2 text-sm text-ink-muted">{t('inside.momHint')}</p>

              {momError ? (
                <div className="mt-3">
                  <Alert tone="error">{momError}</Alert>
                </div>
              ) : null}

              {momLoading ? (
                <div className="mt-4">
                  <Spinner label={t('inside.momLoading')} />
                </div>
              ) : (
                <div className="mt-4 space-y-3">
                  <div>
                    <FieldLabel htmlFor="insideMeetingMinutes">{t('inside.momLabel')}</FieldLabel>
                    <textarea
                      id="insideMeetingMinutes"
                      rows={6}
                      maxLength={4000}
                      className="w-full rounded-md border border-gray-200 px-3 py-2 text-sm shadow-sm focus:border-steel focus:outline-none focus:ring-1 focus:ring-steel"
                      value={momText}
                      onChange={(e) => setMomText(e.target.value)}
                      autoFocus
                    />
                  </div>
                  <div className="flex flex-wrap justify-end gap-2">
                    <Button variant="secondary" disabled={momSaving} onClick={closeMom}>
                      {t('common.cancel')}
                    </Button>
                    <Button disabled={momSaving || !momText.trim()} onClick={() => void saveMom()}>
                      {momSaving ? t('inside.momSaving') : t('inside.momSave')}
                    </Button>
                  </div>
                </div>
              )}
            </Panel>
          </div>
        </div>
      ) : null}
    </div>
  )
}
