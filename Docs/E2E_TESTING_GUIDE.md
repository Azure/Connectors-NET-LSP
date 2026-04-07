# End-to-End Testing Guide

Step-by-step instructions for setting up and running the Connector SDK LSP Server end-to-end with a real connector project.

---

## Overview

This guide walks you through:

1. Building the Connector SDK `.nupkg` from source
2. Staging the package for the LSP server
3. Building and running the LSP server
4. Installing the VS Code extension
5. Opening a connector project and verifying LSP features (hover, completions, CodeLens)

The guide uses the [azure-logicapps-connector-sdk](https://github.com/Azure/Connectors-NET-SDK) as the SDK source and the [azure-managed-connector-poc](https://github.com/Azure/Connectors-NET-LSP) as the test project. You can substitute any project that references the Connector SDK.

---

## Prerequisites

| Requirement | Version | Check |
|-------------|---------|-------|
| .NET SDK | 8.0+ | `dotnet --version` |
| Node.js | 18+ | `node --version` |
| npm | 9+ | `npm --version` |
| VS Code | Latest | `code --version` |
| Git | Any | `git --version` |

### Required Repositories

Clone these repos side by side (the relative paths don't matter, but they make later steps easier):

```
your-workspace/
  connector-sdk-lsp/          # This repo (LSP server + VS Code extension)
  azure-logicapps-connector-sdk/  # Connector SDK source
  azure-managed-connector-poc/    # Test project (DirectConnector)
```

```bash
git clone https://github.com/Azure/Connectors-NET-LSP.git
git clone https://github.com/Azure/Connectors-NET-SDK.git
git clone https://github.com/Azure/Connectors-NET-LSP.git
```

---

## Step 1: Build the Connector SDK NuGet Package

The Connector SDK project has `GeneratePackageOnBuild=true`, so a Release build produces the `.nupkg` automatically.

```bash
cd azure-logicapps-connector-sdk
dotnet build src/Microsoft.Azure.Workflows.Connectors.Sdk/Microsoft.Azure.Workflows.Connectors.Sdk.csproj -c Release
```

**Output:**
```
src/Microsoft.Azure.Workflows.Connectors.Sdk/bin/Release/Microsoft.Azure.Workflows.Connectors.Sdk.1.0.0.nupkg
```

> **Tip:** You do NOT need to publish this package to a NuGet feed. The LSP server loads `.nupkg` files directly from disk.

---

## Step 2: Stage the Package for the Test Project

Copy the built `.nupkg` into the **test project's** `SDK/` directory (e.g., `azure-managed-connector-poc/SDK/`).
The VS Code extension discovers `.nupkg` files from `SDK/` under the opened workspace folder(s),
not from the LSP repo itself.

```bash
cp azure-logicapps-connector-sdk/src/Microsoft.Azure.Workflows.Connectors.Sdk/bin/Release/Microsoft.Azure.Workflows.Connectors.Sdk.1.0.0.nupkg azure-managed-connector-poc/SDK/
```

Alternatively, set the `connectorSdk.sdkNupkgPath` VS Code setting to the absolute path of the `.nupkg`.

Verify the package is present:

```bash
ls azure-managed-connector-poc/SDK/*.nupkg
```

You should see at least:
```
Microsoft.Azure.Workflows.Connectors.Sdk.1.0.0.nupkg
```

> **How auto-discovery works:** Both the server (`Program.cs → TryFindNupkgInSdkFolder`) and the extension (`extension.ts → resolveSdkNupkgPath`) search for `.nupkg` files in `SDK/` directories. When multiple packages exist, the most recently modified file is selected. You can also bypass auto-discovery with the `--sdk` CLI flag or the `connectorSdk.sdkNupkgPath` VS Code setting.

---

## Step 3: Build the LSP Server

```bash
cd connector-sdk-lsp
dotnet build Server/SdkLspServer.csproj
```

This produces `Server/bin/Debug/net8.0/SdkLspServer.dll`, which the VS Code extension's development mode discovers automatically.

---

## Step 4: Install the VS Code Extension

### Option A: Development Mode (F5 — Recommended for Testing)

This is the fastest way to test. No VSIX packaging needed.

1. Open `connector-sdk-lsp/vscode-extension/` as a **separate VS Code window**:
   ```bash
   code connector-sdk-lsp/vscode-extension/
   ```

2. Install npm dependencies:
   ```bash
   cd connector-sdk-lsp/vscode-extension
   npm install
   ```

3. Press **F5** (or **Run → Start Debugging**). The included `.vscode/launch.json` defines a **"Run Extension"** configuration that compiles the TypeScript and launches an **Extension Development Host** — a second VS Code window with the extension loaded.

   > If F5 opens a debugger-type picker instead, ensure you opened the `vscode-extension/` folder (not the repo root) and that `.vscode/launch.json` exists inside it.

4. In the Extension Development Host, open the test project folder:
   **File → Open Folder → select `azure-managed-connector-poc/`**

The extension resolves the server from the sibling build output at `connector-sdk-lsp/Server/bin/Debug/net8.0/SdkLspServer.dll`. If it can't find it, set `connectorSdk.lspServerPath` in the Extension Development Host's settings (**Ctrl+,** → search "connectorSdk"):

```json
{
  "connectorSdk.lspServerPath": "/absolute/path/to/connector-sdk-lsp/Server/bin/Debug/net8.0/SdkLspServer.dll"
}
```

### Option B: VSIX Package (Distribution)

For sharing with other engineers who don't need the LSP server source:

```bash
cd connector-sdk-lsp

# Bundle the server into the extension
dotnet publish Server/SdkLspServer.csproj -c Release -o vscode-extension/server

# Package the extension
cd vscode-extension
npm install
npx @vscode/vsce package
```

This produces a `.vsix` file. Install it in VS Code via **Extensions → ... → Install from VSIX**.

---

## Step 5: Verify LSP Features

### Set Up Connection Entries

The POC project includes a `local.settings.json.template`. Copy it to create a real settings file so the extension can detect DirectClient connections:

```bash
cd azure-managed-connector-poc/DirectConnector
cp local.settings.json.template local.settings.json
```

Then edit `local.settings.json` and replace the placeholder values with real (or realistic test) connection URLs:

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "Connectors:Office365:ConnectionRuntimeUrl": "https://YOUR-INSTANCE.azure-apihub.net/apim/office365/YOUR-CONNECTION-ID",
    "Connectors:SharePoint:ConnectionRuntimeUrl": "https://YOUR-INSTANCE.azure-apihub.net/apim/sharepointonline/YOUR-CONNECTION-ID"
  }
}
```

> **Note:** The URLs don't need to be valid for basic LSP testing (hover, completions, connection detection). However, **dynamic values** (e.g., listing calendars, contacts, or SharePoint sites on hover) require real API Hub connections that can authenticate successfully.

### Authentication

The DirectConnector POC uses `DefaultAzureCredential` to authenticate to API Hub. The LSP server uses the same credential chain for dynamic value resolution. For local testing, the simplest approach:

```bash
az login
```

That's all. Both the Functions runtime and the LSP server pick up your Azure CLI session automatically — no manual bearer token or managed identity setup needed.

| Auth Mode | Config | When to Use |
|-----------|--------|-------------|
| **DefaultAzureCredential** (default) | Leave `ManagedIdentityClientId` commented out (`__` prefix) | Local dev — `az login` is enough |
| **System-assigned MSI** | Set `Connectors:Office365:ManagedIdentityClientId` to `""` | Deployed to Azure with system MSI |
| **User-assigned MSI** | Set `Connectors:Office365:ManagedIdentityClientId` to the client ID GUID | Deployed with a specific user-assigned identity |
| **Explicit token (override)** | Set `connectorSdk.azure.bearerToken` in VS Code settings | When `az login` isn't available or you need a specific token |

> The template file includes commented-out `__Connectors:{name}:ManagedIdentityClientId` keys. Remove the `__` prefix to activate MSI for a connector.

Once saved, the extension picks up the file automatically — check the **Output** panel (**View → Output** → select **"Connector SDK LSP"**) for:
```
Pushed merged connection update (triggered by .../local.settings.json)
```

### Verify Editor Features

Open `DirectConnector/ConnectorFunctions.cs` in the Extension Development Host and verify each feature:

### Hover

Hover over any symbol to see rich documentation:

- **SDK types** (e.g., `Office365Client`, `SharepointonlineClient`) — shows SDK assembly metadata
- **Method signatures** — shows parameter types, XML documentation, return types
- **Connector operations** — shows operation-specific guidance

### Dynamic Values (Requires Real Connections)

When you hover over a **named argument** at a call site where the SDK parameter has the `[DynamicValues]` attribute, the LSP fetches **live data** from the API Hub connection. This validates that your connection URL and authentication are working end-to-end.

> **Important:** Dynamic values only work for parameters decorated with `[DynamicValues("OperationId")]` in the SDK. Currently, **SharePoint** methods have this attribute; Office365 methods do **not** (yet). If you hover a parameter without the attribute (e.g., `calendarId` on Office365), you'll see the standard parameter documentation but no dynamic value list.

#### Which Methods Have Dynamic Values?

The SDK's `SharepointonlineExtensions` class has `[DynamicValues]` on these parameters:

| Method | Parameter | Dynamic Operation | What It Fetches |
|--------|-----------|-------------------|-----------------|
| `GetAllTablesAsync` | `siteAddress` | `GetDataSets` | SharePoint sites you have access to |
| `GetTableAsync` | `siteAddress` | `GetDataSets` | SharePoint sites |
| `GetTableAsync` | `listName` | `GetTables` | Lists/libraries on the site |
| `GetTableAsync` | `limitColumnsByView` | `GetTableViews` | Views for the list |
| `CreateFileAsync` | `siteAddress` | `GetDataSets` | SharePoint sites |
| `CopyFileAsync` | `siteAddress` | `GetDataSets` | SharePoint sites |
| `ListItemPermissionsAsync` | `libraryName` | `GetTablesForLibraries` | Document libraries |

Office365 methods (e.g., `GetEventsCalendarViewV3Async`) do **not** have `[DynamicValues]` attributes, so no dynamic hover values appear for them.

#### Testing Dynamic Values

To test dynamic values, write a call to a SharePoint method using **named arguments**. For example, add a temporary test line in `ConnectorFunctions.cs`:

```csharp
// Temporary test line — hover over siteAddress: to see dynamic values
var tables = await this._sharePointClient
    .GetAllTablesAsync(siteAddress: "https://your-tenant.sharepoint.com/sites/your-site")
    .ConfigureAwait(continueOnCapturedContext: false);
