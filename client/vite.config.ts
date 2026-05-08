import { defineConfig } from 'vite';
import { fileURLToPath, URL } from 'node:url';

// Backend dev URL (matches src/StarsAndSteel.Api/Properties/launchSettings.json "http" profile).
const BACKEND_URL = 'http://localhost:5005';

export default defineConfig({
  resolve: {
    alias: {
      // Single source of truth for the world map. Imported as `@shared/map-data.json`.
      // The server consumes the same file via <Content Include> in StarsAndSteel.Data.csproj.
      '@shared': fileURLToPath(new URL('../shared', import.meta.url)),
    },
  },
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': {
        target: BACKEND_URL,
        changeOrigin: true,
      },
      '/hubs': {
        target: BACKEND_URL,
        changeOrigin: true,
        // SignalR uses WebSockets after the negotiate handshake.
        ws: true,
      },
    },
  },
});
