import * as path from "path";
import * as fs from "fs";

import * as vscode from "vscode";
import {
    LanguageClient,
    LanguageClientOptions,
    ServerOptions,
    TransportKind,
} from "vscode-languageclient/node";

let client: LanguageClient | undefined;
let fileWatcherDisposables: vscode.Disposable[] = [];
let traceOutputChannel: vscode.OutputChannel | undefined;
let tokenRefreshTimer: NodeJS.Timeout | undefined;

async function fileExists(filePath: string): Promise<boolean> {
    try {
        await fs.promises.access(filePath);
        return true;
    } catch {
        return false;
    }
}

export async function activate(context: vscode.ExtensionContext): Promise<void> {
    const outputChannel = vscode.window.createOutputChannel("Connector SDK LSP");
    context.subscriptions.push(outputChannel);

    context.subscriptions.push(
        vscode.commands.registerCommand("connectorSdk.openConnectionView", () => {
            vscode.window.showInformationMessage(
                "Connection management UI is not yet implemented. Configure connections in your connections.json or local.settings.json file."
            );
        })
    );

    // Command invoked by click-to-insert links in hover tooltips
    context.subscriptions.push(
        vscode.commands.registerCommand(
            "sdklsp.applyEdits",
            async (args: { documentUri?: string; edits: Array<{ range: { start: { line: number; character: number }; end: { line: number; character: number } }; newText: string }> }) => {
                const editor = vscode.window.activeTextEditor;
                if (!editor || !args?.edits) {
                    return;
                }

                // Verify the active editor matches the document the edit was created for
                if (args.documentUri && editor.document.uri.toString() !== args.documentUri) {
                    outputChannel.appendLine(`[applyEdits] Skipped: active editor (${editor.document.uri}) does not match edit target (${args.documentUri})`);
                    return;
                }

                const workspaceEdit = new vscode.WorkspaceEdit();
                for (const edit of args.edits) {
                    const range = new vscode.Range(
                        edit.range.start.line,
                        edit.range.start.character,
                        edit.range.end.line,
                        edit.range.end.character
                    );
                    workspaceEdit.replace(editor.document.uri, range, edit.newText);
                }

                const applied = await vscode.workspace.applyEdit(workspaceEdit);
                if (!applied) {
                    outputChannel.appendLine("[applyEdits] Edit was rejected by VS Code");
                }
            }
        )
    );

    context.subscriptions.push(
        vscode.commands.registerCommand("connectorSdk.restartLanguageServer", async () => {
            for (const disposable of fileWatcherDisposables) {
                disposable.dispose();
            }

            fileWatcherDisposables = [];

            // Clear token refresh timer before stopping client to prevent
            // sendNotification on a stopped/disposed LanguageClient
            if (tokenRefreshTimer) {
                clearInterval(tokenRefreshTimer);
                tokenRefreshTimer = undefined;
            }

            if (client) {
                await client.stop();
                client.dispose();
            }

            client = await startLanguageServer(context, outputChannel);
        })
    );

    client = await startLanguageServer(context, outputChannel);
}

export async function deactivate(): Promise<void> {
    if (tokenRefreshTimer) {
        clearInterval(tokenRefreshTimer);
        tokenRefreshTimer = undefined;
    }

    for (const disposable of fileWatcherDisposables) {
        disposable.dispose();
    }

    fileWatcherDisposables = [];

    if (client) {
        await client.stop();
        client.dispose();
    }
}

