# Missive

Missive is a lightweight, cross-platform desktop HTTP client — a simplified take
on Postman. Its difference: your collections are plain files in a folder you
choose, and Missive can synchronize that folder with git directly from the app.
No account, no cloud workspace, no proprietary export format.

> **Status:** early development. You can open a workspace folder, browse its
> request files in the explorer (a sidebar you can resize by dragging its edge),
> edit them in the tabbed request editor and save changes or new requests back
> to disk, send them and inspect the response, and sync the folder with git
> from the Source control view (status, commit, pull, push). Environments (and
> so `{{variable}}` values) are not implemented yet.

## Why Missive

- **Files first.** A collection is just a folder. Each request is one readable
  YAML file you can open, grep, review and edit with any tool.
- **Git native.** Share and version collections the same way you share code:
  commit, pull and push from inside Missive, using your existing git setup.
- **Local and private.** Secrets and response history stay on your machine and
  never end up in the repository.

## Planned features

- [x] Open a folder as a workspace and browse its requests
- [x] Create, rename and delete (nested) collection folders, and move requests
      and folders between them with drag and drop
- [x] Request editor: method, URL, query parameters, headers, auth and body
      (JSON, raw text, form-data, `x-www-form-urlencoded`), saved as YAML files
- [x] Sending requests
- [x] Response viewer: status, time, size, headers and a pretty-printed body
- [ ] Collections as folders, with optional folder-level defaults
- [ ] Environments with `{{variable}}` substitution
- [x] Auth helpers: Basic, Bearer token and API key
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

Keys are always written in this order — `name`, `method`, `url`, `params`,
`headers`, `auth`, `body` — and empty sections are left out. Query parameters
use the same `name: value` form as headers. A parameter or header that is
disabled in the editor is kept in the file with a `~` prefix (so names cannot
start with `~`):

```yaml
params:
  page: "1"
  ~debug: "true"     # disabled
```

Auth types are `basic` (`username`, `password`), `bearer` (`token`) and
`apiKey` (`key`, `value`, and `in: query` to send it as a query parameter
instead of a header). Body types are `json`, `text`, `form` and `urlencoded`.
`form` (multipart) and `urlencoded` bodies hold one `name=value` pair per line;
`urlencoded` values are sent as written, so percent-encode them yourself.

New requests are saved as a file named after the request (`Create user` →
`create-user.yaml`, or `create-user-2.yaml` if that name is taken). Renaming a
request renames its file too, unless the file name was chosen by hand. The
explorer lists every `.yaml`/`.yml` file except `missive.yaml`, `_folder.yaml`,
the `environments/` folder and hidden files and folders; files that can't be
read are shown with an error instead of being hidden. Saving a request whose
content didn't change leaves its file untouched. Comments and keys Missive
doesn't know are not preserved when a request is saved with changes.

Folders are collections and can be nested. From the explorer you can create,
rename (F2) and delete them. Drag a request or a folder onto another folder
(or onto empty space in the explorer for the workspace root) to move it there;
moved files keep their names, and nothing is moved if the destination already
has an entry with that name. Open requests follow renamed and moved folders. Folder
names are kept as typed, but must work on Windows, macOS and Linux (no
`/ \ < > : " | ? *`, no leading dot or trailing dot, no reserved names such as
`CON`), and `environments` is reserved at the workspace root. Deleting a folder
removes the requests in it and only goes ahead when the folder holds nothing
else — request files, `_folder.yaml`, `.gitkeep` and OS files such as
`.DS_Store` — so other files you keep there are never deleted. Git doesn't
track empty folders, so a new folder appears in commits once it contains a
request.

An environment file (`environments/dev.yaml`):

```yaml
name: Development
variables:
  baseUrl: http://localhost:5000
```

Secret values such as `apiToken` belong in `.missive/secrets.yaml`, which
Missive keeps out of git. Files are written with stable key order and minimal
noise so diffs stay small and merges stay easy. The format is still evolving.

## Sending requests

**Send** (or Ctrl+Enter) sends the request as it is in the editor, saved or
not; **Cancel** stops it. Requests are sent by the .NET backend, so browser
rules such as CORS don't apply. When sending:

- Enabled query parameters are appended to the URL, which gets `http://` if it
  has no scheme. Disabled parameters and headers are left out.
- `{{variable}}` references, highlighted in the URL and body editors, are
  resolved first. Until environments exist no
  variables are defined, so a request that uses one is not sent and the
  undefined names are listed.
- Auth adds an `Authorization` header (Basic or Bearer) or the API key header or
  query parameter; a header you set yourself takes precedence. JSON, text and
  `urlencoded` bodies get a matching `Content-Type` unless you set one.
- A `User-Agent: Missive` header is added unless you set one. Redirects are
  followed, compressed responses are decompressed and no cookies are kept
  between requests. A request times out after 60 seconds.

The response pane shows the status, the time until the whole body was read, the
body size and the headers. Request and response bodies are shown in the Monaco
editor with syntax highlighting (JSON, XML, HTML, JavaScript, CSS, YAML,
GraphQL and Markdown, chosen from the body type or the response's
`Content-Type`); JSON request bodies are also validated as you type. JSON
responses are pretty-printed (switch to **Raw** for the original text), images
are previewed, and other binary bodies are not shown. Bodies over 20 MB are cut off. Responses are kept in memory per open
request only; nothing is written to the workspace.

