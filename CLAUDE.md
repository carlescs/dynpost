# CLAUDE.md

Guidance for AI agents working in this repository.

## What Missive is

A simplified, Postman-like desktop HTTP client. Its defining features:

1. **Files are the source of truth.** A workspace is a user-chosen folder; each
   request is one YAML file, folders are collections. There is no hidden
   database — the app must always reflect what is on disk.
2. **Git-friendly by design.** Missive can commit, pull and push the workspace
   from the app. Everything it writes must produce small, predictable diffs.

Initial scope: request editor, response viewer, collections, environments with
`{{variable}}` substitution, auth helpers (Basic, Bearer, API key), local
response history, git sync, a load runner (`Domain/Runs`, `Application/Runs`,
`Infrastructure/Runs`, client `modules/runs`; plans are `*.run.yaml` files,
results go to `.missive/runs/`). Out of scope: scripting/tests, functional
runners, mock servers, cloud accounts. See `README.md` for the planned workspace format.

## Stack and layout

- `src/Missive.Client` — Angular 22 standalone app (no NgModules), Prettier
  configured (`.prettierrc`: 100 cols, single quotes). Styles are SCSS
  (`inlineStyleLanguage: scss`, global `src/styles.scss`). Tailwind CSS v4 runs
  through PostCSS (`.postcssrc.json`) from its own entry, `src/tailwind.css`. Icons with Font Awesome
  (`@fortawesome/angular-fontawesome`, free solid icons). Drag and drop uses
  Angular CDK (`@angular/cdk/drag-drop`); the explorer tree's nested drop
  lists are explained in `modules/explorer/services/explorer-state.ts`. Request and response bodies
  use the Monaco editor (`monaco-editor`, ESM build) through
  `components/code-editor/code-editor.ts`. Monaco is lazy-loaded by
  `components/code-editor/monaco-loader.ts`: its code is a lazy chunk, its workers are
  bundled from local entry points, and its stylesheet is the non-injected
  `monaco` style bundle in `angular.json`. Import it only through the loader
  so it stays out of the initial bundle, and register new languages in
  `components/code-editor/monaco.ts`. PDF responses are rendered with pdf.js (`pdfjs-dist`, its
  `legacy` build, which polyfills APIs older system webviews lack) in
  `components/pdf-viewer/pdf-viewer.ts`; like Monaco it is lazy-loaded
  (`components/pdf-viewer/pdf-loader.ts`) with its worker bundled from a local entry point, and
  `angular.json` copies its data files (cmaps, fonts, ICC profiles, wasm) to `pdfjs/`.
- Client code layout (`src/Missive.Client/src/app`), following the Angular style
  guide (kebab-case file named after its class, no `.component`/`.service`
  suffixes, one folder per component). A component's template and styles are
  separate files next to it — `<name>.html` (`templateUrl`) and, when it needs
  custom styles, `<name>.scss` (`styleUrl`); `ng generate component` is set up
  to emit the same (SCSS, no inline template or styles, plus a `.spec.ts`):
  - `components/<name>/<name>.ts` — the shell (header, activity bar, sidebar,
    document tabs) and UI shared by several modules (code and diff editors, PDF
    viewer, key/value table, variable input).
  - `services/` — root-provided stores and the `Bridge` (with its `MockBackend`).
  - `models/` — bridge message types (`messages.ts`), the client model
    (`workspace.ts`), DTO mapping and other pure helpers.
  - `modules/<name>/` — one lazy-loaded, routable feature per activity-bar view
    (`explorer`, `environments`, `git`), with the same `components/`,
    `services/` and `models/` folders inside and a `<name>.routes.ts` loaded by
    `app.routes.ts`. The route selects the sidebar panel (`#/explorer`,
    `#/environments`, `#/git`); the editor area is shared and rendered by the
    shell (`app.ts`), which uses the modules' editor components. Modules may
    import from the top-level folders, but not from each other — move shared
    code up instead.
- Backend (.NET 10), clean architecture — see "Backend architecture" below:
  - `src/Missive.Domain` — entities, value objects, pure rules.
  - `src/Missive.Application` — use cases and port interfaces.
  - `src/Missive.Infrastructure` — adapters (YAML files, HTTP, git, secrets,
    history).
  - `src/Missive.Desktop` — Photino.NET 4 window, bridge, composition root. Its
    csproj runs `npm ci` + `npm run build` for the client and copies
    `dist/missive-client/browser` into `wwwroot`.
  - `tests/Missive.<Layer>.Tests` — one test project per backend layer
    (Desktop tests drive the bridge router with real use cases and mocked ports).
