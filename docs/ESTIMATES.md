# Estimates

Honest, ranged estimates for building Stars & Steel. Hobby projects always slip — these are calibrated for "weekends + occasional weeknight," not "9–5 full focus." If you treat this like a job you can roughly halve the calendar.

A "weekend" below means ~12 focused hours (Sat morning + Sun afternoon). A "session" is ~3 hours after dinner.

---

## Calendar time per phase

| Phase | Scope | Optimistic | Realistic | Pessimistic |
|-------|-------|-----------:|----------:|------------:|
| 0 — Setup | Repo, projects, scaffolding | ½ weekend | 1 weekend | 1.5 weekends |
| 1 — MVP | Entities, migrations, tick, combat, map, AI Aggressor | 2 weekends | 4 weekends | 6–8 weekends |
| 2 — Diplomacy & depth | Treaties, chat, research, more AI personalities | 2 weekends | 3–4 weekends | 6 weekends |
| 3 — Combat & wonders | Naval, espionage, generals, wonders, doctrines | 2 weekends | 4 weekends | 6+ weekends |
| 4 — Polish | Stock market, replay viewer, weather, etc. | open-ended | open-ended | open-ended |

**Rule of thumb:** plan around the *realistic* column. The optimistic column assumes nothing surprises you. Nothing has ever assumed nothing surprises you, then been right.

### What "playable for the friend group" looks like

End of **Phase 1** you can play a game with your friends. End of **Phase 2** the game feels strategic and worth replaying. Anything past that is sweetener.

So the realistic answer to "when can we play this?" is **roughly 2 months of weekend work to a real first game, 4 months to a polished version.**

---

## Effort breakdown (where the time actually goes)

A common surprise: the visible thing (the map and UI) is rarely where the time goes. Order-of-effort estimate for Phase 1:

| Area | Share of effort | Notes |
|------|----------------:|-------|
| Map data + adjacency authoring | ~15% | Drawing 50 provinces and their neighbors is fiddly. We'll script it. |
| Tick processor + combat resolver | ~20% | The math is small but balance tuning is endless |
| AI personality | ~15% | Even one personality is real work; tuning it takes more |
| EF entities + migrations + seeding | ~10% | The straightforward part |
| REST + SignalR plumbing | ~10% | Mostly boilerplate |
| Phaser map render + interaction | ~15% | Polygons, hover, click, animations |
| HTML/CSS UI overlay | ~10% | Newspaper, panels, resource bar |
| Auth + security | ~5% | Mostly Identity defaults |

---

## Cost estimates

The repeating cost depends entirely on how you host the server.

### Tier 0 — local LAN ($0/month)
Run the server on your own PC. Friends connect over the LAN, or via something free like Tailscale. Perfect for development and for playing with people you live with.

### Tier 1 — small cloud VM ($5–10/month)
A Hetzner or DigitalOcean Linux box, SQL Server in a Docker container, or SQL Server Express on Windows Server Core. Plenty for ≤12 concurrent players.
- **Hetzner CX22:** ~$5/month, 4 GB RAM, Germany or US
- **DigitalOcean Basic:** $6/month, 1 GB RAM (tight; 2 GB at $12 is comfier)

### Tier 2 — managed PaaS ($15–35/month)
Azure App Service Basic B1 + Azure SQL Basic. Easier ops, proper backups.
- App Service B1: ~$13/month
- Azure SQL Basic 5 DTU: ~$5/month
- Total: **~$18/month** comfortably

### Optional one-time / yearly
- Domain name: ~$12/year
- TLS cert: free (Let's Encrypt)
- GitHub: free for private repos
- Visual Studio Community: free
- Everything else in the stack: free

**Recommendation for your friend group:** start on Tier 0 (your PC) for development. Move to Tier 1 ($5/mo Hetzner) once you have 4+ humans wanting to play seriously. You'll never need Tier 2 unless you open it up beyond friends.

---

## What I can do for you vs. what needs you

I can do most of the implementation, but a few things only you can decide or do:

| Thing | Who |
|-------|-----|
| Write code (entities, tick, combat, AI, UI) | Me |
| Run migrations, run the server, see actual SQL Server output | You |
| Approve the design at each phase boundary | You |
| Playtest, tell me what feels boring or unbalanced | You + friends |
| Make creative decisions (theme, map shape, headline tone) | You |
| Connect on a real network so friends can actually play | You |
| Decide when "polish enough" = "ship it" | You |

The most expensive thing in any hobby project is **decisions**. The faster you can react to a "we need to pick X or Y, what's your preference?", the faster I can build.

---

## Risks (the honest list)

| Risk | Likelihood | Impact | Mitigation |
|------|:---------:|:------:|------------|
| Scope creep — features list grows during build | High | Medium | Phase boundaries are checkpoints. New ideas → backlog, not detour. |
| Balance tuning is a swamp | High | Medium | Constants live in one file (`GameConstants.cs`); we tune fast and often |
| Map data authoring takes longer than coded estimates | Medium | Medium | Use a stylized fictional continent; ship the tool to edit it |
| AI feels dumb in playtest | Medium | High (fun-killer) | Utility AI is iterative; start basic, watch one game, fix the dumbest move, repeat |
| Multiplayer hosting headaches (NAT, firewalls) | Medium | High | Tailscale or a $5 cloud box from day one — don't fight your home router |
| You burn out before Phase 2 ships | Medium | Critical | Demo at end of Phase 1 to friends; their excitement is your fuel |
| SignalR reconnect edge cases | Low | Low | Snapshot-on-reconnect strategy is documented; tested early |
| SQL Server LocalDB vs Express vs Linux Docker friction | Low | Medium | Stick with one early; LocalDB for dev, Express on prod box, never mix |

---

## What to build first (the literal first commit checklist)

Once you greenlight, the order is:

1. Create `StarsAndSteel.sln` with the four projects
2. Add EF Core, Identity, JWT NuGet packages
3. Define `User`, `GameWorld`, `Player`, `Province`, `Unit`, `UnitOrder` entities
4. `StarsAndSteelDbContext` with `DbSet<>`s
5. `dotnet ef migrations add InitialIdentity`
6. `dotnet ef migrations add InitialGameWorld`
7. `dotnet ef database update` against your LocalDB
8. Confirm tables exist in SSMS / Azure Data Studio
9. Build out from there

That covers exactly what your original message asked for. Everything else is a follow-up commit.

---

## A note on this estimate's confidence

These are software estimates, which means: I'm 80% sure Phase 1 lands within the realistic-to-pessimistic window. Anyone who quotes you a software project to the day is either lying or very small. We re-estimate at every phase boundary based on what we actually learned.
