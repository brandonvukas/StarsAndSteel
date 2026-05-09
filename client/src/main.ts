// App entry: routes between login → lobby → game based on auth/world state.
//
// We keep the routing trivial (no history API, no router lib) for Phase 1K.
// As the app grows we can move to a hash-based router or wouter/etc.

import './app.css';
import { $auth } from './store/store';
import { mountLoginScreen } from './ui/loginScreen';
import { mountLobbyScreen } from './ui/lobbyScreen';
import { mountGameScreen } from './ui/gameScreen';

const app = document.getElementById('app')!;

function showLogin() {
  mountLoginScreen(app, () => showLobby());
}

function showLobby() {
  mountLobbyScreen(app, worldId => showGame(worldId));
}

function showGame(worldId: string) {
  mountGameScreen(app, worldId).catch(err => {
    app.innerHTML = `
      <div class="error-screen">
        <h2>Failed to start game</h2>
        <pre>${String(err)}</pre>
        <button id="back">Back to lobby</button>
      </div>`;
    app.querySelector('#back')!.addEventListener('click', showLobby);
  });
}

if ($auth.get()) {
  showLobby();
} else {
  showLogin();
}