async function startLanguageServer(
    context: vscode.ExtensionContext,
    outputChannel: vscode.OutputChannel
): Promise<LanguageClient | undefined> {
    const config = vscode.workspace.getConfiguration("connectorSdk");
    const serverDll = await resolveServerPath(config, context);

    if (!serverDll) {
        vscode.window.showErrorMessage(
            "Connector SDK LSP: Could not find SdkLspServer.dll. Set connectorSdk.lspServerPath in settings."
        );
        return undefined;
    }

    outputChannel.appendLine(`Starting LSP server from: ${serverDll}`);

    // Build command-line arguments
    const args: string[] = [];

    const sdkResult = await resolveSdkPath(config, context, outputChannel);
    if (sdkResult) {
        if (sdkResult.type === "assembly") {
            args.push("--sdk-assembly", sdkResult.path);
            outputChannel.appendLine(`SDK assembly: ${sdkResult.path} (source: ${sdkResult.source})`);
        } else {
            args.push("--sdk", sdkResult.path);
            outputChannel.appendLine(`SDK .nupkg: ${sdkResult.path} (source: ${sdkResult.source})`);
        }
    }

    const serverOptions: ServerOptions = {
        run: { command: "dotnet", args: [serverDll, ...args], transport: TransportKind.stdio },
        debug: { command: "dotnet", args: [serverDll, ...args], transport: TransportKind.stdio },
    };

    const initializationOptions = await buildInitializationOptions(config, outputChannel);
    const connectionCount = initializationOptions.connections
        ? Object.values(initializationOptions.connections as Record<string, unknown>).reduce<number>(
              (sum, section) => sum + (section && typeof section === "object" ? Object.keys(section).length : 0),
              0
          )
        : 0;
    outputChannel.appendLine(
        `Initialization: apiConfig=${initializationOptions.apiConfig ? "configured" : "none"}, connections=${connectionCount}`
    );

    const isNewTraceChannel = !traceOutputChannel;
    const traceChannel = traceOutputChannel ?? vscode.window.createOutputChannel("Connector SDK LSP Trace");
    traceOutputChannel = traceChannel;
    if (isNewTraceChannel) {
        context.subscriptions.push(traceChannel);
    }

    const clientOptions: LanguageClientOptions = {
        documentSelector: [{ scheme: "file", language: "csharp" }],
        outputChannel,
        traceOutputChannel: traceChannel,
        initializationOptions,
        middleware: {
            provideHover: async (document, position, token, next) => {
                const result = await next(document, position, token);
                if (result) {
                    // Enable command: URIs in hover markdown so click-to-insert links work
                    const enableTrust = (md: unknown): void => {
                        if (md instanceof vscode.MarkdownString) {
                            md.isTrusted = { enabledCommands: ["sdklsp.applyEdits"] };
                        }
                    };

                    if (result.contents instanceof vscode.MarkdownString) {
                        enableTrust(result.contents);
                    } else if (Array.isArray(result.contents)) {
                        for (const content of result.contents) {
                            enableTrust(content);
                        }
                    }
                }

                return result;
            },
        },
    };

    const client = new LanguageClient(
        "connectorSdkLsp",
        "Connector SDK IntelliSense",
        serverOptions,
        clientOptions
    );

    try {
        await client.start();
        outputChannel.appendLine("LSP server started successfully.");
    } catch (err) {
        const message = err instanceof Error ? err.message : String(err);
        outputChannel.appendLine(`Failed to start LSP server: ${message}`);
        vscode.window.showErrorMessage(
            "Connector SDK LSP: Failed to start language server. " +
            "Ensure the .NET runtime is installed and the server path is valid."
        );
        return undefined;
    }

    // Watch for connection file changes and push updates
    setupConnectionFileWatcher(client, outputChannel);

    // Acquire and push an Azure token to the LSP server (non-blocking).
    // This runs az account get-access-token in the extension host process — outside
    // the hover request path — so it can't be canceled by cursor movement.
    startTokenRefreshLoop(client, config, outputChannel);

    // Restart the token refresh loop if the user changes the bearerToken setting
    const configWatcher = vscode.workspace.onDidChangeConfiguration((event) => {
        if (event.affectsConfiguration("connectorSdk.azure.bearerToken")) {
            const updatedConfig = vscode.workspace.getConfiguration("connectorSdk");
            outputChannel.appendLine("[TokenRefresh] bearerToken setting changed — restarting refresh loop");
            startTokenRefreshLoop(client, updatedConfig, outputChannel);
        }
    });
    fileWatcherDisposables.push(configWatcher);

    return client;
}