```

Hover over `siteAddress:` — if the connection is live and `az login` is active, the tooltip shows:

```
Dynamic values:
- https://contoso.sharepoint.com (click to insert)
- https://contoso.sharepoint.com/sites/TeamSite (click to insert)
```

The server resolves these from the SharePoint API via the connection URL. Clicking a value inserts it into your code.

#### Parameter Name Inference (Fallback)

Even without `[DynamicValues]`, the server infers dynamic operations for certain well-known parameter names:

| Parameter Name | Inferred Operation | Connector |
|----------------|--------------------|-----------|
| `formId` / `form_id` | `ListForms` | Microsoft Forms |
| `teamId` / `team_id` | `GetAllTeams` | Microsoft Teams |
| `channelId` / `channel_id` | `GetChannelsForGroup` | Microsoft Teams |

> If dynamic values show an empty list or an error, check: (1) `az login` is current, (2) the connection URL points to a valid API Hub connection with appropriate permissions, (3) the **Connector SDK LSP** output panel for error details.

### Completions

Start typing inside a method body. The LSP provides:

- SDK symbol completions (types, methods, properties from the Connector SDK assembly)
- Context-aware suggestions based on Roslyn analysis of the open file

### CodeLens

Look for CodeLens annotations above method declarations. These provide quick actions configured via the extension (e.g., "Open Connection View").

---

## Step 6: Verify via Output Panel

The extension's output channel (**Connector SDK LSP**) shows diagnostic information:

```
Starting LSP server from: .../Server/bin/Debug/net8.0/SdkLspServer.dll
SDK .nupkg: .../SDK/Microsoft.Azure.Workflows.Connectors.Sdk.1.0.0.nupkg
Initialization: apiConfig=none, connections=2
LSP server started successfully.
```

If the SDK loads correctly, the server reports the assembly and type count via `window/showMessage`. Look for a notification like:

```
Loaded SDK: 1 assemblies, N types (source: arg)
```

---

## Troubleshooting

### "Could not find SdkLspServer.dll"

The extension couldn't auto-discover the server. Ensure:
1. You built the server (`dotnet build Server/SdkLspServer.csproj`)
2. The extension is opened from the `vscode-extension/` folder inside the LSP repo (so the sibling path `../Server/bin/Debug/net8.0/` resolves correctly)
3. Or set `connectorSdk.lspServerPath` explicitly in VS Code settings

### "SDK failed to load"

The server couldn't find or index the `.nupkg`. This commonly happens in the Extension Development Host because the extension scans only the **open workspace's** `SDK/` folder — not the LSP repo's `SDK/` folder.

**Quick fix:** Copy the `.nupkg` into the target project's workspace:

```bash
mkdir -p azure-managed-connector-poc/SDK
cp connector-sdk-lsp/SDK/Microsoft.Azure.Workflows.Connectors.Sdk.1.0.0.nupkg azure-managed-connector-poc/SDK/
```

Then reload the Extension Development Host (**Ctrl+Shift+P** → "Developer: Reload Window").

**Alternative:** Set the path explicitly in the Extension Development Host's settings (**Ctrl+,**):

```json
{
  "connectorSdk.sdkNupkgPath": "Q:/path/to/connector-sdk-lsp/SDK/Microsoft.Azure.Workflows.Connectors.Sdk.1.0.0.nupkg"
}
```

### No hover/completions appear

1. Verify the server started: check the **Connector SDK LSP** output channel
2. Ensure the file is a `.cs` file (the extension registers for the `csharp` language)
3. Check that C# extension (OmniSharp or C# Dev Kit) isn't conflicting — the LSP server provides its own hover/completion, which may coexist or conflict depending on editor configuration

### Extension Development Host doesn't open

1. Make sure you opened `vscode-extension/` as its own VS Code window (not the repo root) — the `.vscode/launch.json` must be in the workspace root
2. If F5 shows a debugger picker, select **"Run Extension"** manually, or check that `.vscode/launch.json` exists in `vscode-extension/`
3. Ensure `npm install` completed successfully
4. Check that TypeScript compiles: run `npx tsc` in `vscode-extension/` — it should produce files in `out/`
5. Check the VS Code Debug Console for errors

---

## Rebuilding After SDK Changes

When you make changes to the Connector SDK source:

```bash
# 1. Rebuild the SDK package
cd azure-logicapps-connector-sdk
dotnet build src/Microsoft.Azure.Workflows.Connectors.Sdk/Microsoft.Azure.Workflows.Connectors.Sdk.csproj -c Release

