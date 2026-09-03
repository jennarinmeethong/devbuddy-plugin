import { Navigate, Route, Routes, useParams } from "react-router-dom";
import { useSession } from "./api/session";
import { Layout } from "./components/Layout";
import { SignIn } from "./routes/SignIn";
import { SetPassword } from "./routes/SetPassword";
import { Workspaces } from "./routes/Workspaces";
import { Members } from "./routes/Members";
import { Projects } from "./routes/Projects";
import { WorkItems } from "./routes/WorkItems";
import { Records } from "./routes/Records";
import { RecordDetail } from "./routes/RecordDetail";
import { Audit } from "./routes/Audit";
import { Health } from "./routes/Health";
import { PluginAccess } from "./routes/PluginAccess";

/**
 * Routes.
 *
 * Everything below `/w/:workspaceId` is inside one workspace, because every operation is scoped to
 * one and putting it in the path means no screen has to remember which. A person with a grant on
 * exactly one workspace never sees the picker.
 *
 * There is no chat here, deliberately. info.md keeps AI questions in Claude and Codex; this is the
 * surface for the things a person has to do, which are the things AI is not allowed to.
 */
export function App() {
  const { user, loading } = useSession();

  if (loading) {
    return (
      <div className="flex h-full items-center justify-center text-sm text-[var(--color-muted)]">
        Loading…
      </div>
    );
  }

  if (!user) {
    return (
      <Routes>
        <Route path="/set-password" element={<SetPassword />} />
        <Route path="*" element={<SignIn />} />
      </Routes>
    );
  }

  return (
    <Routes>
      <Route path="/" element={<Workspaces />} />
      <Route path="/set-password" element={<SetPassword />} />
      <Route path="/w/:workspaceId" element={<Layout />}>
        <Route index element={<Projects />} />
        <Route path="members" element={<Members />} />
        <Route path="audit" element={<Audit />} />
        <Route path="health" element={<Health />} />
        <Route path="plugin-access" element={<PluginAccess />} />
        <Route path="p/:projectId" element={<WorkItems />} />
        <Route path="p/:projectId/records" element={<Records />} />
        <Route path="p/:projectId/records/:recordId" element={<RecordDetail />} />
      </Route>
      <Route path="*" element={<Missing />} />
    </Routes>
  );
}

function Missing() {
  const params = useParams();

  return <Navigate to={params.workspaceId ? `/w/${params.workspaceId}` : "/"} replace />;
}