async function resolveServerPath(
    config: vscode.WorkspaceConfiguration,
    context: vscode.ExtensionContext
): Promise<string | undefined> {
    // 1. Explicit setting — always wins
    const configured = config.get<string>("lspServerPath");
    if (configured && await fileExists(configured)) {
        return configured;
    }

    // 2. Sibling debug build (development scenario — F5 from vscode-extension/)
    //    Checked BEFORE bundled server so that `dotnet build` output is preferred
    //    over a stale `dotnet publish -o vscode-extension/server` artifact.
    const devPath = path.join(context.extensionPath, "..", "Server", "bin", "Debug", "net8.0", "SdkLspServer.dll");
    if (await fileExists(devPath)) {
        return devPath;
    }

    // 3. Bundled server (VSIX / dotnet publish -o vscode-extension/server)
    const bundled = path.join(context.extensionPath, "server", "SdkLspServer.dll");
    if (await fileExists(bundled)) {
        return bundled;
    }

    return undefined;
}

interface SdkResolution {
    path: string;
    type: "nupkg" | "assembly";
    source: string;
}

async function resolveSdkPath(
    config: vscode.WorkspaceConfiguration,
    context?: vscode.ExtensionContext,
    outputChannel?: vscode.OutputChannel
): Promise<SdkResolution | undefined> {
    // 1. Explicit setting — prefer new sdkPath, fall back to deprecated sdkNupkgPath
    const configured = config.get<string>("sdkPath") || config.get<string>("sdkNupkgPath");
    if (configured && await fileExists(configured)) {
        const type = path.extname(configured).toLowerCase() === ".dll" ? "assembly" as const : "nupkg" as const;
        return { path: configured, type, source: "setting" };
    }

    // 2. Workspace project NuGet references — parse project.assets.json
    const workspaceFolders = vscode.workspace.workspaceFolders;
    if (workspaceFolders) {
        for (const folder of workspaceFolders) {
            const dllPath = await findSdkFromProjectAssets(folder.uri.fsPath, outputChannel, 1);
            if (dllPath) {
                return { path: dllPath, type: "assembly", source: "project-assets" };
            }
        }
    }

    // 3. Workspace SDK/ folder — search for .nupkg files
    if (workspaceFolders) {
        for (const folder of workspaceFolders) {
            const found = await findNewestNupkgInDir(path.join(folder.uri.fsPath, "SDK"));
            if (found) {
                return { path: found, type: "nupkg", source: "workspace-sdk-folder" };
            }
        }
    }

    // 4. Sibling SDK repo build output (development scenario — F5 from vscode-extension/)
    if (context) {
        const sdkRepoBuildDir = path.join(
            context.extensionPath, "..", "..",
            "Connectors-NET-SDK", "src", "Microsoft.Azure.Connectors.Sdk", "bin", "Debug"
        );
        const found = await findNewestNupkgInDir(sdkRepoBuildDir);
        if (found) {
            return { path: found, type: "nupkg", source: "sibling-repo" };
        }
    }

    return undefined;
}

const SDK_PACKAGE_NAME = "Microsoft.Azure.Connectors.Sdk";

