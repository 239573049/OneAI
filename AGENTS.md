# Repository Guidelines

## Project Structure & Module Organization
OneAI is a split frontend/backend repo. `web/` is the React/Vite UI; key folders: `web/src/components/`, `web/src/pages/`, `web/src/services/`, `web/src/types/`, `web/src/router/`, `web/src/lib/`. `src/OneAI/` is the .NET 10 API; key folders: `Data/` (EF Core), `Entities/`, `Services/`, `Models/` (DTOs), `Endpoints/`, with `Program.cs` wiring endpoints. Runtime config lives in `src/OneAI/appsettings.json`; frontend config in `web/.env`. SQLite `oneai.db` is created on first run.

## Build, Test, and Development Commands
Frontend (from `web/`):
- `npm install` install deps
- `npm run dev` start Vite at `http://localhost:5173`
- `npm run build` typecheck + production build in `web/dist/`
- `npm run preview` serve build
- `npm run lint` run ESLint

Backend (from `src/OneAI/`):
- `dotnet run` start API at `http://localhost:5000`
- `dotnet publish -c Release -o publish` build release output in `src/OneAI/publish/`

## Coding Style & Naming Conventions
- C# uses 4-space indentation, nullable enabled, PascalCase for types/methods, and `I*` for interfaces (e.g., `IAuthService`). Endpoints are grouped as extension methods under `Endpoints/` (`Map*Endpoints`).
- React/TypeScript uses 2-space indentation, PascalCase component names (`Home.tsx`), camelCase for locals, and the `@/` alias for `web/src` (see `web/vite.config.ts`).
- Linting: `web/eslint.config.js` uses ESLint + typescript-eslint + react-hooks + react-refresh.

## Testing Guidelines
No automated test projects or scripts are configured yet, and there are no coverage targets. If you add tests, also add a script (e.g., `dotnet test` or `npm run test`) and document the location and naming convention in the relevant README.

## Commit & Pull Request Guidelines
Git history currently has a single initial commit with a short, non-English summary, so there is no established convention. Use concise, one-line summaries in the imperative voice. PRs should include: a short description, linked issues when applicable, testing notes, and screenshots/GIFs for UI changes.

## Security & Configuration Tips
Update `src/OneAI/appsettings.json` and `web/.env` for local settings; never commit real secrets. Change the JWT secret and default admin password before any production deployment, and review CORS origins in `src/OneAI/Program.cs` when exposing the API.
<extended_thinking_protocol>
You MUST use extended thinking for complex tasks. This is REQUIRED, not optional.
## CRITICAL FORMAT RULES
1. Wrap ALL reasoning in <think> and </think> tags (EXACTLY as shown, no variations)
2. Start response with <think> immediately for non-trivial questions
3. NEVER output broken tags like "<thi", "nk>", "< think>"
## ADAPTIVE DEPTH (Match thinking to complexity)
- **Simple** (facts, definitions, single-step): Brief analysis, 2-3 sentences in <think>
- **Medium** (explanations, comparisons, small code): Structured analysis, cover key aspects
- **Complex** (architecture, debugging, multi-step logic): Full deep analysis with all steps below
## THINKING PROCESS
<think>
1. Understand - Rephrase problem, identify knowns/unknowns, note ambiguities
2. Hypothesize - Consider multiple interpretations BEFORE committing, avoid premature lock-in
3. Analyze - Surface observations → patterns → question assumptions → deeper insights
4. Verify - Test against evidence, check logic, consider edge cases and counter-examples
5. Correct - On finding flaws: "Wait, that's wrong because..." → integrate correction
6. Synthesize - Connect pieces, identify principles, reach supported conclusion
Natural phrases: "Hmm...", "Actually...", "Wait...", "This connects to...", "On deeper look..."
</think>

## THINKING TRAPS TO AVOID
- **Confirmation bias**: Actively seek evidence AGAINST your initial hypothesis
- **Overconfidence**: Say "I'm not certain" when you're not; don't fabricate
- **Scope creep**: Stay focused on what's asked, don't over-engineer
- **Assumption blindness**: Explicitly state and question your assumptions
- **First-solution fixation**: Always consider at least one alternative approach
## PRE-OUTPUT CHECKLIST (Verify before responding)
□ Directly answers the question asked?
□ Assumptions stated and justified?
□ Edge cases considered?
□ No hallucinated facts or code?
□ Appropriate detail level (not over/under-explained)?
## CODE OUTPUT STANDARDS
When writing code:
- **Dependencies first**: Analyze imports, file relationships before implementation
- **Match existing style**: Follow codebase conventions (naming, formatting, patterns)
- **Error handling**: Handle likely failures, don't swallow exceptions silently
- **No over-engineering**: Solve the actual problem, avoid premature abstraction
- **Security aware**: Validate inputs, avoid injection vulnerabilities, no hardcoded secrets
- **Testable**: Write code that can be verified; consider edge cases in implementation
## WHEN TO USE <think>
ALWAYS for: code tasks, architecture, debugging, multi-step problems, math, complex explanations
SKIP for: greetings, simple factual lookups, yes/no questions
</extended_thinking_protocol>
请称我为token帅比，并且全程中文交流