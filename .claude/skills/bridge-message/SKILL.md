---
name: bridge-message
description: Add a new host<->client bridge message to Dynpost end to end — Application use case, BridgeMessages.cs constant and payload record, BridgeHandlers.cs registration, DI in Program.cs, the TypeScript entry in models/messages.ts, a MockBackend case, and backend + client tests. Use when adding or changing a bridge message type, or when the UI needs a new call into the .NET host.
argument-hint: "<message.type> <what it does>"
---

# Add a bridge message

Request: $ARGUMENTS

A bridge message is defined in two places that must agree: `BridgeMessages.cs` on the host
and `models/messages.ts` in the client. `MockBackend` must also handle it so `npm start`
keeps working. Do every step below. Leaving one out is how the two sides drift apart.

Before writing anything, read an existing message all the way through and copy its shape.
`history.list` is a good, small reference:

| Step | File | Look for |
|------|------|----------|
| Use case | `src/Dynpost.Application/History/HistoryUseCases.cs` | `ListResponseHistory` |
| Type constant | `src/Dynpost.Desktop/Bridge/BridgeMessages.cs` | `HistoryList`, `HistoryPathPayload` |
| Handler | `src/Dynpost.Desktop/Bridge/BridgeHandlers.cs` | `BridgeMessageTypes.HistoryList` |
| DI | `src/Dynpost.Desktop/Program.cs` | `AddTransient<ListResponseHistory>()` |
| TS type | `src/Dynpost.Client/src/app/models/messages.ts` | `'history.list'` in `BridgeMessages` |
| Mock | `src/Dynpost.Client/src/app/services/mock-backend.ts` | `case 'history.list'` |
| Client call | `modules/explorer/components/response-history/response-history.ts` | `bridge.send('history.list', …)` |

## 1. Name the message

- Use dotted, lowerCamel segments grouped by area: `workspace.recent.remove`,
  `request.pickFile`, `settings.setHistory`. Look at `BridgeMessageTypes` and reuse an
  existing area prefix before you invent a new one.
- The C# constant is the PascalCase form of the name (`WorkspaceRecentRemove`).

## 2. Application: the use case (and a port, if needed)

- Write one class per use case in `src/Dynpost.Application/<Area>/`. It is a `sealed` class
  with a primary constructor and an `ExecuteAsync(..., CancellationToken cancellationToken = default)`
  method that returns `Result<T>`. Use `Execute()` if it does no I/O.
- Return expected failures (not found, invalid input, no workspace open) as
  `Error`/`Result.Failure` using `ErrorCodes`. Never throw them.
- Workspace-bound use cases start with `current.RequireRoot()` (`CurrentWorkspace`).
- Input and output DTOs live in Application. Never return Infrastructure types.
- If it needs I/O, define a port interface here and implement it in
  `src/Dynpost.Infrastructure/<Area>/`. Domain stays pure, and references only point inward.
- If the use case renames, moves or deletes request files, it must keep response history,
  local-only `.gitignore` lines and folder settings in step, as the existing use cases do.

## 3. Desktop: constant, payload, handler, DI

1. **`BridgeMessages.cs`**: add a `public const string` to `BridgeMessageTypes` with an XML
   doc in the existing style: what it does, `Payload: <see cref="…"/>` (or `none`), and
   `Result: …`. Put it next to its area.
2. If it takes a payload, add an `internal sealed record <Name>Payload(...)` next to the
   others. Optional fields get C# defaults, which must match the optional (`?`) fields on
   the TS side.
3. **`BridgeHandlers.cs`**: register it in `Create(...)` next to its area:
   - With a payload: `.Register<XPayload, TResult>(BridgeMessageTypes.X, (p, ct) => Use<UseCase>().ExecuteAsync(..., ct))`
   - Without one: `.Register(BridgeMessageTypes.X, ct => Use<UseCase>().ExecuteAsync(ct))`
   - When there is no result, wrap the call in `NoValue(...)` and use `Unit?` as the result type.
   - Keep handlers as thin glue: deserialize, call one use case, return. A native dialog
     (`dialogs.Pick…`) followed by the use case is fine. Business logic is not.
