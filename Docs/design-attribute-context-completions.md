# LSP Phase 2: Attribute-Context Completions — Design Investigation

> **Created:** 2026-03-22  
> **Context:** DX Vision Phase 2 / Gap 1 — attribute-context completions for `[ConnectorTrigger]`  
> **Investigated by:** Dobby (overnight execution)

## Current State

The CompletionHandler provides three active completion paths:

1. **String literals** — Connection parameters & dynamic values
2. **Generic type arguments** — Trigger payload types (`*TriggerPayload`)
3. **Dot-triggered member access** — `GetManagedConnectors()` chain

All three use Roslyn's AST + semantic model for context detection.

## What Already Exists (Reusable Infrastructure)

| Component | Location | Reuse for Phase 2 |
|-----------|----------|-------------------|
| Roslyn semantic model building | `CompletionHandler.cs ~705` | Assembly loading for `ConnectorNames` classes |
| SDK type enumeration | `SdkIndex.cs` | Discover `ConnectorNames`, `*TriggerOperations` classes |
| Connection name filtering | `ConnectionsHelper.GetConnectionNamesForConnector()` | Filter connections by connector type |
| Attribute detection pattern | `HoverHandler.IsConnectionParameter()` | Reference for walking `AttributeSyntax` |
| Dynamic values API | `DynamicOperationsRegistry` + `LSPStore` | Trigger parameter dynamic values |

## What's Missing (Gaps)

### Gap 1: Attribute Parameter Detection (P1)

The CompletionHandler has no path for detecting when the cursor is inside a C# **attribute argument** like `[ConnectorTrigger(ConnectorName = "|")]`. Current detection only handles method call arguments.

**Implementation approach:**
```csharp
// Walk up AST from token to find AttributeArgumentSyntax
AttributeArgumentSyntax? attrArg = token.Parent?.AncestorsAndSelf()
    .OfType<AttributeArgumentSyntax>()
    .FirstOrDefault();

if (attrArg != null)
{
    var attr = attrArg.AncestorsAndSelf().OfType<AttributeSyntax>().FirstOrDefault();
    var attrName = attr?.Name?.ToString();  // "ConnectorTrigger"
    var paramName = attrArg.NameEquals?.Name.Identifier.Text;  // "ConnectorName"
    // Route to appropriate handler based on paramName
}
```

**Critical:** Attribute detection MUST run before string literal detection (higher priority) because `OperationName = ""|""` is both a string literal and an attribute value.

### Gap 2: ConnectorName Constant Enumeration (P2)

Need to load the `ConnectorNames` static class from the SDK assembly, extract public `const string` fields, and offer them as completion items.

**Data source:** `ConnectorNames.Office365`, `ConnectorNames.SharePointOnline`, `ConnectorNames.Teams` (from B9 implementation)

**Implementation:** Use existing `SdkIndex` assembly loading + `Type.GetFields(BindingFlags.Public | BindingFlags.Static)` to discover constants.

### Gap 3: OperationName Cascading (P3)

When `ConnectorName = ConnectorNames.Office365` is already set, the LSP should offer only Office365 trigger operations (`Office365TriggerOperations.*`).

**Approach:**
1. Parse the attribute to find the `ConnectorName` value (read sibling parameter)  
2. Map connector name → `{Connector}TriggerOperations` class name
3. Load the class, extract constants, offer as completions

### Gap 4: Connection Filtering (P4)

Show only connections matching the selected connector type. `ConnectionsHelper.GetConnectionNamesForConnector()` already exists but is unused.

### Gap 5: Trigger Parameter Discovery (P5)

