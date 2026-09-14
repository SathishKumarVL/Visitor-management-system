import { cn } from '../lib/utils'

const LOADER_SRC = '/branding/tiaano-loader.svg'

export function TiaanoLoaderMark({
  className,
  size = 'md',
}: {
  className?: string
  size?: 'sm' | 'md' | 'lg'
}) {
  const sizeClass =
    size === 'sm' ? 'h-16 w-16' : size === 'lg' ? 'h-40 w-40' : 'h-28 w-28'

  return (
    <img
      src={LOADER_SRC}
      alt=""
      aria-hidden
      className={cn('select-none object-contain', sizeClass, className)}
      draggable={false}
    />
  )
}

/** Full-viewport loading mask for app bootstrap / blocking waits. */
export function LoadingMask({
  label = 'Loading…',
  className,
}: {
  label?: string
  className?: string
}) {
  return (
    <div
      className={cn(
        'fixed inset-0 z-[100] flex flex-col items-center justify-center gap-4 bg-page/95 backdrop-blur-sm',
        className,
      )}
      role="status"
      aria-live="polite"
      aria-busy="true"
      aria-label={label}
    >
      <TiaanoLoaderMark size="lg" />
      <p className="text-sm font-medium tracking-wide text-ink-muted">{label}</p>
    </div>
  )
}