- Import/export of other tools' formats: the interchange model, ports and use
  cases are in `Application/Interchange`; each format (Postman, Insomnia, HAR,
  OpenAPI) is a reader/writer of a JSON tree in `Infrastructure/Interchange`
  (YAML input is converted to the same tree). The host shows the native file
  dialogs (`Desktop/Bridge/IFileDialogs.cs`).
- Sending: `Infrastructure/Http/HttpClientSender.cs` gives each send its own
  `SocketsHttpHandler`, whose connect callback and certificate check time the
  DNS, connect and TLS phases; it follows redirects and keeps cookies itself
  (the handler's `AllowAutoRedirect` and `UseCookies` stay off) so every hop
  can be reported. Per-environment cookie jars (`Application/Cookies`) live in
  `.missive/cookies.json`; raw response bodies are kept in memory by
  `ResponseBodies` for `SaveResponseBody`.
- All projects are in `Missive.sln` (solution folders `src` and `tests`). The
  Angular client is included as a JavaScript project
  (`src/Missive.Client/Missive.Client.esproj`) so it shows up in Visual
  Studio; it is not built by the solution and has npm install/build disabled —
  the Desktop csproj builds it.

## Commands

```sh
dotnet build Missive.sln                      # builds all projects + Angular client
dotnet test Missive.sln                       # runs xUnit tests
dotnet run --project src/Missive.Desktop      # run the desktop app (needs a GUI)
cd src/Missive.Client && npm ci && npm run build   # client only
cd src/Missive.Client && npm start            # browser dev server on :4200
cd src/Missive.Client && npm test             # client unit tests (Vitest; --watch=false to run once)
cd src/Missive.Client && npm run format:check # Prettier check (CI enforces it)
```

Test projects use xUnit with NSubstitute for mocking; there is no linter beyond
Prettier. Client unit tests run on Vitest with jsdom through Angular's
`@angular/build:unit-test` builder (`ng test`, `tsconfig.spec.json`): each
`<name>.spec.ts` sits next to the file it tests, uses `TestBed` for components
and services, and fakes the `Bridge` with `vi.fn()` rather than talking to the
`MockBackend`. Before finishing, run `dotnet build Missive.sln` and
`dotnet test Missive.sln` (or `npm run build` for client-only changes), and
`npm test -- --watch=false` and `npm run format:check` when you touch the client.

CI (`.github/workflows/ci.yml`) runs the Prettier check, the client unit tests and build, and
build + test on Windows, Linux and macOS. `.github/workflows/release.yml`
packages self-contained desktop builds with Velopack (`vpk pack`: installers,
portable copies and update feeds) and, when a `v*` tag is pushed, publishes only
the installers plus the update feeds and `.nupkg` packages to a GitHub release
(portable copies stay in the workflow artifacts). The app updates itself from those releases through the
`IAppUpdater` port (`Infrastructure/Updates/VelopackAppUpdater.cs`). Desktop
also references `Velopack`, only because `vpk` requires `Program.Main` to call
`VelopackApp.Build().Run()` directly. Keep the workflow's `vpk` version equal to
the `Velopack` package version. Add tests
alongside new backend and client code.

## Backend architecture

Clean architecture; the dependency rule is strict — references point inward
only:

```text
Desktop → Infrastructure → Application → Domain
Desktop → Application
```

- **Domain**: plain C# — no I/O, no `HttpClient`, no file system, no YAML or git
  libraries, no DI attributes. Holds the request/collection/environment model,
  `{{variable}}` resolution, auth rules, and validation. Prefer immutable
  records and explicit value objects.
- **Application**: one class per use case (e.g. `SendRequest`,
  `SaveRequest`, `OpenWorkspace`, `CommitChanges`, `PullChanges`,
  `PushChanges`). Defines the ports it needs as interfaces (`IWorkspaceRepository`,
  `IHttpSender`, `IGitService`, `ISecretStore`, `IHistoryStore`, `IClock`).
  Input/output DTOs live here; use cases never return Infrastructure types.
  Expected failures (invalid YAML, merge conflict, network error) are returned
  as results, not thrown.
- **Infrastructure**: implements Application ports. All third-party packages
  for YAML, git and I/O are referenced only here. File-format serialization
  (key order, omitting defaults) lives here, not in Domain.
- **Desktop**: composition root (`Microsoft.Extensions.DependencyInjection`) and
  presentation adapter. Bridge handlers deserialize a message, call one use
  case, and serialize the result — no business logic in handlers or
  `Program.cs`.
- **Tests**: Domain and Application tests are pure unit tests; mock ports
  with **NSubstitute** (`Substitute.For<IHttpSender>()`) rather than
  hand-written fakes or other mocking libraries. Don't mock Domain types — use
  real instances. Infrastructure tests run against temporary directories and real
  temporary git repositories.
- Don't add a project reference that breaks the dependency rule; if you need
  one, add a port in Application instead.

## Architecture rules

- **All network, filesystem and git work happens in the .NET backend**, never in
  the webview. Sending HTTP from .NET avoids CORS and header restrictions.
- The UI talks to the host through Photino's message bridge. Use a typed
  request/response envelope with a correlation id and an explicit error shape;
  keep message types defined in one place on each side and keep them in sync:
  `src/Missive.Desktop/Bridge/BridgeMessages.cs` (handlers in
  `BridgeHandlers.cs`) and `src/Missive.Client/src/app/models/messages.ts`.
  Outside the desktop host the client's `Bridge` falls back to an in-memory
  `MockBackend` (`services/mock-backend.ts`) so `npm start` works; keep it
  roughly in step with new message types.
- The bundled frontend is served by `Missive.Desktop/Hosting/BundledAssets.cs`
  through a Photino custom scheme: `app://localhost/` on Linux/macOS and an
  intercepted `http://localhost/` on Windows (WebView2 can't navigate to custom
  schemes via Photino). Never load it from `file://`: WebView2 blocks the
  module scripts Angular emits. Keep hash routing and the relative base href,
  and don't depend on a dev server or external `http://` resources at runtime
  (on Windows the handler intercepts `http://` requests).

## Workspace file conventions

- YAML, 2-space indent, LF line endings, trailing newline.
- Stable, documented key order; omit empty or default-valued fields.
- File names are slugs of the request name (`create-user.yaml`); renaming a
  request may rename its file, but never touch unrelated files.
- Only rewrite a file when its content actually changed — no reformatting on
  load/save round-trips.
- Secrets, response history and UI state go in `.missive/` (git-ignored),
  never in tracked files. Ensure `.missive/` is in the workspace `.gitignore`.
  App-wide, per-user settings (such as the last opened workspace) go through
  `IAppSettingsStore` into `settings.json` in the user's app data folder, never
  into a workspace or the webview's storage.
- If you change the format, update the examples in `README.md`.

## Git behavior in the app

- Use the user's existing git configuration and credentials: the adapter
  (`Infrastructure/Git/GitCliService.cs`) runs the `git` CLI rather than a git
  library, with terminal prompts disabled so it never hangs. Git use cases act
  on `CurrentWorkspace` like the other use cases.
- Pull always merges (`--no-rebase`); conflicts are left in the working tree and
  reported. Files under `.missive/` are excluded from status and commits even
  if the `.gitignore` entry is missing.
- Never force-push, rewrite history, or auto-resolve conflicts. Report
  conflicts and errors to the user clearly.
- Don't commit or push without an explicit user action.

## Code style

- C#: nullable enabled, implicit usings, file-scoped namespaces, async for I/O.
- TypeScript: strict mode, standalone components, signals for local state;
  format with Prettier.
- Styling: SCSS is the stylesheet language. Tailwind utility classes in
  templates first; when custom styles are needed, put them in the component's
  `<name>.scss` (global styles go in `src/styles.scss`). Keep Tailwind-specific CSS (`@import 'tailwindcss'`,
  `@theme` tokens, `@utility`) in `src/tailwind.css` only. Tailwind v4 doesn't
  support being run through Sass, so don't `@use`/`@import` Tailwind from SCSS.
- Icons: Font Awesome only. Import individual icons
  (`import { faPaperPlane } from '@fortawesome/free-solid-svg-icons'`) and
  render with `<fa-icon [icon]="...">` via `FaIconComponent` so unused icons
  are tree-shaken; don't import whole icon packs or use the CDN/webfont.
- Keep changes focused; update `README.md` when user-visible behavior changes.

## Issues and pull requests

- Every commit message follows Conventional Commits (`feat:`, `fix:`,
  `docs:`, `ci:`, ... with an optional scope such as `fix(client):`; lower-case
  subject). CI runs commitlint (`commitlint.config.mjs`) on each commit of a
  pull request and semantic-release reads them on `main`, so check a message
  with `npx commitlint --from HEAD~1 --to HEAD` (after `npm ci` at the repo
  root) before pushing. Pull request titles follow the same format.

- When merging a pull request that closes an issue, document the outcome in the
  issue itself: add a comment summarizing what was implemented (behavior,
  user-visible changes, notable design decisions, anything left out or deferred)
  and link the pull request, so the issue stays a useful record on its own.
