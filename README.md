# Dynpost

Dynpost is a lightweight, cross-platform desktop HTTP client — a simplified take
on Postman. Its difference: your collections are plain YAML files in a folder
you choose, and Dynpost can commit, pull and push that folder with git directly
from the app. No account, no cloud workspace, no proprietary export format.
Homepage: <https://carlescs.github.io/dynpost/>.

With Dynpost you can:

- **Build and send requests** — method, URL, query parameters, headers, auth
  (Basic, Bearer, API key, OAuth 2.0) and bodies (JSON, text, form-data,
  URL-encoded, GraphQL, binary), with `{{variable}}` values from the selected
  environment.
- **Inspect responses** — status, timing breakdown with redirects, headers,
  cookies, and a pretty-printed body with HTML and PDF previews.
- **Organize collections** — nested folders, drag and drop, Markdown docs per
  request, and values captured from responses into variables.
- **Sync with git** — status, diffs, commit, pull, push and branches, with
  clear reporting of merge conflicts, using your existing git setup.
- **Load test** — `*.run.yaml` plans that send requests from concurrent
  virtual users, with live statistics and pass/fail thresholds.
- **Bring your collections** — import from Postman, Insomnia, HAR and
  OpenAPI/Swagger; export for Postman and Insomnia.
- **Work with an AI assistant** — an optional, built-in MCP server lets Claude
  and other AI assistants browse, edit and send your requests (see
  [AI assistants (MCP)](#ai-assistants-mcp)).

Secrets, cookies, tokens and other local state stay in a git-ignored
`.dynpost/` folder and never end up in the repository.

> **Status:** early development — usable day to day, but the file format and
> features may still change. See [Planned features](#planned-features) for
> what is done and what is next.

## Install

Download the file for your system from the
[latest release](https://github.com/carlescs/dynpost/releases/latest):

| System | File | How to install |
|---|---|---|
| Windows (x64) | `Dynpost-win-Setup.exe` | Run it. Dynpost installs for your user (no admin rights) and adds Start menu and desktop shortcuts; uninstall it from *Installed apps*. |
| macOS (Apple silicon) | `Dynpost-osx-Setup.pkg` | Open it and follow the installer. |
| Linux (x64) | `Dynpost.AppImage` | Make it executable (`chmod +x Dynpost.AppImage`) and run it; keep it wherever you like. |

Dynpost checks for a new version at startup and downloads it in the background;
when one is ready, a **Restart to update** button appears in the header. Nothing
is installed until you click it. Builds run from source don't update
themselves.

Dates and times (response history, cookies, commits, branches, load runs) are
shown in your operating system's regional format, including formats you
customized in its settings. On Linux the format follows the `LC_ALL`, `LC_TIME`
or `LANG` locale.

Dynpost needs [git](https://git-scm.com/downloads) for workspace sync, plus the
system webview: Windows 10 and 11 normally include the Microsoft Edge WebView2
Runtime, macOS includes WebKit, and Linux needs GTK 3, WebKitGTK 4.1 and
libnotify (on Ubuntu 24.04:
`sudo apt-get install libgtk-3-0t64 libwebkit2gtk-4.1-0 libnotify4`).

The builds are not code-signed yet, so Windows SmartScreen may warn about an
unrecognized app (*More info* → *Run anyway*), and macOS may refuse to open it
until you allow it in *System Settings* → *Privacy & Security*.

## Why Dynpost

- **Files first.** A collection is just a folder. Each request is one readable
  YAML file you can open, grep, review and edit with any tool.
- **Git native.** Share and version collections the same way you share code:
  commit, pull and push from inside Dynpost, using your existing git setup.
- **Local and private.** Secrets and response history stay on your machine and
  never end up in the repository.

## Planned features

- [x] Open a folder as a workspace and browse its requests
- [x] Create, rename, duplicate and delete (nested) collection folders, and move
      requests and folders between them with drag and drop
- [x] Duplicate requests, switch between recently used workspaces, and remove
      them from recents
- [x] Open the workspace folder in File Explorer (Finder on macOS, the default
      file manager on Linux) from the explorer's "More actions" menu
- [x] Request descriptions in Markdown (a **Docs** tab), imported and exported
- [x] Request editor: method, URL, query parameters, headers, auth and body
      (JSON, raw text, form-data, `x-www-form-urlencoded`, GraphQL), saved as YAML files
- [x] Sending requests
- [x] Response viewer: status, time, size, headers and a pretty-printed body,
      with HTML and PDF previews; copy or save the body, cookies set, a timing
      breakdown with redirects, and the request as sent
- [x] Optional cookie jar per environment (kept out of git)
- [x] Capture values from responses into variables (JSONPath or header, kept
      out of git)
- [x] Keep chosen requests, environments and run plans on this computer only,
      out of git (see [Local-only files](#local-only-files))
- [x] Collections as folders, with folder-level headers, auth and variables that apply to
      everything inside (see [Folder settings](#folder-settings))
- [x] Environments with `{{variable}}` substitution
- [x] Auth helpers: Basic, Bearer token, API key and OAuth 2.0 (client
      credentials, and authorization code with PKCE in the system browser)
- [x] Local response history per request, compared with the latest response
      in a diff view (kept out of git, see [Response history](#response-history))
- [ ] Git sync: changed-file status and diffs, commit, pull, push, branches
      (switch, create, check out, delete), remotes, and clear reporting of
      merge conflicts
- [x] Load runner: `*.run.yaml` plans that send requests from concurrent
      virtual users, with live statistics and pass/fail thresholds (see
      [Load runs](#load-runs))
- [x] Import from Postman, Insomnia, HAR and OpenAPI/Swagger; export for
      Postman and Insomnia
- [x] A built-in MCP server, off by default, so AI assistants can control the
      app (see [AI assistants (MCP)](#ai-assistants-mcp))

### Non-goals

Dynpost deliberately stays small. Out of scope for now: pre-request/test
scripting, functional collection runners, mock servers, cloud accounts and hosted team
workspaces (git is the collaboration layer).

## Workspace format

A workspace is a folder, usually a git repository:

```text
my-api/
├── dynpost.yaml            # workspace metadata: the layout version (`layout: 2`)
├── environments/
│   ├── dev.yaml
│   └── prod.yaml
├── requests/               # every request; a folder in it is a collection
│   ├── health.yaml
│   └── users/
│       ├── _folder.yaml    # optional folder-level defaults (headers, auth, variables)
│       ├── get-user.yaml
│       └── create-user.yaml
├── plans/                  # load run plans, organized in folders
│   └── perf/
│       └── checkout-smoke.run.yaml
├── uploads/                # anything else is yours: body files, data files, notes
│   └── report.pdf
└── .dynpost/               # local only, git-ignored: secrets, cookies, captured values, OAuth 2.0 tokens, history, UI state
```

Requests and their collection folders live in `requests/`, run plans in
`plans/` and environments in `environments/`. Paths in files and in the app are
always relative to the workspace folder (`requests/users/get-user.yaml`), so a
run plan refers to a request as `requests/users/get-user.yaml` and a body file as
`uploads/report.pdf`. Dynpost leaves every other file and folder alone.

### Moving from the old layout

Workspaces made by earlier versions kept requests, collection folders and run
plans anywhere in the workspace folder. The first time Dynpost opens such a
workspace it reorganizes it, and says what it did:

- Request files and `_folder.yaml` files move into `requests/`, keeping their
  folders (`users/get-user.yaml` → `requests/users/get-user.yaml`), and run plans
  move into `plans/` (`perf/smoke.run.yaml` → `plans/perf/smoke.run.yaml`).
  Empty collection folders move too (with their `.gitkeep`), and folders left
  empty are removed.
- A YAML file only counts as a request when it has a `method` or only request
  keys (`name`, `url`, `headers`, …), so other YAML files (CI or compose files,
  OpenAPI documents) stay where they are, as do all other files, such as body
  and data files, so the paths that point at them keep working.
- Run plans are updated to refer to the requests' new paths; only those paths
  change in the file, comments and formatting stay. Response history, saved
  runs and local-only settings follow the moved files.
- Nothing is overwritten: a file whose new place is already taken stays where
  it is and is listed.
- `dynpost.yaml` gets `layout: 2`, so it happens only once, and Dynpost also
  records it there for a workspace that had nothing to move. Nothing is
  committed: the moves show up in **Source control**, ready to review and
  commit. Teammates on an older version of Dynpost should update before they
  pull that commit.

### Moving from Missive

Dynpost was called Missive before. Opening a workspace made under that name
renames its files: `missive.yaml` becomes `dynpost.yaml`, the local
`.missive/` folder becomes `.dynpost/` (secrets, cookies, history and other
local state come along), and the `.gitignore` line for `.missive/` is changed to
`.dynpost/`. A file or folder is left alone when its new name already exists. The
rename of `missive.yaml` and the `.gitignore` change show up in **Source
control**, ready to commit. Until Dynpost first saves its own settings, it
reads the ones Missive kept, such as recent workspaces and the MCP server's
settings.

Dynpost is packaged as a new app, so an installed Missive isn't replaced by an
update: install Dynpost from the
[latest release](https://github.com/carlescs/dynpost/releases/latest), then
uninstall Missive.

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
`headers`, `auth`, `body`, `capture`, `description` — and empty sections are
left out. Query parameters
use the same `name: value` form as headers. A parameter or header that is
disabled in the editor is kept in the file with a `~` prefix (so names cannot
start with `~`):

```yaml
params:
  page: "1"
  ~debug: "true"     # disabled
```

A request without `auth` inherits the auth of its folders (see
[Folder settings](#folder-settings)); `auth: { type: none }` sends no auth even
when a folder sets one. The other auth types are `basic` (`username`, `password`), `bearer` (`token`),
`apiKey` (`key`, `value`, and `in: query` to send it as a query parameter
instead of a header) and `oauth2` (see [OAuth 2.0](#oauth-20)). Body types are `json`, `text`, `form`, `urlencoded`, `graphql` and `binary`.
`graphql` bodies store a GraphQL `query` and JSON `variables` separately and are sent as JSON POSTs.
`form` (multipart) and `urlencoded` bodies hold a list of `fields`, sent in
order. Each field has a `name` and either a `value` (text) or a `file` (a path);
then, when set, `fileName` (the file name sent for a file part, instead of the
file's own), `contentType` (a part's content type; for files it otherwise comes
from the extension), `encode: true` (`urlencoded` only: percent-encode the name
and value; without it they are sent as written) and `disabled: true` (kept but
not sent). A name may repeat, for example to send several files under one field.
A `binary` body stores a file path in `content` and sends that file as the whole
body. Relative paths are resolved from the workspace root and must stay inside
it; absolute paths are accepted but make requests non-portable. Use the request
editor's file pickers to insert workspace-relative paths where possible.

```yaml
body:
  type: graphql
  query: |
    query User($id: ID!) {
      user(id: $id) { name }
    }
  variables: |
    { "id": "{{userId}}" }
```

```yaml
body:
  type: form
  fields:
    - name: metadata
      value: '{"title": "Report"}'
      contentType: application/json
    - name: file
      file: uploads/report.pdf
      fileName: q3-report.pdf
    - name: debug
      value: "true"
      disabled: true
```

```yaml
body:
  type: urlencoded
  fields:
    - name: email
      value: ada@example.com
      encode: true          # sent as email=ada%40example.com
    - name: remember
      value: "1"
```

Older files kept these bodies as text in `content`, one `name=value` per line
(`name=@path;type=image/png` for a file). They still load and send the same
way, and are only rewritten as `fields` when you edit the body.

In the editor, `form` and `urlencoded` bodies are a table: one row per field
with a checkbox to send it or not, its name, its kind (text or file) and value,
and for multipart parts a content type and file name. Drag rows to reorder them;
**Choose…** and **Add files…** pick one or several files (several files become
rows under the same name) and each file row shows the file's name and size, or a
warning when it is missing, outside the workspace or an absolute path. **Bulk
edit** switches to the text form (`name=value` per line, `~` before a disabled
field, `name=@path;type=…;filename=…` for files) and back. The editor also shows
the `Content-Type` the body is sent with: Dynpost generates
`multipart/form-data; boundary=…` for `form` bodies and replaces a
`Content-Type` header you set, which it points out.

`capture` stores values from a successful response in variables, so a login
request can hand its token to the requests after it — declaratively, without
scripts:

```yaml
capture:
  token: $.access_token    # a JSONPath into the JSON body
  etag: header:ETag        # a response header
```

See [Capturing response values](#capturing-response-values).

`description` holds notes about the request in Markdown, edited in the request
editor's **Docs** tab (with an **Edit** and a **Preview** mode). It is written
last, as a block, so the docs live next to the request in git:

```yaml
description: |
  Creates a user. Needs the `admin` role.

  Returns `409` when the email is taken.
```

The preview renders GitHub-flavored Markdown but shows raw HTML as text and
images as their alt text, and links are not followed, so nothing is loaded from
the network.

New requests are saved as a file named after the request (`Create user` →
`create-user.yaml`, or `create-user-2.yaml` if that name is taken). Renaming a
request renames its file too, unless the file name was chosen by hand. The
explorer lists every `.yaml`/`.yml` file in `requests/` except `_folder.yaml`,
`*.run.yaml` plans and hidden files and folders; files that can't be read are
shown with an error instead of being hidden. Saving a request whose
content didn't change leaves its file untouched. Comments and keys Dynpost
doesn't know are not preserved when a request is saved with changes.

Folders are collections and can be nested. From the explorer you can create,
rename (F2) and delete them. Hovering a folder shows buttons for a new request
and a new sub-folder in it; its "…" button (or a right-click) opens a menu with
everything else: import into it, its settings, rename, duplicate and delete. Drag a request or a folder onto another folder
(or onto the "Drop here to move to the top level" zone that appears at the
bottom of the explorer, or its empty space) to move it there. While dragging, the
destination folder is outlined and marked "Move here", the dragged chip says where
the entry would go (or why it can't), and a collapsed folder opens when you hold
the entry over it for a moment; moved files keep their names, and nothing is moved if the destination already
has an entry with that name. Open requests follow renamed and moved folders. Folder
names are kept as typed, but must work on Windows, macOS and Linux (no
`/ \ < > : " | ? *`, no leading dot or trailing dot, no reserved names such as
`CON`). Deleting a folder
removes the requests in it and only goes ahead when the folder holds nothing
else — request files, `_folder.yaml`, `.gitkeep` and OS files such as
`.DS_Store` — so other files you keep there are never deleted. Links, and
names Dynpost can't handle as workspace paths (such as `notes:2026.txt` or a
name with a backslash on Linux and macOS), also block the delete. Git doesn't
track empty folders, so a new folder appears in commits once it contains a
request.

Right-click a request or a folder in the explorer to duplicate it. A copied
request is named `… copy` (`Get user` → `Get user copy`) and saved next to the
original as `get-user-copy.yaml` (or `get-user-copy-2.yaml` if that is taken).
A copied folder is named `… copy` (then `… copy 2`, `… copy 3`, …) and holds a
byte-for-byte copy of everything in the original. A folder holding symbolic
links, or names that can't be used on every system (such as `a:b.txt` on
Linux), is not copied, so a copy is never silently incomplete. Duplicating only
writes new files and never changes existing ones.

### Folder settings

A collection folder can set headers, auth and variables for every request and
folder inside it, in an optional `_folder.yaml` (and `requests/_folder.yaml` sets
them for the whole workspace). It uses the same `headers` and `auth` keys as a
request file, in that order, followed by `variables`, written like `headers`; a
folder that sets nothing has no file:

```yaml
# requests/users/_folder.yaml
headers:
  Accept: application/json
  X-Team: core
auth:
  type: bearer
  token: "{{apiToken}}"
variables:
  usersUrl: "{{baseUrl}}/users"
  pageSize: "20"
```

The more specific level always wins:

- **Headers** add up from `requests/` down to the request's folder. A sub-folder's
  header replaces a parent's header of the same name (ignoring case), and a
  request's own header replaces both. Inherited headers are sent before the
  request's own. Disabled headers (`~Name`) are not sent, so they don't replace
  anything either.
- **Auth** comes from the closest level that sets one: the request's own auth,
  else its folder's, else that folder's parent's, and so on. A folder or request
  with `auth: { type: none }` stops it there: it and everything below that
  inherits it is sent without auth. When no level sets auth, the request is sent
  without.
- **Variables** add up like headers (names are case-sensitive): a sub-folder's
  variable replaces a parent's of the same name. They are defaults: the selected
  environment's variables (and the values captured for it) win over them, and in
  load runs the CSV row wins over both. A folder variable's value can use
  environment variables and other folder variables (`{{baseUrl}}` above);
  variables that refer to each other in a loop are refused when sending.
  Disabled variables (`~name`) are not used, and folder variables can't be
  secret: `_folder.yaml` is shared through git.

Folder settings apply wherever a request is sent: from the editor (an unsaved
request uses the folder it will be saved in), by load runs, and through the MCP
server. Exports write each request with the headers and auth it inherits, since
other tools' formats don't have folder headers; folder variables are not
exported (the export says so), so their requests keep their `{{references}}`.

Open a folder's settings with **Folder settings** in its menu (the "…" button on
its row, or a right-click), or **Settings for all requests** in the explorer's
"More actions" menu (for `requests/` itself). They open in a tab with a
**Headers**, an **Auth** and a **Variables** tab, which also list what the folder
itself inherits from the folders above. In the request editor, `{{references}}`
to its folders' variables are highlighted as defined, and their hover says which
folder sets them. The **Headers** tab lists the
headers inherited from folders below the request's own, struck through when the
request replaces them, and **Inherit from folder** (the default for new requests)
on the **Auth** tab shows which auth applies and which folder sets it.

An environment file (`environments/dev.yaml`):

```yaml
name: Development
cookieJar: true
variables:
  host: localhost:5000
  baseUrl: http://{{host}}
secrets:
  - apiToken
```

Keys are written in the order `name`, `cookieJar`, `variables`, `secrets`, and
empty sections and defaults (`cookieJar: false`) are left out. Variable names can't contain spaces or braces, and a
value can use other variables (`{{host}}` above); variables that refer to each
other in a loop are refused. `secrets` lists only the *names* of secret
variables: their values stay on each computer, in `.dynpost/secrets.yaml`, so
they are never committed. After cloning or pulling, each person enters their own
secret values in Dynpost:

```yaml
# .dynpost/secrets.yaml — local only
dev.yaml:
  apiToken: s3cr3t
```

`cookieJar: true` makes requests sent with the environment keep cookies between
them (see [Sending requests](#sending-requests)); the cookies themselves are
local too, in `.dynpost/cookies.json`:

```json
{
  "dev.yaml": [
    { "name": "session", "value": "abc123", "domain": "localhost", "path": "/", "httpOnly": true }
  ]
}
```

Values that requests capture from responses are local as well, in
`.dynpost/captures.yaml`, with the same layout as `secrets.yaml`.

A `domain` with a leading dot (`.example.com`) is a cookie set with a `Domain`
attribute, sent to that domain and its subdomains; one without (`localhost`
above) was set without one and is sent to that host only.

The selected environment is remembered per workspace in `.dynpost/state.yaml`.
Whenever Dynpost writes to `.dynpost/`, it makes sure the workspace's
`.gitignore` contains `.dynpost/` (creating the file if needed), and git
operations skip `.dynpost/` regardless. Files are written with stable key order
and minimal noise so diffs stay small and merges stay easy. The format is still
evolving.

## Environments

Pick the environment requests are sent with from the header, or from the
**Environments** view in the activity bar, which also lists every file in
`environments/` and creates new ones. Clicking an environment there selects it;
its pen icon opens it in an editor tab, next to the open requests: its name, and a table of variables, each
of which can be marked **Secret**. New environments are saved as a file named after them
(`Staging` → `staging.yaml`); renaming one renames its file too, unless the file
name was chosen by hand, and its secret values, cookies, captured values and
selection follow. Deleting an environment also removes its stored secret values,
cookies, captured values and OAuth 2.0 tokens.

**Keep cookies between requests** turns on the environment's cookie jar (off by
default). Cookies that responses set, including redirects on the way, are then
kept and sent with later requests using the environment, following the usual
domain, path, expiry and `Secure` rules, so APIs that log in with a session
cookie work as in a browser. A `Cookie` header you set yourself is still sent,
with the jar's cookies added after it. The editor lists the kept cookies, where
each can be deleted, or all cleared at once; turning the jar off and saving
forgets them.

In the request editor, `{{variable}}` references are highlighted in the URL and
body: in violet when the selected environment defines them, in red when it
doesn't. Hover over one to see its value (secret values are not shown).

## Keyboard shortcuts

On macOS, use Cmd wherever Ctrl is listed. **F1**, or *Keyboard shortcuts* in
the settings menu, lists them in the app.

| Shortcut | Action |
| --- | --- |
| Ctrl+Enter | Send the request |
| Ctrl+S | Save the active request, environment or run plan |
| Ctrl+N | New request |
| Ctrl+O | Open a workspace folder |
| Ctrl+W, Ctrl+F4 | Close the open conflict, commit or diff, or else the active tab (asking first if it has unsaved changes) |
| Ctrl+Tab, Ctrl+PageDown | Next tab |
| Ctrl+Shift+Tab, Ctrl+PageUp | Previous tab |
| Ctrl+B | Show or hide the sidebar |
| Ctrl+1 … Ctrl+4 | Explorer, Environments, Load runs, Source control |
| F1 | Keyboard shortcuts |
| Ctrl+Q | Quit Dynpost (asking first if tabs have unsaved changes or load runs are running; desktop app only) |

Shortcuts don't apply while a dialog is open.

Right-click an editor tab for **Close**, **Close others**, **Close to the
right**, **Close saved** and **Close all**; closing several tabs asks once
before discarding unsaved changes. A middle click closes a tab, and when the
tabs don't fit, the mouse wheel scrolls them sideways.

The views in the activity bar (the icons on the left) can be put in any order:
drag an icon to its new place, or focus it and press Alt+Up or Alt+Down. The
order is remembered in your user settings, and the small reset icon at the
bottom of the activity bar puts the views back in their default order. Ctrl+1 … Ctrl+4 keep opening the
same views whatever their order.

## Sending requests

**Send** (or Ctrl+Enter) sends the request as it is in the editor, saved or
not; **Cancel** stops it. Requests are sent by the .NET backend, so browser
rules such as CORS don't apply. When sending:

- Enabled query parameters are appended to the URL, which gets `http://` if it
  has no scheme. Disabled parameters and headers are left out.
- `{{variable}}` references in the URL, parameters, headers, auth and body are
  resolved first with the selected environment. A request that uses a variable
  the environment doesn't define (or a secret with no value on this computer)
  is not sent; the undefined names are listed with a hint on where to set them.
- The headers and auth of the request's folders are applied (see
  [Folder settings](#folder-settings)).
- Auth adds an `Authorization` header (Basic or Bearer) or the API key header or
  query parameter; a header you set yourself (or a folder sets) takes precedence. OAuth 2.0 first
  gets a token if it needs one (see [OAuth 2.0](#oauth-20)). JSON, text and
  `urlencoded` bodies get a matching `Content-Type` unless you set one;
  multipart `form` bodies always get `multipart/form-data` with the boundary. A
  `form` body whose file field has no file, or whose file is outside the
  workspace, is not sent.
- A `User-Agent: Dynpost` header is added unless you set one. Compressed
  responses (gzip, deflate, Brotli and Zstandard) are decompressed. No cookies are kept between requests unless the
  environment's cookie jar is on. A request times out after 60 seconds.
- Up to 20 redirects are followed, never from `https` to `http`. A 303, or a
  301/302 answering a POST, continues as a GET without a body; 307 and 308 keep
  the method and body. The `Authorization` header is not sent on to the new
  location.

The response pane shows the status, the time until the whole body was read, the
body size and these tabs:

- **Body**, described below. **Copy** copies a text body as shown, and **Save**
  writes the body to a file exactly as it was received (the bytes, not the
  decoded text), suggesting a name from `Content-Disposition`, or from the URL
  and `Content-Type` (`/users` answered with JSON → `users.json`).
- **Headers**, in the order received.
- **Cookies**: the cookies the response (and any redirect before it) set, with
  their domain, path, expiry and flags.
- **Timing**: where the time went — DNS lookup, TCP connect, TLS handshake,
  waiting for the first byte, and download — and the redirects followed, each
  with its status, URL and `Location`. Connection phases are left out when no
  new connection was needed.
- **Request**: the request as it went on the wire after redirects: the resolved
  URL and the final headers, with `{{variables}}` substituted and including
  those Dynpost and the connection add (`Host`, `User-Agent`,
  `Accept-Encoding`, `Content-Type`, `Content-Length`, `Cookie`, auth), one
  line per header as sent. The tab shows how many headers were sent. When the
  request was redirected, every request is listed in order: `Authorization`
  is not sent on to a redirected location, so only the first one carries it.
  Credentials (`Authorization`, the API key) and the values of secret variables
  are masked. A multipart body is summarised part by part (name, file name,
  content type and size) rather than shown.

Request and response bodies are shown in the Monaco
editor with syntax highlighting (JSON, XML, HTML, JavaScript, CSS, YAML,
GraphQL and Markdown, chosen from the body type or the response's
`Content-Type`); JSON request bodies are also validated as you type. JSON
responses are pretty-printed (switch to **Raw** for the original text). HTML
responses are previewed rendered in a sandboxed frame that runs no scripts and
loads nothing from the network
(switch to **Source** for the markup), PDFs are rendered with pdf.js (fit to width
or zoomed, with selectable text), images are previewed, and other binary bodies
are not shown (use **Save** to get them). Bodies over 20 MB are cut off. Responses are kept in memory per open
request only; nothing is written to the workspace. The bodies of the latest
responses (up to 64 MB in all) are kept by the backend so they can be saved.

## OAuth 2.0

Pick **OAuth 2.0** on a request's **Auth** tab to have Dynpost get the access
token for you and send it as a Bearer token — the "get a token first" step
other clients need a script for. Two grants are supported:

- **Client credentials**: the app signs in as itself with its client id and
  secret. Dynpost asks the token endpoint for a token the first time you send,
  and again whenever it has expired.
- **Authorization code (PKCE)**: a user signs in. **Get new token** opens the
  sign-in page in your system browser and waits (up to 5 minutes, or until you
  click **Cancel**) for the authorization server to redirect back to Dynpost on
  a loopback address — `http://127.0.0.1:<free port>/callback`, or the
  **Redirect URL** you set if the client only allows a registered one (it must
  be an `http` URL on `127.0.0.1`, `localhost` or `[::1]`). PKCE (`S256`) and a
  random `state` are always used. Sending without a token asks you to sign in;
  an expired token is renewed with its refresh token when there is one.

```yaml
auth:
  type: oauth2
  grant: authorizationCode     # or clientCredentials
  authUrl: https://auth.example.com/authorize
  tokenUrl: https://auth.example.com/oauth/token
  redirectUri: http://127.0.0.1:8765/callback   # optional
  clientId: dynpost
  clientSecret: "{{clientSecret}}"
  scope: openid profile
  audience: https://api.example.com             # optional, for Auth0 and similar
  credentials: body            # optional: send the id and secret as form fields
```

Keys are written in that order, and empty ones and `credentials: header` (the
default, a Basic `Authorization` header to the token endpoint) are left out;
`authUrl` and `redirectUri` only apply to the authorization code grant. Every
value can use `{{variables}}`: keep the client secret in a **secret** variable
of the environment so it never reaches the request file or git.

Tokens are kept on this computer only, per environment, in
`.dynpost/oauth2-tokens.json` (with their refresh tokens and expiry), and are
shared by every request with the same token URL, authorization URL, client id,
scope and audience. They follow a renamed environment and are deleted with it.
The Auth tab shows whether there is a token and when it expires; **Clear**
forgets it. Like other credentials, the token is masked in the response's
**Request** tab.

## Capturing response values

A request's **Capture** tab lists variables to fill from its response: each
row names a variable and where its value comes from, either a JSONPath into the
JSON body or a header:

| From | Captures |
| --- | --- |
| `$.access_token` | the `access_token` property of the body |
| `$.data.items[0].id`, `$['odd key']` | nested properties and array items |
| `$` | the whole body |
| `header:ETag` | the first `ETag` header (names are case-insensitive) |

Only that subset of JSONPath is supported — no wildcards, filters or recursive
descent — so a capture always names a single value. Strings are captured as
they are, numbers and booleans as written, and objects and arrays as compact
JSON; a `null` or missing value captures nothing.

After a successful (2xx) response, the captured values are stored for the
selected environment, on this computer only (`.dynpost/captures.yaml`), never in
tracked files. They take precedence over the environment's own values of the
same variables, so the next requests use `{{token}}` straight away; hovering a
reference says when its value was captured, and a captured value of a secret
variable is masked like any secret. Responses with another status capture
nothing, so an error never overwrites a good token, and without a selected
environment nothing is stored. The response's **Captures** tab shows what each
rule captured, or why it captured nothing. The environment editor lists the
captured values, where each can be forgotten (bringing back the environment's
value), or all at once.

Captures are not imported or exported: other tools express them as scripts.

## Response history

Every response to a saved request is kept on this computer, in the workspace's
git-ignored `.dynpost/history/` folder. The **History** button in the response
panel lists the responses kept for the request, newest first, with their
status, time, duration and size. Select one to see its body (pretty-printed
when it is JSON) or its status line and headers, and tick **Compare with
current response** to see the differences from the response in the viewer in a
diff editor. Responses can be deleted one by one, or all at once with **Clear**.
Requests that have never been saved have no history.

The history follows its request when the request is renamed (with its file),
moved, or its folder is renamed or moved, and is deleted with the request or the
folder. It is kept per request file, so a file renamed or moved outside Dynpost
starts a new history.

What is written to disk is the response as shown, with the request as sent
(credentials already hidden). Cookie values (in `Set-Cookie` headers and the
cookies list) and the values of the environment's secret variables are hidden
too. **Response history…** in the settings menu turns the history off, and sets
how many responses are kept per request (20 by default) and the largest body
kept (1 MB by default): longer text bodies are cut off and larger binary bodies
are left out. When a workspace's history passes 256 MB, its oldest responses
are dropped. These settings are per user, in Dynpost's own settings file.

`.dynpost/history/index.json` lists the entries of each request file; each
response is a JSON file next to it, named after its entry id:

```json
{
  "requests/users/get-user.yaml": [
    {
      "id": "20261107T120000000-1a2b3c4d",
      "sentAt": "2026-11-07T12:00:00+00:00",
      "method": "GET",
      "url": "https://api.example.com/users/1",
      "status": 200,
      "statusText": "OK",
      "durationMs": 120,
      "size": 85,
      "contentType": "application/json",
      "bytes": 1460,
      "bodyOmitted": false
    }
  ]
}
```

## Load runs

A load run sends a chosen set of requests repeatedly from several concurrent
virtual users and reports latency percentiles, throughput and errors. The plan
is a git-tracked `*.run.yaml` file (open it from the **Load runs** view in an
editor tab, next to the open requests and environments, and edit it with a
visual form or as YAML text; the form keeps comments and unknown keys). Each open
plan follows its own run, so several plans can run at once; a running plan's tab
can't be closed until it stops. Results
are saved locally under `.dynpost/runs/` and never committed. Runs use the
environment selected in the app, or the plan's own `environments` when it lists
some, and send no cookies. Like requests, a plan whose file is
named after it (`checkout-smoke.run.yaml` for "Checkout smoke") is renamed
along with it, and its earlier runs follow; files with hand-picked names keep
them. Deleting a plan (the trash button next to it in the list or in its
editor) removes its file and its saved runs, after asking; a plan can't be
deleted while it is running.

Plans live in the workspace's `plans/` folder and are organized in folders inside it, like
requests in `requests/`. The **Load runs** view shows them as a tree. Its header makes a new plan or
folder at the top; hover a folder to make a plan or a sub-folder in it, rename it (F2) or delete
it (Delete), and right-click it to duplicate it as "… copy". Drag a plan or a folder onto
another folder, or onto the drop zone at the bottom (or empty space) for the top level, to move
it; the drag shows the destination as in the explorer. A plan keeps its earlier
runs when it or its folder is renamed or moved; deleting a folder deletes its plans and their
runs, after asking. A folder with a running plan can't be renamed, moved or deleted.

The visual form lists the requests as steps you can drag into order (and picks
their order, with each request's weight and share for a random pick), sums up the
load (users × iterations × requests) and sketches the users over time with the
ramp-up. Next to it, the results dashboard follows the run live: headline
numbers, requests per second and average latency over time (hover a second for
its numbers), each threshold with its verdict, latency statistics (a switch
shows them per request, per environment when the plan lists environments, or
only for the whole run), status codes, error reasons, the failed and slowest requests (expand
one for its data row, body file and response) and the earlier runs of the plan.
Hover an earlier run and press its trash button to delete it with its request
log and saved responses, or press **Clear** above the list to delete them all. To delete several,
tick their checkboxes (shift-click ticks a range, the box above the list ticks every run) and press
**Delete** above the list. Deleting always asks first, keeps the plan itself, and waits until a
running plan stops.

The **Requests** tab next to **Dashboard** (or the **All requests** link on the
dashboard) shows a table of every request the run sent, in the order they
finished: time into the run, virtual user, iteration, method and URL, status,
latency and result. Click a row for its data row, body file, failure and a preview of the
response, and filter to the failed requests. The table scrolls through the whole
log: only the rows in view are rendered, and the log is read 100 requests at a
time as you scroll to them. It follows a live run too. The first 10,000 requests of a run are kept, in
`.dynpost/runs/samples/<run id>.json`.

The response preview keeps only the start of each body. To keep whole responses,
turn on **Save whole responses** in the plan (`saveResponses: true`): the body
of each logged request is written to
`.dynpost/runs/responses/<run id>/<n>.<ext>` (named by its number in the run,
with an extension for its media type, such as `12.json`), and its row shows
where. A saved PDF response opens in the PDF previewer from its row; the
previewer, here and in the response viewer, has a maximize button that makes it
fill the window (press it again or Esc to restore). Saved responses are
deleted with the plan's runs. Each zone of the editor, the plan and
the results, has a maximize button that gives it the full width; press it
again to restore the side-by-side layout.

```yaml
name: Checkout smoke
requests:
  - requests/shop/list-products.yaml
  - requests/shop/create-order.yaml
  - requests/shop/cancel-order.yaml
disabled:            # listed requests that are not sent (kept for later)
  - requests/shop/cancel-order.yaml
order: random        # sequential (default), shuffle, random or roundRobin
weights:             # with random: relative chance of each request (default 1)
  requests/shop/list-products.yaml: 4
environments:        # optional: run with these, not the environment selected in the app
  - environments/staging.yaml
  - environments/load.yaml
disabledEnvironments:  # listed environments that are not used (kept for later)
  - environments/staging.yaml
environmentOrder: random  # like order: sequential (default), shuffle, random or roundRobin
environmentWeights:  # with random: relative chance of each environment (default 1)
  environments/load.yaml: 3
load:
  threads: 10        # concurrent virtual users
  iterations: 50     # per thread; or `duration: 2m`
  rampUp: 10s
  thinkTime: 100ms   # or { min: 50ms, max: 300ms }
  maxRps: 200
data:
  file: users.csv    # each row's columns become {{variables}}
  mode: perThread    # sequential (default), random or perThread
  onEof: stop        # recycle (default) or stop
bodies:              # per request: send a file of a folder as its body
  requests/shop/create-order.yaml:
    folder: perf/orders
    pattern: "*.json"  # optional: only files whose names match
    mode: random       # sequential (default), random or perThread
    onEof: recycle     # recycle (default) or stop
  mode: perThread    # or shared
  onEof: wrap        # or stop
saveResponses: true  # keep each logged response body in .dynpost/runs/responses/
assert:
  "*":
    status: 200-299
    maxTime: 800ms
thresholds:
  p95: 500ms
  errorRate: 1%
  throughput: 50
```

`order` sets how each virtual user takes the requests in an iteration:
`sequential` sends them all in the listed order, `shuffle` sends them all in a
new random order, `random` sends one request picked at random (more often the
higher its `weight`; a weight of 0 never picks it), and `roundRobin` sends the
next request in the list, with each user starting at a different one so the
requests are spread evenly. With `random` and `roundRobin` an iteration is one
request, so `iterations` counts requests per user.

`environments` lists the environments the plan runs with, whichever one is
selected in the app; leave it out to use the selected one. `environmentOrder`
takes them the way `order` takes requests, once per iteration of each virtual
user, and each environment taken sends the iteration's requests (picked by
`order`, with the same data row): `sequential` sends them with every environment
in the listed order, `shuffle` with every environment in a new random order,
`random` with one environment picked by its `environmentWeights` weight (0
never picks it), and `roundRobin` with the next environment, each user starting
at a different one. So with `sequential` or `shuffle`, an iteration sends its
requests once per environment, and the load summary counts them. In the visual
form, the **Environments** card lists them like the request steps (add one from
the workspace, drag to reorder, pick the order, set weights for a random pick).
`disabledEnvironments` switches environments off like `disabled` does requests:
they keep their place and weight, but runs don't use them (their files don't
even need to exist), and at least one must stay enabled (remove them all to use
the selected environment instead). In the visual form, each environment has a
switch to turn it on or off. Each request in the run's log shows the
environment it was sent with, and the dashboard shows the statistics of each
environment (requests, requests per second, failures and latency spread) above
those of each request, which combine every environment. A plan that lists an
enabled environment which no longer exists can't start (the form flags it);
like request paths, the references aren't updated when an environment file is
renamed.

`disabled` turns requests off without taking them out of the plan: they keep
their place, assertions and weight, but runs skip them (their files don't even
need to exist), and at least one request must stay enabled. In the visual form,
each step has a switch to turn it on or off.

`data` and `bodies` share their selection modes: `sequential` hands out the
entries in order from one cursor shared by every user, `random` picks one each
time, and `perThread` deals them out in turns so each user gets its own. When a
`sequential` or `perThread` source runs out, `recycle` starts again from the top
and `stop` ends that user. A CSV row is picked once per iteration and its columns
override the environment's variables.

With `bodies`, each time a request is sent its body is replaced by one file of
the folder (files directly in it, hidden ones skipped, in ordinal order of their
names, so `sequential` picks the same order on every machine; `pattern` filters
them with `*` and `?`). The request keeps its body type and so its
`Content-Type`: `json`, `text`, `graphql` (the file is the query) and
`urlencoded` requests use the file's UTF-8 text, with `{{variables}}` resolved
(CSV columns included), and `binary` requests send the file as is. Requests
with no body or a multipart `form` body can't take one from a folder. The files
are read once when the run starts (at most 10 MB each); an empty or missing
folder stops the run from starting. Each request of the run records the body
file it sent.

Without a `status` assertion, a response with status 400 or higher counts as an
error. A plan can use up to 5000 threads (each keeps at most one connection busy
at a time); a run over 200 threads needs confirmation. Not yet supported:
CSV/JSON/HTML export, comparing runs, per-thread cookie jars and folder bodies
for multipart `form` requests.

## Import and export

Dynpost doesn't need its own exchange format — the workspace *is* plain files —
but you can bring requests in from other tools and take them out again.

**Import** (the import button in the explorer header, or **Import into this folder…** in a
folder's menu) reads a file and recognizes its format from the
content, in JSON or YAML:

| Format | What is imported |
| --- | --- |
| Postman collection v2.0 / v2.1 | Folders, requests, auth (inherited from folders and the collection, OAuth 2.0 included), bodies; collection variables become an environment. Path variables (`/users/:id`) become their value, or `{{id}}` if they have none. |
| Postman environment | An environment; `secret` variables stay secret (their values go to `.dynpost/secrets.yaml`). |
| Insomnia export v4 | Folders, requests (with their auth, OAuth 2.0 included) and environments: each sub-environment becomes an environment that includes the base environment's variables. `{{ _.name }}` references become `{{name}}`. |
| HAR 1.2 (browser dev tools) | One request per entry, in a folder per host, without connection headers such as `Host`. |
| OpenAPI 3.x / Swagger 2.0 | One request per operation, in a folder per tag, with example values for parameters and an example JSON body built from the schema. Each server becomes an environment defining `{{baseUrl}}`; security schemes become Basic, Bearer, API key or OAuth 2.0 auth using secret `{{username}}`/`{{password}}`, `{{token}}`, `{{apiKey}}` or `{{clientId}}`/`{{clientSecret}}` variables, which you fill in (OAuth 2.0 schemes use their client credentials or authorization code flow and the scopes the operation requires; others get a Bearer `{{token}}`). Path parameters without an example become `{{variables}}`. |

OAuth 2.0 settings whose redirect URL is a web page (such as Postman's own
callback) get Dynpost's automatic loopback redirect instead, which the OAuth
client may need to allow; this is listed after the import.

Request descriptions are imported from Postman and Insomnia, and from an
OpenAPI operation's `description` (its `summary` names the request).

Form-data and urlencoded bodies are imported as fields, keeping disabled fields
(disabled), file fields with their path and content type, and several files
under one Postman field as one row each. Urlencoded values are stored decoded
with `encode: true` when they need percent-encoding. Exports write the same
fields back, file fields as files.

Imported requests are saved as ordinary request files in a new folder named
after the collection (`Shop`, or `Shop 2` if that name is taken), and
environments as new environment files; nothing that already exists is changed.
What Dynpost can't represent is skipped and listed after the import: other auth
types (digest, OAuth 1, the OAuth 2 implicit and password grants, …), file uploads, scripts, and HTTP methods other than
`GET`, `POST`, `PUT`, `PATCH`, `DELETE`, `HEAD` and `OPTIONS`. Repeated query
parameters or headers keep their first value. GraphQL bodies are kept as a
separate query and variables object and can be sent and edited without embedding
the query in JSON.

**Export** (the export button in the explorer header) writes the whole
workspace to a file you choose:

- **Postman collection v2.1** (`.postman_collection.json`), which Postman,
  Insomnia, Bruno, Hoppscotch and most other clients import. Each environment
  is written next to it as a Postman environment file
  (`dev.postman_environment.json`; a name that is taken gets a number).
- **Insomnia v4** (`.insomnia.json`), a single file with the requests and the
  environments, using Insomnia's `{{ _.name }}` references.

Each request is exported with the headers and auth it inherits from its folders
(see [Folder settings](#folder-settings)); folder variables are not exported. Request descriptions are exported too. Untick **Include environments** to
export only the requests. Secret values are
never exported: secret variables are exported with empty values (as `secret`
variables in Postman). Request files that can't be read are skipped and
reported. Exporting the same workspace again produces the same file, so
exports diff cleanly.

## Git sync

When the workspace folder is a git repository, Dynpost shows which request and
environment files changed, lets you commit them with a message, pulls and
pushes the current branch, and switches, creates, merges and deletes branches (local or on the remote). It uses your existing git credentials. Dynpost never
force-pushes and never resolves conflicts automatically: conflicting files are
reported so you can resolve them (in Dynpost's editor or your own tools).

Dynpost runs your installed `git` command-line client, so your git
configuration, hooks and credential helpers apply as usual. In detail:

- **Initialize**: if the workspace folder is not inside a git repository, the
  Source control view offers to create one there (`git init`, using your
  default branch name) and adds `.dynpost/` to the folder's `.gitignore`,
  creating the file if needed. Add a remote (see **Remotes** below) to push it.
- **Status** lists changed files inside the workspace folder only (the
  workspace can be a subfolder of a larger repository), plus the branch, its
  upstream and how many commits it is ahead of or behind it. It updates by
  itself as files change — saved in Dynpost, edited in another program, or
  committed, staged or checked out with other git tools (including a workspace
  inside a larger repository or a linked worktree) — and again when you come
  back to the window; changes under `.dynpost/` don't count. An open diff
  follows the file as it changes. An upstream branch that was deleted on the
  remote is shown as such.
- **Fetch** (the cloud button) checks the remotes for new commits without
  changing your files (`git fetch --all --prune`), so the number of commits to
  pull is current.
- **Diff**: click a changed file to see what changed since the last commit.
  A request file can be shown as a **Request** — laid out like the request
  editor, read-only, with the changed name, method, URL, params, headers, auth
  fields, body and description marked — or as **Text**, its YAML side by side (or inline).
  Other files, and request files that can't be read as requests, are shown as
  text; binary files are not shown. Viewing a diff never stages anything.
- **Commit** stages and commits every change in the workspace, or only the files
  you tick in the changes list, and never touches files outside the workspace.
  Files in `.dynpost/` are never committed, even if they are not git-ignored.
  Git only sees saved files, so the view warns when open tabs have unsaved
  changes and offers **Save all**.
- **Local-only files** are left out of git; see below.
- **Identity**: if git has no name and email to record on commits
  (`user.name`, `user.email`), the view asks for them and saves them for all
  your repositories (`git config --global`) or, if you untick that, for this
  repository only.
- **Changes view**: the changes list can be shown as a flat list or as a
  folder tree.
- **Discard** (the undo button on a row, or *Discard selected* / *Discard all*)
  throws away uncommitted changes after a confirmation: edits and deletions go
  back to the last commit, and new files are deleted. It cannot be undone and is
  unavailable during a merge.
- **Pull** merges the upstream branch into yours; it never rebases. If the merge
  conflicts, nothing is resolved for you: the conflicted files are listed and
  Dynpost walks you through them (see below). A branch with no upstream yet
  pulls from the branch of the same name on `origin` (or the only remote) and
  tracks it from then on — so a repository you initialized in Dynpost can be
  joined with an existing remote one, even though their histories are
  unrelated. A pull that would overwrite uncommitted changes is refused and
  names the files to commit or discard first.
- **Push** pushes the current branch without force, to its upstream branch
  (even if it is named differently, or was deleted on the remote) unless you
  configured a separate push remote. A branch with no upstream yet is published
  to `origin` (or the only remote) and tracks it from then on; it needs at
  least one commit. If the remote has commits you don't have, the push is
  rejected, Dynpost fetches them so you can see what to pull, and asks you to
  pull first.
- **Errors** are explained in plain terms: failed sign-ins (with a hint to set
  up a credential helper or SSH key), an unreachable remote, a repository locked
  by another git program (Dynpost waits briefly and tries again first), or a
  repository git refuses to trust. Dynpost never shows a git prompt: git runs
  with terminal prompts disabled, though GUI credential helpers still work.
- **History**: the **History** tab lists the commits on the current branch that
  changed files in the workspace, newest first, 50 at a time (**Load more** for
  older ones). Choosing a commit opens it for review: its message, author and
  date, the workspace files it changed, and a diff of each file against the
  commit before it (the first parent, for a merge). A request file can be shown
  as a **Request** or as **Text**, just like in the diff view, and the same
  fallback to text applies. Reviewing history is
  read-only; it never checks out or changes anything.
- **Branches** lists the local branches (the current one marked, each with its
  upstream and last commit) and the remote branches as of the last fetch.
  - **Switch** checks out another local branch (`git switch`, never forced).
    It is refused while there are uncommitted changes in the workspace — the
    files are named, as for a pull — or a merge is in progress, so a switch
    never carries changes over or loses them. If open tabs have unsaved edits,
    Dynpost asks first: they stay in their tabs. After a switch the explorer,
    open tabs and environments are reloaded from disk.
  - **Create** (the + button) makes a branch at the current commit, without an
    upstream (**Publish** pushes it), and switches to it unless you untick
    that; uncommitted changes come along, since both branches start from the
    same commit.
  - **Check out** a remote branch (`origin/feature`) to create a local branch
    of the same name (`feature`) that tracks it, refused like a switch when
    there are uncommitted changes. A remote branch that a local branch
    already tracks offers to switch to that branch instead.
  - **Merge** (the merge button on a local or remote branch) merges that
    branch into the current one after a confirmation (`git merge --no-edit`,
    with git's usual message such as `Merge branch 'feature'`). It
    fast-forwards when it can, unless your git configuration says otherwise,
    and never rebases or squashes. It is refused while there are uncommitted
    changes — in the workspace, or to tracked files anywhere in the
    repository, so aborting the merge always puts your branch back exactly —
    or a merge is in progress. If it conflicts, the conflicted files are listed
    and resolved as after a pull (see below); conflicted files outside the
    workspace folder are named, to resolve with your own git tools. Merging a
    remote branch uses its commits as of the last fetch.
  - **Delete** removes a local branch after a confirmation; the current branch
    can't be deleted. If the branch has commits that aren't merged into its
    upstream or the current branch, git keeps it and Dynpost asks a second
    time, saying the commits will be lost, before deleting it anyway
    (`git branch -D`). Nothing is deleted on the remote.
  - **Delete on the remote** (the trash button on a remote branch) deletes that
    branch on its remote after a confirmation (`git push --delete`, never
    forced), for everyone who uses the remote. Local branches are kept; one
    that tracked it shows its upstream as deleted. A branch someone else
    already deleted is simply dropped from the list. The branch to delete is worked out from
    the remote's fetch settings, so a renamed mapping deletes the right branch;
    if they don't map it to exactly one branch, nothing is deleted.
- **Remotes** lists the repository's remotes with their URLs. You can add one,
  rename it or change its URL (`git remote add`, `rename`, `set-url`), or
  remove it after confirming (`git remote remove`, which also drops its
  remote-tracking branches; nothing is deleted on the remote). Nothing is
  fetched or pushed when you change remotes.

### Local-only files

Every request, environment and run plan is shared through git by default. To
keep one on your computer only — say a `local` environment pointing at your own
machine, or a scratch request — right-click the request in the explorer and
choose **Keep on this computer only**, or click the laptop button next to an
environment or run plan (it shows when you hover the row). Local-only items are
marked with an amber laptop icon; choose **Share through git** or click the
icon again to share them.

Dynpost records the choice where git itself sees it: one anchored line per file
in the workspace's `.gitignore`, so any git tool leaves the file out too:

```gitignore
.dynpost/
/environments/local.yaml
/requests/users/scratch.yaml
```

If the file was already committed, Dynpost also stops tracking it
(`git rm --cached`; the file stays on disk), so the Source control view lists it
as deleted. Your next commit removes it from the repository; teammates who pull
that commit lose their copy, so tell them first if they still need it. Sharing
the file again before committing simply undoes this; after committing, the file
shows up as a new file to commit.

Renaming, moving, duplicating or deleting a local-only request (or the folder
that holds it), or renaming or deleting a local-only environment, or renaming a local-only run plan, updates its
`.gitignore` line, so it never becomes shared by accident; copies of local-only
requests are local-only too. Only lines of this exact form count: a file that a
broader pattern (such as `*.local.yaml`) ignores is not shown as local-only.

### Resolving merge conflicts

When a pull or a branch merge stops on conflicts, the Source control view shows a merge banner
and marks each conflicted file. **Resolve** opens it side by side, your
version (the current branch) next to the incoming one, and for each file you
choose:

- **Keep this version** — take your side or the incoming side as-is (or keep a
  deletion, if that side deleted the file);
- **Mark as resolved** — after editing the file yourself in any editor to
  combine both sides. Dynpost refuses while the file still has
  `<<<<<<<` / `=======` / `>>>>>>>` conflict markers.

Once every file is resolved, **Commit all** completes the merge; the commit
message is pre-filled with git's merge message. **Abort merge** abandons the
merge instead (`git merge --abort`) and puts your branch back as it was before
the pull or merge, after asking you to confirm.

Dynpost reopens the last workspace at the next launch, if its folder still
exists. It also keeps the ten most recently opened workspaces, listed on the
welcome screen and in the header next to **Open folder**; folders that no longer
exist are dropped from the list. Both are stored per user, outside any
workspace, in `settings.json` in the app data folder (`%APPDATA%\Dynpost` on
Windows, `~/.config/Dynpost` on Linux and macOS).

## AI assistants (MCP)

Dynpost has a built-in [Model Context Protocol](https://modelcontextprotocol.io)
server, so an AI assistant such as Claude Code or Claude Desktop can work with
the workspace open in the app. It is **off by default**: turn it on in
*Settings* (the gear) → *AI assistants (MCP)…*.

While it is on, the server listens on `http://localhost:47823/mcp` (the port
can be changed) using the Streamable HTTP transport. Only apps on this computer
can reach it, and they must send the token shown in the dialog as
`Authorization: Bearer <token>`; requests from web pages are refused. *Replace…*
makes a new token, disconnecting clients that have the old one. The setting,
port and token are kept in your user settings, never in a workspace.

The dialog shows the command that adds Dynpost to Claude Code:

```sh
claude mcp add --transport http dynpost http://localhost:47823/mcp --header "Authorization: Bearer <token>"
```

and the JSON entry for other clients:

```json
{
  "mcpServers": {
    "dynpost": {
      "type": "http",
      "url": "http://localhost:47823/mcp",
      "headers": { "Authorization": "Bearer <token>" }
    }
  }
}
```

The assistant can use these tools, always on the workspace open in the app:

| Tool | What it does |
| --- | --- |
| `get_workspace` | Lists the requests and collection folders. |
| `read_request`, `create_request`, `save_request`, `move_request`, `delete_request` | Read and change request files. |
| `create_folder` | Creates a folder in `requests/` or `plans/`. |
| `get_folder_settings` | Reads the headers, auth and variables a folder of `requests/` (or `requests/` itself) sets for everything in it. |
| `save_folder_settings` | Replaces the headers, auth and variables a folder of `requests/` sets (its `_folder.yaml`, removed when it sets nothing). |
| `list_history`, `get_history_entry` | Read the responses kept in a saved request's [history](#response-history) (cookie and secret values are hidden there already), to compare responses or find when something changed. |
| `open_in_app` | Opens a request, run plan or environment in a tab, so the assistant can show you what it made or changed. |
| `send_request` | Sends a saved request (keeping the response in its history) or an unsaved one, with the selected environment, and returns the response. Long bodies are cut off at 64 KB of text and binary bodies are left out. |
| `list_environments`, `read_environment`, `create_environment`, `select_environment` | Work with environments. Secret values are never shown to the assistant. |
| `list_run_plans`, `read_run_plan`, `create_run_plan`, `save_run_plan`, `move_run_plan`, `delete_run_plan` | Read, create, change and organize [load run](#load-runs) plans. New plans, like new requests, are named after themselves and never replace an existing file. |
| `start_run`, `get_run`, `cancel_run` | Start a load run of a saved plan (a run of more than 200 threads needs confirming), follow it — `get_run` can wait up to 50 seconds for it to finish — and stop it. A run the assistant starts opens in the plan's tab, where you can follow and cancel it. Only the last 120 seconds of a run's timeline are shown to the assistant. |
| `list_runs`, `get_run_samples`, `delete_runs` | A plan's earlier runs, a run's request log (up to 100 requests at a time), and deleting saved runs. |
| `git_status`, `git_diff`, `git_log`, `git_show` | Show the branch and changed files, how a changed file differs from the last commit, the commits of the branch, and one commit's message and files — so the assistant can review its changes before you commit them. |

Changes the assistant makes appear in the app right away: the explorer, open
requests and run plans without unsaved edits, environments, earlier runs and the
git status are re-read.
Committing, pulling and pushing stay with you — the assistant can't do them.

## Architecture

```text
Angular UI (webview)  ⇄  Photino message bridge  ⇄  .NET backend
```

Requests are sent from the .NET backend rather than the webview, so they are not
subject to browser CORS rules and can use any headers.

The backend follows **clean architecture**. Dependencies point inward only:

```text
Dynpost.Desktop ──► Dynpost.Infrastructure ──► Dynpost.Application ──► Dynpost.Domain
```

(`Dynpost.Desktop` also references `Dynpost.Application` directly to invoke use
cases.)

| Project | Responsibility | May reference |
| --- | --- | --- |
| `Dynpost.Domain` | Entities and value objects (`Workspace`, `Collection`, `HttpRequestDefinition`, `Environment`, `Auth`), variable resolution and other pure rules. No I/O, no framework dependencies. | — |
| `Dynpost.Application` | Use cases (open workspace, save request, send request, resolve environment, commit/pull/push) and the ports they need, e.g. `IWorkspaceRepository`, `IHttpSender`, `IGitService`, `ISecretStore`, `IHistoryStore`. | Domain |
| `Dynpost.Infrastructure` | Adapters implementing the ports: YAML file storage, `HttpClient` sender, git, `.dynpost/` secrets, cookies and history. | Application, Domain |
| `Dynpost.Desktop` | Composition root and presentation adapters: Photino window, bridge message dispatch to use cases, the MCP server's tools, dependency injection wiring. | All of the above |

The Angular client is a separate presentation layer that only talks to
`Dynpost.Desktop` through bridge messages.

## Prerequisites

- .NET 11 SDK
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
dotnet run --project src/Dynpost.Desktop
```

The .NET build runs `npm ci` and the Angular production build, then copies the
frontend into `wwwroot` beside the desktop executable. The host resolves assets
relative to its executable, not the current working directory, and serves them
to the webview through a custom scheme handler (`app://localhost/`, or an
intercepted `http://localhost/` on Windows) rather than `file://`, because
WebView2 won't run the Angular build's module scripts from `file://`.

## Frontend development

```sh
cd src/Dynpost.Client
npm ci
npm start
```

Open `http://localhost:4200` for Angular live reload in a browser. Outside the
desktop host there is no .NET backend, so the client talks to an in-memory
sample workspace instead (nothing is written to disk). The desktop host always
loads the bundled frontend; rebuild/restart it to see frontend changes. Routing uses hash URLs and assets use a relative base URL.

## Build and publish

```sh
dotnet build Dynpost.sln
dotnet publish src/Dynpost.Desktop -c Release -o artifacts/publish
```

Run the `Dynpost` executable (`Dynpost.exe` on Windows) in
`artifacts/publish`. Keep its `wwwroot` and `Assets` directories and native
libraries alongside it. This is a framework-dependent publish: the target machine needs the .NET 11
runtime and the platform webview prerequisites above, but not Node.js.

## Repository layout

- `src/Dynpost.Desktop`: Photino window, bridge, composition root, and
  build/publish integration.
- `src/Dynpost.Client`: standalone Angular app with SCSS styles, Tailwind
  CSS, Font Awesome icons, Angular CDK drag and drop and the Monaco editor.
- `src/Dynpost.Domain`, `src/Dynpost.Application`, `src/Dynpost.Infrastructure`:
  backend layers (see [Architecture](#architecture)).
- `tests/Dynpost.Domain.Tests`, `tests/Dynpost.Application.Tests`: fast unit
  tests with no I/O.
- `tests/Dynpost.Infrastructure.Tests`: integration tests against temporary
  folders and git repositories.
- `site/`: the static homepage (`index.html` plus `assets/`), published to
  GitHub Pages by `.github/workflows/pages.yml` on every push to `main` that
  changes it. Its screenshot is the client's browser preview (`npm start`).

Run the backend tests (xUnit, with NSubstitute for mocking) with:

```sh
dotnet test Dynpost.sln
```

Run the client unit tests (Vitest, through `ng test`) from `src/Dynpost.Client`;
`npm test` watches for changes, and this runs them once:

```sh
npm test -- --watch=false
```

Format the client with Prettier (`npm run format`, or `npm run format:check`
to verify) from `src/Dynpost.Client`. No other linter is configured yet.

## CI and releases

GitHub Actions runs on every pull request and push to `main`
(`.github/workflows/ci.yml`): it checks client formatting, runs the client unit tests
and builds the Angular app, then builds the solution and runs the tests on Windows, Linux and macOS.

Commit messages follow [Conventional Commits](https://www.conventionalcommits.org)
(`feat:`, `fix:`, `feat!:` / `BREAKING CHANGE:`, ...). After `npm install` at the
repo root, a [Lefthook](https://github.com/evilmartians/lefthook) `commit-msg`
hook runs commitlint (`commitlint.config.mjs`), and CI lints pull request
commits too. On every push to `main`, `.github/workflows/tag.yml` runs
[semantic-release](https://semantic-release.gitbook.io) (`.releaserc.json`),
which picks the next `vMAJOR.MINOR.PATCH` from the commits since the last tag
(`fix` → patch, `feat` → minor, breaking → major), pushes the tag and starts the
release build for it. Merge pull requests with a merge commit or by rebasing, not
by squashing: semantic-release reads every commit, and a squash would fold a
branch's `fix:` commits into one `feat:` and drop them from the release notes.

To release when the commits wouldn't produce one (or to choose the bump yourself),
run the **Create tag** workflow by hand on `main` (Actions → Create tag → Run
workflow) and pick `patch`, `minor` or `major`, or type an exact `version` such
as `1.4.0` or `1.4.0-beta.1`. Left on `auto`, it runs semantic-release as on a
push. A manual version must be newer than the last release tag; the workflow
pushes it as an ordinary `vX.Y.Z` tag and starts the release build, and
semantic-release counts the next automatic release from that tag. Pre-release
tags are ignored by semantic-release, so the next automatic release still
counts from the last stable one.

Pushing a version tag publishes desktop builds (`.github/workflows/release.yml`).
semantic-release does this automatically; to do it by hand:

```sh
git tag v0.1.0
git push origin v0.1.0
```

Each OS builds a self-contained publish (no .NET runtime needed on the target
machine) and packages it with [Velopack](https://velopack.io) (`vpk pack`) into
an installer, a portable copy and an update feed: `Dynpost-win-Setup.exe` and
`Dynpost-win-Portable.zip` for `win-x64`, `Dynpost.AppImage` for `linux-x64`,
and `Dynpost-osx-Setup.pkg` and `Dynpost-osx-Portable.zip` for `osx-arm64`.
The GitHub release for the tag gets only the installers (`Dynpost-win-Setup.exe`,
`Dynpost.AppImage`, `Dynpost-osx-Setup.pkg`), together with the `.nupkg`
packages and `releases.<channel>.json` feeds (channels `win`, `linux`, `osx`)
that installed copies read to update themselves; the portable copies are kept
in the workflow artifacts only. Tags with a suffix such as
`v0.2.0-beta.1` are marked as pre-releases; only pre-release builds update to
pre-releases. Running the workflow manually produces the same files as workflow
artifacts without creating a release. Keep the `vpk` version in the workflow
equal to the `Velopack` package version in `Dynpost.Infrastructure`. The builds
are not code-signed or notarized yet.
