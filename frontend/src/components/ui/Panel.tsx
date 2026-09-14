import { Link } from 'react-router-dom'
import { cn } from '../../lib/utils'
import type { PropsWithChildren, ReactNode } from 'react'
import { LoadingMask, TiaanoLoaderMark } from '../LoadingMask'

export function PageHeader({
  title,
  subtitle,
  actions,
  eyebrow,
}: {
  title: string
  subtitle?: string
  actions?: ReactNode
  eyebrow?: string
}) {
  return (
    <div className="mb-6 flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
      <div>
        {eyebrow ? (
          <p className="mb-1 text-xs font-semibold uppercase tracking-[0.14em] text-primary">{eyebrow}</p>
        ) : null}
        <h1 className="text-2xl font-semibold tracking-tight text-ink sm:text-3xl">{title}</h1>
        {subtitle ? <p className="mt-1.5 max-w-2xl text-sm leading-relaxed text-ink-muted">{subtitle}</p> : null}
      </div>
      {actions ? <div className="flex flex-wrap gap-2">{actions}</div> : null}
    </div>
  )
}

export function Panel({ children, className }: PropsWithChildren<{ className?: string }>) {
  return (
    <div
      className={cn(
        'rounded-2xl border border-border/80 bg-white p-4 shadow-card sm:p-5',
        className,
      )}
    >
      {children}
    </div>
  )
}

export function Badge({ children, className }: PropsWithChildren<{ className?: string }>) {
  return (
    <span
      className={cn(
        'inline-flex items-center rounded-full px-2.5 py-1 text-xs font-semibold tracking-wide',
        className,
      )}
    >
      {children}
    </span>
  )
}

export function Spinner({ label = 'Loading…', fullScreen = false }: { label?: string; fullScreen?: boolean }) {
  if (fullScreen) {
    return <LoadingMask label={label} />
  }

  return (
    <div className="flex flex-col items-center justify-center gap-3 py-16 text-ink-muted" role="status" aria-live="polite">
      <TiaanoLoaderMark size="md" />
      <span className="text-sm font-medium tracking-wide">{label}</span>
    </div>
  )
}

export function Skeleton({ className }: { className?: string }) {
  return <div className={cn('skeleton', className)} aria-hidden />
}

export function Alert({
  tone = 'info',
  children,
}: PropsWithChildren<{ tone?: 'info' | 'error' | 'success' | 'warning' }>) {
  const styles = {
    info: 'border-primary/20 bg-aqua-light/70 text-primary-deep',
    error: 'border-danger/30 bg-red-50 text-danger',
    success: 'border-success/30 bg-emerald-50 text-success',
    warning: 'border-warning/35 bg-amber-50 text-warning',
  }
  return (
    <div className={cn('rounded-xl border px-4 py-3 text-sm', styles[tone])} role="status">
      {children}
    </div>
  )
}

export function EmptyState({
  title,
  description,
  icon,
  action,
}: {
  title: string
  description?: string
  icon?: ReactNode
  action?: ReactNode
}) {
  return (
    <div className="rounded-2xl border border-dashed border-border bg-white/80 px-6 py-14 text-center shadow-sm">
      {icon ? <div className="mx-auto mb-3 flex h-12 w-12 items-center justify-center rounded-2xl bg-aqua-light text-primary">{icon}</div> : null}
      <p className="text-base font-semibold text-ink">{title}</p>
      {description ? <p className="mx-auto mt-1 max-w-md text-sm text-ink-muted">{description}</p> : null}
      {action ? <div className="mt-4 flex justify-center">{action}</div> : null}
    </div>
  )
}

export function ModeTile({
  title,
  description,
  onClick,
  accent = false,
  className,
  icon,
}: {
  title: string
  description?: string
  onClick: () => void
  accent?: boolean
  className?: string
  icon?: ReactNode
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'group flex min-h-28 w-full flex-col items-start justify-center rounded-2xl border px-5 py-5 text-left shadow-card transition duration-200',
        accent
          ? 'border-transparent bg-brand-gradient text-white hover:brightness-105'
          : 'border-border bg-white text-ink hover:-translate-y-0.5 hover:border-primary/30 hover:shadow-elevated',
        className,
      )}
    >
      {icon ? (
        <span
          className={cn(
            'mb-3 inline-flex h-11 w-11 items-center justify-center rounded-xl text-lg',
            accent ? 'bg-white/15' : 'bg-aqua-light text-primary',
          )}
        >
          {icon}
        </span>
      ) : null}
      <span className="text-lg font-bold tracking-wide">{title}</span>
      {description ? (
        <span className={cn('mt-1 text-sm leading-relaxed', accent ? 'text-white/85' : 'text-ink-muted')}>
          {description}
        </span>
      ) : null}
    </button>
  )
}

export function KpiCard({
  label,
  value,
  to,
  hint,
  icon,
}: {
  label: string
  value: number | string
  to?: string
  hint?: string
  icon?: ReactNode
}) {
  const content = (
    <>
      <div className="flex items-start justify-between gap-3">
        <div className="inline-flex h-11 w-11 items-center justify-center rounded-xl bg-aqua-light text-primary">
          {icon ?? <span className="text-lg font-semibold">•</span>}
        </div>
        {hint ? <span className="text-xs font-medium text-primary">{hint}</span> : null}
      </div>
      <div className="mt-4 text-3xl font-semibold tracking-tight text-ink">{value}</div>
      <div className="mt-1 text-sm text-ink-muted">{label}</div>
    </>
  )

  const className =
    'block rounded-2xl border border-border/80 bg-white p-5 shadow-card transition duration-200 hover:-translate-y-0.5 hover:border-primary/25 hover:shadow-elevated focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary'

  if (to) {
    return (
      <Link to={to} className={className}>
        {content}
      </Link>
    )
  }
  return <div className={className}>{content}</div>
}
