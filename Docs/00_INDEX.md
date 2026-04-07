# SDK Connectors - Complete Documentation Index

**SDK Version:** 1.141.0.10  
**Last Updated:** 2025-11-05  
**Breaking Changes:** ⚠️ YES - Input parameter types changed

## ⚠️ Important: Version 1.141.0.10 Breaking Changes

This version introduces **strongly-typed Input parameters**. Many parameters that were `Expression<Func<string>>` now require specific enum or class types.

**See:** [SDK_UPDATE_SUMMARY.md](./SDK_UPDATE_SUMMARY.md) for complete migration guide.

---

## Available Connectors

1. **[MSN Weather](./01_MSNWEATHER.md)** - Weather data and triggers (5 methods)
2. **[Microsoft Forms](./02_MICROSOFTFORMS.md)** - Form webhooks and responses (2 methods, +1 new)
3. **[Teams](./03_TEAMS.md)** - Microsoft Teams integration (35 methods, +2 new)
4. **[Office365](./04_OFFICE365.md)** - Office 365 operations (50 methods, +13 new)
5. **[Outlook](./05_OUTLOOK.md)** - Email operations (33 methods, +9 new)
6. **[Common Data Service](./06_COMMONDATASERVICE.md)** - Dataverse/CRM operations (15 methods, +7 new)

**Total: 140 methods (+32 from v1.141.0.9)**

## Reference Documentation

- **[SDK Analysis](./SDK_ANALYSIS.md)** - Complete SDK structure analysis (v1.141.0.10)
- **[SDK Update Summary](./SDK_UPDATE_SUMMARY.md)** - **NEW:** v1.141.0.9 → v1.141.0.10 migration guide
- **[Lambda Syntax Update](./LAMBDA_SYNTAX_UPDATE_SUMMARY.md)** - Recent syntax changes (v1.141.0.9)
- **[Lambda Signature Analysis](./SDK_LAMBDA_SIGNATURE_ANALYSIS.md)** - Expression<Func<T>> details
- **[LLM Quick Reference](./LLM_QUICK_REF.md)** - AI assistant guide
- **[README](./README.md)** - Getting started
- **[Regeneration Guide](./REGENERATION_GUIDE.md)** - How to regenerate docs

---

## Quick Reference

### Accessing Connectors

All connectors are accessed via `GetManagedConnectors()`:

```csharp
var connectors = builder.GetManagedConnectors();

// Access specific connector
var weatherConnector = connectors.Msnweather;
var formsConnector = connectors.Microsoftforms;
var teamsConnector = connectors.Teams;
var office365Connector = connectors.Office365;
var outlookConnector = connectors.Outlook;
var cdsConnector = connectors.Commondataservice;
```

---

## Common Patterns

### All Methods Follow These Patterns:

1. **Connection ID First**
   ```csharp
   Method(
       [ConnectionName] string connectionId,  // Always first
       ...other parameters
   )
   ```

2. **Expression Parameters**
   ```csharp
   Expression<Func<string>> parameter        // Standard string parameter
   Expression<Func<int>> number              // Standard int parameter
   Expression<Func<SomeInput>> enumParam     // ⚠️ NEW: Input enum parameter
   ```

3. **Input Types (NEW in v1.141.0.10)** ⚠️
   ```csharp
   // Many parameters now use Input enums/classes
   Expression<Func<CurrentWeatherunitsInput>> units  // Instead of string
   Expression<Func<PostFeedNotificationposterInput>> poster  // Instead of string
   Expression<Func<GetEmailsV3importanceInput>> importance  // Instead of string
   ```

4. **Attributes on Methods**
   ```csharp
   [ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "connectorname")]
   ```

5. **Attributes on Parameters**
   ```csharp
   [ConnectionName] string connectionId
   [DynamicValues("OperationName")] Expression<Func<string>> parameter
   ```

---

## Input Types (NEW in v1.141.0.10) ⚠️

### Overview

Parameters that previously accepted strings now use method-specific Input enums or classes.

**Naming Convention:** `{MethodName}{parameterName}Input`

### Enum Input Types

**Examples:**

**MSN Weather:**
```csharp
public enum CurrentWeatherunitsInput
{
    [EnumMember(Value = "I")]
    Imperial,
    [EnumMember(Value = "C")]
    Metric
}

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

**Teams:**
```csharp
public enum PostFeedNotificationposterInput
{
    [EnumMember(Value = "Flow Bot")]
    FlowBot,
    [EnumMember(Value = "User")]
    User
}

public enum GetChatschatTypeInput
{
    [EnumMember(Value = "all")]
    AllChatTypes,
    [EnumMember(Value = "group")]
    Group,
    [EnumMember(Value = "oneonone")]
    OneOnOne
}
```

**Office365/Outlook:**
```csharp
public enum GetEmailsV3importanceInput
{
    Low,
    Normal,
    High
}
```

### Class Input Types

Complex parameters use Input classes:

```csharp
public class CreateChannelbodyInput
{
    public string displayName { get; set; }
    public string description { get; set; }
    // ... additional properties
}

public class NewMeeting
{
    public string subject { get; set; }
    public DateTime start { get; set; }
    public DateTime end { get; set; }
    // ... additional properties
}
```

### EnumMember Attributes

Enums map C# names to API values:

```csharp
[EnumMember(Value = "Is equal to")]
IsEqualTo  // Use IsEqualTo in C#, sends "Is equal to" to API
```

### Migration Example

**Before (v1.141.0.9):**
```csharp
var weather = connector.CurrentWeather(
    location: () => "Seattle, WA",
    units: () => "Imperial"  // ❌ String
);

