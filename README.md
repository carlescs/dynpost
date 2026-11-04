# Missive

A minimal cross-platform desktop app using Photino.NET 4, .NET 10, and Angular 22.
Photino opens the compiled Angular frontend in a native webview; no web server is
needed at runtime.

## Prerequisites

- .NET 10 SDK
- Node.js 22.22.3+ (22.x) or 24.15.0+ (24.x), with npm
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
host always loads the bundled frontend; rebuild/restart it to see frontend changes.
The welcome screen includes a counter to verify Angular interaction. Routing uses
hash URLs and assets use a relative base URL so they work from local files.

## Build and publish

```sh
dotnet build Missive.sln
dotnet publish src/Missive.Desktop -c Release -o artifacts/publish
```

Run the `Missive.Desktop` executable (`Missive.Desktop.exe` on Windows) in
`artifacts/publish`. Keep its `wwwroot` directory and native libraries alongside
it. This is a framework-dependent publish: the target machine needs the .NET 10
runtime and the platform webview prerequisites above, but not Node.js.

## Layout

- `src/Missive.Desktop`: Photino window and build/publish integration.
- `src/Missive.Client`: standalone Angular app, styles, and routes.

No test runner or linter is configured. `npm run build` in the frontend and
`dotnet build Missive.sln` validate TypeScript/templates and the desktop host.