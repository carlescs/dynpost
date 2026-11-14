# CLAUDE.md

Guidance for AI agents working in this repository.

## What Dynpost is

A simplified, Postman-like desktop HTTP client. Its defining features:

1. **Files are the source of truth.** A workspace is a user-chosen folder; each
   request is one YAML file, folders are collections. There is no hidden
   database — the app must always reflect what is on disk.
2. **Git-friendly by design.** Dynpost can commit, pull and push the workspace
   from the app. Everything it writes must produce small, predictable diffs.

Initial scope: request editor, response viewer, collections, environments with
`{{variable}}` substitution, auth helpers (Basic, Bearer, API key), local
response history, git sync, a load runner (`Domain/Runs`, `Application/Runs`,
`Infrastructure/Runs`, client `modules/runs`; plans are `*.run.yaml` files in
`plans/`, results go to `.dynpost/runs/`). Out of scope: scripting/tests, functional
runners, mock servers, cloud accounts. See `README.md` for the planned workspace format.

## Stack and layout

- `src/Dynpost.Client` — Angular 22 standalone app (no NgModules), Prettier
  configured (`.prettierrc`: 100 cols, single quotes). Styles are SCSS
  (`inlineStyleLanguage: scss`, global `src/styles.scss`). Tailwind CSS v4 runs
  through PostCSS (`.postcssrc.json`) from its own entry, `src/tailwind.css`. Icons with Font Awesome
  (`@fortawesome/angular-fontawesome`, free solid icons). Drag and drop uses
  Angular CDK (`@angular/cdk/drag-drop`); the folder trees (explorer, Load runs) share
  `services/tree-state.ts`, which explains their nested drop lists, and
  `components/folder-drop`. Dialogs use Angular CDK
  Dialog (`@angular/cdk/dialog`) through the root `Dialogs` service (`services/dialogs.ts`):
  `dialogs.confirm(...)` replaces `window.confirm` (never use the browser's `confirm`/`alert`/
  `prompt`), and `dialogs.open(Component)` opens any other dialog over the shared blurred
  backdrop; style dialog content with Tailwind classes. Request and response bodies
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
- Client code layout (`src/Dynpost.Client/src/app`), following the Angular style
  guide (kebab-case file named after its class, no `.component`/`.service`
  suffixes, one folder per component). A component's template and styles are
  separate files next to it — `<name>.html` (`templateUrl`) and, when it needs
  custom styles, `<name>.scss` (`styleUrl`); `ng generate component` is set up
  to emit the same (SCSS, no inline template or styles, plus a `.spec.ts`):
  - `components/<name>/<name>.ts` — the shell (header, activity bar, sidebar,
    document tabs) and UI shared by several modules (code and diff editors, PDF
    viewer, key/value table, variable input, folder drop target and name input of the folder trees).
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
- Backend (.NET 11), clean architecture — see "Backend architecture" below:
  - `src/Dynpost.Domain` — entities, value objects, pure rules.
  - `src/Dynpost.Application` — use cases and port interfaces.
  - `src/Dynpost.Infrastructure` — adapters (YAML files, HTTP, git, secrets,
    history).
  - `src/Dynpost.Desktop` — Photino.NET 4 window, bridge, composition root. Its
    csproj runs `npm ci` + `npm run build` for the client and copies
    `dist/dynpost-client/browser` into `wwwroot`.
  - `tests/Dynpost.<Layer>.Tests` — one test project per backend layer
    (Desktop tests drive the bridge router with real use cases and mocked ports).
- Import/export of other tools' formats: the interchange model, ports and use
  cases are in `Application/Interchange`; each format (Postman, Insomnia, HAR,
  OpenAPI) is a reader/writer of a JSON tree in `Infrastructure/Interchange`
  (YAML input is converted to the same tree). The host shows the native file
  dialogs (`Desktop/Bridge/IFileDialogs.cs`).
