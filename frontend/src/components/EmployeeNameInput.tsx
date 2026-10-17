import { useEffect, useMemo, useRef, useState } from 'react'
import type { EmployeeDto } from '../types/api'
import { TextInput } from './ui/Field'
import { cn } from '../lib/utils'

type Props = {
  employees: EmployeeDto[]
  value: string
  onChange: (name: string) => void
  placeholder?: string
  maxLength?: number
  'aria-label'?: string
}

/** Free-text name input that suggests matching employees as you type. */
export function EmployeeNameInput({
  employees,
  value,
  onChange,
  placeholder = 'Search employee…',
  maxLength,
  'aria-label': ariaLabel,
}: Props) {
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState(0)
  const wrapRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    function onDoc(e: MouseEvent) {
      if (!wrapRef.current?.contains(e.target as Node)) setOpen(false)
    }
    document.addEventListener('mousedown', onDoc)
    return () => document.removeEventListener('mousedown', onDoc)
  }, [])

  const matches = useMemo(() => {
    const q = value.trim().toLowerCase()
    const list = q
      ? employees.filter(
          (e) =>
            e.fullName.toLowerCase().includes(q) ||
            (e.code || '').toLowerCase().includes(q) ||
            (e.departmentName || '').toLowerCase().includes(q),
        )
      : employees
    return list.slice(0, 10)
  }, [employees, value])

  function pick(emp: EmployeeDto) {
    onChange(emp.fullName)
    setOpen(false)
  }

  const showList = open && matches.length > 0

  return (
    <div ref={wrapRef} className="relative">
      <TextInput
        autoComplete="off"
        role="combobox"
        aria-label={ariaLabel}
        aria-expanded={showList}
        aria-autocomplete="list"
        placeholder={placeholder}
        maxLength={maxLength}
        value={value}
        onChange={(e) => {
          onChange(e.target.value)
          setActive(0)
          setOpen(true)
        }}
        onFocus={() => setOpen(true)}
        onKeyDown={(e) => {
          if (!showList) {
            if (e.key === 'ArrowDown') setOpen(true)
            return
          }
          if (e.key === 'ArrowDown') {
            e.preventDefault()
            setActive((i) => Math.min(i + 1, matches.length - 1))
          } else if (e.key === 'ArrowUp') {
            e.preventDefault()
            setActive((i) => Math.max(i - 1, 0))
          } else if (e.key === 'Enter') {
            e.preventDefault()
            pick(matches[Math.min(active, matches.length - 1)])
          } else if (e.key === 'Escape' || e.key === 'Tab') {
            setOpen(false)
          }
        }}
      />
      {showList ? (
        <ul
          role="listbox"
          className="absolute z-30 mt-1 max-h-56 w-full min-w-[220px] overflow-auto rounded-md border border-gray-200 bg-white py-1 shadow-lg"
        >
          {matches.map((emp, i) => (
            <li key={emp.id} role="option" aria-selected={i === active}>
              <button
                type="button"
                className={cn(
                  'flex w-full flex-col items-start px-3 py-2 text-left text-sm hover:bg-aqua-light/50',
                  i === active ? 'bg-aqua-light/40' : '',
                )}
                onMouseDown={(e) => e.preventDefault()}
                onMouseEnter={() => setActive(i)}
                onClick={() => pick(emp)}
              >
                <span className="font-medium text-ink">{emp.fullName}</span>
                <span className="text-xs text-ink-muted">
                  {[emp.designation, emp.departmentName].filter(Boolean).join(' · ') || '—'}
                </span>
              </button>
            </li>
          ))}
        </ul>
      ) : null}
    </div>
  )
}
