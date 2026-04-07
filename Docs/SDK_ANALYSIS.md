# SDK Analysis - Microsoft.Azure.Workflows.Sdk.Agents v1.141.0.10

## Overview

The SDK provides typed extension methods for Azure managed connectors (MSN Weather, Microsoft Forms, Teams, Office365, Outlook, Common Data Service) and introduces the **AgentBuilder** class for building AI-powered workflow agents.

**MAJOR UPDATE v1.141.0.10:**
- 🆕 **AgentBuilder class** - Build AI agents with Azure OpenAI/OpenAI
- ⚠️ **+113 new methods** (80.7% increase: 140 → 253 methods)
- ⚠️ **BREAKING:** Strongly-typed Input parameters replace string parameters
- ✅ **Agent connection decorator** with `[Agent]` attribute

**Previous Change:** Namespace changed from `Microsoft.Azure.Workflows.Agents.Sdk` to `Microsoft.Azure.Workflows.Sdk.Agents` in v1.141.0.9

---

## Key Structures

### 1. AgentBuilder Class (🆕 NEW in v1.141.0.10)

**Complete AI agent configuration for building intelligent workflows.**

```csharp
public class AgentBuilder : IWorkflowAction, INamedObject, IAgentActionBuilder
{
    public string Name { get; private set; }
    public string DeploymentId { get; set; }
    public AgentModelType AgentModelType { get; set; }
    public AgentPromptMessage[] Messages { get; set; }
    public AgentModelSettings AgentModelSettings { get; set; }
    
    [Agent(Type = ConnectorType.AgentConnection, ConnectorName = "agent", 
           Id = "connectionProviders/agent")]
    public string ConnectionName { get; set; }
    
    public IAgentActionBuilder AddTool<T>(
        Action<IAgentToolBuilder<T>> toolBuilder, 
        string description, 
        T parameters) where T : class;
        
    public void WithName(string name);
}
```

**Key Points:**
- `ConnectionName` property with `[Agent]` attribute specifies the AI model connection
- Distinct from connector connections (Teams, Outlook, etc.)
- Add workflow actions as tools via `AddTool<T>()`
- Configure model behavior with `AgentModelSettings`
- Set system prompts via `Messages` array

**Usage Example:**
```csharp
var agent = new AgentBuilder
{
    AgentModelType = AgentModelType.AzureOpenAI,
    DeploymentId = "gpt-4.1",
    ConnectionName = "agent",  // AI model connection
    AgentModelSettings = new AgentModelSettings
    {
        AgentChatCompletionSettings = new AgentChatCompletionSettings
        {
            MaxTokens = 3000,
            Temperature = 0,
        }
    },
    Messages = new AgentPromptMessage[]
    {
        new AgentPromptMessage
        {
            Role = MessageRole.System,
            Content = "You are a helpful assistant..."
        }
    }
};
```

**See:** [AGENT_BUILDER_GUIDE.md](AGENT_BUILDER_GUIDE.md) for complete documentation.

### 2. WorkflowActions Entry Point (NEW in v1.141.0.9)

```csharp
// Static class providing access to managed connectors
public static class WorkflowActions
{
    public static WorkflowBuiltInActions BuiltIn = new WorkflowBuiltInActions();
    public static WorkflowManagedActions ManagedConnectors = new WorkflowManagedActions();
}
```

**Usage:**
```csharp
// Access connectors via static property
var weather = WorkflowActions.ManagedConnectors
    .Msnweather("weather-connection")
    .CurrentWeather(
        location: () => "Seattle, WA",
        units: () => "Imperial"
    );
```

**Key Change:** Connection ID now passed at connector instantiation, not on each method call.

### 3. Connector Instantiation Pattern

Each connector is accessed via an extension method that takes the connection ID:

```csharp
public static class MsnweatherInstanceExtensions
{
    public static MsnweatherInstance Msnweather(
        this WorkflowManagedActions actions,
        string connectionId)
    {
        return new MsnweatherInstance(connectionId);
    }
}
```

