# Contributing to Sweeprr

Thank you for your interest in contributing to Sweeprr! To ensure the codebase remains clean, maintainable, and aligned with our architecture, please follow these guidelines.

## Immutable Tech Stack

All additions must adhere strictly to our core technology stack:
1. **Backend**: .NET 9, ASP.NET Core, Entity Framework Core with SQLite.
2. **Frontend**: React, TypeScript, Vite, React Router v6.
3. **Styling**: **Vanilla CSS ONLY**. Do not introduce TailwindCSS, Bootstrap, or any other utility or CSS framework.
4. **Icons**: **Phosphor Icons** (`@phosphor-icons/react`) using the **duotone** weight variant.

---

## Getting Started

### Prerequisites
- .NET 9 SDK
- Node.js (v20+)
- pnpm (v9+)

### Local Development Loop
Sweeprr is structured as a pnpm workspace monorepo. You can launch both the frontend Vite dev server and the backend API concurrently using:

```bash
pnpm install
pnpm run dev
```

This starts:
- Frontend on `http://localhost:5173` (with Vite HMR and `/api` proxy).
- Backend on `http://localhost:5000` (with dotnet watch hot reload).

---

## Code Quality Standards

- **Nullability**: Nullable reference types are enabled (`<Nullable>enable</Nullable>`). All new C# code must be null-safe and compile without warnings.
- **Safety Invariants**:
  - Transient failure (e.g. timeout reading a user's playstate) must exclude that item from the sweep, never matching or deleting it.
  - Empty rule groups must match **nothing** (to prevent accidental catalog-wide deletion).
  - Unmonitoring **MUST** complete successfully before a delete operation is issued.
- **Database Migrations**: Additions to database models must include EF Core migrations. Apply them via `dotnet ef migrations add <Name>`. They are run automatically on startup.
- **Tests**: Ensure any new logic has corresponding test coverage. Run tests using `dotnet test`.

---

## Commit & PR Policy

- **No Automated Commits**: If you are using agentic AI, the agent is strictly prohibited from running git commands directly. Contributors must manually stage and commit changes.
- **Conventional Commits**: All commit messages must follow the Conventional Commits specification.
  - Example: `feat(api): add system info endpoint for version tracking`
