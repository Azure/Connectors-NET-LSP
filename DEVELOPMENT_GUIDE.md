# LSP Server Development & Debugging Guide

Complete guide for developing and debugging the Connectors LSP Server with automatic rebuild and breakpoint support.

---

## 🚀 Quick Start

### Watch Mode Development (No Debugging)

**Terminal 1: Start watch mode**
```bash
cd <repo-root>   # connector-sdk-lsp
dotnet watch --project Server --no-hot-reload
```

**VS Code: Start extension**

1. Open `vscode-extension/` as its own VS Code window
2. Run `npm install` in the terminal if you haven't already
3. Press **F5** — the included `.vscode/launch.json` defines **"Run Extension"** which compiles TypeScript via the `npm: watch` pre-launch task and launches an Extension Development Host

Alternatively, set `connectorSdk.lspServerPath` in your VS Code settings to point to the server DLL built from `Server/`.

**Make changes:**
- Edit C# files → Watch rebuilds and restarts automatically
- In Extension Development Host: **Ctrl+Shift+P** (or **Cmd+Shift+P** on macOS) → **"Reload Window"**
- Changes are live!

---

### Debugging with Breakpoints

**Step 1: Start extension** (see above for how to launch the Extension Development Host)

**Step 2: Attach debugger** (connector-sdk-lsp window)
```
Press F5 → "🚀 Attach to Local LSP Server (One-Click)"
```

**Step 3: Pick the process**
- Pre-launch shows: `✓ Found LSP Server process: PID 12345`
- Process picker appears
- Click the process with `SdkLspServer.dll` in command line
- Debugger attaches!

**Step 4: Debug**
- Set breakpoints anywhere
- Use the extension → breakpoints hit!

---

## 📋 Development Workflow

### Typical Day

```bash
# Terminal 1: Auto-rebuild
cd <repo-root>   # connector-sdk-lsp
dotnet watch --project Server --no-hot-reload

# VS Code 1: Extension development (vscode-extension/)
code .
Press F5

# VS Code 2: Server debugging (connector-sdk-lsp)
code .
Open HoverHandler.cs (or any file)
Set breakpoints
Press F5 → Pick process
Debug!
```

### Making Changes

1. **Edit C# code** → Watch rebuilds automatically
2. **Reload Extension Host** → Cmd+Shift+P → "Reload Window"
3. **Reattach debugger** → Press F5 → Pick process
4. **Test changes** → Breakpoints hit with new code

---

## 🔧 How It Works

### Development Server Resolution

The VS Code extension resolves the LSP server path in the following order:

1. **Explicit setting** — `connectorSdk.lspServerPath` in VS Code settings
2. **Sibling debug build** — `<repo-root>/Server/bin/Debug/net8.0/SdkLspServer.dll` (preferred in dev mode so `dotnet build` output is used instead of a stale published artifact)
3. **Bundled server** — `<extension-path>/server/SdkLspServer.dll` (populated by publishing the server into `vscode-extension/server/` before packaging; see [VSIX packaging](#vs-code-extension-packaging) in the README)

For development, the sibling build output is discovered automatically when you open the repo root in VS Code and launch the extension via F5. Alternatively, set `connectorSdk.lspServerPath` in `.vscode/settings.json`:

```json
{
  "connectorSdk.lspServerPath": "<repo-root>/Server/bin/Debug/net8.0/SdkLspServer.dll"
}
```

### Watch Mode

`dotnet watch` monitors C# files and automatically rebuilds when you save changes.

### Debugger Attachment

The "One-Click" configuration:
1. Runs a pre-launch task to verify the server is running
2. Shows a process picker with all processes
3. You select the one with `SdkLspServer.dll`
4. Debugger attaches instantly

---

## 🎯 Key Breakpoint Locations

### Handle Hover Requests
```csharp
// HoverHandler.cs - line ~40
public async Task<Hover?> Handle(HoverParams request, CancellationToken cancellationToken)
{
    // Set breakpoint here ⬅️
    var uri = request.TextDocument.Uri;
    ...
}
```

### Handle Code Lens
```csharp
// CodeLensHandler.cs - line ~35
public async Task<CodeLensContainer?> Handle(CodeLensParams request, CancellationToken cancellationToken)
{
    // Set breakpoint here ⬅️
    ...
}
```

### SDK Index Queries
```csharp
// SdkIndex.cs - line ~150
public MethodInfo? FindMethod(string methodName)
{
    // Set breakpoint here ⬅️
    ...
}
```

### Document Access
```csharp
// BufferManager.cs - line ~30
public bool TryGetBuffer(Uri uri, out string content)
{
    // Set breakpoint here ⬅️
    return _buffers.TryGetValue(uri, out content);
}
```

### Server Initialization
```csharp
// Program.cs - line ~15
private static async Task Main(string[] args)
{
    // Set breakpoint here ⬅️ to debug startup
    ...
}
```

---

## 🐛 Troubleshooting

### "LSP Server not running" error

**Cause**: Extension not started  
**Fix**: Start the VS Code extension first (LogicAppsUX, F5)

### Multiple processes in picker

**Cause**: Multiple dotnet processes running  
**Solution**: Look for `SdkLspServer.dll` in command line. The pre-launch output tells you the exact PID.

### Breakpoints show as hollow/unverified

**Cause**: Source mismatch or wrong process  
**Fix**: 
```bash
dotnet clean && dotnet build
# Then reattach debugger
```

### "Can't find process with SdkLspServer.dll"

**Check**:
```bash
./get-lsp-pid.sh  # Shows if server is running
```

If not running, start the extension first.

### Changes not appearing

**Verify**:
1. Watch task shows "Waiting for a file to change..." (rebuild complete)
2. You reloaded the Extension Development Host window
3. You're testing in the Extension Development Host (not main window)

### Debugger attached but breakpoints don't hit

**Check**:
1. Code path is executing (add log statement to verify)
2. Correct process selected (run `./get-lsp-pid.sh` to confirm)
3. Using dev build (check env var is set in launch.json)
4. No code mismatch (rebuild: `dotnet clean && dotnet build`)

---

## 🛠 Helper Scripts

### `find-lsp-process.sh`
Shows running LSP server processes with detailed info.

```bash
./find-lsp-process.sh
# Output:
# ✅ Found LSP Server process(es):
# PID 12345: dotnet .../SdkLspServer.dll
```

### `get-lsp-pid.sh`
Used by the pre-launch task. Verifies server is running before showing process picker.

---

## 📁 Modified Files

### connector-sdk-lsp (this repo)

- **Server/Program.cs** - `--wait-for-debugger` flag support
- **.vscode/launch.json** - "One-Click" debug configuration
- **.vscode/tasks.json** - Watch task + verification task
- **.vscode/settings.json** - Enhanced process picker display
- **vscode-extension/** - VS Code extension with SDK discovery, connection file watching, and connection merging

---

## 🎓 Advanced Tips

### Two-Window Debugging

Debug both the extension AND the server simultaneously:

**Window 1 (Extension):**
- Set breakpoints in TypeScript extension code (`vscode-extension/src/extension.ts`)
- Press F5

**Window 2 (Server):**
- Set breakpoints in C# server code
- Press F5 → Attach

**Flow:**
- Trigger feature in Extension Development Host
- Breakpoint hits in TypeScript (Window 1)
- Step through, see LSP request sent
- Continue → breakpoint hits in C# (Window 2)
- Step through server handling
- See response returned

### Conditional Breakpoints

Right-click breakpoint → "Edit Breakpoint" → Add condition:

```csharp
// Only break for specific file
uri.ToString().Contains("MyFile.cs")

// Only break for specific position
position.Line == 42

// Only break when condition is true
someVariable != null && someVariable.Count > 0
```

### Logpoints

Non-breaking trace points that log to Debug Console:

Right-click in gutter → "Add Logpoint"
```
Message: Position: {position.Line},{position.Character}
```

### Watch Expressions

Add to Watch panel:
- `uri.ToString()` - Current document URI
- `position` - Current cursor position
- `_buffers.Count` - Number of tracked documents

---

## 📚 Additional Resources

- **HTTPCLIENT_GUIDE.md** - HTTP client integration

---

## ✨ Benefits

- ⚡ **Auto-rebuild in seconds** - Watch mode rebuilds and restarts automatically
- 🐛 **Full debugging** - Breakpoints anywhere
- 🔍 **Inspect everything** - Variables, call stacks, LSP messages
- 🔄 **Fast iteration** - Code → Rebuild → Reload → Debug
- 🎯 **One-click attach** - Minimal friction
- 📚 **Well documented** - This guide!

---

**Happy developing and debugging! 🚀**

Last updated: October 2025