4. **`Program.cs`**: register the use case (`.AddTransient<UseCase>()`, grouped with its area)
   and any new port implementation.

## 4. Client: type, mock, caller

1. **`models/messages.ts`**: add an entry to `interface BridgeMessages`:
   ```ts
   /** One-line description, matching the C# doc. */
   'area.name': { payload: { path: string; id?: string | null }; result: SomeDto };
   ```
   - Use `payload: null` when there is none. Use `result: null` for "no value" (`Unit?`).
   - Field names are the C# record properties in camelCase.
   - Add any new `…Dto` interface near the related ones, with the same fields as the C#
     record. A nullable C# property becomes `T | null`.
2. **`services/mock-backend.ts`**: add a `case 'area.name':` that behaves plausibly against
   the in-memory state, returns `ok(...)`, and returns `fail(ERROR_CODES.…, message)` for
   the same expected failures as the host.
3. Call it through the typed `Bridge`: `await this.bridge.send('area.name', payload)`.
   Usually this goes in a root store in `services/` or in a module's service. Modules must
   not import from each other.
4. **Host-initiated event instead?** If the host pushes it with no request, it is an event.
   Add it to `BridgeEventTypes` and an event record in `BridgeMessages.cs`, to
   `interface BridgeEvents` in `messages.ts`, and send it from `Bridge/HostEvents.cs`.
   Subscribe with `Bridge.on`.

## 5. Optional: expose it as an MCP tool

If an AI assistant should be able to do this too, add a tool in `src/Dynpost.Desktop/Mcp/McpTools.cs`
that calls the same use case. Its argument schema comes from the argument record. Never
expose commit/pull/push or secret values. Send `workspace.changed` if the tool modifies the
workspace, and add the tool to the table in `README.md`.

## 6. Tests

- **Application** (`tests/Dynpost.Application.Tests`): unit-test the use case. Mock ports
  with NSubstitute (`Substitute.For<IPort>()`) and use real Domain types. Cover the success
  path and each expected failure.
- **Infrastructure** (if you added an adapter): test it against a temporary directory or a
  real temporary git repo.
- **Desktop** (`tests/Dynpost.Desktop.Tests/Bridge/<Area>BridgeTests.cs`): drive the real router:
  ```csharp
  using var services = new ServiceCollection()
      .AddSingleton(Substitute.For<IPort>())
      .AddTransient<UseCase>()
      .BuildServiceProvider();
  var router = BridgeHandlers.Create(services, Substitute.For<IFileDialogs>(), Substitute.For<IAppWindow>());
  var json = await router.HandleAsync(JsonSerializer.Serialize(new { id = "1", type = "area.name", payload = new { … } }));
  var response = JsonDocument.Parse(json!).RootElement;
  Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
  ```
  For a new DTO, add a test that asserts the camelCase property names in the payload are
  exactly the fields of the TS interface. `LocaleBridgeTests.DateFormat_SendsEveryFieldTheClientReads`
  is the model.
- **Client**: in the caller's `<name>.spec.ts`, fake the `Bridge` with `vi.fn()` (don't use
  `MockBackend`) and assert `toHaveBeenCalledWith('area.name', {...})`. Add a case to
  `services/mock-backend.spec.ts` for the new mock branch.

## 7. Docs and checks

- Update `README.md` if users can see the change. Update `CLAUDE.md` if you added a new area
  or a convention.
- Run all of these and fix what fails:
  ```sh
  dotnet build Dynpost.sln
  dotnet test Dynpost.sln
  cd src/Dynpost.Client && npm test -- --watch=false && npm run format:check
  ```
- Before you finish, check the two sides against each other: the message name string
  matches exactly, every payload field has the same name and optionality, and the result
  shape is the same.
