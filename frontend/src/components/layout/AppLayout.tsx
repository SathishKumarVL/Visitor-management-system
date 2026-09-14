import { useEffect, useMemo, useState } from 'react'
import { NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { BrandLogo } from '../BrandLogo'
import { OfflineBanner } from './OfflineBanner'
import { Button } from '../ui/Button'
import { useAuthStore } from '../../store/authStore'
import { visibleMenu } from '../../lib/roles'
import { cn, primaryRole } from '../../lib/utils'

const TABLET_MQ = '(max-width: 1023px)'

function isMenuItemActive(pathname: string, itemPath: string, allPaths: string[]): boolean {
  const matches = allPaths.filter((p) => pathname === p || pathname.startsWith(`${p}/`))
  if (matches.length === 0) return false
  const best = matches.reduce((a, b) => (a.length >= b.length ? a : b))
  return best === itemPath
}

function greetingForNow() {
  const h = new Date().getHours()
  if (h < 12) return 'Good morning'
  if (h < 17) return 'Good afternoon'
  return 'Good evening'
}

const MENU_ICONS: Record<string, string> = {
  dashboard: '◫',
  reception: '▣',
  security: '⬡',
  host: '◎',
  visitors: '👤',
  inside: '●',
  expected: '◷',
  reports: '▤',
  departments: '▦',
  hosts: '☆',
  purposes: '◇',
  locations: '⌖',
  users: '☺',
  settings: '⚙',
  audit: '☰',
  passes: '▣',
  verify: '✓',
  emergency: '!',
}

export function AppLayout() {
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
  const items = visibleMenu(user?.roles ?? [])
  const allPaths = items.map((i) => i.path)
  const role = primaryRole(user?.roles ?? [])
  const pageTitle = useMemo(() => {
    const match = items.find((i) => isMenuItemActive(location.pathname, i.path, allPaths))
    return match?.label ?? 'Visitor Management'
  }, [items, location.pathname, allPaths])

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

  return (
    <div className="min-h-screen">
      <OfflineBanner />
      <div className="relative mx-auto flex min-h-screen max-w-[1680px]">
        {isTablet && open ? (
          <button
            type="button"
            className="fixed inset-0 z-30 bg-ink/35 backdrop-blur-[1px]"
            aria-label="Close menu"
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
              Visitor Management
            </p>
          </div>
          <nav className="flex flex-1 flex-col gap-1 overflow-y-auto p-3" aria-label="Main">
            {items.map((item) => {
              const active = isMenuItemActive(location.pathname, item.path, allPaths)
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
                    'flex min-h-11 items-center gap-3 rounded-xl px-3 py-2.5 text-sm font-medium transition duration-200',
                    active
                      ? 'bg-brand-gradient text-white shadow-md'
                      : 'text-ink-muted hover:bg-aqua-light/70 hover:text-ink',
                  )}
                >
                  <span
                    className={cn(
                      'inline-flex h-7 w-7 items-center justify-center rounded-lg text-xs',
                      active ? 'bg-white/15' : 'bg-mint text-primary',
                    )}
                    aria-hidden
                  >
                    {MENU_ICONS[item.key] ?? '•'}
                  </span>
                  <span className="truncate">{item.label}</span>
                </NavLink>
              )
            })}
          </nav>
          <div className="border-t border-border/70 p-3">
            <div className="rounded-xl bg-mint px-3 py-3">
              <p className="text-xs text-ink-muted">Signed in</p>
              <p className="truncate text-sm font-semibold text-ink">{user?.fullName}</p>
              <p className="truncate text-xs text-primary">{role}</p>
            </div>
          </div>
        </aside>

        <div className="flex min-w-0 flex-1 flex-col">
          <header className="no-print sticky top-0 z-20 border-b border-border/80 bg-white/90 backdrop-blur">
            <div className="flex min-h-14 items-center gap-3 px-3 sm:px-5">
              <button
                type="button"
                className="inline-flex min-h-11 min-w-11 items-center justify-center rounded-xl border border-border bg-white text-ink hover:bg-aqua-light"
                onClick={() => setOpen((v) => !v)}
                aria-label={open ? 'Close menu' : 'Open menu'}
                aria-expanded={open}
              >
                <span className="text-lg" aria-hidden>
                  ☰
                </span>
              </button>
              <div className="min-w-0">
                <p className="truncate text-xs text-ink-muted">
                  {greetingForNow()} · {role === 'Reception' ? 'Reception Desk' : role ?? 'Workspace'}
                </p>
                <h1 className="truncate text-base font-semibold text-ink sm:text-lg">{pageTitle}</h1>
              </div>
              <div className="ml-auto flex items-center gap-2 sm:gap-3">
                <div className="hidden rounded-full bg-aqua-light px-3 py-1 text-xs font-medium text-primary sm:inline-flex">
                  Live
                </div>
                <div className="hidden text-right sm:block">
                  <div className="text-sm font-semibold text-ink">{user?.fullName}</div>
                  <div className="text-xs text-ink-muted">{user?.roles.join(', ')}</div>
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
                  Sign out
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
