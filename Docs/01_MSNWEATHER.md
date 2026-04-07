# MSN Weather Connector - Complete Reference

**Namespace:** `Microsoft.Azure.Workflows.Sdk.Agents.Connectors.Msnweather`  
**Connector Name:** `msnweather`  
**Type:** API Management  
**SDK Version:** 1.141.0.10  
**Breaking Changes:** ⚠️ YES - All methods now use Input enums

---

## 📋 Table of Contents

### Quick Links
- [Overview](#overview)
- [⚠️ Breaking Changes v1.141.0.10](#️-breaking-changes-v114101)
- [Input Types](#input-types)
- [Actions](#actions)
- [Triggers](#triggers)
- [Complete Usage Example](#complete-usage-example)
- [Summary](#summary)

### Weather Operations (Actions)
- [CurrentWeather](#currentweather) - Get current weather conditions
- [TodaysForecast](#todaysforecast) - Get today's weather forecast
- [TomorrowsForecast](#tomorrowsforecast) - Get tomorrow's weather forecast

### Weather Monitoring (Triggers)
- [WhenOnCurrentWeatherChange](#whenoncurrentweatherchange) - Trigger when weather metric changes
- [WhenOnCurrentConditionsChange](#whenoncurrentconditionschange) - Trigger when weather conditions change

---

## Overview

The MSN Weather connector provides access to current weather conditions, forecasts, and weather change triggers.

**Access via:**
```csharp
using Microsoft.Azure.Workflows.Sdk.Agents;
using Microsoft.Azure.Workflows.Sdk.Agents.Connectors.Msnweather;

var connector = WorkflowActions.ManagedConnectors.Msnweather("weather-connection");
```

**Total Methods:** 8 (5 Actions, 3 Triggers)  
**Change from v1.141.0.9:** +3 methods (+60%)  
**Breaking Changes:** ⚠️ ALL methods now use strongly-typed Input enums for units parameters

---

## ⚠️ Breaking Changes (v1.141.0.10)

All weather methods now use Input enums instead of strings for the `units` parameter.

**Before (v1.141.0.9):**
```csharp
var weather = connector.CurrentWeather(
    location: () => "Seattle, WA",
    units: () => "Imperial"  // ❌ String
);
```

**After (v1.141.0.10):**
```csharp
var weather = connector.CurrentWeather(
    location: () => "Seattle, WA",
    units: () => CurrentWeatherunitsInput.Imperial  // ✅ Enum
);
```

---

## Input Types

### CurrentWeatherunitsInput

Used by: `CurrentWeather` action

```csharp
public enum CurrentWeatherunitsInput
{
    [EnumMember(Value = "I")]
    Imperial,    // Fahrenheit, miles, etc.
    
    [EnumMember(Value = "C")]
    Metric       // Celsius, kilometers, etc.
}
```

**Values:**
- `Imperial` - US customary units (°F, mph, inches) - API Value: "I"
- `Metric` - International System units (°C, km/h, mm) - API Value: "C"

---

### TodaysForecastunitsInput

Used by: `TodaysForecast` action

```csharp
public enum TodaysForecastunitsInput
{
    [EnumMember(Value = "I")]
    Imperial,
    
    [EnumMember(Value = "C")]
    Metric
}
```

**Values:**
- `Imperial` - US customary units - API Value: "I"
- `Metric` - International System units - API Value: "C"

---

### TomorrowsForecastunitsInput

Used by: `TomorrowsForecast` action

```csharp
public enum TomorrowsForecastunitsInput
{
    [EnumMember(Value = "I")]
    Imperial,
    
    [EnumMember(Value = "C")]
    Metric
}
```

**Values:**
- `Imperial` - US customary units - API Value: "I"
- `Metric` - International System units - API Value: "C"

---

### OnCurrentWeatherChangeMeasureInput

Used by: `WhenOnCurrentWeatherChange` trigger

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

**Values:**
- `Temperature` - Temperature measurement - API Value: "Temperature"
- `UVIndex` - UV Index measurement - API Value: "UV Index"
- `Humidity` - Humidity percentage - API Value: "Humidity"
- `WindSpeed` - Wind speed measurement - API Value: "Wind Speed"

---

### OnCurrentWeatherChangeWhenInput

Used by: `WhenOnCurrentWeatherChange` trigger

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

**Values:**
- `IsEqualTo` - Trigger when value equals target - API Value: "Is equal to"
- `GoesOver` - Trigger when value exceeds target - API Value: "Goes over"
- `GoesBelow` - Trigger when value drops below target - API Value: "Goes below"

---

### OnCurrentConditionsChangeunitsInput

Used by: `WhenOnCurrentConditionsChange` trigger

```csharp
public enum OnCurrentConditionsChangeunitsInput
{
    [EnumMember(Value = "I")]
    Imperial,
    
    [EnumMember(Value = "C")]
    Metric
}
```

**Values:**
- `Imperial` - US customary units - API Value: "I"
- `Metric` - International System units - API Value: "C"

---

## Actions

### CurrentWeather

Gets the current weather conditions for a specified location.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "msnweather")]
public static IOutputWorkflowAction<CurrentWeather> CurrentWeather(
    [ConnectionName] string connectionId,
    Expression<Func<string>> location,
    Expression<Func<CurrentWeatherunitsInput>> units)
```

**Parameters:**
- `connectionId` **[ConnectionName]** - MSN Weather connection ID
- `location` - Location to get weather for (e.g., "Seattle, WA", "90210", "London, UK")
- `units` - Measurement units (Imperial or Metric)

**Returns:** `IOutputWorkflowAction<CurrentWeather>`

**Usage:**
```csharp
var weather = WorkflowActions.ManagedConnectors
    .Msnweather("weather-connection")
    .CurrentWeather(
        location: () => "Seattle, WA",
        units: () => CurrentWeatherunitsInput.Imperial
    );
```

**API:** `GET` (MSN Weather API endpoint)

---

### TodaysForecast

Gets the weather forecast for today at a specified location.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "msnweather")]
public static IOutputWorkflowAction<WeatherForecast> TodaysForecast(
    [ConnectionName] string connectionId,
    Expression<Func<string>> location,
    Expression<Func<TodaysForecastunitsInput>> units)
```

**Parameters:**
- `connectionId` **[ConnectionName]** - MSN Weather connection ID
- `location` - Location to get forecast for
- `units` - Measurement units (Imperial or Metric)

**Returns:** `IOutputWorkflowAction<WeatherForecast>`

**Usage:**
```csharp
var forecast = WorkflowActions.ManagedConnectors
    .Msnweather("weather-connection")
    .TodaysForecast(
        location: () => "New York, NY",
        units: () => TodaysForecastunitsInput.Metric
    );
```

**API:** `GET` (MSN Weather API endpoint)

---

### TomorrowsForecast

Gets the weather forecast for tomorrow at a specified location.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "msnweather")]
public static IOutputWorkflowAction<WeatherForecast> TomorrowsForecast(
    [ConnectionName] string connectionId,
    Expression<Func<string>> location,
    Expression<Func<TomorrowsForecastunitsInput>> units)
```

**Parameters:**
- `connectionId` **[ConnectionName]** - MSN Weather connection ID
- `location` - Location to get forecast for
- `units` - Measurement units (Imperial or Metric)

**Returns:** `IOutputWorkflowAction<WeatherForecast>`

**Usage:**
```csharp
var forecast = WorkflowActions.ManagedConnectors
    .Msnweather("weather-connection")
    .TomorrowsForecast(
        location: () => "Los Angeles, CA",
        units: () => TomorrowsForecastunitsInput.Imperial
    );
```

**API:** `GET` (MSN Weather API endpoint)

---

## Triggers

### WhenOnCurrentWeatherChange

Triggers a workflow when a specific weather metric (temperature, humidity, UV index, wind speed) meets a specified condition.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "msnweather")]
public static IOutputWorkflowTrigger<CurrentWeather> WhenOnCurrentWeatherChange(
    [ConnectionName] string connectionId,
    Expression<Func<string>> location,
    Expression<Func<OnCurrentWeatherChangeMeasureInput>> measure,
    Expression<Func<OnCurrentWeatherChangeWhenInput>> when,
    Expression<Func<double>> target,
    [DynamicValues("GetMeasureUnits")] Expression<Func<string>> units)
```

**Parameters:**
- `connectionId` **[ConnectionName]** - MSN Weather connection ID
- `location` - Location to monitor
- `measure` - Weather metric to monitor (Temperature, UVIndex, Humidity, WindSpeed)
- `when` - Condition type (IsEqualTo, GoesOver, GoesBelow)
- `target` - Target value to compare against
- `units` **[DynamicValues("GetMeasureUnits")]** - Measurement units (populated from GetMeasureUnits)

**Returns:** `IOutputWorkflowTrigger<CurrentWeather>`

**Usage:**
```csharp
var trigger = WorkflowActions.ManagedTriggers
    .Msnweather("weather-connection")
    .WhenOnCurrentWeatherChange(
        location: () => "Seattle, WA",
        measure: () => OnCurrentWeatherChangeMeasureInput.Temperature,
        when: () => OnCurrentWeatherChangeWhenInput.GoesOver,
        target: () => 75.0,
        units: () => "F"
    );
```

**Common Scenarios:**
- Alert when temperature exceeds 90°F
- Notify when UV index goes over 7
- Trigger when humidity drops below 30%
- Alert when wind speed exceeds 25 mph

**API:** Webhook-based trigger

---

### WhenOnCurrentConditionsChange

Triggers a workflow when the overall weather conditions change at a specified location.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "msnweather")]
public static IOutputWorkflowTrigger<CurrentWeather> WhenOnCurrentConditionsChange(
    [ConnectionName] string connectionId,
    Expression<Func<string>> location,
    Expression<Func<OnCurrentConditionsChangeunitsInput>> units)
```

**Parameters:**
- `connectionId` **[ConnectionName]** - MSN Weather connection ID
- `location` - Location to monitor
- `units` - Measurement units (Imperial or Metric)

**Returns:** `IOutputWorkflowTrigger<CurrentWeather>`

**Usage:**
```csharp
var trigger = WorkflowActions.ManagedTriggers
    .Msnweather("weather-connection")
    .WhenOnCurrentConditionsChange(
        location: () => "Boston, MA",
        units: () => OnCurrentConditionsChangeunitsInput.Imperial
    );
```

**Use Cases:**
- Monitor general weather changes
- Track condition transitions (sunny to rainy, clear to cloudy, etc.)
- General weather monitoring without specific thresholds

**API:** Webhook-based trigger

---

## Complete Usage Example

### Weather Monitoring Agent

```csharp
using Microsoft.Azure.Workflows.Sdk.Agents;
using Microsoft.Azure.Workflows.Sdk.Agents.Connectors.Msnweather;

// Create weather connector
var weather = WorkflowActions.ManagedConnectors
    .Msnweather("weather-connection");

// Get current weather
var currentWeather = weather.CurrentWeather(
    location: () => "Seattle, WA",
    units: () => CurrentWeatherunitsInput.Imperial
);

// Get today's forecast
var todayForecast = weather.TodaysForecast(
    location: () => "Seattle, WA",
    units: () => TodaysForecastunitsInput.Imperial
);

// Get tomorrow's forecast
var tomorrowForecast = weather.TomorrowsForecast(
    location: () => "Seattle, WA",
    units: () => TomorrowsForecastunitsInput.Imperial
);

// Set up weather change trigger
var temperatureTrigger = WorkflowActions.ManagedTriggers
    .Msnweather("weather-connection")
    .WhenOnCurrentWeatherChange(
        location: () => "Seattle, WA",
        measure: () => OnCurrentWeatherChangeMeasureInput.Temperature,
        when: () => OnCurrentWeatherChangeWhenInput.GoesOver,
        target: () => 80.0,
        units: () => "F"
    );

// Monitor general conditions
var conditionsTrigger = WorkflowActions.ManagedTriggers
    .Msnweather("weather-connection")
    .WhenOnCurrentConditionsChange(
        location: () => "Seattle, WA",
        units: () => OnCurrentConditionsChangeunitsInput.Imperial
    );
```

### Agent Integration

```csharp
var agent = new AgentBuilder
{
    AgentModelType = AgentModelType.AzureOpenAI,
    DeploymentId = "gpt-4",
    ConnectionName = "agent",
    Messages = new AgentPromptMessage[]
    {
        new AgentPromptMessage
        {
            Role = MessageRole.System,
            Content = "You are a weather assistant. Provide weather information and alerts."
        }
    }
};

// Add weather tool to agent
agent.AddTool<WeatherToolParameters>(
    tool => {
        var weatherAction = WorkflowActions.ManagedConnectors
            .Msnweather("weather-connection")
            .CurrentWeather(
                location: () => tool.Parameters.Location,
                units: () => CurrentWeatherunitsInput.Imperial
            );
        tool.AddAction(weatherAction);
    },
    description: "Gets current weather for a location",
    parameters: new WeatherToolParameters()
);
```

---

## Summary

### MSN Weather Connector Provides:
- ✅ **5 Weather Actions** - Current weather and forecasts
- ✅ **3 Weather Triggers** - Metric-based changes, general condition changes
- ✅ **Strongly-typed inputs** - All enum-based for type safety
- ✅ **Flexible location support** - City names, zip codes, coordinates
- ✅ **Multiple units** - Imperial and Metric systems

### Common Use Cases:
- **Weather dashboards** - Display current conditions and forecasts
- **Smart home automation** - Adjust thermostats based on weather
- **Travel planning** - Check weather for destinations
- **Outdoor activities** - Monitor conditions for events
- **Safety alerts** - Notify about extreme weather conditions
- **Agriculture** - Track weather for farming operations

### Change Summary (v1.141.0.9 → v1.141.0.10):
- **Total Methods:** 5 → 8 (+3 methods, +60%)
- **Actions:** 3 → 5 (+2 methods)
- **Triggers:** 2 → 3 (+1 method)
- **Breaking Change:** All methods now use Input enums instead of strings

### DynamicValues Operations:
- `GetMeasureUnits` - Provides measurement units for triggers

---

**Version:** 1.141.0.10  
**Status:** ✅ Production Ready  
**Breaking Changes:** ⚠️ YES - Input parameter types changed  
**Last Updated:** November 5, 2025

---

_For complete SDK documentation, see [README.md](README.md). For AgentBuilder integration, see [AGENT_BUILDER_GUIDE.md](AGENT_BUILDER_GUIDE.md)._