async function findSdkFromProjectAssets(
    folderPath: string,
    outputChannel?: vscode.OutputChannel,
    maxDepth: number = 1
): Promise<string | undefined> {
    // Find .csproj files in the folder. If maxDepth > 0, also check immediate subdirectories.
    try {
        const entries = await fs.promises.readdir(folderPath);
        const csprojFiles = entries.filter((f) => f.endsWith(".csproj"));

        for (const csproj of csprojFiles) {
            const projectDir = folderPath;
            const assetsPath = path.join(projectDir, "obj", "project.assets.json");

            if (!(await fileExists(assetsPath))) {
                continue;
            }

            try {
                const assetsContent = await fs.promises.readFile(assetsPath, "utf-8");
                const assets = JSON.parse(assetsContent) as {
                    libraries?: Record<string, { path?: string; type?: string }>;
                    packageFolders?: Record<string, unknown>;
                    targets?: Record<string, Record<string, { compile?: Record<string, unknown> }>>;
                };

                // Find the SDK library entry
                const sdkLibKey = Object.keys(assets.libraries ?? {}).find((key) =>
                    key.startsWith(SDK_PACKAGE_NAME + "/")
                );

                if (!sdkLibKey || !assets.libraries?.[sdkLibKey]?.path) {
                    continue;
                }

                const libraryPath = assets.libraries[sdkLibKey].path!;
                const packageFolders = Object.keys(assets.packageFolders ?? {});

                // Find compile assets from the first target framework that contains SDK DLLs
                const targets = assets.targets ?? {};
                let dllAssets: string[] = [];

                for (const targetKey of Object.keys(targets)) {
                    const targetPackage = targets[targetKey]?.[sdkLibKey];
                    const compileAssets = targetPackage?.compile ?? {};
                    const targetDlls = Object.keys(compileAssets).filter((asset) => asset.endsWith(".dll"));

                    if (targetDlls.length > 0) {
                        dllAssets = targetDlls;
                        break;
                    }
                }

                if (dllAssets.length === 0) {
                    continue;
                }

                // Resolve the DLL path from package folders
                for (const dllRelPath of dllAssets) {
                    for (const pkgFolder of packageFolders) {
                        const fullPath = path.join(pkgFolder, libraryPath, dllRelPath);
                        if (await fileExists(fullPath)) {
                            outputChannel?.appendLine(
                                `SDK discovered from ${csproj} → ${sdkLibKey} → ${fullPath}`
                            );
                            return fullPath;
                        }
                    }
                }
            } catch (err) {
                outputChannel?.appendLine(
                    `Failed to parse ${assetsPath}: ${err instanceof Error ? err.message : String(err)}`
                );
            }
        }

        // Also check subdirectories if depth budget remains (skip well-known irrelevant dirs)
        const skipDirs = new Set(["node_modules", ".git", "bin", "obj", ".vs", ".vscode", "TestResults"]);
        if (maxDepth > 0) {
            for (const entry of entries) {
                if (skipDirs.has(entry)) {
                    continue;
                }

                const subDir = path.join(folderPath, entry);
                try {
                    const subStat = await fs.promises.stat(subDir);
                    if (!subStat.isDirectory()) {
                        continue;
                    }

                    const result = await findSdkFromProjectAssets(subDir, outputChannel, maxDepth - 1);
                    if (result) {
                        return result;
                    }
                } catch {
                    continue;
                }
            }
        }
    } catch {
        // Folder not readable
    }

    return undefined;
}

async function findNewestNupkgInDir(dirPath: string): Promise<string | undefined> {
    try {
        const stat = await fs.promises.stat(dirPath);
        if (!stat.isDirectory()) {
            return undefined;
        }

        const entries = await fs.promises.readdir(dirPath);
        const nupkgFiles = entries.filter((f) => f.endsWith(".nupkg"));
        if (nupkgFiles.length === 0) {
            return undefined;
        }

        const nupkgWithStats = await Promise.all(
            nupkgFiles.map(async (file) => {
                const fullPath = path.join(dirPath, file);
                const fileStat = await fs.promises.stat(fullPath);
                return { file, fullPath, mtimeMs: fileStat.mtimeMs };
            }),
        );

        nupkgWithStats.sort((a, b) => {
            if (b.mtimeMs !== a.mtimeMs) {
                return b.mtimeMs - a.mtimeMs;
            }
            return a.file.localeCompare(b.file);
        });

        return nupkgWithStats[0].fullPath;
    } catch {
        return undefined;
    }
}

interface ConnectionsConfig {
    managedApiConnections?: Record<string, unknown>;
    directClientConnections?: Record<string, unknown>;
}

