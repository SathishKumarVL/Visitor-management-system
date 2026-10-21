import { useCallback, useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { notificationsApi } from '../../lib/api'
import { disablePush, enablePush, getPushState, syncPushSubscription, type PushState } from '../../lib/push'
import { cn, formatDateTime } from '../../lib/utils'
import { useAuthStore } from '../../store/authStore'
import { useI18n } from '../../i18n'
import type { NotificationItemDto, NotificationSummaryDto } from '../../types/api'

const POLL_MS = 60_000
const SHOWN_KEY = 'notifications.loginPopupShown'

function BellIcon() {
  return (
    <svg viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth={2} aria-hidden>
      <path strokeLinecap="round" strokeLinejoin="round" d="M15 17h5l-1.4-1.4A2 2 0 0 1 18 14.2V11a6 6 0 1 0-12 0v3.2c0 .5-.2 1-.6 1.4L4 17h5m6 0a3 3 0 1 1-6 0m6 0H9" />
    </svg>
  )
}

export function NotificationBell() {
  const { t } = useI18n()
  const navigate = useNavigate()
  const userId = useAuthStore((s) => s.user?.id)
  const [summary, setSummary] = useState<NotificationSummaryDto | null>(null)
  const [open, setOpen] = useState(false)
  const [welcome, setWelcome] = useState(false)
  const [pushState, setPushState] = useState<PushState>('unsupported')
  const [pushBusy, setPushBusy] = useState(false)
  const [pushMessage, setPushMessage] = useState<{ tone: 'ok' | 'error'; text: string } | null>(null)
  // Items that were new when the panel opened keep their dot while it stays open.
  const [freshKeys, setFreshKeys] = useState<ReadonlySet<string>>(new Set())
  const panelRef = useRef<HTMLDivElement>(null)

  const load = useCallback(async () => {
    try {
      const next = await notificationsApi.summary()
      setSummary(next)
      return next
    } catch {
      return null
    }
  }, [])

  const markSeen = useCallback(async (shown: NotificationSummaryDto) => {
    setFreshKeys(freshKeysOf(shown))
    if (shown.unseen === 0) return
    try {
      await notificationsApi.markSeen()
      setSummary((s) => (s ? { ...s, unseen: 0 } : s))
    } catch {
      // The badge simply stays until the next successful open.
    }
  }, [])

  // Poll while signed in; refresh straight away when the tab comes back into view.
  useEffect(() => {
    if (!userId) return
    let cancelled = false
    void load().then((first) => {
      if (cancelled || !first || first.total === 0) return
      // Once per sign-in session: open the list so pending work is seen at login.
      const key = `${SHOWN_KEY}:${userId}`
      if (sessionStorage.getItem(key)) return
      sessionStorage.setItem(key, '1')
      setWelcome(true)
      setOpen(true)
      void markSeen(first)
    })
    const timer = window.setInterval(() => {
      if (document.visibilityState === 'visible') void load()
    }, POLL_MS)
    const onVisible = () => {
      if (document.visibilityState === 'visible') void load()
    }
    document.addEventListener('visibilitychange', onVisible)
    return () => {
      cancelled = true
      window.clearInterval(timer)
      document.removeEventListener('visibilitychange', onVisible)
    }
  }, [userId, load, markSeen])

  useEffect(() => {
    if (!userId) return
    void syncPushSubscription()
      .catch(() => undefined)
      .finally(() => void getPushState().then(setPushState))
  }, [userId])

  // A tapped push notification asks an already-open tab to show its page.
  useEffect(() => {
    if (!('serviceWorker' in navigator)) return
    const onMessage = (event: MessageEvent) => {
      if (event.data?.type === 'open-url' && typeof event.data.url === 'string') {
        navigate(event.data.url)
        void load().then((next) => next && markSeen(next))
      }
    }
    navigator.serviceWorker.addEventListener('message', onMessage)
    return () => navigator.serviceWorker.removeEventListener('message', onMessage)
  }, [navigate, load, markSeen])

  useEffect(() => {
    if (!open) return
    const onDown = (e: MouseEvent) => {
      if (panelRef.current && !panelRef.current.contains(e.target as Node)) close()
    }
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') close()
    }
    document.addEventListener('mousedown', onDown)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('mousedown', onDown)
      document.removeEventListener('keydown', onKey)
    }
  }, [open])

  function close() {
    setOpen(false)
    setWelcome(false)
    setPushMessage(null)
  }

  function go(link: string) {
    close()
    navigate(link)
  }

  async function runPush(action: () => Promise<string | undefined | void>) {
    setPushBusy(true)
    setPushMessage(null)
    try {
      const message = await action()
      if (message) setPushMessage({ tone: 'ok', text: message })
    } catch (e) {
      setPushMessage({ tone: 'error', text: e instanceof Error ? e.message : String(e) })
    } finally {
      setPushBusy(false)
      setPushState(await getPushState())
    }
  }

  const total = summary?.total ?? 0
  const unseen = summary?.unseen ?? 0

  return (
    <div className="relative" ref={panelRef}>
      <button
        type="button"
        onClick={() => {
          if (open) return close()
          setOpen(true)
          void load().then((next) => next && markSeen(next))
        }}
        className="relative inline-flex min-h-11 min-w-11 items-center justify-center rounded-xl border border-border bg-white text-ink hover:bg-aqua-light"
        aria-label={unseen > 0 ? `${t('notifications.title')} (${unseen})` : t('notifications.title')}
        aria-expanded={open}
        aria-haspopup="dialog"
      >
        <BellIcon />
        {unseen > 0 ? (
          <span className="absolute -right-1.5 -top-1.5 inline-flex min-w-5 items-center justify-center rounded-full bg-danger px-1 text-[11px] font-semibold leading-5 text-white">
            {unseen > 99 ? '99+' : unseen}
          </span>
        ) : null}
      </button>

      {open ? (
        <div
          role="dialog"
          aria-label={t('notifications.title')}
          className="fixed inset-x-2 top-16 z-50 max-h-[calc(100vh-5rem)] overflow-y-auto rounded-2xl border border-border bg-white shadow-xl sm:absolute sm:inset-x-auto sm:right-0 sm:top-full sm:mt-2 sm:w-[24rem]"
        >
          <div className="flex items-start justify-between gap-3 border-b border-border/70 px-4 py-3">
            <div>
              <p className="text-sm font-semibold text-ink">
                {welcome ? t('notifications.welcomeTitle') : t('notifications.title')}
              </p>
              {welcome ? <p className="text-xs text-ink-muted">{t('notifications.welcomeHint')}</p> : null}
            </div>
            <button
              type="button"
              onClick={close}
              className="rounded-lg px-2 text-lg leading-none text-ink-muted hover:text-ink"
              aria-label={t('notifications.close')}
            >
              ×
            </button>
          </div>

          {summary === null ? (
            <p className="px-4 py-6 text-center text-sm text-ink-muted">{t('common.loading')}</p>
          ) : total === 0 ? (
            <p className="px-4 py-6 text-center text-sm text-ink-muted">{t('notifications.allCaughtUp')}</p>
          ) : (
            <div className="divide-y divide-border/70">
              <Section
                title={summary.appointmentsAreMine ? t('notifications.myAppointments') : t('notifications.expectedToday')}
                count={summary.appointmentsTodayCount}
                items={summary.appointmentsToday}
                isFresh={(i) => freshKeys.has(`appointment:${i.visitId}`)}
                viewAll="/visitors/expected"
                describe={(i) => i.time ?? ''}
                onOpen={go}
                t={t}
              />
              <Section
                title={t('notifications.momPending')}
                count={summary.momPendingCount}
                items={summary.momPending}
                isFresh={(i) => freshKeys.has(`mom:${i.visitId}`)}
                viewAll="/visitors/mom-pending"
                describe={(i) => `${t('notifications.checkedOut')} ${formatDateTime(i.at)}`}
                onOpen={go}
                t={t}
              />
              <Section
                title={t('notifications.approvals')}
                count={summary.approvalsCount}
                items={summary.approvals}
                isFresh={(i) => freshKeys.has(`approval:${i.visitId}`)}
                viewAll="/visitors?status=1"
                describe={(i) => formatDateTime(i.at)}
                onOpen={go}
                t={t}
              />
            </div>
          )}

          <PushControls
            state={pushState}
            busy={pushBusy}
            message={pushMessage}
            onEnable={() => runPush(async () => {
              await enablePush()
              return t('notifications.pushEnabled')
            })}
            onDisable={() => runPush(async () => {
              await disablePush()
              return t('notifications.pushDisabled')
            })}
            onTest={() => runPush(() => notificationsApi.testPush())}
            t={t}
          />
        </div>
      ) : null}
    </div>
  )
}

