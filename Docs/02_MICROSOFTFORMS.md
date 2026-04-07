# Microsoft Forms Connector - Complete Reference

**Namespace:** `Microsoft.Azure.Workflows.Sdk.Agents.Connectors.Microsoftforms`  
**Connector Name:** `microsoftforms`  
**Type:** API Management  
**SDK Version:** 1.141.0.10  
**Breaking Changes:** ⚠️ None for existing methods

---

## 📋 Table of Contents

### Quick Links
- [Overview](#overview)
- [Actions](#actions)
- [Triggers](#triggers)
- [DynamicValues Operations](#dynamicvalues-operations)
- [Complete Usage Example](#complete-usage-example)
- [Summary](#summary)

### Form Operations (Actions)
- [GetFormResponseById](#getformresponsebyid) - Get a specific form response

### Form Webhooks (Triggers)
- [WhenCreateFormWebhook](#whencreateformwebhook) - Trigger when form receives new response

---

## Overview

The Microsoft Forms connector provides integration with Microsoft Forms for retrieving form responses and setting up webhooks for new submissions.

**Access via:**
```csharp
using Microsoft.Azure.Workflows.Sdk.Agents;
using Microsoft.Azure.Workflows.Sdk.Agents.Connectors.Microsoftforms;

var connector = WorkflowActions.ManagedConnectors.Microsoftforms("forms-connection");
```

**Total Methods:** 2 (1 Action, 1 Trigger)  
**Change from v1.141.0.9:** +1 trigger method  
**Breaking Changes:** None for existing methods

---

## Actions

### GetFormResponseById

Retrieves a specific response from a Microsoft Form by its response ID.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "microsoftforms")]
public static IOutputWorkflowAction<JToken> GetFormResponseById(
    [ConnectionName] string connectionId,
    [DynamicValues("ListForms")] Expression<Func<string>> formId,
    Expression<Func<int>> responseId)
```

**Parameters:**
- `connectionId` **[ConnectionName]** - Microsoft Forms connection ID
- `formId` **[DynamicValues("ListForms")]** - ID of the form (populated from ListForms operation)
- `responseId` - ID of the specific response to retrieve

**Returns:** `IOutputWorkflowAction<JToken>` - Form response data as JSON

**Usage:**
```csharp
var response = WorkflowActions.ManagedConnectors
    .Microsoftforms("forms-connection")
    .GetFormResponseById(
        formId: () => "form-abc123",
        responseId: () => 42
    );
```

**API:** `GET /formapi/api/forms('{formId}')/responses?response_id={responseId}`

**Response Example:**
```json
{
    "responder": "user@example.com",
    "submitDate": "2025-11-05T12:00:00Z",
    "answers": [
        {
            "questionId": "q1",
            "answer": "Sample Answer"
        }
    ]
}
```

---

## Triggers

### WhenCreateFormWebhook

Creates a webhook that triggers a workflow when a form receives a new response.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "microsoftforms")]
public static IWorkflowTrigger WhenCreateFormWebhook(
    [ConnectionName] string connectionId,
    [DynamicValues("ListForms")] Expression<Func<string>> formId)
```

**Parameters:**
- `connectionId` **[ConnectionName]** - Microsoft Forms connection ID
- `formId` **[DynamicValues("ListForms")]** - ID of the form to monitor (populated from ListForms operation)

**Returns:** `IWorkflowTrigger`

**Usage:**
```csharp
var trigger = WorkflowActions.ManagedTriggers
    .Microsoftforms("forms-connection")
    .WhenCreateFormWebhook(
        formId: () => "form-abc123"
    );
```

**API:** `POST /formapi/api/forms/{formId}/webhooks`

**Webhook Payload:**
The trigger receives form submission data including:
- Responder information
- Submission timestamp
- All question IDs and answers
- Form metadata

**Common Use Cases:**
- Process survey responses automatically
- Send confirmation emails to respondents
- Update databases with form submissions
- Trigger approval workflows
- Generate reports from form data

---

## DynamicValues Operations

### ListForms

**Referenced by:** `GetFormResponseById`, `WhenCreateFormWebhook`

The `ListForms` operation provides a list of available forms for the `formId` parameter. This operation is called by the LSP server to populate form dropdowns.

**Purpose:** Enables form selection in the editor via IntelliSense

**Expected Response Format:**
```json
{
    "value": [
        {
            "id": "form-abc123",
            "title": "Customer Feedback Survey"
        },
        {
            "id": "form-def456",
            "title": "Event Registration"
        }
    ]
}
```

**Note:** ListForms is a DynamicValues source operation, not directly callable as a workflow action.

---

## Complete Usage Example

### Form Response Processor

```csharp
using Microsoft.Azure.Workflows.Sdk.Agents;
using Microsoft.Azure.Workflows.Sdk.Agents.Connectors.Microsoftforms;

// Create Forms connector
var forms = WorkflowActions.ManagedConnectors
    .Microsoftforms("forms-connection");

// Set up webhook trigger for new responses
var trigger = WorkflowActions.ManagedTriggers
    .Microsoftforms("forms-connection")
    .WhenCreateFormWebhook(
        formId: () => "form-abc123"
    );

// Retrieve a specific response
var response = forms.GetFormResponseById(
    formId: () => "form-abc123",
    responseId: () => 100
);
```

### Agent Integration with Forms

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
            Content = "You are a form response analyzer. Process and summarize form submissions."
        }
    }
};

// Add forms retrieval tool to agent
agent.AddTool<FormToolParameters>(
    tool => {
        var formsAction = WorkflowActions.ManagedConnectors
            .Microsoftforms("forms-connection")
            .GetFormResponseById(
                formId: () => tool.Parameters.FormId,
                responseId: () => tool.Parameters.ResponseId
            );
        tool.AddAction(formsAction);
    },
    description: "Retrieves and analyzes a form response",
    parameters: new FormToolParameters()
);
```

### Workflow Example: Survey Response Handler

```csharp
// Trigger on new survey responses
var surveyTrigger = WorkflowActions.ManagedTriggers
    .Microsoftforms("forms-connection")
    .WhenCreateFormWebhook(
        formId: () => "customer-survey-2025"
    );

// Process the response
// The trigger output contains the full form submission
// You can extract fields and process them in subsequent workflow actions
```

---

## Summary

### Microsoft Forms Connector Provides:
- ✅ **1 Action** - Retrieve form responses by ID
- ✅ **1 Trigger** - Webhook for new form submissions
- ✅ **DynamicValues Integration** - Form dropdown selection
- ✅ **JSON Response Format** - Flexible data structure
- ✅ **Real-time Notifications** - Instant webhook triggers

### Common Use Cases:
- **Customer Feedback Collection** - Process survey responses automatically
- **Event Registration** - Handle RSVP forms and registrations
- **Lead Generation** - Capture and route lead forms
- **Employee Surveys** - Aggregate and analyze employee feedback
- **Order Forms** - Process product or service orders
- **Support Tickets** - Create tickets from form submissions
- **Quiz/Assessment Processing** - Auto-grade and distribute results

### Change Summary (v1.141.0.9 → v1.141.0.10):
- **Total Methods:** 2 → 2 (1 → 2 unique operations with webhooks)
- **Actions:** 1 (unchanged)
- **Triggers:** 0 → 1 (+1 webhook trigger)
- **Breaking Changes:** None

### Integration Points:
- **Microsoft Forms** - Direct integration with Forms API
- **Webhook Support** - Real-time form submission notifications
- **LSP Server** - Form dropdown via DynamicValues
- **Agent Tools** - Retrievable form data for AI processing

---

**Version:** 1.141.0.10  
**Status:** ✅ Production Ready  
**Breaking Changes:** None  
**Last Updated:** November 5, 2025

---

_For complete SDK documentation, see [README.md](README.md). For AgentBuilder integration, see [AGENT_BUILDER_GUIDE.md](AGENT_BUILDER_GUIDE.md)._
