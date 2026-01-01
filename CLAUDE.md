# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

OneAI is a full-stack AI account management system built with React + .NET 10. It manages multiple AI service accounts (OpenAI, Claude, Gemini) with OAuth integration, request logging, and quota tracking.

**Architecture**: Split frontend/backend monorepo
- Frontend: `web/` - React 19 + TypeScript + Vite + shadcn/ui
- Backend: `src/OneAI/` - .NET 10 Minimal APIs + EF Core + SQLite

## Development Commands

### Backend (from `src/OneAI/`)
```bash
dotnet run                           # Start API at http://localhost:5000
dotnet build                         # Build project
dotnet publish -c Release -o publish # Production build
dotnet ef migrations add <Name>      # Create migration
dotnet ef database update            # Apply migrations
```

API documentation available at: `http://localhost:5000/scalar`

### Frontend (from `web/`)
```bash
npm install      # Install dependencies
npm run dev      # Start dev server at http://localhost:5173
npm run build    # TypeScript check + production build
npm run lint     # Run ESLint
npm run preview  # Preview production build
```

## Key Architecture Patterns

### Backend Architecture

**Minimal APIs Pattern**: Endpoints are organized as extension methods in `Endpoints/` directory. Each endpoint file exports a `Map*Endpoints(IEndpointRouteBuilder)` method that groups related routes.

**Service Registration**: All services registered in `Program.cs` lines 128-153. Use scoped services for per-request state, singletons for shared caches (`AccountQuotaCacheService`, `IOAuthSessionService`).

**Database Strategy**:
- Two separate SQLite databases: `AppDbContext` (main data) and `LogDbContext` (request logs)
- Migrations in `AppMigrations/` and `LogMigrations/`
- Database initialization happens in `Program.cs` lines 233-247

**Background Services**: Two hosted services run continuously:
- `AIRequestLogWriterService`: Consumes log queue and writes to database
- `AIRequestAggregationBackgroundService`: Aggregates hourly statistics

**Logging Architecture**: Uses Channel-based queue (line 45-49) for async log writing. Producers push to channel via `AIRequestLogService`, consumer (`AIRequestLogWriterService`) writes batches to database.

**OAuth Flow**:
- Session-based OAuth state stored in `InMemoryOAuthSessionService` (singleton)
- OAuth helpers per provider:
  - OpenAI: `OpenAiOAuthHelper` (Authorization Code + PKCE)
  - Claude: `ClaudeCodeOAuthHelper` (Authorization Code + PKCE)
  - Gemini: `GeminiAntigravityOAuthHelper`, `GeminiOAuthHelper` (Authorization Code + PKCE)
  - Factory: `FactoryOAuthService` (Device Authorization Flow via WorkOS)
- OAuth tokens stored in `AIAccount.OAuthToken` as JSON
- Factory uses WorkOS Device Authorization Flow (RFC 8628) - user authorizes via device code, polls token endpoint

**Quota Tracking**: `AccountQuotaCacheService` (singleton) maintains in-memory cache of account quotas with rate limiting logic.

### Frontend Architecture

**API Layer**: Centralized in `web/src/services/api.ts`
- Unified fetch wrapper with request/response interceptors
- Automatic JWT token injection from localStorage
- Auto-redirect to `/login` on 401 responses
- Custom `ApiException` for error handling
- Path alias `@/` maps to `web/src/`

**Service Pattern**: Domain-specific services in `web/src/services/`:
- `auth.ts`: Authentication (login, getCurrentUser)
- `account.ts`: AI account management
- `settings.ts`: System settings
- `logs.ts`: Request log queries

**Response Format**: All API responses follow `ApiResponse<T>` structure:
```typescript
{
  code: number,      // 0 or 200 = success
  message: string,
  data: T
}
```

**Routing**: React Router 7 in `web/src/router/`. Protected routes should check authentication state.

## Configuration

### Backend Config (`src/OneAI/appsettings.json`)
- `ConnectionStrings:DefaultConnection`: Main database path
- `ConnectionStrings:LogConnection`: Log database path (optional, defaults to main)
- `Jwt:SecretKey`: JWT signing key (MUST change for production)
- `Jwt:ExpirationMinutes`: Token lifetime (default 1440 = 24h)
- `AdminAccount`: Hardcoded admin credentials (username: admin, password: admin123)
- `Gemini:CodeAssistEndpoint`: Gemini API endpoint