teams.PostFeedNotification(
    poster: () => "FlowBot",  // ❌ String
    notificationType: () => "user",  // ❌ String
    body: () => messageBody
);
```

**After (v1.141.0.10):**
```csharp
using Microsoft.Azure.Workflows.Sdk.Agents.Connectors.Msnweather;
using Microsoft.Azure.Workflows.Sdk.Agents.Connectors.Teams;

var weather = connector.CurrentWeather(
    location: () => "Seattle, WA",
    units: () => CurrentWeatherunitsInput.Imperial  // ✅ Enum
);

teams.PostFeedNotification(
    poster: () => PostFeedNotificationposterInput.FlowBot,  // ✅ Enum
    notificationType: () => PostFeedNotificationnotificationTypeInput.User,  // ✅ Enum
    body: () => messageBody
);
```

### Benefits

- ✅ **Compile-time type safety** - Invalid values won't compile
- ✅ **IntelliSense support** - IDE shows all valid options
- ✅ **Self-documenting** - Clear what values are accepted
- ✅ **Prevents runtime errors** - No typos or invalid strings
- ✅ **Refactoring safety** - Compiler catches all usages

---

## Attribute Reference

### ConnectorOperation

Marks methods as connector operations.

```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "teams")]
```

**Properties:**
- `Type` - Always `ConnectorType.ApiManagement` for managed connectors
- `ConnectorName` - Connector identifier (e.g., "teams", "microsoftforms")

---

### ConnectionName

Marks connection ID parameters.

```csharp
public static IWorkflowAction Method(
    [ConnectionName] string connectionId,
    ...
)
```

**Purpose:** Indicates parameter requires a connection reference from connections.json

---

### DynamicValues

Indicates parameter values come from calling another operation.

```csharp
public static IWorkflowAction Method(
    ...
    [DynamicValues("ListItems")] Expression<Func<string>> itemId
)
```

**Purpose:** 
- LSP server should call the specified operation to get valid values
- Present as completion options to user
- Common examples: "ListForms", "GetAllTeams", "GetChannelsForGroup"

---

## Method Types

### Actions

Methods that return `IWorkflowAction` or `IOutputWorkflowAction<T>`.

**Constraint:** `where TBuilder : IActionBuilder`

**Example:**
```csharp
public static IOutputWorkflowAction<T> ActionMethod<TBuilder>(...)
    where TBuilder : IActionBuilder
```

---

### Triggers

Methods that return `IWorkflowTrigger` or `IOutputWorkflowTrigger<T>`.

**Constraint:** `where TBuilder : ITriggerBuilder`

**Example:**
```csharp
public static IOutputWorkflowTrigger<T> TriggerMethod<TBuilder>(...)
    where TBuilder : ITriggerBuilder
```

**Naming Convention:** Trigger methods often start with "When" (e.g., `WhenOnNewChannelMessage`)

---

## Response Types

### Typed Responses

Most methods return strongly-typed responses:

```csharp
IOutputWorkflowAction<CurrentWeather>
IOutputWorkflowAction<GetAllTeamsResponse>
IOutputWorkflowTrigger<OnNewChannelMessageResponseItem[]>
```

**Access:** `action.Body.PropertyName`

---

### Dynamic Responses

Some methods return `JToken` for flexible/dynamic data:

```csharp
IOutputWorkflowAction<JToken>
```

**Access:** `action.Body["propertyName"]`

**Use Case:** When response structure varies or is complex

---

## For LSP Implementation

### Hover Information

Show for each method:
1. Full signature
2. Parameter descriptions with attributes
3. Return type
4. Connector name and type
5. Related operations (for DynamicValues)

**Example:**
```
TeamsExtensions.GetAllTeams<TBuilder>

Gets all Microsoft Teams the user is a member of.

Parameters:
  • connectionId [ConnectionName]: Teams connection ID

Returns: IOutputWorkflowAction<GetAllTeamsResponse>

Connector: teams (API Management)
```

---

### Completion

When user types `builder.GetManagedConnectors().`:
- List all 6 connectors with brief descriptions

When user types `.Connectorname.`:
- List all methods available on that connector
- Show method signature preview
- Indicate if Action or Trigger

When user is entering parameter with `[DynamicValues]`:
- Call the referenced operation
- Show available values as completion options
- Include descriptions if available

---

## Summary Statistics

| Connector | Actions | Triggers | Key Features |
|-----------|---------|----------|--------------|
| MSN Weather | 1 | 2 | Weather data, change monitoring |
| Microsoft Forms | 1 | 1 | Form responses, webhooks |
| Teams | 10+ | 5+ | Messages, channels, meetings |
| Office365 | 5+ | 2+ | Calendar, contacts, mail |
| Outlook | 8+ | 3+ | Email operations |
| Common Data Service | 15+ | 1 | Dataverse CRUD operations |

---

## Next Steps

1. Review individual connector documentation for detailed method signatures
2. Check attribute usage for LSP integration
3. Implement hover and completion based on patterns shown
4. Use DynamicValues to enhance user experience

---

**Note:** Each connector guide includes:
- Complete method signatures
- Parameter details with attributes
- Usage examples
- Response type definitions
- LSP integration guidance
