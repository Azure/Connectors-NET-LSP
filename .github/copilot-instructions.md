# Copilot Instructions for Connectors-NET-LSP

## Overview

This repository contains the Language Server Protocol (LSP) server and VS Code extension for Azure Connectors SDK development. It provides intelligent code assistance including hover, completion, CodeLens, and SDK assembly analysis. Code must follow the team's coding conventions based on BPM repo standards.

## Quick Reference: Coding Style Rules

### File Structure

```csharp
//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace SdkLspServer
{
    public class YourClass
    {
    }
}
```

**Rules:**

- Copyright header: Use `//----` (4 dashes) format with double space before "All rights reserved"
- Usings OUTSIDE namespace (standard C# convention)
- Usings sorted: System.* first, then alphabetically
- No empty lines between using groups

### Naming and Qualification

| Element | Rule | Example |
|---------|------|---------|
| Static members | Qualify with class name | `MyClass.StaticMethod()` |
| Instance members | Qualify with `this.` | `this.instanceField` |
| Private fields | Use `_camelCase` | `private readonly string _connectionString;` |
| Constants | Qualify with class name | `MyClass.DefaultTimeout` |
| Local variables | Use complete English terms | `parameter` not `p`, `method` not `m` |
| Lambda parameters | Use descriptive names | `methods.Where(method => ...)` not `m => ...` |

**Variable naming rules:**

- Use complete, unabbreviated English terms for all identifiers
- No single-letter variable names, even in lambdas (use `arg`, `item`, `method`, `parameter`)
- No placeholder names (`blah`, `foo`, `temp`, `x`) — always use meaningful names

### File Organization

**One type per file:**

- Declare only one class, struct, enum, or interface per file
- File name must match the type name
- Exception: Nested types (e.g., private helper classes) are allowed within the containing type

### Async/Await Format

**ALWAYS use this multi-line format:**

```csharp
var result = await this.httpClient
    .GetAsync(requestUri)
    .ConfigureAwait(continueOnCapturedContext: false);
```

**Rules:**

- Period on NEW line, not end of previous line
- Arguments indented ONE level (4 spaces)
- ALWAYS use `ConfigureAwait(continueOnCapturedContext: false)` with explicit parameter name
- Exception: Skip await for lone return statement (unless inside `using` block)

**DO NOT:**

```csharp
// Wrong: ConfigureAwait without named parameter
.ConfigureAwait(false);

// Wrong: Method call on same line as object
var result = await this.httpClient.GetAsync(requestUri)
    .ConfigureAwait(continueOnCapturedContext: false);

// Wrong: Dot at end of line
var result = await this.httpClient.
    GetAsync(requestUri);
```

### Method Calls with Multiple Parameters

**Single line** if all parameters fit:

```csharp
this.DoSomething(param1, param2);
```

**Boolean parameters MUST always use named arguments:**

```csharp
// Correct
this.CreateHandler(logger, isEnabled: true);

// Wrong - unnamed boolean is ambiguous
this.CreateHandler(logger, true);
```

**Multi-line with named parameters:**

```csharp
return await this
    .ProcessRequestAsync(
        requestUri: uri,
        content: payload,
        cancellationToken: cancellationToken)
    .ConfigureAwait(continueOnCapturedContext: false);
```

### Comments

**Inline comments - use NOTE format:**

```csharp
// NOTE(username): Explanation of why this code exists.
var result = DoSomething();
```

**Rules:**

- Empty line ABOVE comment (unless first line in block)
- NO empty line between comment and code it describes
- Prefix: `// NOTE(username):` where username is your GitHub username
- Do NOT comment on the 'what' unless the code is obscure; instead comment on the 'why' when appropriate

**XML documentation - required for all public APIs:**

```csharp
/// <summary>
/// Processes the incoming request and returns the result.
/// </summary>
/// <param name="request">The request to process.</param>
public async Task<Response> ProcessAsync(Request request)
```

**Rules:**

- End descriptions with period
- Document return values with `<returns>` tags on all public methods
- Use `<see cref="ClassName"/>` for type references

### Exception Handling

```csharp
try
{
    await this
        .DoWorkAsync()
        .ConfigureAwait(continueOnCapturedContext: false);
}
catch (SpecificException ex)
{
    this.logger.LogError(ex, "Failed: '{Message}'.", ex.Message);
    throw;
}
catch (Exception ex) when (!ex.IsFatal())
{
    throw new InvalidOperationException(message: "Operation failed.", innerException: ex);
}
```

**Rules:**

- Exception variable name: `ex` (not `exception`)
- Use exception filter `when (!ex.IsFatal())` for general catches to avoid catching fatal exceptions
- Wrap inserted values in single quotes in error messages
- End error messages with period
- **All exceptions must have descriptive messages** — never throw exceptions without context

### String Comparison

**ALWAYS use StringComparison:**

```csharp
// Correct
string.Equals(str1, str2, StringComparison.OrdinalIgnoreCase)
str.StartsWith(prefix, StringComparison.Ordinal)
```

**DO NOT:**

```csharp
str1 == str2
str1.Equals(str2)
```

### Spacing and Braces

**Empty line after closing brace:**

```csharp
if (condition)
{
    DoSomething();
}

DoSomethingElse();  // Empty line above
```

**NO empty line before closing brace.**

**Switch statements - empty line between cases:**

```csharp
switch (value)
{
    case "A":
        HandleA();
        break;

    case "B":
        HandleB();
        break;

    default:
        throw new InvalidOperationException();
}
```

### Variable Declaration

**Use `var` when type is obvious:**

```csharp
var items = new List<string>();
var response = await this.GetResponseAsync();
```

**Use explicit type for null initialization:**

```csharp
byte[] buffer = null;  // Not: var buffer = (byte[])null;
```

### Ternary Operators

**Put `?` and `:` at START of new line:**

```csharp
var result = condition
    ? valueIfTrue
    : valueIfFalse;
```

### Logical Operators

**Put `||` and `&&` at END of line:**

```csharp
if (string.IsNullOrEmpty(value1) ||
    string.IsNullOrEmpty(value2) ||
    string.IsNullOrEmpty(value3))
```

### Access Modifiers

**ALWAYS explicit - order: access, static, readonly, other:**

```csharp
public static readonly string DefaultValue = "default";
private readonly ILogger _logger;
internal async Task ProcessAsync()
```

### Class Layout Order

1. Constants
2. Static fields
3. Instance fields
4. Constructors
5. Properties
6. Public methods
7. Internal methods
8. Private methods

Within each group: public → internal → private

## Patterns to Avoid

| Anti-Pattern | Correct Pattern |
|--------------|-----------------|
| `.Result` on Task | `await task.ConfigureAwait(continueOnCapturedContext: false)` |
| `.Wait()` on Task | `await task.ConfigureAwait(continueOnCapturedContext: false)` |
| `Task.Run()` for I/O | `await` the async method directly |
| `new Exception("msg.")` | `new SpecificException(message: "msg.")` |
| Magic numbers | Named constants (e.g., `MyClass.DefaultTimeoutSeconds`) |
| Magic strings (e.g., `"type"`, `"object"`) | Named constants (e.g., `SchemaPropertyNames.Type`) |
| `[0]` or `.First()` | `.Single()` (or `.SingleOrDefault()` + explicit validation) |

## Pre-Commit Review Checklist

Before committing, verify:

1. **Coding standards** — Grep changed files for:
   - Single-letter lambda parameters (`p =>`, `v =>`, `c =>`) → rename to descriptive names
   - `StringComparison` missing on `.StartsWith()`, `.EndsWith()`, `.Contains()`, `.IndexOf()`, `.Replace()`
   - Unnamed boolean arguments → add named parameter
   - `ConfigureAwait(false)` → `ConfigureAwait(continueOnCapturedContext: false)`

2. **Comment staleness** — For every behavior change, search for comments/docs describing the old behavior. Update all of them, not just the code. Common locations:
   - XML `<summary>` docs on the changed method
   - Inline comments near the changed code
   - Startup/initialization comments referencing the feature
   - README sections describing the feature

3. **Edge cases** — For every new parameter, function, or code path, ask:
   - What if the input is null/empty?
   - What if the input is the wrong type (e.g., DLL path where nupkg expected)?
   - What if a dependency is missing (e.g., no `project.assets.json`)?
   - What if a collection has 0 items? 1 item? Many items?

4. **Design completeness** — Before pushing a fix, review the full call chain:
   - Does the caller handle all return values correctly?
   - Are property/parameter names accurate for all callers (not just the original use case)?
   - Will the next reviewer find a cascading issue from this change?

5. **Platform edge cases** — Consider all target environments:
   - Windows: drive-letter roots (`C:\`), backslash paths, case-insensitive filesystem
   - macOS/Linux: no `grep -P`, case-sensitive filesystem, forward slashes, BSD vs GNU tools
   - CLI args: empty values (`--flag=`), missing values (`--flag --next-flag`), multiple values
   - NuGet: multi-TFM projects, multiple package folders, case variations in package keys, missing `project.assets.json`
   - Large workspaces: skip `node_modules`, `.git`, `bin`, `obj` during filesystem scans

## Design-First Approach

Before implementing a new feature or significant change:

1. **Draft the API surface** — Write function signatures, parameter types, return types, setting names, and CLI arguments in a scratch comment or directly in the code as stubs
2. **Review naming** — Are names accurate for all callers? Will they be misleading when the feature evolves?
3. **Enumerate edge cases** — Walk through the platform checklist above for each new parameter
4. **Consider the full call chain** — Trace from CLI/extension entry point through to the final consumer. Identify null paths, empty collections, and type mismatches
5. **Then implement** — Write the body only after the design is settled

This prevents cascading review cycles where each fix introduces new naming, edge case, or consistency issues.

## Testing

```csharp
[TestMethod]
public async Task MethodName_Scenario_ExpectedResult()
{
    // Arrange
    var input = CreateTestInput();

    // Act
    var result = await this.service
        .ProcessAsync(input)
        .ConfigureAwait(continueOnCapturedContext: false);

    // Assert
    Assert.IsNotNull(result);
}
```

**Rules:**

- Test method naming: `MethodName_Scenario_ExpectedResult`
- Use async/await, never `.Result`
- Use `ConfigureAwait(continueOnCapturedContext: false)` in tests too

## Git Workflow

- Branch naming: `feature/description`, `fix/description`, `docs/description`
- Never push directly to main
- Always create PR for review

## Architecture: Diagnostic Validators

### Validators target SDK consumers, not SDK authors

Diagnostic validators (`IDiagnosticValidator`) analyze **customer code** that calls SDK connector
client methods — not the SDK source code itself. For example, a validator checks whether
`client.GetAllTablesAsync("foobar", ct)` passes a valid value, not whether the SDK's
`[DynamicValues]` attribute is correctly declared.

- **Call-site analysis** requires `CompilationService` for semantic model (resolve `IMethodSymbol`,
  inspect `IParameterSymbol` attributes)
- **Attribute-declaration analysis** would only help SDK generator authors — not the LSP's target audience

### `[DynamicValues]` is not string-only

The SDK uses `[DynamicValues]` on non-string parameters too:

```csharp
// SharePoint approval types use int
[DynamicValues("GetApprovalTypes")] int approvalType
```

Do not assume `[DynamicValues]` parameters are always `string`.

### DynamicValueItem.Value quoting

Hover and completion handlers store cached values with surrounding `"` for code insertion:

```csharp
Value = $"\"{item.Name}\""   // e.g., "\"siteUrl\""
```

Any code comparing cached values against `Token.ValueText` (which returns unquoted content)
must strip surrounding quotes first. See `ValuesMatch()` in `DynamicValuesValidator`.

### CompilationService assembly identity

When distinguishing source-defined types from SDK metadata references, compare against
`semanticModel.Compilation.AssemblyName` rather than hard-coding the compilation name.
Currently `CompilationService.GetCompilation()` uses `"LspAnalysis"` and
`CreateSdkMetadataCompilation()` uses `"SdkMetadataCompilation"`, but comparing dynamically
ensures the validator stays correct if these names change.

### VSTHRD103: Prefer async GetText in validators

In diagnostic validators (which implement `IDiagnosticValidator.ValidateAsync`), prefer
`await tree.GetTextAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false)`
over synchronous `SyntaxTree.GetText()`. The `VSTHRD103` analyzer flags synchronous calls
in async methods. Note: `CompilationService` uses `GetText()` synchronously in a non-async
context, which is fine.

### Development: DLL lock workaround

The VS Code extension locks `SdkLspServer.dll` at `Server/bin/Debug/net8.0/`. When iterating
on the server code while the extension is running:

1. Build to an alternate path: `dotnet build Server/SdkLspServer.csproj -o <writable-folder>`
   (e.g., `C:\temp\lsp-build` on Windows or `/tmp/lsp-build` on macOS/Linux)
2. Point `connectorSdk.lspServerPath` in workspace settings to the alternate path
3. Reload the VS Code window to pick up the new DLL