**Connector Instance Captures Connection:**
```csharp
public class MsnweatherInstance(string connectionId)
{
    // All methods automatically use the connectionId from constructor
    public IOutputWorkflowAction<CurrentWeather> CurrentWeather(
        Expression<Func<string>> location,
        Expression<Func<string>> units)
    {
        return MsnweatherExtensions.CurrentWeather(
            connectionId,  // Passed internally
            location,
            units
        );
    }
}
```

**Available Connectors:**
- `Office365` - Office 365 operations (91 methods) - **+41 new (+82%)**
- `Teams` - Microsoft Teams operations (64 methods) - **+29 new (+82.9%)**
- `Outlook` - Outlook operations (59 methods) - **+26 new (+78.8%)**
- `Commondataservice` - Common Data Service operations (28 methods) - **+13 new (+86.7%)**
- `Msnweather` - MSN Weather API (8 methods) - **+3 new (+60%)**
- `Microsoftforms` - Microsoft Forms operations (3 methods) - **+1 new (+50%)**

**Total:** 253 methods across all connectors (+113 from v1.141.0.9, +80.7%)

---

## Namespace Structure

### Main Namespaces
```
Microsoft.Azure.Workflows.Sdk.Agents
├── Connectors
│   ├── Commondataservice
│   ├── Microsoftforms
│   ├── Msnweather
│   ├── Office365
│   ├── Outlook
│   └── Teams
├── Expressions
└── Runtime
```

**Legacy Namespace (DEPRECATED):**
- `Microsoft.Azure.Workflows.Agents.Sdk` - Old namespace, no longer used

---

## Connector Method Patterns

### Extension Methods (Static - Under the Hood)

These static extension methods are used internally by the instance classes:

```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "{connector}")]
public static IOutputWorkflowAction<TResponse> MethodName(
    [ConnectionName] string connectionId,
    Expression<Func<T>> parameter1,
    [DynamicValues("Operation")] Expression<Func<T>> parameter2,
    // ... other parameters
)
```

**Key Points:**
- First parameter has `[ConnectionName]` attribute (passed internally by instance)
- Parameters use `Expression<Func<T>>` for dynamic evaluation
- `[DynamicValues("OperationName")]` attribute for dependent dropdowns
- Returns `IOutputWorkflowAction<T>` or `IWorkflowAction`

### Instance Methods (Public API - What Users Call)

What users actually call on connector instances:

```csharp
public class MsnweatherInstance(string connectionId)
{
    public IOutputWorkflowAction<CurrentWeather> CurrentWeather(
        Expression<Func<string>> location,  // No connectionId parameter!
        Expression<Func<string>> units)
    {
        // Calls static method internally, passing captured connectionId
        return MsnweatherExtensions.CurrentWeather(
            connectionId, location, units);
    }
}
```

**Key Points:**
- No `connectionId` parameter in public API
- Connection captured from constructor
- Cleaner method signatures for users

### Trigger Methods

Triggers follow the same pattern:

```csharp
public class MsnweatherInstanceTriggers(string connectionId)
{
    public IOutputWorkflowTrigger<CurrentWeather> WhenOnCurrentWeatherChange(
        Expression<Func<string>> location,
        Expression<Func<string>> measure,
        // ... other parameters
    )
    {
        return MsnweatherExtensions.WhenOnCurrentWeatherChange(
            connectionId, location, measure, ...);
    }
}
```

**Key Points:**
- Returns `IOutputWorkflowTrigger<T>` or `IWorkflowTrigger`
- Used for event-driven workflows
- Webhook-based triggers register callbacks
- Connection ID captured from constructor

---

## Attribute Reference

### [ConnectorOperation]
Marks a method as a connector operation.

**Properties:**
- `Type` - `ConnectorType.ApiManagement`
- `ConnectorName` - Lowercase connector identifier (e.g., "teams", "outlook")

### [ConnectionName]
Marks a parameter as the connection identifier.

**Usage:** Always on first parameter (`connectionId`)

### [DynamicValues("OperationName")]
Marks a parameter that should be populated with values from another operation.

**Example:**
```csharp
[DynamicValues("ListForms")] Expression<Func<string>> formId
```