async function buildInitializationOptions(config: vscode.WorkspaceConfiguration, outputChannel: vscode.OutputChannel): Promise<Record<string, unknown>> {
    const options: Record<string, unknown> = {};

    // API Config — only send apiConfig when there's useful auth/Azure configuration.
    const subscriptionId = config.get<string>("azure.subscriptionId") || "";
    const resourceGroup = config.get<string>("azure.resourceGroup") || "";
    const baseUrl = config.get<string>("azure.baseUrl") || "https://management.azure.com";

    const bearerToken = config.get<string>("azure.bearerToken");

    const hasAzureConfig = bearerToken || (subscriptionId && resourceGroup);
    if (hasAzureConfig) {
        const apiConfig: Record<string, string> = {
            baseUrl,
            subscriptionId,
            resourceGroup,
            apiVersion: "2018-07-01-preview",
        };

        if (bearerToken) {
            apiConfig.bearerToken = bearerToken;
        }

        options.apiConfig = apiConfig;
    }

    // Connections
    const connections = await loadConnectionsFromWorkspace(config, outputChannel);
    if (connections) {
        options.connections = connections;
    }

    return options;
}

async function loadConnectionsFromWorkspace(
    config: vscode.WorkspaceConfiguration,
    outputChannel: vscode.OutputChannel
): Promise<ConnectionsConfig | undefined> {
    // 1. Explicit setting
    const configured = config.get<string>("connectionsFilePath");
    if (configured && await fileExists(configured)) {
        return parseConnectionsFile(configured, outputChannel);
    }

    // 2. Auto-detect from workspace — merge both connection sources
    const workspaceFolders = vscode.workspace.workspaceFolders;
    if (!workspaceFolders) {
        return undefined;
    }

    const merged: ConnectionsConfig = {};

    for (const folder of workspaceFolders) {
        // Check for connections.json (Codeful pattern) — at root and in subdirectories
        for (const candidate of await findFilesInFolder(folder.uri.fsPath, "connections.json")) {
            const parsed = await parseConnectionsFile(candidate, outputChannel);
            if (parsed?.managedApiConnections) {
                merged.managedApiConnections = {
                    ...merged.managedApiConnections,
                    ...parsed.managedApiConnections,
                };
            }
        }

        // Check for local.settings.json (DirectClient pattern) — at root and in subdirectories
        for (const candidate of await findFilesInFolder(folder.uri.fsPath, "local.settings.json")) {
            const directClient = await parseDirectClientSettings(candidate, outputChannel);
            if (directClient?.directClientConnections) {
                merged.directClientConnections = {
                    ...merged.directClientConnections,
                    ...directClient.directClientConnections,
                };
            }
        }
    }

    if (!merged.managedApiConnections && !merged.directClientConnections) {
        return undefined;
    }

    return merged;
}

async function parseConnectionsFile(filePath: string, outputChannel: vscode.OutputChannel): Promise<ConnectionsConfig | undefined> {
    try {
        const content = await fs.promises.readFile(filePath, "utf8");
        return JSON.parse(content) as ConnectionsConfig;
    } catch (err) {
        const message = err instanceof Error ? err.message : String(err);
        outputChannel.appendLine(`Failed to parse connections file '${filePath}': ${message}`);
        return undefined;
    }
}

/**
 * Parses local.settings.json to extract DirectClient connection entries.
 * Looks for keys matching "Connectors:{name}:ConnectionRuntimeUrl".
 */
async function parseDirectClientSettings(filePath: string, outputChannel: vscode.OutputChannel): Promise<ConnectionsConfig | undefined> {
    try {
        const content = await fs.promises.readFile(filePath, "utf8");
        const settings = JSON.parse(content) as { Values?: Record<string, string> };
        const values = settings?.Values;
        if (!values) {
            return undefined;
        }

        const directClientConnections: Record<string, { connectorType: string; connectionRuntimeUrl: string }> = {};
        const runtimeUrlPattern = /^Connectors:([^:]+):ConnectionRuntimeUrl$/;

        for (const [key, value] of Object.entries(values)) {
            const match = runtimeUrlPattern.exec(key);
            if (match) {
                const connectionName = match[1];
                // Extract connector type from runtime URL: /apim/{connectorType}/{resourceName}
                const connectorTypeMatch = /\/apim\/([^/]+)\//.exec(value);
                directClientConnections[connectionName] = {
                    connectorType: connectorTypeMatch ? connectorTypeMatch[1] : connectionName.toLowerCase(),
                    connectionRuntimeUrl: value,
                };
            }
        }

        if (Object.keys(directClientConnections).length === 0) {
            return undefined;
        }

        return { directClientConnections };
    } catch (err) {
        const message = err instanceof Error ? err.message : String(err);
        outputChannel.appendLine(`Failed to parse DirectClient settings '${filePath}': ${message}`);
        return undefined;
    }
}

