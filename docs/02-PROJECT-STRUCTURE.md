# 02 — Project Structure

We split the backend into a small set of class library projects so concerns don't bleed into each other. The frontend is a separate folder with its own `package.json`.

## Repo layout

```
C:\source\Personal\WorldWar\
├── docs\                              <-- you are here
├── shared\                            <-- canonical content shared by server and client
│   └── map-data.json                  <-- the ~80 provinces: id, name, polygon, center, adjacencies
├── src\
│   ├── StarsAndSteel.sln
│   ├── StarsAndSteel.Api\               <-- ASP.NET Core entry point
│   │   ├── Program.cs
│   │   ├── Controllers\
│   │   ├── Hubs\
│   │   │   └── GameHub.cs
│   │   ├── BackgroundServices\
│   │   │   └── GameTickService.cs
│   │   ├── Dto\
│   │   ├── Validation\
│   │   ├── appsettings.json
│   │   └── StarsAndSteel.Api.csproj
│   │
│   ├── StarsAndSteel.Core\              <-- domain types, no infra deps
│   │   ├── Entities\
│   │   │   ├── User.cs
│   │   │   ├── GameWorld.cs
│   │   │   ├── Player.cs
│   │   │   ├── Province.cs
│   │   │   ├── Unit.cs
│   │   │   ├── UnitOrder.cs
│   │   │   ├── Building.cs
│   │   │   ├── DiplomaticRelation.cs
│   │   │   ├── ResearchProgress.cs
│   │   │   ├── NewspaperItem.cs
│   │   │   └── ChatMessage.cs
│   │   ├── Enums\
│   │   ├── ValueObjects\
│   │   └── StarsAndSteel.Core.csproj
│   │
│   ├── StarsAndSteel.Data\              <-- EF Core, migrations
│   │   ├── StarsAndSteelDbContext.cs
│   │   ├── Configurations\            <-- IEntityTypeConfiguration<T>
│   │   ├── Migrations\
│   │   ├── Seeding\
│   │   │   └── MapSeeder.cs           <-- creates the ~50 provinces
│   │   └── StarsAndSteel.Data.csproj
│   │
│   ├── StarsAndSteel.Game\              <-- pure game logic (testable)
│   │   ├── Tick\
│   │   │   ├── TickProcessor.cs
│   │   │   ├── ResourceProductionStep.cs
│   │   │   ├── MovementStep.cs
│   │   │   ├── CombatStep.cs
│   │   │   ├── AiTurnStep.cs
│   │   │   └── EventStep.cs
│   │   ├── Combat\
│   │   │   └── CombatResolver.cs
│   │   ├── Ai\
│   │   │   ├── IAiPersonality.cs
│   │   │   ├── AggressorAi.cs
│   │   │   └── ...
│   │   └── StarsAndSteel.Game.csproj
│   │
│   └── StarsAndSteel.Tests\
│       ├── CombatResolverTests.cs
│       ├── TickProcessorTests.cs
│       └── StarsAndSteel.Tests.csproj
│
└── client\
    ├── package.json
    ├── tsconfig.json
    ├── vite.config.ts
    ├── index.html
    └── src\
        ├── main.ts
        ├── game\
        │   ├── PhaserGame.ts
        │   ├── scenes\
        │   │   ├── BootScene.ts
        │   │   ├── MapScene.ts
        │   │   └── HudScene.ts
        │   ├── render\
        │   │   ├── ProvinceRenderer.ts
        │   │   └── UnitRenderer.ts
        │   └── input\
        │       └── ProvinceClickHandler.ts
        ├── ui\                        <-- HTML/CSS overlay
        │   ├── DiplomacyPanel.ts
        │   ├── BuildMenu.ts
        │   ├── Newspaper.ts
        │   └── ResourceBar.ts
        ├── net\
        │   ├── api.ts                 <-- fetch wrapper for /api/*
        │   ├── hub.ts                 <-- SignalR client wrapper
        │   └── types.ts               <-- shared DTO shapes
        ├── state\
        │   └── store.ts               <-- single source of truth on the client
        └── styles\
            └── ui.css
```

## The `shared/` folder

`shared/map-data.json` is the single source of truth for the world map. It lives at the repo root — *not* under `src/` (server) or `client/` — because both consume it:

- **Server:** `MapSeeder` in `StarsAndSteel.Data/Seeding/` reads it at world-creation time and produces flat row records (`ProvinceRow`, `AdjacencyRow`) that `WorldFactory` inserts inside the world-creation transaction. The `.csproj` adds the file as `<Content Include="..\..\shared\map-data.json" CopyToOutputDirectory="PreserveNewest" />` so it's available next to the assembly at runtime.
- **Client:** Vite is configured (in `vite.config.ts`) with a resolve alias `@shared` → `../shared`, so `import mapData from '@shared/map-data.json'` works in TypeScript with full type inference (a `map-data.d.ts` next to the JSON declares the shape).

This way the two never drift. If you edit the map, both sides recompile against the same data.

## Why split into 4 backend projects?

Because of dependency direction:

```
Api  ──┐
       ├──> Game ──> Core
Data ──┘
```

- **Core** has *no* dependencies. Just plain C# entity classes and enums. Easy to share, easy to test.
- **Data** depends only on Core + EF Core. Knows how to persist things.
- **Game** depends only on Core. Pure logic. The combat resolver and tick processor live here so they're trivially unit-testable with no DB.
- **Api** depends on everything and wires it together. This is where the web-shaped concerns live (HTTP, SignalR, auth).

Tests can mock the data layer and exercise game logic in isolation. This is the part that pays off long-term.

## Naming conventions

- C#: PascalCase classes, `_camelCase` private fields, async methods end in `Async`.
- TypeScript: camelCase functions, PascalCase types, files named after the main export.
- DB: PascalCase tables (`Provinces`), PascalCase columns (`OwnerPlayerId`). EF Core defaults are fine.
- Branches: `feat/<thing>`, `fix/<thing>`, `chore/<thing>`.

## Initial NuGet packages

For `StarsAndSteel.Api`:
- `Microsoft.AspNetCore.Authentication.JwtBearer`
- `Microsoft.AspNetCore.SignalR` (built-in to ASP.NET Core)
- `Serilog.AspNetCore`
- `FluentValidation.AspNetCore`
- `Swashbuckle.AspNetCore` (Swagger UI for the API while we build)

For `StarsAndSteel.Data`:
- `Microsoft.EntityFrameworkCore.SqlServer`
- `Microsoft.EntityFrameworkCore.Design`
- `Microsoft.EntityFrameworkCore.Tools`
- `Microsoft.AspNetCore.Identity.EntityFrameworkCore`

For `StarsAndSteel.Tests`:
- `xunit`, `xunit.runner.visualstudio`
- `FluentAssertions`
- `Testcontainers.MsSql`
- `Microsoft.NET.Test.Sdk`

## Initial npm packages (client)

```
npm i phaser @microsoft/signalr chart.js
npm i -D typescript vite @types/node
```

That's it for now. We add more if we need them.
