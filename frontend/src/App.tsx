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
import { HostModePage } from './pages/HostModePage'
import { NewVisitorWizardPage } from './pages/NewVisitorWizardPage'
import { VisitorsPage } from './pages/VisitorsPage'
import { VisitorDetailPage } from './pages/VisitorDetailPage'
import { MeetingMinutesPage } from './pages/MeetingMinutesPage'
import { CurrentlyInsidePage } from './pages/CurrentlyInsidePage'
import { ExpectedVisitorsPage } from './pages/ExpectedVisitorsPage'
import { VisitorPassesPage } from './pages/VisitorPassesPage'
import { PassPrintPage } from './pages/PassPrintPage'
import { VerifyVisitorPage } from './pages/VerifyVisitorPage'
import { FaceCheckoutPage } from './pages/FaceCheckoutPage'
import { ReportsPage } from './pages/ReportsPage'
import { DepartmentsPage, DesignationsPage, LocationsPage, PurposesPage } from './pages/MastersPages'
import { FeedbackQuestionsPage } from './pages/FeedbackQuestionsPage'
import { MeetingHallsPage } from './pages/MeetingHallsPage'
import { EmployeeMasterPage } from './pages/EmployeeMasterPage'
import { ManualCheckoutsPage } from './pages/ManualCheckoutsPage'
import { MomPendingPage } from './pages/MomPendingPage'
import { SettingsPage } from './pages/SettingsPage'
import { ChangePasswordPage } from './pages/ChangePasswordPage'
import { MyDetailsPage } from './pages/MyDetailsPage'
import { ForgotPasswordPage, ResetPasswordPage } from './pages/PasswordResetPages'
import { InvitationPage } from './pages/InvitationPage'
import { VisitorWelcomePage } from './pages/VisitorWelcomePage'
import { VisitorThankYouPage } from './pages/VisitorThankYouPage'

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
        <Route path="/forgot-password" element={<ForgotPasswordPage />} />
        <Route path="/reset-password" element={<ResetPasswordPage />} />
        <Route path="/invite" element={<InvitationPage />} />
        <Route element={<ProtectedRoute />}>
          <Route element={<AppLayout />}>
            <Route path="/" element={<HomeRedirect />} />
            <Route path="/change-password" element={<ChangePasswordPage />} />
            <Route path="/my-details" element={<MyDetailsPage />} />
            <Route path="/unauthorized" element={<UnauthorizedPage />} />

            <Route element={<ProtectedRoute roles={['SuperAdmin', 'Admin', 'Reception', 'Security', 'Host']} />}>
              <Route path="/dashboard" element={<DashboardPage />} />
              <Route path="/visitors" element={<VisitorsPage />} />
              <Route path="/visitors/inside" element={<CurrentlyInsidePage />} />
              <Route path="/visitors/:id" element={<VisitorDetailPage />} />
            </Route>

            <Route element={<ProtectedRoute roles={['SuperAdmin', 'Admin']} />}>
              <Route path="/employee-master" element={<EmployeeMasterPage />} />
              <Route path="/visitors/manual-checkouts" element={<ManualCheckoutsPage />} />
            </Route>

            <Route element={<ProtectedRoute roles={['SuperAdmin', 'Admin', 'Host']} />}>
              <Route path="/visitors/mom-pending" element={<MomPendingPage />} />
            </Route>

            <Route element={<ProtectedRoute roles={['SuperAdmin', 'Admin', 'Reception']} />}>
              <Route path="/reception" element={<ReceptionModePage />} />
              <Route path="/visitors/new" element={<NewVisitorWizardPage />} />
              <Route path="/visitors/welcome" element={<VisitorWelcomePage />} />
              <Route path="/reports" element={<ReportsPage />} />
            </Route>

            <Route element={<ProtectedRoute roles={['SuperAdmin', 'Admin', 'Security']} />}>
              <Route path="/security" element={<Navigate to="/visitors/inside" replace />} />
              <Route path="/emergency" element={<Navigate to="/visitors/inside" replace />} />
            </Route>

            <Route element={<ProtectedRoute roles={['SuperAdmin', 'Admin', 'Host']} />}>
              <Route path="/host" element={<HostModePage />} />
              <Route path="/visitors/:id/minutes" element={<MeetingMinutesPage />} />
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
              <Route path="/visitors/thank-you" element={<VisitorThankYouPage />} />
            </Route>

            <Route element={<ProtectedRoute roles={['SuperAdmin', 'Admin']} />}>
              <Route path="/masters/employees" element={<Navigate to="/employee-master" replace />} />
              <Route path="/masters/hosts" element={<Navigate to="/employee-master" replace />} />
              <Route path="/masters/departments" element={<DepartmentsPage />} />
              <Route path="/masters/designations" element={<DesignationsPage />} />
              <Route path="/masters/purposes" element={<PurposesPage />} />
              <Route path="/masters/locations" element={<LocationsPage />} />
              <Route path="/masters/feedback" element={<FeedbackQuestionsPage />} />
              <Route path="/masters/mail-content" element={<Navigate to="/settings?tab=mailContent" replace />} />
              <Route path="/masters/meeting-halls" element={<MeetingHallsPage />} />
              <Route path="/users" element={<Navigate to="/employee-master" replace />} />
              <Route path="/settings" element={<SettingsPage />} />
            </Route>
          </Route>
        </Route>

        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </BrowserRouter>
  )
}