/**
 * Finds a named file at the folder root and in immediate subdirectories (one level deep).
 * Returns all matching paths.
 */
async function findFilesInFolder(folderPath: string, fileName: string): Promise<string[]> {
    const results: string[] = [];

    // Check root
    const rootCandidate = path.join(folderPath, fileName);
    if (await fileExists(rootCandidate)) {
        results.push(rootCandidate);
    }

    // Check immediate subdirectories
    try {
        const entries = await fs.promises.readdir(folderPath, { withFileTypes: true });
        for (const entry of entries) {
            if (entry.isDirectory() && !entry.name.startsWith(".") && entry.name !== "node_modules" && entry.name !== "bin" && entry.name !== "obj") {
                const subCandidate = path.join(folderPath, entry.name, fileName);
                if (await fileExists(subCandidate)) {
                    results.push(subCandidate);
                }
            }
        }
    } catch {
        // Ignore read errors on directory listing
    }

    return results;
}

function setupConnectionFileWatcher(
    client: LanguageClient,
    outputChannel: vscode.OutputChannel
): void {
    // Watch connections.json
    const connectionsWatcher = vscode.workspace.createFileSystemWatcher("**/connections.json");
    connectionsWatcher.onDidChange((uri) => pushMergedConnectionUpdate(uri, client, outputChannel));
    connectionsWatcher.onDidCreate((uri) => pushMergedConnectionUpdate(uri, client, outputChannel));
    fileWatcherDisposables.push(connectionsWatcher);

    // Watch local.settings.json
    const settingsWatcher = vscode.workspace.createFileSystemWatcher("**/local.settings.json");
    settingsWatcher.onDidChange((uri) => pushMergedConnectionUpdate(uri, client, outputChannel));
    settingsWatcher.onDidCreate((uri) => pushMergedConnectionUpdate(uri, client, outputChannel));
    fileWatcherDisposables.push(settingsWatcher);
}

async function pushMergedConnectionUpdate(
    uri: vscode.Uri,
    client: LanguageClient,
    outputChannel: vscode.OutputChannel
): Promise<void> {
    const config = vscode.workspace.getConfiguration("connectorSdk");
    const connections = await loadConnectionsFromWorkspace(config, outputChannel);
    if (connections) {
        client.sendNotification("custom/updateConnections", connections);
        outputChannel.appendLine(`Pushed merged connection update (triggered by ${uri.fsPath})`);
    }
}

// ─── Token Refresh ──────────────────────────────────────────────────────────────

/** Interval between token refreshes (45 minutes — well before the 60-min expiry). */
const TOKEN_REFRESH_INTERVAL_MS = 45 * 60 * 1000;

/**
 * Acquires an Azure API Hub token from the extension host and pushes it to the
 * LSP server. Runs in a non-blocking loop so hover requests never pay the
 * AzureCliCredential cold-start cost.
 *
 * Strategy:
 * 1. If the user set `connectorSdk.azure.bearerToken`, skip — the LSP server
 *    already uses the explicit token.
 * 2. Run `az account get-access-token --resource https://apihub.azure.com` in the
 *    extension host process. This is the same credential the LSP server's
 *    DefaultAzureCredential would use, but acquired here outside the hover path.
 * 3. Push the token to the LSP server via the `custom/updateApiConfig` notification.
 * 4. Repeat every 45 minutes.
 */
