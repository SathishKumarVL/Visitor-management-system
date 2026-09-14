import { cn } from '../../lib/utils'
import type { ButtonHTMLAttributes, PropsWithChildren } from 'react'

type Variant = 'primary' | 'secondary' | 'amber' | 'danger' | 'ghost'
type Size = 'md' | 'lg' | 'sm'

const variants: Record<Variant, string> = {
  primary:
    'bg-brand-gradient text-white shadow-md hover:brightness-105 disabled:opacity-50 disabled:shadow-none',
  secondary:
    'bg-white text-ink border border-border hover:bg-aqua-light/60 disabled:bg-gray-100',
  amber:
    'bg-brand-gradient text-white shadow-md hover:brightness-105 font-semibold disabled:opacity-50',
  danger: 'bg-danger text-white hover:brightness-95',
  ghost: 'bg-transparent text-primary hover:bg-aqua-light/70',
}

const sizes: Record<Size, string> = {
  sm: 'min-h-11 px-3.5 text-sm rounded-xl',
  md: 'min-h-12 px-5 text-sm rounded-xl',
  lg: 'min-h-14 px-6 text-base rounded-2xl',
}

interface Props extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant
  size?: Size
  loading?: boolean
}

export function Button({
  className,
  variant = 'primary',
  size = 'md',
  loading = false,
  children,
  type = 'button',
  disabled,
  ...rest
}: PropsWithChildren<Props>) {
  return (
    <button
      type={type}
      disabled={disabled || loading}
      className={cn(
        'inline-flex items-center justify-center gap-2 font-medium transition duration-200 disabled:cursor-not-allowed',
        variants[variant],
        sizes[size],
        className,
      )}
      {...rest}
    >
      {loading ? (
        <span
          className="h-4 w-4 animate-spin rounded-full border-2 border-white/40 border-t-white"
          aria-hidden
        />
      ) : null}
      {children}
    </button>
  )
}