## Git sync

When the workspace folder is a git repository, Missive shows which request and
environment files changed, lets you commit them with a message, and pulls and
pushes the current branch. It uses your existing git credentials. Missive never
force-pushes and never resolves conflicts automatically: conflicting files are
reported so you can resolve them (in Missive's editor or your own tools).

Missive runs your installed `git` command-line client, so your git
configuration, hooks and credential helpers apply as usual. In detail:

- **Status** lists changed files inside the workspace folder only (the
  workspace can be a subfolder of a larger repository), plus the branch, its
  upstream and how many commits it is ahead of or behind it. It refreshes when
  you come back to the window.
- **Commit** stages and commits every change in the workspace, or only the files
  you tick in the changes list, and never touches files outside the workspace.
  Files in `.missive/` are never committed, even if they are not git-ignored.
- **Pull** merges the upstream branch into yours; it never rebases. If the merge
  conflicts, nothing is resolved for you: the conflicted files are listed and
  Missive walks you through them (see below).
- **Push** pushes the current branch without force. A branch with no upstream
  yet is published to `origin` (or the only remote) and tracks it from then on.
  If the remote has commits you don't have, the push is rejected and you are
  asked to pull first.

### Resolving merge conflicts

When a pull stops on conflicts, the Source control view shows a merge banner
and marks each conflicted file. **Resolve** opens it side by side, your
version (the current branch) next to the incoming one, and for each file you
choose:

- **Keep this version** — take your side or the incoming side as-is (or keep a
  deletion, if that side deleted the file);
- **Mark as resolved** — after editing the file yourself in any editor to
  combine both sides. Missive refuses while the file still has
  `<<<<<<<` / `=======` / `>>>>>>>` conflict markers.

Once every file is resolved, **Commit all** completes the merge; the commit
message is pre-filled with git's merge message. **Abort merge** abandons the
merge instead (`git merge --abort`) and puts your branch back as it was before
the pull, after asking you to confirm.

Missive reopens the last workspace at the next launch, if its folder still
exists. That choice is stored per user, outside any workspace, in
`settings.json` in the app data folder (`%APPDATA%\Missive` on Windows,
`~/.config/Missive` on Linux and macOS).

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
relative to its executable, not the current working directory, and serves them
to the webview through a custom scheme handler (`app://localhost/`, or an
intercepted `http://localhost/` on Windows) rather than `file://`, because
WebView2 won't run the Angular build's module scripts from `file://`.

## Frontend development

```sh
cd src/Missive.Client
npm ci
npm start
```

Open `http://localhost:4200` for Angular live reload in a browser. Outside the
desktop host there is no .NET backend, so the client talks to an in-memory
sample workspace instead (nothing is written to disk). The desktop host always
loads the bundled frontend; rebuild/restart it to see frontend changes. Routing uses hash URLs and assets use a relative base URL.

## Build and publish

```sh
dotnet build Missive.sln
dotnet publish src/Missive.Desktop -c Release -o artifacts/publish
```

Run the `Missive` executable (`Missive.exe` on Windows) in
`artifacts/publish`. Keep its `wwwroot` and `Assets` directories and native
libraries alongside it. This is a framework-dependent publish: the target machine needs the .NET 10
runtime and the platform webview prerequisites above, but not Node.js.

## Repository layout

- `src/Missive.Desktop`: Photino window, bridge, composition root, and
  build/publish integration.
- `src/Missive.Client`: standalone Angular app with SCSS styles, Tailwind
  CSS, Font Awesome icons, Angular CDK drag and drop and the Monaco editor.
- `src/Missive.Domain`, `src/Missive.Application`, `src/Missive.Infrastructure`:
  backend layers (see [Architecture](#architecture)).
- `tests/Missive.Domain.Tests`, `tests/Missive.Application.Tests`: fast unit
  tests with no I/O.
- `tests/Missive.Infrastructure.Tests`: integration tests against temporary
  folders and git repositories.

Run the backend tests (xUnit, with NSubstitute for mocking) with:

```sh
dotnet test Missive.sln
```

Format the client with Prettier (`npm run format`, or `npm run format:check`
to verify) from `src/Missive.Client`. No other linter is configured yet.

## CI and releases

GitHub Actions runs on every pull request and push to `main`
(`.github/workflows/ci.yml`): it checks client formatting and builds the Angular
app, then builds the solution and runs the tests on Windows, Linux and macOS.

Pushing a version tag publishes desktop builds (`.github/workflows/release.yml`):

```sh
git tag v0.1.0
git push origin v0.1.0
```

Each build is a self-contained publish (no .NET runtime needed on the target
machine; the platform webview prerequisites above still apply) for `win-x64`
(`.zip`), `linux-x64` and `osx-arm64` (`.tar.gz`), attached to a GitHub release
for the tag. Tags with a suffix such as `v0.2.0-beta.1` are marked as
pre-releases. Running the workflow manually produces the same builds as workflow
artifacts without creating a release. The macOS build is not signed or
notarized yet.