function startTokenRefreshLoop(
    languageClient: LanguageClient,
    config: vscode.WorkspaceConfiguration,
    outputChannel: vscode.OutputChannel
): void {
    // Increment epoch so any in-flight acquisition from a previous loop is discarded
    tokenRefreshEpoch++;
    const currentEpoch = tokenRefreshEpoch;

    // Skip if user provided an explicit bearer token (treat empty/whitespace as unset)
    const explicitToken = (config.get<string>("azure.bearerToken") ?? "").trim();
    if (explicitToken) {
        outputChannel.appendLine("[TokenRefresh] Explicit bearerToken configured - skipping auto-refresh");
        if (tokenRefreshTimer) {
            clearInterval(tokenRefreshTimer);
            tokenRefreshTimer = undefined;
        }

        languageClient.sendNotification("custom/updateApiConfig", { bearerToken: explicitToken });
        outputChannel.appendLine("[TokenRefresh] Pushed explicit bearerToken to LSP server");
        return;
    }

    // Clear any existing timer and reset the in-flight flag before starting a new loop.
    if (tokenRefreshTimer) {
        clearInterval(tokenRefreshTimer);
        tokenRefreshTimer = undefined;
    }

    tokenAcquisitionInFlight = false;

    // Acquire immediately (non-blocking), then repeat on interval
    acquireAndPushToken(languageClient, outputChannel, currentEpoch);

    tokenRefreshTimer = setInterval(
        () => acquireAndPushToken(languageClient, outputChannel, currentEpoch),
        TOKEN_REFRESH_INTERVAL_MS
    );
}

let tokenAcquisitionInFlight = false;
let tokenRefreshEpoch = 0;

async function acquireAndPushToken(
    languageClient: LanguageClient,
    outputChannel: vscode.OutputChannel,
    epoch: number
): Promise<void> {
    if (tokenAcquisitionInFlight) {
        outputChannel.appendLine("[TokenRefresh] Skipped - acquisition already in flight");
        return;
    }

    tokenAcquisitionInFlight = true;
    try {
        const token = await acquireApiHubToken(outputChannel);
        if (!token) {
            return;
        }

        // If the loop was restarted while az was running, discard the stale result
        if (epoch !== tokenRefreshEpoch) {
            outputChannel.appendLine("[TokenRefresh] Discarded stale token (loop was restarted)");
            return;
        }

        languageClient.sendNotification("custom/updateApiConfig", {
            bearerToken: token,
        });
        outputChannel.appendLine("[TokenRefresh] Pushed fresh API Hub token to LSP server");
    } catch (err) {
        const message = err instanceof Error ? err.message : String(err);
        outputChannel.appendLine(`[TokenRefresh] Failed to push token: ${message}`);
    } finally {
        tokenAcquisitionInFlight = false;
    }
}

/**
 * Acquires an access token for the API Hub resource using `az account get-access-token`.
 * Returns the raw access token string, or undefined on failure.
 */
async function acquireApiHubToken(outputChannel: vscode.OutputChannel): Promise<string | undefined> {
    const { execFile } = await import("child_process");

    return new Promise((resolve) => {
        execFile(
            "az",
            ["account", "get-access-token", "--resource", "https://apihub.azure.com", "--query", "accessToken", "-o", "tsv"],
            // shell: true is required on Windows because `az` is actually `az.cmd` (a batch script)
            // and execFile without a shell can't execute .cmd files. The command and args are
            // hardcoded constants, so shell: true does not introduce injection risk.
            { timeout: 15000, shell: process.platform === "win32" },
            (error, stdout, stderr) => {
                if (error) {
                    outputChannel.appendLine(`[TokenRefresh] az CLI token acquisition failed: ${error.message}`);
                    if (stderr) {
                        outputChannel.appendLine(`[TokenRefresh] stderr: ${stderr.trim()}`);
                    }

                    resolve(undefined);
                    return;
                }

                const token = stdout.trim();
                if (!token) {
                    outputChannel.appendLine("[TokenRefresh] az CLI returned empty token");
                    resolve(undefined);
                    return;
                }

                outputChannel.appendLine("[TokenRefresh] Acquired API Hub token via az CLI");
                resolve(token);
            }
        );
    });
}
