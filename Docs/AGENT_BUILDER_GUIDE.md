# AgentBuilder Guide - Azure Workflows Agents SDK

**SDK Version:** 1.141.0.10  
**Namespace:** `Microsoft.Azure.Workflows.Sdk.Agents`  
**Status:** ✅ Production Ready (NEW in v1.141.0.10)

---

## 📋 Table of Contents

### Quick Links
- [Overview](#overview)
- [AgentBuilder Class](#agentbuilder-class)
- [Configuration Properties](#configuration-properties)
- [Agent Connection](#agent-connection)
- [Adding Tools](#adding-tools)
- [Complete Usage Example](#complete-usage-example)
- [Reference Types](#reference-types)

---

## Overview

The `AgentBuilder` class enables you to create AI-powered workflow agents that can execute actions using Azure OpenAI or other AI models. Agents can be configured with system prompts, model settings, and tools (workflow actions) that the AI can invoke.

**Key Features:**
- ✅ Configure AI model connection (Azure OpenAI, etc.)
- ✅ Set system prompts and messages
- ✅ Configure model parameters (temperature, max tokens, etc.)
- ✅ Add workflow actions as tools for the agent to use
- ✅ Full type safety with strongly-typed configuration

---

## AgentBuilder Class

**Namespace:** `Microsoft.Azure.Workflows.Sdk.Agents`

```csharp
public class AgentBuilder : IWorkflowAction, INamedObject, IAgentActionBuilder
{
    // Configuration Properties
    public string Name { get; private set; }
    public string DeploymentId { get; set; }
    public AgentModelType AgentModelType { get; set; }
    public AgentPromptMessage[] Messages { get; set; }
    public AgentModelSettings AgentModelSettings { get; set; }
    
    [Agent(Type = ConnectorType.AgentConnection, ConnectorName = "agent", 
           Id = "connectionProviders/agent")]
    public string ConnectionName { get; set; }
    
    // Methods
    public IAgentActionBuilder AddTool<T>(
        Action<IAgentToolBuilder<T>> toolBuilder, 
        string description, 
        T parameters) where T : class;
        
    public void WithName(string name);
}
```

---

## Configuration Properties

### DeploymentId

**Type:** `string`  
**Required:** Yes

The deployment ID of your AI model.

```csharp
DeploymentId = "gpt-4.1"
```

### AgentModelType

**Type:** `AgentModelType` (enum)  
**Required:** Yes

Specifies the type of AI model provider.

**Available Values:**
```csharp
public enum AgentModelType
{
    AzureOpenAI,
    OpenAI,
    // Additional model types...
}
```

**Usage:**
```csharp
AgentModelType = AgentModelType.AzureOpenAI
```

### AgentModelSettings

**Type:** `AgentModelSettings`  
**Required:** Yes

Configures the AI model's behavior and parameters.

**Structure:**
```csharp
public class AgentModelSettings
{
    public AgentChatCompletionSettings AgentChatCompletionSettings { get; set; }
    public AgentDeploymentModelProperties DeploymentModelProperties { get; set; }
}
```

**AgentChatCompletionSettings Properties:**
- `MaxTokens` (int) - Maximum tokens in response
- `Temperature` (double) - Randomness (0.0 - 1.0)
- `FrequencyPenalty` (double) - Reduce repetition (-2.0 to 2.0)
- `PresencePenalty` (double) - Encourage new topics (-2.0 to 2.0)
- `TopP` (double) - Nucleus sampling (0.0 - 1.0)

**AgentDeploymentModelProperties:**
- `Name` (string) - Model name (e.g., "gpt-4o")
- `Format` (string) - Format type (e.g., "OpenAI")
- `Version` (string) - Model version (e.g., "2025-04-14")

**Example:**
```csharp
AgentModelSettings = new AgentModelSettings
{
    AgentChatCompletionSettings = new AgentChatCompletionSettings
    {
        MaxTokens = 3000,
        Temperature = 0,
        FrequencyPenalty = 0.1,
        PresencePenalty = 0.1,
        TopP = 0.1,
    },
    DeploymentModelProperties = new AgentDeploymentModelProperties
    {
        Name = "gpt-4o",
        Format = "OpenAI",
        Version = "2025-04-14"
    }
}
```

### Messages

**Type:** `AgentPromptMessage[]`  
**Required:** Yes

System prompts and conversation context for the agent.

**Structure:**
```csharp
public class AgentPromptMessage
{
    public MessageRole Role { get; set; }
    public string Content { get; set; }
}

public enum MessageRole
{
    System,
    User,
    Assistant
}
```

**Example:**
```csharp
Messages = new AgentPromptMessage[]
{
    new AgentPromptMessage
    {
        Role = MessageRole.System,
        Content = "You are a helpful weather assistant. Provide clear and concise weather information."
    }
}
```

---

## Agent Connection

### ConnectionName Property

**Type:** `string`  
**Required:** Yes  
**Attribute:** `[Agent(Type = ConnectorType.AgentConnection, ConnectorName = "agent", Id = "connectionProviders/agent")]`

Specifies the connection to the AI model provider (e.g., Azure OpenAI connection).

**Key Points:**
- This is **NOT** a connector connection (Teams, Outlook, etc.)
- This is the **AI model connection** that the agent uses
- Must reference a connection configured in your `connections.json`
- The `[Agent]` attribute marks this as the agent's AI connection

**Example:**
```csharp
ConnectionName = "agent"  // References connection in connections.json
```

**connections.json Example:**
```json
{
  "agent": {
    "id": "connectionProviders/agent",
    "connectionProperties": {
      "connectionString": "your-azure-openai-connection-string"
    }
  }
}
```

---

## Adding Tools

Tools are workflow actions that the AI agent can invoke. Use the `AddTool<T>()` method to add connector actions or custom workflows.

### AddTool<T> Method

**Signature:**
```csharp
public IAgentActionBuilder AddTool<T>(
    Action<IAgentToolBuilder<T>> toolBuilder, 
    string description, 
    T parameters
) where T : class
```

**Parameters:**
- `toolBuilder` - Action to configure the tool with workflow actions
- `description` - Description of what the tool does (shown to AI)
- `parameters` - Schema defining the tool's parameters

**Example - Adding Weather Tool:**
```csharp
agent.AddTool<WeatherToolParameters>(
    tool => {
        // Add weather connector action
        var weather = WorkflowActions.ManagedConnectors
            .Msnweather("weather-connection")
            .CurrentWeather(
                location: () => tool.Parameters.Location,
                units: () => CurrentWeatherunitsInput.Imperial
            );
        tool.AddAction(weather);
    },
    description: "Get current weather for a location",
    parameters: new WeatherToolParameters()
);

// Parameter class
public class WeatherToolParameters
{
    public string Location { get; set; }
}
```

---

## Complete Usage Example

Here's a complete example of configuring an agent with the weather connector:

```csharp
using Microsoft.Azure.Workflows.Sdk.Agents;
using Microsoft.Azure.Workflows.Sdk.Agents.Connectors.Msnweather;

#region Agent Configuration
var agent = new AgentBuilder
{
    // AI Model Configuration
    AgentModelType = AgentModelType.AzureOpenAI,
    DeploymentId = "gpt-4.1",
    
    // Model Behavior Settings
    AgentModelSettings = new AgentModelSettings
    {
        AgentChatCompletionSettings = new AgentChatCompletionSettings
        {
            MaxTokens = 3000,
            Temperature = 0,
            FrequencyPenalty = 0.1,
            PresencePenalty = 0.1,
            TopP = 0.1,
        },
        DeploymentModelProperties = new AgentDeploymentModelProperties
        {
            Name = "gpt-4o",
            Format = "OpenAI",
            Version = "2025-04-14"
        }
    },
    
    // System Prompt
    Messages = new AgentPromptMessage[]
    {
        new AgentPromptMessage
        {
            Role = MessageRole.System,
            Content = @"You are an agent to get the weather. Greet the user warmly 
                       and provide the current weather conditions for their location, 
                       including temperature, weather type (e.g., sunny, cloudy, rainy), 
                       humidity, and wind speed. If a forecast is available, summarize 
                       the expected weather for the next 24 hours. Make the response 
                       clear and conversational, and suggest whether it's a good day 
                       for outdoor activities based on the weather."
        }
    },
    
    // Agent AI Model Connection
    ConnectionName = "agent",  // References Azure OpenAI connection
};
#endregion

#region Add Weather Tool
// Define tool parameters schema
public class WeatherParameters
{
    public string Location { get; set; }
}

// Add weather tool
agent.AddTool<WeatherParameters>(
    tool => {
        var weatherConnector = WorkflowActions.ManagedConnectors
            .Msnweather("weather-connection");
            
        var currentWeather = weatherConnector.CurrentWeather(
            location: () => tool.Parameters.Location,
            units: () => CurrentWeatherunitsInput.Imperial
        );
        
        tool.AddAction(currentWeather);
    },
    description: "Gets the current weather conditions for a specified location",
    parameters: new WeatherParameters()
);
#endregion

// Use the agent in your workflow
agent.WithName("WeatherAgent");
```

---

## Reference Types

### AgentModelType Enum

```csharp
public enum AgentModelType
{
    AzureOpenAI,
    OpenAI
}
```

### MessageRole Enum

```csharp
public enum MessageRole
{
    System,
    User,
    Assistant
}
```

### IAgentActionBuilder Interface

```csharp
public interface IAgentActionBuilder
{
    IAgentActionBuilder AddTool<T>(
        Action<IAgentToolBuilder<T>> toolBuilder, 
        string description, 
        T parameter
    ) where T : class;
}
```

### IAgentToolBuilder<T> Interface

```csharp
public interface IAgentToolBuilder<T> where T : class
{
    T Parameters { get; }
    void AddAction(IWorkflowAction action);
}
```

---

## Best Practices

### System Prompts
- ✅ Be specific about the agent's role and capabilities
- ✅ Include guidelines for response format
- ✅ Specify tone and style preferences
- ✅ Mention available tools and when to use them

### Model Settings
- ✅ Use `Temperature = 0` for deterministic, factual responses
- ✅ Use `Temperature > 0.7` for creative or varied responses
- ✅ Set `MaxTokens` based on expected response length
- ✅ Use penalties to reduce repetition

### Tool Configuration
- ✅ Provide clear, descriptive tool descriptions
- ✅ Define parameter schemas that match tool requirements
- ✅ Test tools independently before adding to agent
- ✅ Handle tool errors gracefully

---

## Common Scenarios

### Weather Assistant Agent
See [Complete Usage Example](#complete-usage-example) above.

### Email Management Agent
```csharp
var agent = new AgentBuilder
{
    AgentModelType = AgentModelType.AzureOpenAI,
    DeploymentId = "gpt-4",
    Messages = new[] 
    {
        new AgentPromptMessage
        {
            Role = MessageRole.System,
            Content = "You are an email management assistant. Help users read, send, and organize emails."
        }
    },
    ConnectionName = "agent"
};

// Add Outlook tools
agent.AddTool<EmailParameters>(
    tool => {
        var outlook = WorkflowActions.ManagedConnectors
            .Outlook("outlook-connection");
        // Add email actions...
    },
    description: "Send and manage emails",
    parameters: new EmailParameters()
);
```

### Teams Collaboration Agent
```csharp
var agent = new AgentBuilder
{
    AgentModelType = AgentModelType.AzureOpenAI,
    DeploymentId = "gpt-4",
    Messages = new[]
    {
        new AgentPromptMessage
        {
            Role = MessageRole.System,
            Content = "You are a Teams collaboration assistant. Help users post messages, create channels, and manage teams."
        }
    },
    ConnectionName = "agent"
};

// Add Teams tools
agent.AddTool<TeamsParameters>(
    tool => {
        var teams = WorkflowActions.ManagedConnectors
            .Teams("teams-connection");
        // Add Teams actions...
    },
    description: "Manage Teams channels and messages",
    parameters: new TeamsParameters()
);
```

---

## Troubleshooting

### Common Issues

**Q: "ConnectionName not found"**  
A: Ensure the connection name matches an entry in `connections.json` with the correct agent connection configuration.

**Q: "Tool not executing"**  
A: Verify tool description is clear and parameter schema matches the tool's requirements.

**Q: "Agent not responding as expected"**  
A: Review system prompt for clarity and adjust model settings (temperature, max tokens).

**Q: "Connection string invalid"**  
A: Verify Azure OpenAI connection string is properly configured in connections.json.

---

## Summary

The **AgentBuilder** class provides:
- ✅ Complete AI agent configuration
- ✅ Agent connection management with `[Agent]` attribute
- ✅ Flexible tool/action integration
- ✅ Fine-grained model behavior control
- ✅ Type-safe configuration

**Version:** 1.141.0.10  
**Status:** ✅ Production Ready  
**Breaking Changes:** ⚠️ NEW FEATURE (no breaking changes to existing code)

---

_For connector-specific documentation, see the individual connector guides: [01_MSNWEATHER.md](01_MSNWEATHER.md), [03_TEAMS.md](03_TEAMS.md), [05_OUTLOOK.md](05_OUTLOOK.md), etc._
