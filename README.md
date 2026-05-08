# Azure Connectors SDK — LSP Server

A Language Server Protocol (LSP) server and VS Code extension that provides intelligent code assistance for [Azure Connectors SDK](https://github.com/Azure/Connectors-NET-SDK) development. Built with OmniSharp.Extensions.LanguageServer and Roslyn for comprehensive C# code analysis.

> [!CAUTION]
> **Early Preview — Not for Production Use**
>
> This extension and LSP server are currently in early preview and under active development. They are intended for evaluation, experimentation, and feedback purposes only.
>
> - **Do not use in production environments.**
> - **Breaking changes should be expected** across extension behavior, LSP features, and configuration in future releases.
> - Features may be added, modified, or removed without prior notice.
>
> We welcome feedback and contributions — please [open an issue](https://github.com/Azure/Connectors-NET-LSP/issues) with questions, suggestions, or bug reports.

## Features

- **Document Sync**: Real-time document tracking via thread-safe in-memory `BufferManager`
- **Intelligent Hover**: Rich hover information with method signatures, documentation, and SDK-specific guidance following LSP standards
- **CodeLens Integration**: Configurable code lenses for SDK methods with quick access to documentation and connection creation (command names set via `initializationOptions`)
- **Completion**: Context-aware completions with dynamic value suggestions for connector operations
- **SDK Assembly Analysis**: Automatic indexing and analysis of Connectors SDK assemblies from `.nupkg` packages, plus Roslyn-based analysis of project code and its references (including DirectClient usage)
- **Connection Management**: Unified connection model supporting both managed API connections (`connections.json`) and DirectClient connections (`local.settings.json`), with runtime updates via LSP notifications
- **VS Code Extension**: Bundled extension that discovers SDK packages, watches connection files for changes, and merges connections from multiple sources
- **LSP Compliance**: Full compliance with Language Server Protocol standards for broad editor compatibility

## Requirements

- .NET SDK 8.0+
- A Connectors SDK `.nupkg` (loaded automatically from `SDK/` directory or via `--sdk` argument)
- An LSP-compatible editor (VS Code recommended — a purpose-built extension is included)

## Build

```bash
# From repo root
dotnet build
```

## Test

```bash
dotnet test
```

> **Note:** Some tests in `SdkIndexConstantDiscoveryTests` require a Connectors SDK `.nupkg` in the `SDK/` directory and will be skipped if not present.

## Run the Server

```bash
# Launch the LSP server (stdio)
dotnet run --project Server
```

**Note**: The server communicates via stdio and is designed to be launched by LSP clients, not run manually.

### Framework-Dependent Publish (run with `dotnet`)

If your client expects to start the server with the `dotnet` host, publish a framework-dependent build:

```bash
# Uses the MSBuild convenience target (outputs to ../LSPServer)
dotnet msbuild Server/SdkLspServer.csproj -t:PublishFrameworkDependent
```

Then launch the server via the dotnet CLI:

```bash
dotnet LSPServer/SdkLspServer.dll --sdk <path-to-sdk-nupkg>
```

Connections are not passed via the command line. They are supplied by the LSP client through `initializationOptions.connections` at startup or the `custom/updateConnections` notification at runtime.

## Production Build & Distribution

For production deployment (e.g., bundling with the VS Code extension), you can build a self-contained single-file executable:

```bash
# Build for current platform (outputs to ../LSPServer)
dotnet msbuild Server/SdkLspServer.csproj -t:PublishCurrentPlatform

# Or build for a specific platform
dotnet msbuild Server/SdkLspServer.csproj -t:PublishWindows   # ../LSPServer-win
dotnet msbuild Server/SdkLspServer.csproj -t:PublishMacOS     # ../LSPServer-mac
dotnet msbuild Server/SdkLspServer.csproj -t:PublishLinux     # ../LSPServer-linux
```

This creates a standalone executable (e.g., `SdkLspServer.exe` on Windows, `SdkLspServer` on macOS/Linux) that:

- Requires no .NET runtime installation
- Bundles all dependencies in a single file
- Can be distributed directly with the VS Code extension

### VS Code Extension Packaging

The `vscode-extension/` directory contains a purpose-built VS Code extension that manages the LSP server lifecycle. To package it as a `.vsix`:

```bash
# First, publish the LSP server into the extension's server/ directory
dotnet publish Server/SdkLspServer.csproj -c Release -o vscode-extension/server

cd vscode-extension
npm ci
npx --no-install @vscode/vsce package
```

The extension expects the server at `<extension-path>/server/SdkLspServer.dll`. If the `server/` directory is not populated, users must set `connectorSdk.lspServerPath` in VS Code settings.

### Releasing a New Version

Releases are automated via `.github/workflows/release.yml`. Pushing a `v*` tag triggers the workflow which builds, tests, packages the VSIX, and creates a GitHub Release with the `.vsix` attached.

```bash
# 1. Update CHANGELOG.md with the new version entry
# 2. Update vscode-extension/package.json version to match
# 3. Create PR, get it merged
# 4. Tag and release:
git tag v0.2.0
git push origin v0.2.0
```

**Known gotchas:**

- **Version already matches:** `npm version` fails with "Version not changed" if `package.json` already has the target version. The workflow uses `--allow-same-version` to handle this, so it's safe to set the version in the PR and tag the same value.
- **Dirty working tree:** `npm version` fails on a dirty git tree. The workflow runs the version bump *before* `dotnet publish` (which creates files in `vscode-extension/server/`) to avoid this.
- **Engine version alignment:** `vsce package` fails if `@types/vscode` version exceeds `engines.vscode` in `package.json`. Keep them aligned when upgrading either.
- **Verifying installed build:** The LSP server assembly version defaults to `1.0.0.0`. To confirm which build is running, check the DLL path in the extension output — VSIX installs load from `~/.vscode/extensions/microsoft.connector-sdk-intellisense-<version>/server/`.

The extension automatically:

- Discovers SDK `.nupkg` files in the workspace
- Watches `connections.json` and `local.settings.json` for changes and sends merged `custom/updateConnections` updates to the server
- Merges connections from both managed API (`connections.json`) and DirectClient connections parsed from `local.settings.json`
- Starts/stops the LSP server with the workspace lifecycle

## Repository Structure

- `Server/` — Main LSP server project
  - `Program.cs` — Server initialization, CLI arg parsing, and dependency injection
  - `BufferManager.cs` — Thread-safe in-memory document buffer management
  - `SdkIndex.cs` — SDK assembly indexing and metadata management
  - `NupkgLoader.cs` — NuGet package discovery and assembly loading utilities
  - `Handlers/` — LSP protocol handlers
    - `CodeLensHandler.cs` — Configurable code lenses for SDK methods
    - `TextDocumentSyncHandler.cs` — Document synchronization and change tracking
    - `HoverHandler/` — Rich hover information with SDK integration
    - `CompletionHandler/` — Context-aware completions with dynamic value support
  - `Services/` — Server-side services
    - `Api/` — API service for dynamic operations metadata
    - `CodeLens/` — CodeLens configuration (configurable command names)
    - `Connections/` — Abstract connection model (managed API + DirectClient)
    - `Telemetry/` — Telemetry service abstraction
  - `Store/` — Runtime data stores
    - `DocumentData/` — Per-document metadata slices
    - `DynamicData/` — Cached dynamic values for completions
    - `SessionData/` — Session-scoped data
- `vscode-extension/` — VS Code extension (TypeScript)
  - `src/extension.ts` — Extension entry point: lifecycle, file watching, connection merging
  - `package.json` — Extension manifest with settings and commands
- `SDK/` — SDK assembly storage directory
- `Docs/` — Design documents and guides
  - `architecture.md` — Architecture overview, analysis strategy, and design decisions *(planned — [#60](https://github.com/Azure/Connectors-NET-LSP/issues/60))*
  - `design-attribute-context-completions.md` — Attribute-context completions design
  - `E2E_TESTING_GUIDE.md` — End-to-end testing guide
- `sdk-lsp-server.sln` — Solution file

## LSP Capabilities

- **Text Document Sync**: Full document synchronization (`TextDocumentSyncKind.Full`)
- **Hover Provider**: Context-aware hover information with SDK documentation
- **Code Lens Provider**: Configurable code lenses with editor-specified command names
- **Completion Provider**: SDK-aware completions with dynamic value resolution
- **Custom Notifications**: Runtime connection updates via `custom/updateConnections`

## Using With an Editor

The server integrates with any LSP-compatible editor by:

- Launching via `dotnet run --project Server` with stdio transport
- Associating with C# files (`*.cs`) and the `csharp` language identifier
- Providing document selector: `{ language: "csharp", scheme: "file" }`

### VS Code Integration

The included `vscode-extension/` provides a ready-to-use VS Code experience:

- Automatically starts the server with stdio transport
- Discovers SDK packages and connection files in the workspace
- Watches `connections.json` and `local.settings.json` for changes and sends merged updates to the server
- Merges DirectClient connections from workspace configuration (e.g., `local.settings.json`)
- Registers configurable CodeLens commands and Connectors SDK settings

## End-to-End Testing

See [Docs/E2E_TESTING_GUIDE.md](Docs/E2E_TESTING_GUIDE.md) for step-by-step instructions to build the Connectors SDK `.nupkg`, stage it, and verify the full LSP experience with a real connector project.

## Development Notes

- **Testing**: Run `dotnet test` from the repo root. Test project: `Server.Tests/`
- **Architecture**: Built on OmniSharp.Extensions.LanguageServer with full Roslyn integration
- **SDK Integration**: Automatically discovers and loads SDK assemblies for enhanced IntelliSense
- **DirectClient Support**: DirectClient connections are supplied by the VS Code extension, derived from `local.settings.json` configuration
- **Connection Model**: `ConnectionsConfig` with separate `ManagedApiConnections` and `DirectClientConnections` dictionaries; `ConnectionInfo` is a property within `ManagedApiConnection`
- **Buffer Management**: Thread-safe document tracking via `BufferManager` for real-time synchronization

## Git Pre-Commit Hook

This repo includes a pre-commit hook to enforce formatting and catch build errors locally.

Enable it once per clone:

```bash
git config core.hooksPath hooks
```

What it does:

- Runs `dotnet format` to auto-fix style/format issues (install if missing: `dotnet tool install -g dotnet-format`).
- If changes were made by the formatter, it auto-stages them and continues the commit.
- Builds the solution with analyzers.
- Optionally runs tests if you set `RUN_TESTS=1` in your environment.

Disable or bypass for a single commit with `--no-verify`.

## License

MIT