This creates a parent-child relationship where:
- Parent operation: `ListForms` - fetches available forms
- Child parameter: `formId` - dropdown populated with form list

---

## Example: MSN Weather Connector

### Namespace
```csharp
namespace Microsoft.Azure.Workflows.Sdk.Agents.Connectors.Msnweather
```

### Public Usage Pattern

**Access the connector:**
```csharp
using Microsoft.Azure.Workflows.Sdk.Agents;
using Microsoft.Azure.Workflows.Sdk.Agents.Connectors.Msnweather;

var weather = WorkflowActions.ManagedConnectors
    .Msnweather("weather-connection");
```

### Current Weather (Action)

**User calls this (v1.141.0.10):**
```csharp
var current = weather.CurrentWeather(
    location: () => "Seattle, WA",
    units: () => CurrentWeatherunitsInput.Imperial  // ⚠️ CHANGED: Now enum instead of string
);
```

**Returns:** `IOutputWorkflowAction<CurrentWeather>`

**Under the hood (static extension method):**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "msnweather")]
public static IOutputWorkflowAction<CurrentWeather> CurrentWeather(
    [ConnectionName] string connectionId,
    Expression<Func<string>> location,
    Expression<Func<CurrentWeatherunitsInput>> units  // ⚠️ CHANGED: Input enum type
)
```

**Available Input Values:**
```csharp
public enum CurrentWeatherunitsInput
{
    [EnumMember(Value = "I")]
    Imperial,
    [EnumMember(Value = "C")]
    Metric
}
```

### Weather Change Trigger

**User calls this (v1.141.0.10):**
```csharp
var trigger = WorkflowActions.ManagedTriggers
    .Msnweather("weather-connection")
    .WhenOnCurrentWeatherChange(
        location: () => "Seattle, WA",
        measure: () => OnCurrentWeatherChangeMeasureInput.Temperature,  // ⚠️ CHANGED: Enum
        when: () => OnCurrentWeatherChangeWhenInput.GoesOver,  // ⚠️ CHANGED: Enum
        target: () => 75.0,
        units: () => "F"  // Still string - DynamicValues from GetMeasureUnits
    );
```

**Returns:** `IOutputWorkflowTrigger<CurrentWeather>`

**Under the hood (static extension method):**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "msnweather")]
public static IOutputWorkflowTrigger<CurrentWeather> WhenOnCurrentWeatherChange(
    [ConnectionName] string connectionId,
    Expression<Func<string>> location,
    Expression<Func<OnCurrentWeatherChangeMeasureInput>> measure,  // ⚠️ CHANGED
    Expression<Func<OnCurrentWeatherChangeWhenInput>> when,  // ⚠️ CHANGED
    Expression<Func<double>> target,
    [DynamicValues("GetMeasureUnits")] Expression<Func<string>> units
)
```

**Available Measure Values:**
```csharp
public enum OnCurrentWeatherChangeMeasureInput
{
    Temperature,
    [EnumMember(Value = "UV Index")]
    UVIndex,
    Humidity,
    [EnumMember(Value = "Wind Speed")]
    WindSpeed
}
```

**Available When Values:**
```csharp
public enum OnCurrentWeatherChangeWhenInput
{
    [EnumMember(Value = "Is equal to")]
    IsEqualTo,
    [EnumMember(Value = "Goes over")]
    GoesOver,
    [EnumMember(Value = "Goes below")]
    GoesBelow
}
```

**DynamicValues:**
- `units` parameter populated by calling `GetMeasureUnits` operation

---

## DynamicValues Hierarchies

### Common Data Service
```
GetOrganizations (root)
└── GetEntityListEnum (requires: organization)
    ├── GetEntityRelationships (requires: entityName)
    ├── GetBoundActions (requires: entityName)
    ├── GetAttributeFiltersCodeless (requires: entityName)
    └── GetCatalogs (requires: entityName)

GetUnboundActions (standalone)
```

### Office 365
```
CalendarGetTables_V2 (calendars list)
ContactGetTablesV2 (contact folders)
GetRoomLists_V2 (room lists)
```

