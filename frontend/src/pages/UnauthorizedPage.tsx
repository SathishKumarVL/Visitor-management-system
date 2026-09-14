import { Link } from 'react-router-dom'
import { Button } from '../components/ui/Button'
import { Panel } from '../components/ui/Panel'

export function UnauthorizedPage() {
  return (
    <Panel className="mx-auto max-w-lg text-center">
      <h1 className="text-2xl font-semibold text-steel">Unauthorized</h1>
      <p className="mt-2 text-gray-500">You do not have permission to view this page.</p>
      <div className="mt-6 flex justify-center gap-2">
        <Link to="/">
          <Button>Go home</Button>
        </Link>
      </div>
    </Panel>
  )
}
