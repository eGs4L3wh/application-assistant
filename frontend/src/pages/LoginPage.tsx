import { api } from "../api";

export default function LoginPage() {
  return (
    <div className="login-shell">
      <div className="login-panel">
        <p className="brand">Application Assistant</p>
        <h1>Track applications with your CV at hand.</h1>
        <p className="lede">
          Sign in with Google to upload your CV and start a new application from one dashboard.
        </p>
        <a className="btn btn-primary" href={api.loginUrl()}>
          Continue with Google
        </a>
      </div>
      <div className="login-visual" aria-hidden="true" />
    </div>
  );
}