function freshKeysOf(summary: NotificationSummaryDto): ReadonlySet<string> {
  const keys = new Set<string>()
  for (const i of summary.appointmentsToday) if (i.isNew) keys.add(`appointment:${i.visitId}`)
  for (const i of summary.momPending) if (i.isNew) keys.add(`mom:${i.visitId}`)
  for (const i of summary.approvals) if (i.isNew) keys.add(`approval:${i.visitId}`)
  return keys
}

function Section({
  title,
  count,
  items,
  isFresh,
  viewAll,
  describe,
  onOpen,
  t,
}: {
  title: string
  count: number
  items: NotificationItemDto[]
  isFresh: (item: NotificationItemDto) => boolean
  viewAll: string
  describe: (item: NotificationItemDto) => string
  onOpen: (link: string) => void
  t: (key: string, fallback?: string) => string
}) {
  if (count === 0) return null
  return (
    <section className="px-4 py-3">
      <div className="mb-1.5 flex items-center justify-between gap-2">
        <h3 className="text-xs font-semibold uppercase tracking-wide text-ink-muted">
          {title} <span className="ml-1 rounded-full bg-aqua-light px-2 py-0.5 text-primary">{count}</span>
        </h3>
        {count > items.length ? (
          <button type="button" onClick={() => onOpen(viewAll)} className="text-xs font-medium text-primary hover:underline">
            {t('notifications.viewAll')}
          </button>
        ) : null}
      </div>
      <ul className="flex flex-col gap-1">
        {items.map((item) => (
          <li key={item.visitId}>
            <button
              type="button"
              onClick={() => onOpen(item.link)}
              className="flex w-full items-start justify-between gap-3 rounded-xl px-2 py-1.5 text-left hover:bg-aqua-light/60"
            >
              <span className="min-w-0">
                <span className="flex items-center gap-1.5 truncate text-sm font-medium text-ink">
                  {isFresh(item) ? (
                    <span className="h-2 w-2 shrink-0 rounded-full bg-danger" aria-label={t('notifications.new', 'New')} />
                  ) : null}
                  <span className="truncate">{item.title}</span>
                </span>
                {item.subtitle ? <span className="block truncate text-xs text-ink-muted">{item.subtitle}</span> : null}
              </span>
              <span className="shrink-0 text-xs text-ink-muted">{describe(item)}</span>
            </button>
          </li>
        ))}
      </ul>
    </section>
  )
}

