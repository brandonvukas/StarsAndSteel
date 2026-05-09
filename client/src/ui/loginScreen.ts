// Login + register screen. Renders into the host element passed in. On
// successful login, calls the onAuthed callback so main.ts can swap to lobby.

import { login, register, HttpError } from '../api/rest';
import { setAuth } from '../store/store';

export function mountLoginScreen(host: HTMLElement, onAuthed: () => void) {
  host.innerHTML = `
    <div class="login-card">
      <h1>Stars &amp; Steel</h1>
      <form id="login-form" autocomplete="on">
        <label>Email <input name="email" type="email" required /></label>
        <label>Password <input name="password" type="password" required /></label>
        <button type="submit">Log in</button>
        <button type="button" id="register-btn">Register a new account</button>
        <p class="status"></p>
      </form>
      <form id="register-form" hidden>
        <label>Email <input name="email" type="email" required /></label>
        <label>Display name <input name="displayName" required minlength="2" maxlength="32" /></label>
        <label>Password <input name="password" type="password" required minlength="8" /></label>
        <button type="submit">Create account</button>
        <button type="button" id="back-btn">Back to login</button>
        <p class="status"></p>
      </form>
    </div>`;

  const loginForm = host.querySelector<HTMLFormElement>('#login-form')!;
  const registerForm = host.querySelector<HTMLFormElement>('#register-form')!;
  const showRegister = () => { loginForm.hidden = true; registerForm.hidden = false; };
  const showLogin = () => { registerForm.hidden = true; loginForm.hidden = false; };

  host.querySelector('#register-btn')!.addEventListener('click', showRegister);
  host.querySelector('#back-btn')!.addEventListener('click', showLogin);

  loginForm.addEventListener('submit', async ev => {
    ev.preventDefault();
    const fd = new FormData(loginForm);
    const status = loginForm.querySelector<HTMLElement>('.status')!;
    status.textContent = '';
    try {
      const auth = await login(fd.get('email') as string, fd.get('password') as string);
      setAuth(auth);
      onAuthed();
    } catch (e) {
      status.textContent = formatErr(e, 'Login failed');
    }
  });

  registerForm.addEventListener('submit', async ev => {
    ev.preventDefault();
    const fd = new FormData(registerForm);
    const status = registerForm.querySelector<HTMLElement>('.status')!;
    status.textContent = '';
    try {
      await register(
        fd.get('email') as string,
        fd.get('displayName') as string,
        fd.get('password') as string,
      );
      status.textContent = 'Registered. You can log in now.';
      showLogin();
    } catch (e) {
      status.textContent = formatErr(e, 'Registration failed');
    }
  });
}

function formatErr(e: unknown, fallback: string): string {
  if (e instanceof HttpError) {
    if (e.body && typeof e.body === 'object' && 'detail' in e.body) {
      return String((e.body as { detail: unknown }).detail);
    }
    return `${fallback} (HTTP ${e.status})`;
  }
  return fallback;
}