- Sending: `Infrastructure/Http/HttpClientSender.cs` sends through one shared,
  pooled `SocketsHttpHandler` so connections are reused (a handler per send
  exhausts ephemeral ports in load runs); idle pooled connections close after
  15 seconds, and a send that fails on a reused connection the server already
  dropped (reset or closed before any response) is sent again; its connect callback and plaintext
  stream filter time the DNS, connect and TLS phases of each new connection for
  the send that opened it (passed in the request's `Options`); it follows redirects and keeps cookies itself
  (the handler's `AllowAutoRedirect` and `UseCookies` stay off) so every hop
  can be reported. Per-environment cookie jars (`Application/Cookies`) live in
  `.dynpost/cookies.json`; raw response bodies are kept in memory by
  `ResponseBodies` for `SaveResponseBody`. The response to a saved request is
  recorded by `Application/History/ResponseHistory.cs` (cookie values and secrets
  hidden) through the `IHistoryStore` port into `.dynpost/history/`
  (`Infrastructure/History/JsonHistoryStore.cs`, keyed by request path); use
  cases that rename, move or delete requests or folders keep it in step, and
  its limits are per-user `AppSettings.History`.
- Folder settings: a collection folder's `_folder.yaml` sets headers and auth for everything in it
  (`Domain/Requests/FolderDefaults.cs`, read and written through the `IFolderDefaultsStore` port by
  `Infrastructure/Workspaces/YamlFolderDefaultsStore.cs`). `Application/Folders/FolderInheritance`
  combines them from `requests/` down (closer levels win; a request's own headers and auth win
  over all; `RequestAuth.Inherit`, a request file without `auth`, takes the folders' auth) and
  every use case that sends or exports requests applies it (`SendRequest`, `StartRun`,
  `ExportWorkspace`). The client edits them in a folder tab (`services/folder-settings-store.ts`,
  explorer `folder-editor`, shared `auth-editor`).
- MCP server (AI assistants control the app): off by default, toggled in the header's settings
  menu (`components/mcp-settings-dialog`). Its settings (enabled, port, token) are per-user
  `AppSettings.Mcp`, changed by the use cases in `Application/Mcp` through the `IMcpServer` port.
  `Desktop/Mcp` holds the presentation adapter: `McpHttpServer` (Streamable HTTP on
  `http://localhost:{port}/mcp` via `HttpListener`, bearer token, `Origin` check, JSON responses
  only, no sessions), `McpProtocol` (JSON-RPC: `initialize`, `ping`, `tools/list`, `tools/call`)
  and `McpTools`, which maps each tool to one use case like `BridgeHandlers` (argument schemas are
  generated from the argument records). Tools that change the workspace send the UI a
  `workspace.changed` event, `start_run` a `run.started` event and `open_in_app` an `item.open`
  event (`Bridge/HostEvents.cs`; the client's `Bridge.on` and `services/workspace-sync.ts` re-read
  what changed, open the plan's tab to follow the run, and open the item asked for; the item's kind
  comes from `Application/Workspaces/FindWorkspaceItem.cs`). `get_run` waits for a run through `Application/Runs/WaitForRun.cs`. Keep commit/pull/push out of the tools and
  never show secret values to the assistant; update the tool table in `README.md` when adding one.
- Dates: the client never formats dates with the webview's locale or Angular's
  `DatePipe`; it uses `DateFormatStore` (`services/date-format-store.ts`), which formats
  with the operating system's regional patterns read by the host
  (`Application/Locale`, `Infrastructure/Locale/SystemRegionalSettings.cs`, message
  `locale.dateFormat`) through `models/date-format.ts`.
- `site/` is the static homepage (plain HTML/CSS, no build step, no external resources),
  deployed to GitHub Pages (`https://carlescs.github.io/dynpost/`) by
  `.github/workflows/pages.yml`. Keep its feature list and download file names in step with
  `README.md` and `release.yml`.
- All projects are in `Dynpost.sln` (solution folders `src` and `tests`). The
  Angular client is included as a JavaScript project
  (`src/Dynpost.Client/Dynpost.Client.esproj`) so it shows up in Visual
  Studio; it is not built by the solution and has npm install/build disabled —
  the Desktop csproj builds it.

## Commands

```sh
dotnet build Dynpost.sln                      # builds all projects + Angular client
dotnet test Dynpost.sln                       # runs xUnit tests
dotnet run --project src/Dynpost.Desktop      # run the desktop app (needs a GUI)
cd src/Dynpost.Client && npm ci && npm run build   # client only
cd src/Dynpost.Client && npm start            # browser dev server on :4200
cd src/Dynpost.Client && npm test             # client unit tests (Vitest; --watch=false to run once)
cd src/Dynpost.Client && npm run format:check # Prettier check (CI enforces it)
```

Test projects use xUnit with NSubstitute for mocking; there is no linter beyond
Prettier. Client unit tests run on Vitest with jsdom through Angular's
`@angular/build:unit-test` builder (`ng test`, `tsconfig.spec.json`): each
`<name>.spec.ts` sits next to the file it tests, uses `TestBed` for components
and services, and fakes the `Bridge` with `vi.fn()` rather than talking to the
`MockBackend`. Helpers shared by client specs live in `src/app/testing/` (e.g.
`useTestDateFormat()` to check dates are shown in the system format). Before finishing, run `dotnet build Dynpost.sln` and
`dotnet test Dynpost.sln` (or `npm run build` for client-only changes), and
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
  records and explicit value objects. Fixed sets of variants (`RequestAuth`,
  `OutgoingBody`, `CaptureSource`, `WorkspaceEntry`) are C# `closed` records:
  switch expressions over them list every case with no `_` fallback, so adding a
  variant is a compiler warning at each switch that must handle it.
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
  keep message types defined in one place on each side and keep them in sync (host-initiated
  events, which carry `event` instead of an id, are `BridgeEventTypes` / `BridgeEvents`):
  `src/Dynpost.Desktop/Bridge/BridgeMessages.cs` (handlers in
  `BridgeHandlers.cs`) and `src/Dynpost.Client/src/app/models/messages.ts`.
  Outside the desktop host the client's `Bridge` falls back to an in-memory
  `MockBackend` (`services/mock-backend.ts`) so `npm start` works; keep it
  roughly in step with new message types.
- The bundled frontend is served by `Dynpost.Desktop/Hosting/BundledAssets.cs`
  through a Photino custom scheme: `app://localhost/` on Linux/macOS and an
  intercepted `http://localhost/` on Windows (WebView2 can't navigate to custom
  schemes via Photino). Never load it from `file://`: WebView2 blocks the
  module scripts Angular emits. Keep hash routing and the relative base href,
  and don't depend on a dev server or external `http://` resources at runtime
  (on Windows the handler intercepts `http://` requests).

## Workspace file conventions

- Layout (`Domain/Workspaces/WorkspaceLayout.cs`): requests and their collection
  folders live in `requests/`, run plans in `plans/`, environments in
  `environments/`; every other file is the user's. Paths are always
  workspace-relative (`requests/users/get-user.yaml`), in the bridge, in plan
  files, in `.dynpost/` and in `.gitignore`. The client trees show `requests`
  and `plans` as their top level (`TreeState.root`). Workspaces in the old
  layout (everything at the root) are moved on open by
  `Application/Workspaces/LayoutMigration.cs`, which then records `layout: 2`
  in `dynpost.yaml`; bump `WorkspaceLayout.CurrentLayoutVersion` and extend it
  if the layout changes again. The app was called Missive before: the same use case first renames
  `missive.yaml` and `.missive/` (and their `.gitignore` line) through
  `IWorkspaceLayoutStore.RenameLegacyFilesAsync`, and `JsonAppSettingsStore` reads the old
  `Missive` settings file until the new one exists. The GitHub repository was renamed to `carlescs/dynpost` (GitHub redirects the old URLs).
- YAML, 2-space indent, LF line endings, trailing newline.
- Stable, documented key order; omit empty or default-valued fields.
- File names are slugs of the request name (`create-user.yaml`); renaming a
  request may rename its file, but never touch unrelated files.
- Only rewrite a file when its content actually changed — no reformatting on
  load/save round-trips.
- Secrets, response history and UI state go in `.dynpost/` (git-ignored),
  never in tracked files. Ensure `.dynpost/` is in the workspace `.gitignore`.
  App-wide, per-user settings (such as the last opened workspace) go through
  `IAppSettingsStore` into `settings.json` in the user's app data folder, never
  into a workspace or the webview's storage.
- If you change the format, update the examples in `README.md`.

## Git behavior in the app

- Use the user's existing git configuration and credentials: the adapter
  (`Infrastructure/Git/GitCliService.cs`) runs the `git` CLI rather than a git
  library, with terminal prompts disabled so it never hangs. Git use cases act
  on `CurrentWorkspace` like the other use cases.
- The status stays current on its own: `Application/Git/WatchWorkingTree` follows the open
  workspace through the `IWorkingTreeWatcher` port (`Infrastructure/Git/FileSystemWorkingTreeWatcher.cs`:
  `FileSystemWatcher`s on the workspace and, when elsewhere, its git folder; only `HEAD`, `index`,
  refs and merge state count there; `.dynpost/` is ignored; bursts are debounced) and the host
  sends a `git.changed` event, on which the client's `GitStore.refreshInBackground` re-reads the
  status without a busy state, queueing behind a running operation rather than dropping the read.
  Git reads run with `GIT_OPTIONAL_LOCKS=0` so they don't write `.git/index` and re-trigger it.
- Pull always merges (`--no-rebase`); conflicts are left in the working tree and
  reported. Files under `.dynpost/` are excluded from status and commits even
  if the `.gitignore` entry is missing.
- Local-only requests, environments and run plans (`Application/Sharing`) are
  anchored lines in the workspace `.gitignore`
  (`Infrastructure/Sharing/GitIgnoreLocalOnlyFiles.cs`); making one local-only
  also untracks it (`git rm --cached`), and `CommitAsync` commits such staged
  removals through a temporary index because `commit --only` would re-add them.
  Use cases that rename, move, copy or delete files keep those lines in step.
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

- Never squash merge a pull request: merge it with a merge commit, so every
  commit reaches `main` as written and semantic-release sees each one's type
  (a squash keeps only the title's, which can hide a `feat:` or `fix:`).

- When merging a pull request that closes an issue, document the outcome in the
  issue itself: add a comment summarizing what was implemented (behavior,
  user-visible changes, notable design decisions, anything left out or deferred)
  and link the pull request, so the issue stays a useful record on its own.
