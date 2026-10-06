import { useEffect, useMemo, useRef, useState } from 'react'
import type { EmployeeDto } from '../types/api'
import { FieldLabel, TextInput } from './ui/Field'
import { cn } from '../lib/utils'

type Props = {
  employees: EmployeeDto[]
  value: string
  onSelect: (employee: EmployeeDto | null) => void
  disabled?: boolean
  placeholder?: string
  id?: string
}

export function EmployeeSearchField({
  employees,
  value,
  onSelect,
  disabled,
  placeholder = 'Search employee name…',
  id = 'employeeSearch',
}: Props) {
  const selected = employees.find((e) => e.id === value) ?? null
  const [query, setQuery] = useState(selected?.fullName ?? '')
  const [open, setOpen] = useState(false)
  const wrapRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    setQuery(selected?.fullName ?? '')
  }, [selected?.fullName, value])

  useEffect(() => {
    function onDoc(e: MouseEvent) {
      if (!wrapRef.current?.contains(e.target as Node)) setOpen(false)
    }
    document.addEventListener('mousedown', onDoc)
    return () => document.removeEventListener('mousedown', onDoc)
  }, [])

  const matches = useMemo(() => {
    const q = query.trim().toLowerCase()
    if (!q) return employees.slice(0, 12)
    return employees
      .filter(
        (e) =>
          e.fullName.toLowerCase().includes(q) ||
          (e.designation || '').toLowerCase().includes(q) ||
          (e.code || '').toLowerCase().includes(q) ||
          (e.departmentName || '').toLowerCase().includes(q),
      )
      .slice(0, 12)
  }, [employees, query])

  return (
    <div ref={wrapRef} className="relative">
      <FieldLabel htmlFor={id}>Employee name *</FieldLabel>
      <TextInput
        id={id}
        autoComplete="off"
        disabled={disabled}
        placeholder={placeholder}
        value={query}
        onChange={(e) => {
          setQuery(e.target.value)
          setOpen(true)
          if (selected && e.target.value !== selected.fullName) onSelect(null)
        }}
        onFocus={() => setOpen(true)}
      />
      {open && !disabled ? (
        <ul
          className={cn(
            'absolute z-20 mt-1 max-h-56 w-full overflow-auto rounded-md border border-gray-200 bg-white py-1 shadow-lg',
          )}
          role="listbox"
        >
          {matches.length === 0 ? (
            <li className="px-3 py-2 text-sm text-ink-muted">No matching employees</li>
          ) : (
            matches.map((emp) => (
              <li key={emp.id}>
                <button
                  type="button"
                  className={cn(
                    'flex w-full flex-col items-start px-3 py-2 text-left text-sm hover:bg-aqua-light/50',
                    emp.id === value ? 'bg-aqua-light/40' : '',
                  )}
                  onClick={() => {
                    onSelect(emp)
                    setQuery(emp.fullName)
                    setOpen(false)
                  }}
                >
                  <span className="font-medium text-ink">{emp.fullName}</span>
                  <span className="text-xs text-ink-muted">
                    {[emp.designation, emp.departmentName].filter(Boolean).join(' · ') || '—'}
                  </span>
                </button>
              </li>
            ))
          )}
        </ul>
      ) : null}
    </div>
  )
}
