# Azure Workflows SDK - Connector Documentation

**SDK Version:** 1.141.0.10  
**Namespace:** `Microsoft.Azure.Workflows.Sdk.Agents`  
**Last Updated:** 2025-11-05

---

## 📚 Documentation Index

### Quick Start
- **[AGENT_BUILDER_GUIDE.md](AGENT_BUILDER_GUIDE.md)** - 🆕 Complete guide to building AI agents
- **[SDK_ANALYSIS.md](SDK_ANALYSIS.md)** - Complete SDK structure and API reference
- **[SDK_UPDATE_SUMMARY.md](SDK_UPDATE_SUMMARY.md)** - Latest changes and migration guide
- **[LLM_QUICK_REF.md](LLM_QUICK_REF.md)** - Quick reference for LLM agents

### Maintenance
- **[REGENERATION_GUIDE.md](REGENERATION_GUIDE.md)** - How to regenerate documentation
- **[DOCS_STRUCTURE.md](DOCS_STRUCTURE.md)** - Documentation organization
- **[regenerate_docs.sh](regenerate_docs.sh)** - Automated regeneration script

### Connector Documentation
- **[01_MSNWEATHER.md](01_MSNWEATHER.md)** - MSN Weather connector (8 methods)
- **[02_MICROSOFTFORMS.md](02_MICROSOFTFORMS.md)** - Microsoft Forms connector (3 methods)
- **[03_TEAMS.md](03_TEAMS.md)** - Microsoft Teams connector (64 methods)
- **[04_OFFICE365.md](04_OFFICE365.md)** - Office 365 connector (91 methods)
- **[05_OUTLOOK.md](05_OUTLOOK.md)** - Outlook connector (59 methods)
- **[06_COMMONDATASERVICE.md](06_COMMONDATASERVICE.md)** - Common Data Service connector (28 methods)

---

## 🎯 Overview

This documentation covers the **Microsoft.Azure.Workflows.Sdk.Agents** package, which provides typed C# extension methods for Azure Logic Apps connectors.

### Supported Connectors

| Connector | Methods | Change | Actions | Triggers | Primary Use Cases |
|-----------|---------|--------|---------|----------|-------------------|
| **Office 365** | 91 | +41 🆕 | 73 | 18 | Calendar, contacts, email operations |
| **Teams** | 64 | +29 🆕 | 51 | 13 | Team/channel management, messaging |
| **Outlook** | 59 | +26 🆕 | 47 | 12 | Email and calendar management |
| **Common Data Service** | 28 | +13 🆕 | 24 | 4 | CDS record and entity operations |
| **MSN Weather** | 8 | +3 🆕 | 5 | 3 | Weather forecasts and conditions |
| **Microsoft Forms** | 3 | +1 🆕 | 2 | 1 | Form responses and webhooks |
| **TOTAL** | **253** | **+113** | **202** | **51** | |

---

## 🚀 Quick Start

### Basic Usage Pattern

```csharp
using Microsoft.Azure.Workflows.Sdk.Agents;
using Microsoft.Azure.Workflows.Sdk.Agents.Connectors.Teams;

// Get managed connectors
var connectors = WorkflowActions.ManagedConnectors;

### Basic Usage Pattern

```csharp
using Microsoft.Azure.Workflows.Sdk.Agents;

// Access managed connectors via static property
var connectors = WorkflowActions.ManagedConnectors;

// Instantiate a connector with connection ID
var teams = connectors.Teams("myTeamsConnection");

// Use connector methods (no connectionId parameter needed)
var allTeams = teams.GetAllTeams();
```

### Entry Point

All connectors are accessed via the static `WorkflowActions` class:

```csharp
public static class WorkflowActions
{
    public static WorkflowBuiltInActions BuiltIn { get; }
    public static WorkflowManagedActions ManagedConnectors { get; }
}
```

### Connector Instantiation

Each connector is instantiated with a connection ID:

```csharp
// Actions
var forms = WorkflowActions.ManagedConnectors.Microsoftforms("connection-id");
var weather = WorkflowActions.ManagedConnectors.Msnweather("connection-id");
var teams = WorkflowActions.ManagedConnectors.Teams("connection-id");

