# 10 — Auth & Security

Persistent multiplayer games attract cheaters. Cheating in our friend group will mostly be one of us trying to mess with the others "for science." We design assuming someone *will* poke at this.

## Authentication

### Stack
- ASP.NET Core Identity (built-in), backed by EF Core / SQL Server
- Cookie auth for the SPA page (HTTP-only, secure, SameSite=Lax)
- JWT bearer for SignalR (passed via `access_token` query param, the documented SignalR pattern)

### Registration / login flow
1. `POST /api/auth/register` — username + email + password. Hash via Identity's PBKDF2.
2. `POST /api/auth/login` — sets cookie + returns JWT (15 min expiry, refresh handled by re-login on 401).
3. JWT contains `sub = userId`, `name = displayName`. No game state in the token.

### Why two creds (cookie + JWT)?
SignalR over WebSockets can't easily attach `Authorization` headers. Microsoft's recommended pattern is the `access_token` query string. The cookie covers REST.

### Password requirements
Identity defaults: min 8 chars, 1 upper, 1 digit, 1 special. We can soften for the friend group but tighter is safer.

## Authorization

### Layered model
- **Authenticated** — must be logged in. Almost every endpoint.
- **Player in world** — must have a Player row in the target world. Enforced in a custom `[RequireWorldPlayer]` filter that reads `worldId` from route.
- **Owner of resource** — must own the unit/province/order being acted on. Checked in the controller.

These checks happen **server-side, every time**. Never trust the client's claim that "this is my unit."

## Server authority (the anti-cheat doctrine)

> The client may believe whatever it wants. The server is the only thing that decides what is true.

Concretely:

| Concern | Where it's enforced |
|---------|---------------------|
| "This unit is mine" | Server re-checks ownership on every order |
| "This province is adjacent" | Server validates against canonical adjacency list |
| "I have 500 gold" | Server reads from DB; client-shown value is a hint |
| "Combat result" | Server runs the resolver; client just animates the outcome it received |
| "Fog of war" | Snapshot endpoint *omits* hidden info; client literally doesn't have it to peek at |
| "Order timing" | Server stamps `IssuedAtTick`; client cannot back-date |

**Critical implication for the API design:** the snapshot endpoint must filter what it returns based on what the calling player can see. We do not send the full world to the client and let it hide things in the UI — we send the player a personalized, fog-aware payload. Anything we send is leaked.

## Rate limiting

ASP.NET Core 10's built-in `RateLimiter` middleware. Limits per-user:
- Auth endpoints: 5 per minute
- Order endpoints: 60 per minute (a player issuing more orders than 1/sec is bot-like; humans don't need that)
- Chat: 10 per minute
- Snapshot: 30 per minute (it's expensive)

Globally we cap connections per IP at 5 simultaneous SignalR connections (covers tabs without inviting abuse).

## Input validation

FluentValidation on every request DTO. Reject:
- IDs that don't parse as Guids
- Strings over their max length (chat 500 chars, name 50, etc.)
- Enum values outside the declared set
- Negative quantities anywhere

Order validation includes ownership and feasibility checks listed in `06-BACKEND-API.md`.

## SQL injection

EF Core parameterizes everything we'd plausibly write. We never concat user strings into raw SQL. If we ever need raw SQL (e.g., a complex leaderboard), we use `FromSqlInterpolated` so EF parameterizes it.

## Cross-site stuff

- **CSRF**: Cookie auth is vulnerable. We use the AntiForgery token middleware on state-changing endpoints, sent via header from the SPA.
- **CORS**: Same-origin only (the SPA is served from the same host). No CORS allowance for arbitrary origins.
- **CSP**: A modest content security policy: `default-src 'self'; script-src 'self'; img-src 'self' data:`. Tightens later.

## Secrets

- Connection string lives in `appsettings.Development.json` (LocalDB) for dev.
- For "real" (when we host this somewhere), use **User Secrets** in dev and **environment variables** in prod. Never commit a real connection string.
- JWT signing key generated once per environment, stored in env var `STARSANDSTEEL_JWT_KEY`.

## Logging & audit

Serilog logs every:
- Login attempt (success/fail) with user + IP
- Order issued (player, world, type, target)
- Combat resolution (world, tick, attacker, defender, casualties)
- Tick start/end (world, tick, duration, error if any)

Logs roll daily, retained 14 days. Useful for "wait, who attacked me at 3am?" forensics with our friends.

## Cheat scenarios we explicitly defend against

| Attack | Defense |
|--------|---------|
| Edit JS to "click" on hidden provinces | Server doesn't send hidden province data; nothing to click |
| Modify outgoing request to "move" enemy unit | Server checks unit ownership against `User.Id` → 403 |
| Replay or back-date an order | Server stamps tick; orders are processed only in the upcoming tick |
| Spam orders to lag the tick | Per-user rate limits + per-tick decision budget on processing |
| Open a second account to scout an ally | Hard to fully prevent, but: per-IP connection caps + email verification raise the bar. Honestly, for a friend group, not a problem. |
| SignalR group hopping to listen to others' events | Group membership is server-controlled in `JoinWorld`; client can't join arbitrary groups |
| SQL injection via display name | EF parameterization + name length cap + char-set validator |

## Things we accept as out of scope

- Bot detection (we have AI players, not anti-bot)
- DDoS mitigation (rely on host's protections; not in scope at our scale)
- Multi-account detection (covered by a "trust your friends" policy)