### Frontend Config (`web/.env`)
- `VITE_API_BASE_URL`: Backend API URL (default: http://localhost:5000/api)

### CORS Origins
Configured in `src/OneAI/Program.cs` lines 154-163. Currently allows:
- `http://localhost:5173` (Vite)
- `http://localhost:3000` (alternative)

## Database Schema

**AIAccounts**: Main entity for AI service accounts
- Supports both API key (`ApiKey`) and OAuth (`OAuthToken` JSON field)
- `IsEnabled`: Soft disable flag
- `IsRateLimited`: Quota exceeded flag
- `Provider`: Service name (OpenAI, Claude, Factory, Gemini, Gemini-Antigravity)
- Extension methods for OAuth serialization: `GetClaudeOauth()`, `SetClaudeOAuth()`, `GetFactoryOAuth()`, `SetFactoryOAuth()`, etc.

**AIRequestLog**: Request logging for analytics
- Linked to `AIAccount` via `AccountId`
- Tracks model, tokens, latency, status
- Aggregated into hourly summaries by background service

**SystemSettings**: Key-value store for runtime configuration
- `Key` is unique
- `DataType` field indicates value type
- Initialized with defaults in `DbInitializer.InitializeSettingsAsync()`

## Important Implementation Details

**JWT Authentication**: Custom event handlers in `Program.cs` lines 70-122 handle malformed tokens gracefully. Unauthorized endpoints don't fail on invalid tokens; only protected endpoints return 401.

**Static File Serving**: Backend serves frontend SPA from `wwwroot/`. Fallback route (lines 280-298) returns `index.html` for non-API routes to support client-side routing.

**Response Compression**: Brotli + Gzip enabled (lines 161-175) for JSON, JS, CSS, HTML.

**Request Logging**: Serilog configured with TraceId and ClientIp enrichment (lines 183-203). Request logs written to console with timing info.

**Settings Cache**: `SettingsService` loads all settings into memory on startup (line 246) for fast access. Implements `ISettingsService`.

## Common Patterns

### Adding a New Endpoint
1. Create `Endpoints/MyFeatureEndpoints.cs`
2. Define `public static void MapMyFeatureEndpoints(this IEndpointRouteBuilder endpoints)`
3. Use `endpoints.MapGroup("/api/myfeature").WithTags("My Feature")`
4. Register in `Program.cs`: `app.MapMyFeatureEndpoints();`

### Adding a New Service
1. Create interface in `Services/IMyService.cs`
2. Implement in `Services/MyService.cs`
3. Register in `Program.cs`: `builder.Services.AddScoped<IMyService, MyService>();`
4. Inject via constructor in endpoints/other services

### Adding a Frontend Page
1. Create component in `web/src/pages/MyPage.tsx`
2. Add route in `web/src/router/index.tsx`
3. Create service in `web/src/services/myFeature.ts` if needed
4. Define types in `web/src/types/myFeature.ts`

### Adding a Database Entity
1. Create entity in `src/OneAI/Entities/MyEntity.cs`
2. Add `DbSet<MyEntity>` to `AppDbContext.cs`
3. Configure in `OnModelCreating` if needed
4. Run: `dotnet ef migrations add AddMyEntity`
5. Run: `dotnet ef database update`

## Code Style

**C#**: 4-space indent, PascalCase for types/methods, `I*` prefix for interfaces, nullable reference types enabled

**TypeScript/React**: 2-space indent, PascalCase for components, camelCase for variables/functions

**Endpoint Grouping**: Use `.WithTags()` for OpenAPI categorization, `.RequireAuthorization()` for protected routes

## Security Notes

- Default admin credentials (admin/admin123) are in `appsettings.json` - change before production
- JWT secret key must be strong and unique in production
- OAuth tokens stored as JSON in database - ensure database encryption in production
- CORS origins must be restricted to known domains in production
- Token stored in localStorage (frontend) - consider httpOnly cookies for enhanced security