### Outlook
```
CalendarGetTables (calendars list)
ContactGetTables (contact folders)
```

### Microsoft Forms
```
ListForms (root - returns all accessible forms)
```

### MSN Weather
```
GetMeasureUnits (measurement unit options)
```

### Teams
No DynamicValues operations - all parameters are direct input

---

## Migration Guide: Agents.Sdk → Sdk.Agents

### Namespace Changes

**OLD:**
```csharp
using Microsoft.Azure.Workflows.Agents.Sdk;
using Microsoft.Azure.Workflows.Agents.Sdk.Connectors.Teams;
```

**NEW:**
```csharp
using Microsoft.Azure.Workflows.Sdk.Agents;
using Microsoft.Azure.Workflows.Sdk.Agents.Connectors.Teams;
```

### File Name Changes

**OLD:**
- `Microsoft.Azure.Workflows.Agents.Sdk.1.141.0.7.nupkg`
- `Microsoft.Azure.Workflows.Agents.Sdk.dll`

**NEW:**
- `Microsoft.Azure.Workflows.Sdk.Agents.1.141.0.9.nupkg`
- `Microsoft.Azure.Workflows.Sdk.Agents.dll`

### Code Impact

✅ **No code changes required** - Only namespace imports need updating
- Method signatures unchanged
- Connector names unchanged
- API unchanged

---

## LSP Server Integration

### SdkIndex Loading

The LSP server loads the SDK at startup:

```csharp
// NupkgLoader extracts and loads assemblies
SdkIndex? index = await SdkIndex.TryCreateAsync(sdkPath);

// Index provides:
// - Assembly paths for reflection
// - Type information for hover/completion
// - Method signatures for IntelliSense
```

### Usage in Handlers

**HoverHandler:**
- Checks `IsSymbolFromSdk()` to identify SDK types
- Provides rich hover information for SDK methods
- Extracts connection parameters for suggestions

**CompletionHandler:**
- Suggests connector methods after `GetManagedConnectors().`
- Provides dynamic values for parameters with `[DynamicValues]`
- Uses LSPStore to cache API responses

**CodeLensHandler:**
- Detects connector operations via `[ConnectorOperation]` attribute
- Shows "Create connection" code lens for methods needing connections
- Extracts connector type and name for VS Code integration

---

## Version History

| SDK Version | Release Date | Changes |
|-------------|--------------|---------|
| 1.141.0.10 | 2025-11-05 | 🆕 **AgentBuilder** class, **+113 methods** (+80.7%), **BREAKING:** Input parameters |
| 1.141.0.9 | 2025-10-28 | Namespace change: Agents.Sdk → Sdk.Agents |
| 1.141.0.7 | 2025-10-15 | Previous version with old namespace |

---

## Statistics

- **Total Connectors:** 6
- **Total Methods:** 253 (+113 from v1.141.0.9, +80.7%)
  - **Actions:** 202
  - **Triggers:** 51
- **DynamicValues Operations:** 15+
- **Input Types (Enums/Classes):** 150+
- **Decompiled Lines:** ~15,072
- **Connector with Most Methods:** Office 365 (91)
- **Connector with Least Methods:** Microsoft Forms (3)
- **New Features:** AgentBuilder class, Agent connection decorator

### Method Count Breakdown by Connector

| Connector | v1.141.0.9 | v1.141.0.10 | Change |
|-----------|------------|-------------|--------|
| Office 365 | 50 | 91 | +41 (+82%) |
| Teams | 35 | 64 | +29 (+82.9%) |
| Outlook | 33 | 59 | +26 (+78.8%) |
| Common Data Service | 15 | 28 | +13 (+86.7%) |
| MSN Weather | 5 | 8 | +3 (+60%) |
| Microsoft Forms | 2 | 3 | +1 (+50%) |

---

**Last Updated:** 2025-11-05  
**SDK Version:** 1.141.0.10  
**Namespace:** Microsoft.Azure.Workflows.Sdk.Agents  
**Breaking Changes:** ⚠️ YES - Input parameter types, +113 new methods