function PushControls({
  state,
  busy,
  message,
  onEnable,
  onDisable,
  onTest,
  t,
}: {
  state: PushState
  busy: boolean
  message: { tone: 'ok' | 'error'; text: string } | null
  onEnable: () => void
  onDisable: () => void
  onTest: () => void
  t: (key: string, fallback?: string) => string
}) {
  if (state === 'unsupported') return null
  const linkButton = 'text-xs font-medium text-primary hover:underline disabled:opacity-50'
  return (
    <div className="border-t border-border/70 bg-mint/40 px-4 py-3 text-xs text-ink-muted">
      {state === 'needs-install' ? <p>{t('notifications.pushNeedsInstall')}</p> : null}
      {state === 'denied' ? <p>{t('notifications.pushBlocked')}</p> : null}
      {state === 'off' ? (
        <div className="flex items-center justify-between gap-3">
          <p>{t('notifications.pushOffHint')}</p>
          <button type="button" className={cn(linkButton, 'shrink-0')} disabled={busy} onClick={onEnable}>
            {t('notifications.pushEnable')}
          </button>
        </div>
      ) : null}
      {state === 'on' ? (
        <div className="flex flex-wrap items-center justify-between gap-2">
          <p>{t('notifications.pushOn')}</p>
          <span className="flex gap-3">
            <button type="button" className={linkButton} disabled={busy} onClick={onTest}>
              {t('notifications.pushTest')}
            </button>
            <button type="button" className={linkButton} disabled={busy} onClick={onDisable}>
              {t('notifications.pushDisable')}
            </button>
          </span>
        </div>
      ) : null}
      {message ? (
        <p className={cn('mt-1.5', message.tone === 'error' ? 'text-danger' : 'text-primary')} role="status">
          {message.text}
        </p>
      ) : null}
    </div>
  )
}