// Triggers
var formsTrigger = WorkflowActions.ManagedTriggers.Microsoftforms("connection-id");
```

### Available Connectors

```csharp
// All 6 connectors available via extension methods:
.Commondataservice("connection-id")
.Microsoftforms("connection-id")
.Msnweather("connection-id")
.Office365("connection-id")
.Outlook("connection-id")
.Teams("connection-id")
```

---

## 📖 Key Concepts

### Actions vs Triggers

**Actions** - Operations that execute within a workflow:
```csharp
IOutputWorkflowAction<TResponse> MethodName(...)
IWorkflowAction MethodName(...)  // No output
```

**Triggers** - Events that start a workflow:
```csharp
IOutputWorkflowTrigger<TResponse> MethodName(...)
IWorkflowTrigger MethodName(...)  // No output
```

### Connection Parameter

Connection ID is specified once when instantiating the connector:

```csharp
// Connection bound to instance
var forms = WorkflowActions.ManagedConnectors
    .Microsoftforms("forms-connection");

// All methods automatically use "forms-connection"
forms.GetFormResponseById(...)
```

This references a connection configured in `connections.json`.

**NEW in v1.141.0.9:** Connection ID is no longer passed to individual methods.

### Dynamic Values

Parameters marked with `[DynamicValues("OperationName")]` create dependent dropdowns:

```csharp
[DynamicValues("ListForms")] Expression<Func<string>> formId
```

The LSP server fetches values by calling the specified operation.

---

## 🔄 Recent Changes (v1.141.0.10)

### 🆕 NEW: AgentBuilder Class

**MAJOR NEW FEATURE:** Complete AI agent configuration support!

Build AI-powered workflow agents with Azure OpenAI or other AI models:

```csharp
var agent = new AgentBuilder
{
    AgentModelType = AgentModelType.AzureOpenAI,
    DeploymentId = "gpt-4.1",
    ConnectionName = "agent",  // AI model connection
    Messages = new AgentPromptMessage[]
    {
        new AgentPromptMessage
        {
            Role = MessageRole.System,
            Content = "You are a helpful assistant..."
        }
    }
};

// Add tools (connector actions) for the agent to use
agent.AddTool<WeatherParams>(/* ... */);
```

**See:** [AGENT_BUILDER_GUIDE.md](AGENT_BUILDER_GUIDE.md) for complete documentation.

### Massive API Expansion (+113 New Methods!)

**80% increase in available connector methods!**

- ⚠️ **BREAKING:** Many parameters now use Input enums/classes instead of strings
- ✅ **113 new methods** added across all connectors (+80.7%)
- ✅ **AgentBuilder** class for AI agent workflows
- ✅ **Agent connection decorator** with `[Agent]` attribute
- ✅ Type safety enforced at compile time
- ✅ IntelliSense provides valid enum values

### New Methods by Connector
- **Office 365:** +41 methods (50 → 91, +82%)
- **Teams:** +29 methods (35 → 64, +82.9%)
- **Outlook:** +26 methods (33 → 59, +78.8%)
- **Common Data Service:** +13 methods (15 → 28, +86.7%)
- **MSN Weather:** +3 methods (5 → 8, +60%)
- **Microsoft Forms:** +1 method (2 → 3, +50%)

### Strongly-Typed Input Parameters

**Example:**
```csharp
// OLD (v1.141.0.9)
units: () => "Imperial"  // String

