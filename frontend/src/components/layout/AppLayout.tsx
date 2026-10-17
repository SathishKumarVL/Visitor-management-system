import { useEffect, useMemo, useState } from 'react'
import { NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { BrandLogo } from '../BrandLogo'
import { OfflineBanner } from './OfflineBanner'
import { Button } from '../ui/Button'
import { useAuthStore } from '../../store/authStore'
import { roleLabel, visibleMenu, type MenuItem } from '../../lib/roles'
import { cn, primaryRole } from '../../lib/utils'
import { LanguageSwitcher, useI18n } from '../../i18n'

const TABLET_MQ = '(max-width: 1023px)'
const MASTERS_OPEN_KEY = 'nav.mastersOpen'

function isMenuItemActive(pathname: string, itemPath: string, allPaths: string[]): boolean {
  const matches = allPaths.filter((p) => pathname === p || pathname.startsWith(`${p}/`))
  if (matches.length === 0) return false
  const best = matches.reduce((a, b) => (a.length >= b.length ? a : b))
  return best === itemPath
}

const MENU_ICONS: Record<string, string> = {
  dashboard: '▦',
  reception: '⌂',
  host: '◎',
  visitors: '◎',
  inside: '◉',
  expected: '◷',
  reports: '▤',
  departments: '▣',
  employees: '☰',
  purposes: '◇',
  locations: '⌖',
  feedback: '☆',
  mailcontent: '✉',
  users: '☺',
  meetinghalls: '▢',
  settings: '⚙',
  mydetails: '☺',
  employeemaster: '☻',
}

export function AppLayout() {
  const { t } = useI18n()
  const [isTablet, setIsTablet] = useState(() =>
    typeof window !== 'undefined' ? window.matchMedia(TABLET_MQ).matches : false,
  )
  const [open, setOpen] = useState(() =>
    typeof window !== 'undefined' ? !window.matchMedia(TABLET_MQ).matches : true,
  )
  const user = useAuthStore((s) => s.user)
  const logout = useAuthStore((s) => s.logout)
  const navigate = useNavigate()
  const location = useLocation()
  const items = visibleMenu(user?.roles ?? [], user?.allowedMenuKeys)
  const allPaths = items.map((i) => i.path)
  const role = primaryRole(user?.roles ?? [])
  const pageTitle = useMemo(() => {
    const match = items.find((i) => isMenuItemActive(location.pathname, i.path, allPaths))
    return match ? t(`nav.${match.key}`) : t('nav.visitorManagement')
  }, [items, location.pathname, allPaths, t])

  const greeting = useMemo(() => {
    const h = new Date().getHours()
    if (h < 12) return t('greeting.morning')
    if (h < 17) return t('greeting.afternoon')
    return t('greeting.evening')
  }, [t])

  useEffect(() => {
    const mq = window.matchMedia(TABLET_MQ)
    const sync = () => {
      const tablet = mq.matches
      setIsTablet(tablet)
      setOpen(!tablet)
    }
    sync()
    mq.addEventListener('change', sync)
    return () => mq.removeEventListener('change', sync)
  }, [])

  useEffect(() => {
    if (isTablet) setOpen(false)
  }, [location.pathname, isTablet])

  const reachedViaOther = (i: MenuItem) => !!i.reachedFrom && items.some((x) => x.key === i.reachedFrom)
  const navItems = items.filter((i) => !reachedViaOther(i))
  const masterItems = navItems.filter((i) => i.group === 'masters')
  const mastersActive = masterItems.some((i) => isMenuItemActive(location.pathname, i.path, allPaths))
  const [mastersOpen, setMastersOpen] = useState(() => localStorage.getItem(MASTERS_OPEN_KEY) !== '0')

  useEffect(() => {
    if (mastersActive) setMastersOpen(true)
  }, [mastersActive])

  function toggleMasters() {
    setMastersOpen((v) => {
      localStorage.setItem(MASTERS_OPEN_KEY, v ? '0' : '1')
      return !v
    })
  }

  function renderLink(item: MenuItem, nested: boolean) {
    const active =
      isMenuItemActive(location.pathname, item.path, allPaths) ||
      items.some(
        (i) => i.reachedFrom === item.key && reachedViaOther(i) && isMenuItemActive(location.pathname, i.path, allPaths),
      )
    return (
      <NavLink
        key={item.key}
        to={item.path}
        end={item.path === '/visitors'}
        aria-current={active ? 'page' : undefined}
        onClick={() => {
          if (isTablet) setOpen(false)
        }}
        className={cn(
          'flex items-center gap-3 rounded-xl px-3 text-sm font-medium transition duration-200',
          nested ? 'min-h-10 py-1.5' : 'min-h-12 py-2.5',
          active
            ? 'bg-brand-gradient text-white shadow-md'
            : 'text-ink-muted hover:bg-aqua-light/70 hover:text-ink',
        )}
      >
        <span
          className={cn(
            'inline-flex shrink-0 items-center justify-center rounded-xl leading-none',
            nested ? 'h-8 w-8 text-base' : 'h-10 w-10 text-lg',
            active ? 'bg-white/20 text-white' : 'bg-mint text-ink',
          )}
          aria-hidden
        >
          {MENU_ICONS[item.key] ?? '•'}
        </span>
        <span className="truncate">{t(`nav.${item.key}`)}</span>
      </NavLink>
    )
  }

  return (
    <div className="min-h-screen">
      <OfflineBanner />
      <div className="relative mx-auto flex min-h-screen max-w-[1680px]">
        {isTablet && open ? (
          <button
            type="button"
            className="fixed inset-0 z-30 bg-ink/35 backdrop-blur-[1px]"
            aria-label={t('nav.closeMenu')}
            onClick={() => setOpen(false)}
          />
        ) : null}

        <aside
          className={cn(
            'no-print z-40 flex flex-col border-r border-border/80 bg-white/95 shadow-sm transition-all duration-200',
            isTablet
              ? cn(
                  'fixed bottom-0 left-0 top-0 w-72 max-w-[85vw]',
                  open ? 'translate-x-0' : '-translate-x-full',
                )
              : cn('sticky top-0 h-screen shrink-0', open ? 'w-64' : 'w-0 overflow-hidden border-0'),
          )}
        >
          <div className="border-b border-border/70 px-4 py-5">
            <BrandLogo className="h-10" />
            <p className="mt-3 text-[11px] font-semibold uppercase tracking-[0.16em] text-primary">
              {t('nav.visitorManagement')}
            </p>
          </div>
          <nav className="flex flex-1 flex-col gap-1 overflow-y-auto p-3" aria-label="Main">
            {navItems.map((item) => {
              if (item.group !== 'masters') return renderLink(item, false)
              if (item !== masterItems[0]) return null
              return (
                <div key="group-masters" className="flex flex-col gap-1">
                  <button
                    type="button"
                    onClick={() => toggleMasters()}
                    aria-expanded={mastersOpen}
                    className={cn(
                      'flex min-h-12 items-center gap-3 rounded-xl px-3 py-2.5 text-left text-sm font-medium transition duration-200',
                      mastersActive && !mastersOpen
                        ? 'bg-aqua-light text-ink'
                        : 'text-ink-muted hover:bg-aqua-light/70 hover:text-ink',
                    )}
                  >
                    <span
                      className="inline-flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-mint text-lg leading-none text-ink"
                      aria-hidden
                    >
                      ▦
                    </span>
                    <span className="flex-1 truncate">{t('nav.masters')}</span>
                    <span className={cn('text-xs transition-transform', mastersOpen ? 'rotate-90' : '')} aria-hidden>
                      ▶
                    </span>
                  </button>
                  {mastersOpen ? (
                    <div className="ml-4 flex flex-col gap-1 border-l border-border/80 pl-2">
                      {masterItems.map((child) => renderLink(child, true))}
                    </div>
                  ) : null}
                </div>
              )
            })}
          </nav>
        </aside>

        <div className="flex min-w-0 flex-1 flex-col">
          <header className="no-print sticky top-0 z-20 border-b border-border/80 bg-white/90 backdrop-blur">
            <div className="flex min-h-14 items-center gap-3 px-3 sm:px-5">
              <button
                type="button"
                className="inline-flex min-h-11 min-w-11 items-center justify-center rounded-xl border border-border bg-white text-ink hover:bg-aqua-light"
                onClick={() => setOpen((v) => !v)}
                aria-label={open ? t('nav.closeMenu') : t('nav.openMenu')}
                aria-expanded={open}
              >
                <span className="text-lg" aria-hidden>
                  ☰
                </span>
              </button>
              <div className="min-w-0">
                <p className="truncate text-xs text-ink-muted">
                  {greeting} · {role === 'Reception' ? t('nav.receptionDesk') : role === 'Host' ? t('nav.host') : role ?? t('nav.workspace')}
                </p>
                <h1 className="truncate text-base font-semibold text-ink sm:text-lg">{pageTitle}</h1>
              </div>
              <div className="ml-auto flex items-center gap-2 sm:gap-3">
                <LanguageSwitcher className="hidden sm:flex" />
                <div className="hidden text-right sm:block">
                  <div className="text-sm font-semibold text-ink">{user?.fullName}</div>
                  <div className="text-xs text-ink-muted">{user?.roles.map(roleLabel).join(', ')}</div>
                </div>
                <div
                  className="flex h-10 w-10 items-center justify-center rounded-full bg-brand-gradient text-sm font-semibold text-white"
                  aria-hidden
                >
                  {(user?.fullName ?? 'U').slice(0, 1).toUpperCase()}
                </div>
                <Button
                  variant="secondary"
                  size="sm"
                  onClick={async () => {
                    await logout()
                    navigate('/login')
                  }}
                >
                  {t('nav.signOut')}
                </Button>
              </div>
            </div>
          </header>

          <main className="min-w-0 flex-1 p-4 sm:p-6 lg:p-8">
            <Outlet />
          </main>
        </div>
      </div>
    </div>
  )
}
