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
response history, git sync. Out of scope: scripting/tests, runners, mock
servers, cloud accounts. See `README.md` for the planned workspace format.

## Stack and layout

- `src/Missive.Desktop` — .NET 10 console app hosting a Photino.NET 4 window.
  Its csproj runs `npm ci` + `npm run build` for the client and copies
  `dist/missive-client/browser` into `wwwroot`. Host-side services (HTTP
  sending, workspace I/O, git) live here.
- `src/Missive.Client` — Angular 22 standalone app (no NgModules), Prettier
  configured (`.prettierrc`: 100 cols, single quotes).
- When non-UI logic grows (YAML model, variable resolution, git), move it into a
  `src/Missive.Core` class library with a matching test project rather than
  growing `Program.cs`.

## Commands

```sh
dotnet build Missive.sln                      # builds host + Angular client
dotnet run --project src/Missive.Desktop      # run the desktop app (needs a GUI)
cd src/Missive.Client && npm ci && npm run build   # client only
cd src/Missive.Client && npm start            # browser dev server on :4200
```

No tests or linter exist yet. Before finishing, at minimum run
`dotnet build Missive.sln` (or `npm run build` for client-only changes). If you
add tests, document the command here.

## Architecture rules

- **All network, filesystem and git work happens in .NET**, never in the
  webview. Sending HTTP from .NET avoids CORS and header restrictions.
- The UI talks to the host through Photino's message bridge. Use a typed
  request/response envelope with a correlation id and an explicit error shape;
  keep message types defined in one place on each side and keep them in sync.
- The frontend is loaded from local files: keep hash routing and the relative
  base href working. Don't depend on a dev server at runtime.

## Workspace file conventions

- YAML, 2-space indent, LF line endings, trailing newline.
- Stable, documented key order; omit empty or default-valued fields.
- File names are slugs of the request name (`create-user.yaml`); renaming a
  request may rename its file, but never touch unrelated files.
- Only rewrite a file when its content actually changed — no reformatting on
  load/save round-trips.
- Secrets, response history and UI state go in `.missive/` (git-ignored),
  never in tracked files. Ensure `.missive/` is in the workspace `.gitignore`.
- If you change the format, update the examples in `README.md`.

## Git behavior in the app

- Use the user's existing git configuration and credentials.
- Never force-push, rewrite history, or auto-resolve conflicts. Report
  conflicts and errors to the user clearly.
- Don't commit or push without an explicit user action.

## Code style

- C#: nullable enabled, implicit usings, file-scoped namespaces, async for I/O.
- TypeScript: strict mode, standalone components, signals for local state;
  format with Prettier.
- Keep changes focused; update `README.md` when user-visible behavior changes.