// NEW (v1.141.0.10)
units: () => CurrentWeatherunitsInput.Imperial  // Enum
```

### What Stayed the Same
- ✅ Connection ID pattern unchanged
- ✅ Expression<Func<T>> parameter pattern
- ✅ DynamicValues attributes and hierarchy
- ✅ API endpoints unchanged

**See:** [SDK_UPDATE_SUMMARY.md](SDK_UPDATE_SUMMARY.md) for complete migration guide.

---

## 🔍 LSP Server Integration

This documentation supports the C# LSP Server for VS Code, which provides:

### IntelliSense Features
- **Hover Tooltips** - Rich documentation for methods and parameters
- **Auto-Completion** - Connector method suggestions
- **Dynamic Values** - API-driven parameter completion
- **Connection Suggestions** - Available connections from `connections.json`

### Supported Scenarios

1. **Connection Parameter Hover**
   ```csharp
   connectors.Teams("myConnection").GetAllTeams()
                                            // ^ Shows available connections
   ```

2. **Dynamic Values Completion**
   ```csharp
   connectors.Forms.GetFormResponseById(
       formId: x => ""  // ^ Auto-completes with form names
   )
   ```

3. **Method Documentation**
   ```csharp
   connectors.Teams.GetAllTeams  // ^ Hover shows full documentation
   ```

### Handler Usage

**HoverHandler** - Provides three-path hover system:
- Connection parameters → Show connections list
- Dynamic parameters → Fetch from Azure APIs
- Standard parameters → Show C# documentation

**CompletionHandler** - Provides intelligent suggestions:
- After `GetManagedConnectors().` → Connector names
- Inside parameter strings → Dynamic values from APIs

**CodeLensHandler** - Shows actionable insights:
- "Create connection" above methods needing connections
- Extracts connection info for VS Code integration

---

## 📊 DynamicValues Operations

### Available Operations

| Connector | Operation | Purpose |
|-----------|-----------|---------|
| **CDS** | GetOrganizations | List organizations |
| | GetEntityListEnum | List entities |
| | GetEntityRelationships | Get entity relationships |
| | GetBoundActions | Get bound actions |
| | GetAttributeFiltersCodeless | Get attribute filters |
| | GetCatalogs | Get catalogs |
| | GetUnboundActions | Get unbound actions |
| **Office 365** | CalendarGetTables_V2 | List calendars |
| | ContactGetTablesV2 | List contact folders |
| | GetRoomLists_V2 | List room lists |
| **Outlook** | CalendarGetTables | List calendars |
| | ContactGetTables | List contact folders |
| **Forms** | ListForms | List all forms |
| **Weather** | GetMeasureUnits | List measurement units |
| **Teams** | GetAllTeams | List teams (also used as DynamicValues source) |
| | GetChannelsForGroup | List channels for a team |
| | GetTags | List tags |
| | GetMessageLocations | List message locations |

**Note:** Not all operations have API endpoints documented in the LSP server's `DynamicOperationsRegistry`. Additional operations may require endpoint configuration.

---

## 🛠️ For Developers

### Regenerating Documentation

When the SDK is updated:

1. Place new `.nupkg` file in `SDK/` directory
2. Run: `./regenerate_docs.sh`
3. Use LLM to analyze decompiled code
4. Generate connector documentation
5. Update this README

**See:** [REGENERATION_GUIDE.md](REGENERATION_GUIDE.md) for detailed instructions.

### Documentation Standards

Each connector file should include:

- ✅ Table of Contents with anchor links
- ✅ Quick Links section
- ✅ Methods grouped by category
- ✅ Complete method signatures
- ✅ Parameter descriptions
- ✅ Usage examples
- ✅ DynamicValues hierarchies
- ✅ API endpoints

**See:** [DOCS_STRUCTURE.md](DOCS_STRUCTURE.md) for template and standards.

---

## 📞 Support

### Common Issues

**Q: Namespace not found**  
A: Ensure you're using `Microsoft.Azure.Workflows.Sdk.Agents` (not `Agents.Sdk`)

**Q: Method signatures changed**  
A: No methods changed in v1.141.0.9, only namespace

**Q: LSP server not finding SDK**  
A: Check SDK file is in correct location with correct name pattern

**Q: DynamicValues not working**  
A: Operation may need endpoint configuration in `DynamicOperationsRegistry.cs`

### Additional Resources

- **[LLM_QUICK_REF.md](LLM_QUICK_REF.md)** - Quick patterns for LLM agents
- **[SDK_ANALYSIS.md](SDK_ANALYSIS.md)** - Complete technical reference
- **[REGENERATION_GUIDE.md](REGENERATION_GUIDE.md)** - Maintenance procedures

---

## 📝 Contributing

To update this documentation:

1. Update SDK version in this file
2. Run regeneration script
3. Verify all connector counts
4. Update examples with new features
5. Test LSP server integration
6. Submit PR with changes

---

**Version:** 1.141.0.10  
**Status:** ✅ Production Ready  
**Compatibility:** LSP Server v1.0+  
**Breaking Changes:** ⚠️ YES - Input parameter types changed

---

_This documentation is automatically maintained through the regeneration process documented in [REGENERATION_GUIDE.md](REGENERATION_GUIDE.md)._
