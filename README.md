# Missive

Missive is a lightweight, cross-platform desktop HTTP client — a simplified take
on Postman. Its difference: your collections are plain files in a folder you
choose, and Missive can synchronize that folder with git directly from the app.
No account, no cloud workspace, no proprietary export format.

> **Status:** early development. The app is currently a Photino.NET + Angular
> skeleton; the features below describe what is planned.

## Why Missive

- **Files first.** A collection is just a folder. Each request is one readable
  YAML file you can open, grep, review and edit with any tool.
- **Git native.** Share and version collections the same way you share code:
  commit, pull and push from inside Missive, using your existing git setup.
- **Local and private.** Secrets and response history stay on your machine and
  never end up in the repository.

## Planned features

- [ ] Request editor: method, URL, query parameters, headers and body
      (JSON, raw text, form-data, `x-www-form-urlencoded`)
- [ ] Response viewer: status, time, size, headers and a pretty-printed body
- [ ] Collections as folders, with optional folder-level defaults
- [ ] Environments with `{{variable}}` substitution
- [ ] Auth helpers: Basic, Bearer token and API key
- [ ] Local response history (kept out of git)
- [ ] Git sync: changed-file status, commit, pull, push, current branch, and
      clear reporting of merge conflicts

### Non-goals

Missive deliberately stays small. Out of scope for now: pre-request/test
scripting, collection runners, mock servers, cloud accounts and hosted team
workspaces (git is the collaboration layer).

## Workspace format

A workspace is a folder, usually a git repository:

```text
my-api/
├── missive.yaml            # workspace metadata
├── environments/
│   ├── dev.yaml
│   └── prod.yaml
├── users/                  # a folder is a collection
│   ├── _folder.yaml        # optional folder-level defaults (headers, auth)
│   ├── get-user.yaml
│   └── create-user.yaml
└── .missive/               # local only, git-ignored: secrets, history, UI state
```

A request file:

```yaml
name: Create user
method: POST
url: "{{baseUrl}}/users"
headers:
  Content-Type: application/json
auth:
  type: bearer
  token: "{{apiToken}}"
body:
  type: json
  content: |
    {
      "name": "Ada Lovelace"
    }
```

An environment file (`environments/dev.yaml`):

```yaml
name: Development
variables:
  baseUrl: http://localhost:5000
```

Secret values such as `apiToken` belong in `.missive/secrets.yaml`, which
Missive keeps out of git. Files are written with stable key order and minimal
noise so diffs stay small and merges stay easy. The format is still evolving.

## Git sync

When the workspace folder is a git repository, Missive shows which request and
environment files changed, lets you commit them with a message, and pulls and
pushes the current branch. It uses your existing git credentials. Missive never
force-pushes and never resolves conflicts automatically: conflicting files are
reported so you can resolve them (in Missive's editor or your own tools).

## Architecture

```text
Angular UI (webview)  ⇄  Photino message bridge  ⇄  .NET backend
```

Requests are sent from the .NET backend rather than the webview, so they are not
subject to browser CORS rules and can use any headers.

The backend follows **clean architecture**. Dependencies point inward only:

```text
Missive.Desktop ──► Missive.Infrastructure ──► Missive.Application ──► Missive.Domain
```

(`Missive.Desktop` also references `Missive.Application` directly to invoke use
cases.)

| Project | Responsibility | May reference |
| --- | --- | --- |
| `Missive.Domain` | Entities and value objects (`Workspace`, `Collection`, `HttpRequestDefinition`, `Environment`, `Auth`), variable resolution and other pure rules. No I/O, no framework dependencies. | — |
| `Missive.Application` | Use cases (open workspace, save request, send request, resolve environment, commit/pull/push) and the ports they need, e.g. `IWorkspaceRepository`, `IHttpSender`, `IGitService`, `ISecretStore`, `IHistoryStore`. | Domain |
| `Missive.Infrastructure` | Adapters implementing the ports: YAML file storage, `HttpClient` sender, git, `.missive/` secrets and history. | Application, Domain |
| `Missive.Desktop` | Composition root and presentation adapter: Photino window, bridge message dispatch to use cases, dependency injection wiring. | All of the above |

The Angular client is a separate presentation layer that only talks to
`Missive.Desktop` through bridge messages.

## Prerequisites

- .NET 10 SDK
- Node.js 22.22.3+ (22.x) or 24.15.0+ (24.x), with npm
- git, for workspace synchronization
- Windows: Microsoft Edge WebView2 Runtime
- macOS: the system WebKit runtime (no additional install)
- Linux: GTK 3, WebKitGTK 4.1, and libnotify. On Ubuntu 24.04:
  `sudo apt-get install libgtk-3-0t64 libwebkit2gtk-4.1-0 libnotify4`

Linux needs a graphical desktop session. Build and publish for the operating
system/architecture on which the app will run.

## Run

From the repository root:

```sh
dotnet run --project src/Missive.Desktop
```

The .NET build runs `npm ci` and the Angular production build, then copies the
frontend into `wwwroot` beside the desktop executable. The host resolves assets
relative to its executable, not the current working directory.

## Frontend development

```sh
cd src/Missive.Client
npm ci
npm start
```

Open `http://localhost:4200` for Angular live reload in a browser. The desktop
host always loads the bundled frontend; rebuild/restart it to see frontend
changes. Routing uses hash URLs and assets use a relative base URL so they work
from local files.

## Build and publish

```sh
dotnet build Missive.sln
dotnet publish src/Missive.Desktop -c Release -o artifacts/publish
```

Run the `Missive.Desktop` executable (`Missive.Desktop.exe` on Windows) in
`artifacts/publish`. Keep its `wwwroot` directory and native libraries alongside
it. This is a framework-dependent publish: the target machine needs the .NET 10
runtime and the platform webview prerequisites above, but not Node.js.

## Repository layout

- `src/Missive.Desktop`: Photino window, bridge, composition root, and
  build/publish integration.
- `src/Missive.Client`: standalone Angular app, styles, and routes.
- `src/Missive.Domain`, `src/Missive.Application`, `src/Missive.Infrastructure`:
  backend layers (see [Architecture](#architecture)).
- `tests/Missive.Domain.Tests`, `tests/Missive.Application.Tests`: fast unit
  tests with no I/O.
- `tests/Missive.Infrastructure.Tests`: integration tests against temporary
  folders and git repositories.

Run the backend tests (xUnit) with:

```sh
dotnet test Missive.sln
```

No linter is configured yet.