Show trigger parameter names from `{Connector}TriggerParameters.{Operation}.*` constants. Requires the generator changes from GAP3 (now implemented in BPM PR #15134321).

---

## Phase 2 Implementation Notes (2026-03-23)

> **Implemented by:** Dobby (overnight execution)

### Task 1 Research Findings: Roslyn Completion Best Practices

**OmniSharp-roslyn approach:** OmniSharp delegates entirely to `Microsoft.CodeAnalysis.Completion.CompletionService.GetCompletionsAsync()` — Roslyn's built-in completion engine. It does NOT do custom AST walking for completions. Roslyn internally handles incomplete code through error recovery.

**Key insight — Roslyn's limitation for our use case:**
When a developer types `Deserialize<` (no closing `>`), Roslyn's parser uses error recovery but produces an `ExpressionStatement` with a `LessThanExpression`, NOT a `TypeArgumentListSyntax`. The `<` becomes a `LessThanToken` that's part of a comparison expression, not a generic type argument. This is the fundamental limitation — using Roslyn's CompletionService would give us C# completions but NOT our SDK-specific trigger payload suggestions.

**Speculative semantic model:** `SemanticModel.GetSpeculativeTypeInfo()` is designed for "what if this type were at position X" scenarios with complete type names. It doesn't help with incomplete `<` contexts because there's no complete type to resolve.

**Decision:** The text-based approach from PR #18 is the right direction, but we tightened it to ONLY fire for known deserialization method names (`Deserialize`, `DeserializeAsync`, `DeserializeObject`, `DeserializeObjectAsync`). This eliminates false positives from comparisons (`if (x <`), LINQ operators, and other `<` usage.

### Implementation Summary

| Gap | Status | Approach |
|-----|--------|----------|
| P1: Attribute Parameter Detection | **Implemented** | Dual-path: AST walking (`AttributeArgumentSyntax`) + text-based fallback for incomplete code |
| P2: ConnectorName Constants | **Implemented** | `SdkIndex` eagerly discovers `ConnectorNames` class via `MetadataLoadContext` + `GetRawConstantValue()` |
| P3: OperationName Cascading | **Implemented** | Reads sibling `ConnectorName` from attribute, maps to `*TriggerOperations` class, filters constants |
| P4: Connection Filtering | **Implemented** | Wired `ConnectionsHelper.GetConnectionNamesForConnector()` into attribute `Connection` parameter completion |
| P5: Trigger Payload Filtering | **Implemented** | `GetPayloadTypeForOperation()` maps operation → payload type; `HandleTriggerPayloadTypeCompletion` reads enclosing method's attribute |
| Robust `<` Detection | **Implemented** | Whitelist of deserialization method names instead of any `<` after a method name |

### New SdkIndex Capabilities

The `SdkIndex` now discovers at startup (via `MetadataLoadContext` + `GetRawConstantValue()`):
- `ConnectorNameConstants` — All `public const string` fields from `ConnectorNames` class
- `TriggerOperationsByConnector` — Dictionary of connector name → `ImmutableArray<SdkConstant>` from `*TriggerOperations` classes
- `GetPayloadTypeForOperation(connector, operation)` — Maps to `{Connector}{Operation}TriggerPayload` type name

### CompletionHandler Priority Chain (Updated)

```
PRIORITY 0: Attribute argument context
  → [ConnectorTriggerMetadata(ConnectorName = |)] → ConnectorNames.* constants
  → [ConnectorTriggerMetadata(OperationName = |)] → *TriggerOperations.* (filtered by ConnectorName)
  → [ConnectorTriggerMetadata(Connection = |)]    → Connections (filtered by ConnectorName)
  Uses: AST (AttributeArgumentSyntax) + text-based fallback

PRIORITY 1: String literal context (unchanged)
  → Connection parameters
  → Dynamic values ([DynamicValues] attribute)

PRIORITY 2: Generic type argument / Deserialize< context (enhanced)
  → ONLY fires for known deserialization method names
  → If enclosing method has [ConnectorTriggerMetadata], filters to matching payload type
  → Otherwise, shows all *TriggerPayload types

PRIORITY 3: Dot-triggered member access (unchanged)
  → GetManagedConnectors() chain
```

### Risk: String-in-Attribute Collision — Resolved

The attribute handler (Priority 0) now runs BEFORE the string literal handler (Priority 1). When the cursor is at `ConnectorName = "office365"`, the attribute handler catches it and returns `ConnectorNames.*` completions, preventing the string handler from returning generic connection completions.

### Test Coverage

44 new tests added (78 total, up from 34):
- `AttributeCompletionTests` — AST-based attribute parameter extraction, text-based fallback, deserialization method whitelist, enclosing method attribute detection
- `SdkIndexConstantDiscoveryTests` — Validates ConnectorNames, TriggerOperations, and payload type mapping from actual SDK nupkg

## Recommended Implementation Order

1. **Add `HandleAttributeCompletionAsync()`** — New method in CompletionHandler
2. **Add attribute detection to `Handle()`** — Insert before string literal detection
3. **Implement ConnectorName completions** — Reflect on `ConnectorNames` class
4. **Implement OperationName cascading** — Read sibling, reflect on `*TriggerOperations`
5. **Implement Connection filtering** — Wire up `GetConnectionNamesForConnector()`
6. **Implement trigger parameter discovery** — Reflect on `*TriggerParameters.*`

## Key Files

| File | Purpose |
|------|---------|
| `Server/Handlers/CompletionHandler/CompletionHandler.cs` | Primary file to modify |
| `Server/Handlers/HoverHandler/HoverHandler.cs` | Reference for attribute walking patterns |
| `Server/Services/Connections/ConnectionsHelper.cs` | Connection filtering (ready to use) |
| `Server/SdkIndex.cs` | SDK type discovery (ready to use) |

## Risk: String-in-attribute collision

The most subtle issue is that `ConnectorName = "office365"` is simultaneously:
- A string literal (current handler would fire)
- An attribute argument value (new handler should fire)

The attribute handler must run first and short-circuit the string handler. Otherwise, the developer gets generic string completions instead of `ConnectorNames.*` constant suggestions.
