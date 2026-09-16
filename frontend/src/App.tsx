import { useEffect } from 'react'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import { AppLayout } from './components/layout/AppLayout'
import { ProtectedRoute } from './components/ProtectedRoute'
import { useAuthStore } from './store/authStore'
import { useSettingsStore } from './store/settingsStore'
import { LoadingMask } from './components/LoadingMask'
import { LoginPage } from './pages/LoginPage'
import { HomeRedirect } from './pages/HomeRedirect'
import { UnauthorizedPage } from './pages/UnauthorizedPage'
import { DashboardPage } from './pages/DashboardPage'
import { ReceptionModePage } from './pages/ReceptionModePage'
import { SecurityModePage } from './pages/SecurityModePage'
import { HostModePage } from './pages/HostModePage'
import { NewVisitorWizardPage } from './pages/NewVisitorWizardPage'
import { VisitorsPage } from './pages/VisitorsPage'
import { VisitorDetailPage } from './pages/VisitorDetailPage'
import { CurrentlyInsidePage } from './pages/CurrentlyInsidePage'
import { ExpectedVisitorsPage } from './pages/ExpectedVisitorsPage'
import { VisitorPassesPage } from './pages/VisitorPassesPage'
import { PassPrintPage } from './pages/PassPrintPage'
import { VerifyVisitorPage } from './pages/VerifyVisitorPage'
import { EmergencyModePage } from './pages/EmergencyModePage'
import { FaceCheckoutPage } from './pages/FaceCheckoutPage'
import { ReportsPage } from './pages/ReportsPage'
import { DepartmentsPage, LocationsPage, PurposesPage } from './pages/MastersPages'
import { SitesPage } from './pages/SitesPage'
import { HostsPage } from './pages/HostsPage'
import { UsersPage } from './pages/UsersPage'
import { SettingsPage } from './pages/SettingsPage'
import { AuditLogsPage } from './pages/AuditLogsPage'
import { ChangePasswordPage } from './pages/ChangePasswordPage'

export default function App() {
  const bootstrap = useAuthStore((s) => s.bootstrap)
  const initialized = useAuthStore((s) => s.initialized)
  const loadSettings = useSettingsStore((s) => s.load)

  useEffect(() => {
    void bootstrap()
    void loadSettings()
  }, [bootstrap, loadSettings])

  if (!initialized) {
    return <LoadingMask label="Starting TIAANO VMS…" />
  }

  return (
    <BrowserRouter>
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route element={<ProtectedRoute />}>
          <Route element={<AppLayout />}>
            <Route path="/" element={<HomeRedirect />} />
            <Route path="/change-password" element={<ChangePasswordPage />} />
            <Route path="/unauthorized" element={<UnauthorizedPage />} />

            <Route element={<ProtectedRoute roles={['SuperAdmin', 'Admin', 'Reception', 'Security', 'Host']} />}>
              <Route path="/dashboard" element={<DashboardPage />} />
              <Route path="/visitors" element={<VisitorsPage />} />
              <Route path="/visitors/inside" element={<CurrentlyInsidePage />} />
              <Route path="/visitors/:id" element={<VisitorDetailPage />} />
            </Route>

            <Route element={<ProtectedRoute roles={['SuperAdmin', 'Admin', 'Reception']} />}>
              <Route path="/reception" element={<ReceptionModePage />} />
              <Route path="/visitors/new" element={<NewVisitorWizardPage />} />
              <Route path="/reports" element={<ReportsPage />} />
            </Route>

            <Route element={<ProtectedRoute roles={['SuperAdmin', 'Admin', 'Security']} />}>
              <Route path="/security" element={<SecurityModePage />} />
              <Route path="/emergency" element={<EmergencyModePage />} />
            </Route>

            <Route element={<ProtectedRoute roles={['SuperAdmin', 'Admin', 'Host']} />}>
              <Route path="/host" element={<HostModePage />} />
            </Route>

            <Route element={<ProtectedRoute roles={['SuperAdmin', 'Admin', 'Reception', 'Host', 'Security']} />}>
              <Route path="/visitors/expected" element={<ExpectedVisitorsPage />} />
            </Route>

            <Route element={<ProtectedRoute roles={['SuperAdmin', 'Admin', 'Reception', 'Security']} />}>
              <Route path="/passes" element={<VisitorPassesPage />} />
              <Route path="/passes/:visitId/print" element={<PassPrintPage />} />
              <Route path="/verify" element={<VerifyVisitorPage />} />
              <Route path="/scan" element={<VerifyVisitorPage />} />
              <Route path="/checkout/face" element={<FaceCheckoutPage />} />
            </Route>

            <Route element={<ProtectedRoute roles={['SuperAdmin', 'Admin']} />}>
              <Route path="/masters/departments" element={<DepartmentsPage />} />
              <Route path="/masters/hosts" element={<HostsPage />} />
              <Route path="/masters/purposes" element={<PurposesPage />} />
              <Route path="/masters/locations" element={<LocationsPage />} />
              <Route path="/masters/sites" element={<SitesPage />} />
              <Route path="/users" element={<UsersPage />} />
              <Route path="/settings" element={<SettingsPage />} />
              <Route path="/audit" element={<AuditLogsPage />} />
            </Route>
          </Route>
        </Route>

        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </BrowserRouter>
  )
}
