import { cn } from '../../lib/utils'
import { forwardRef, useState, type InputHTMLAttributes, type SelectHTMLAttributes, type TextareaHTMLAttributes } from 'react'

const fieldBase =
  'min-h-12 w-full max-w-full rounded-xl border border-border bg-white px-3.5 text-base text-ink shadow-sm placeholder:text-gray-300 transition duration-200 focus:border-primary focus:ring-2 focus:ring-primary/20 sm:min-h-12'

export function FieldLabel({ children, htmlFor }: { children: React.ReactNode; htmlFor?: string }) {
  return (
    <label htmlFor={htmlFor} className="mb-1.5 block text-sm font-medium text-ink">
      {children}
    </label>
  )
}

/** Ref-forwarding so callers can move focus to a field, e.g. after a validation error. */
export const TextInput = forwardRef<HTMLInputElement, InputHTMLAttributes<HTMLInputElement>>(
  function TextInput({ className, ...props }, ref) {
    return <input ref={ref} className={cn(fieldBase, className)} {...props} />
  },
)

export function PasswordInput({ className, ...props }: Omit<InputHTMLAttributes<HTMLInputElement>, 'type'>) {
  const [visible, setVisible] = useState(false)
  return (
    <div className="relative max-w-full">
      <input
        {...props}
        type={visible ? 'text' : 'password'}
        className={cn(fieldBase, 'py-2 pl-3.5 pr-12', className)}
      />
      <button
        type="button"
        onClick={() => setVisible((v) => !v)}
        className="absolute inset-y-0 right-0 flex min-w-11 items-center justify-center px-3 text-ink-muted hover:text-primary"
        aria-label={visible ? 'Hide password' : 'Show password'}
        title={visible ? 'Hide password' : 'Show password'}
      >
        {visible ? (
          <svg viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden>
            <path d="M3 3l18 18" strokeLinecap="round" />
            <path d="M10.6 10.6a2 2 0 002.8 2.8" strokeLinecap="round" />
            <path d="M9.9 5.1A10.5 10.5 0 0112 5c5 0 9.3 3.1 10.7 7.5a11.3 11.3 0 01-4.1 5.1" strokeLinecap="round" />
            <path d="M6.1 6.1A11.4 11.4 0 001.3 12.5C2.7 16.9 7 20 12 20c1.7 0 3.3-.4 4.7-1" strokeLinecap="round" />
          </svg>
        ) : (
          <svg viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden>
            <path d="M1.5 12.5C2.9 8.1 7.2 5 12.2 5s9.3 3.1 10.7 7.5c-1.4 4.4-5.7 7.5-10.7 7.5S2.9 16.9 1.5 12.5z" />
            <circle cx="12" cy="12.5" r="3" />
          </svg>
        )}
      </button>
    </div>
  )
}

export function TextSelect({ className, children, ...props }: SelectHTMLAttributes<HTMLSelectElement>) {
  return (
    <div className={cn('relative max-w-full', className)}>
      <select
        className={cn(fieldBase, 'field-select appearance-none py-2.5 pl-3.5 pr-11 leading-normal disabled:cursor-not-allowed disabled:bg-gray-50 disabled:text-gray-500')}
        {...props}
      >
        {children}
      </select>
      <span
        className="pointer-events-none absolute inset-y-0 right-0 flex w-11 items-center justify-center text-ink-muted"
        aria-hidden
      >
        <svg viewBox="0 0 20 20" className="h-4 w-4" fill="none" stroke="currentColor" strokeWidth="2">
          <path d="M5 7.5L10 12.5L15 7.5" strokeLinecap="round" strokeLinejoin="round" />
        </svg>
      </span>
    </div>
  )
}

export function TextArea({ className, ...props }: TextareaHTMLAttributes<HTMLTextAreaElement>) {
  return <textarea className={cn(fieldBase, 'min-h-24 py-3', className)} {...props} />
}

export function FieldError({ message }: { message?: string | null }) {
  if (!message) return null
  return (
    <p className="mt-1 text-sm text-danger" role="alert">
      {message}
    </p>
  )
}

export function SelectChip({
  selected,
  onToggle,
  children,
}: {
  selected: boolean
  onToggle: () => void
  children: React.ReactNode
}) {
  return (
    <button
      type="button"
      onClick={onToggle}
      aria-pressed={selected}
      className={cn(
        'flex min-h-12 items-center justify-between gap-3 rounded-xl border px-4 py-3 text-left text-sm font-medium transition duration-200',
        selected
          ? 'border-transparent bg-brand-gradient text-white shadow-md'
          : 'border-border bg-white text-ink hover:border-primary/40 hover:bg-aqua-light/50',
      )}
    >
      <span>{children}</span>
      {selected ? (
        <span className="inline-flex h-6 w-6 items-center justify-center rounded-full bg-white/20 text-xs" aria-hidden>
          ✓
        </span>
      ) : null}
    </button>
  )
}