# 2. Copy the updated package
cp src/Microsoft.Azure.Workflows.Connectors.Sdk/bin/Release/Microsoft.Azure.Workflows.Connectors.Sdk.1.0.0.nupkg ../connector-sdk-lsp/SDK/

# 3. Restart the LSP server (in the Extension Development Host)
#    Ctrl+Shift+P → "Connector SDK: Restart Language Server"
#    Or reload the window: Ctrl+Shift+P → "Developer: Reload Window"
```

---

## Rebuilding After LSP Server Changes

When you make changes to the LSP server source:

### With Watch Mode (Recommended)

Run `dotnet watch` in a terminal — it rebuilds automatically on save:

```bash
cd connector-sdk-lsp
dotnet watch --project Server --no-hot-reload
```

Then in the Extension Development Host, reload the window to pick up the new server binary.

### Without Watch Mode

```bash
cd connector-sdk-lsp
dotnet build Server/SdkLspServer.csproj
```

Then reload the Extension Development Host window.

---

## Quick Reference

| Task | Command |
|------|---------|
| Build SDK package | `dotnet build src/.../Microsoft.Azure.Workflows.Connectors.Sdk.csproj -c Release` |
| Build LSP server | `dotnet build Server/SdkLspServer.csproj` |
| Run server standalone | `dotnet run --project Server -- --sdk SDK/your-package.nupkg` |
| Install extension deps | `cd vscode-extension && npm install` |
| Launch extension (dev) | F5 in VS Code with `vscode-extension/` open |
| Package VSIX | `dotnet publish Server -c Release -o vscode-extension/server && cd vscode-extension && npx @vscode/vsce package` |
| Restart server | Ctrl+Shift+P → "Connector SDK: Restart Language Server" |
| Check server logs | View → Output → "Connector SDK LSP" |
