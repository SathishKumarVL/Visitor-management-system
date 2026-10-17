import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiErrorMessage, authApi } from '../lib/api'
import type { MyDetailsDto } from '../types/api'
import { Alert, PageHeader, Panel, Spinner } from '../components/ui/Panel'
import { Button } from '../components/ui/Button'
import { roleLabel } from '../lib/roles'

function Detail({ label, value }: { label: string; value?: string | null }) {
  return (
    <div>
      <dt className="text-xs font-semibold uppercase tracking-[0.08em] text-ink-muted">{label}</dt>
      <dd className="mt-1 text-sm text-ink">{value?.trim() ? value : '—'}</dd>
    </div>
  )
}

/** The signed-in employee's own details; nothing about other people or the location. */
export function MyDetailsPage() {
  const [details, setDetails] = useState<MyDetailsDto | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    authApi.myDetails().then(setDetails).catch((e) => setError(apiErrorMessage(e)))
  }, [])

  return (
    <div className="mx-auto max-w-3xl">
      <PageHeader
        title="My details"
        subtitle="Your login and employee record. Ask your administrator to correct anything that is wrong."
        actions={
          <Link to="/change-password">
            <Button variant="secondary">Change password</Button>
          </Link>
        }
      />
      {error ? <div className="mb-4"><Alert tone="error">{error}</Alert></div> : null}
      {!details && !error ? <Spinner label="Loading your details…" /> : null}
      {details ? (
        <Panel>
          <dl className="grid gap-5 sm:grid-cols-2">
            <Detail label="Full name" value={details.fullName} />
            <Detail label="Username" value={details.username} />
            <Detail label="Email" value={details.email} />
            <Detail label="Role" value={details.roles.map(roleLabel).join(', ')} />
            <Detail label="Employee code" value={details.employeeCode} />
            <Detail label="Designation" value={details.designation} />
            <Detail label="Department" value={details.departmentName} />
            <Detail label="Location" value={details.locationName} />
            <Detail label="Mobile" value={details.phone} />
            <Detail label="Intercom" value={details.intercom} />
            <Detail
              label="Face sign-in"
              value={
                details.faceLoginCount > 0
                  ? `Registered (${details.faceLoginCount} ${details.faceLoginCount === 1 ? 'face' : 'faces'})`
                  : 'Not registered'
              }
            />
          </dl>
        </Panel>
      ) : null}
    </div>
  )
}
