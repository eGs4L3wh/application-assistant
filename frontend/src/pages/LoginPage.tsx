import { api } from "../api";

export default function LoginPage() {
  return (
    <div className="login-shell">
      <div className="login-panel">
        <p className="brand">Application Assistant</p>
        <h1>Turn a job ad into a CV written for that role.</h1>
        <p className="lede">
          Sign in with Google, keep your experience on file, and draft a tailored CV for each application.
        </p>
        <a className="btn btn-primary" href={api.loginUrl()}>
          Continue with Google
        </a>
      </div>
      <div className="login-visual" aria-hidden="true" />
    </div>
  );
}
