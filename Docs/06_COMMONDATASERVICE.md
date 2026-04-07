# Common Data Service (Dataverse) Connector - Complete Reference

**Namespace:** `Microsoft.Azure.Workflows.Sdk.Agents.Connectors.Commondataservice`  
**Connector Name:** `commondataservice`  
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

### Record Operations (Actions)
- [ListRecords](#listrecords) - Query and list entity records
- [CreateRecord](#createrecord) - Create a new entity record
- [GetItemCodeless](#getitemcodeless) - Get a specific record by ID
- [UpdateRecord](#updaterecord) - Update an existing record
- [DeleteRecord](#deleterecord) - Delete a record

### File & Image Operations (Actions)
- [UpdateEntityFileImageFieldContent](#updateentityfileimagefi eldcontent) - Update file/image field
- [GetEntityFileImageFieldContent](#getentityfileimagefieldcontent) - Get file/image field

### Business Logic Operations (Actions)
- [PerformUnboundAction](#performunboundaction) - Execute unbound actions
- [PerformBoundAction](#performboundaction) - Execute bound actions
- [AssociateEntities](#associateentities) - Create entity relationships
- [DisassociateEntities](#disassociateentities) - Remove entity relationships

### Search & Batch Operations (Actions)
- [GetRelevantRows](#getrelevantrows) - Search records with relevance
- [ExecuteChangeset](#executechangeset) - Batch operations

### Event Monitoring (Triggers)
- [WhenSubscribeWebhookTrigger](#whensubscribewebhooktrigger) - Trigger on entity changes
- [WhenBusinessEventsTrigger](#whenbusinesseventstrigger) - Trigger on business events

---

## Overview

The Common Data Service (Dataverse) connector provides comprehensive CRUD operations, file management, business logic execution, and entity relationship management for Microsoft Dataverse.

**Access via:**
```csharp
using Microsoft.Azure.Workflows.Sdk.Agents;
using Microsoft.Azure.Workflows.Sdk.Agents.Connectors.Commondataservice;

var connector = WorkflowActions.ManagedConnectors.Commondataservice("cds-connection");
```

**Total Methods:** 15 (13 Actions, 2 Triggers)  
**Change from v1.141.0.9:** +13 methods (+86.7%)  
**Breaking Changes:** None for existing methods

---

## Actions

### ListRecords

Query and retrieve a list of records from a Dataverse entity with OData filtering and pagination.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "commondataservice")]
public static IOutputWorkflowAction<EntityItemList> ListRecords(
    [ConnectionName] string connectionId,
    [DynamicValues("GetOrganizations")] Expression<Func<string>> organization,
    [DynamicValues("GetEntityListEnum")] Expression<Func<string>> entityName,
    Expression<Func<string>> select = null,
    Expression<Func<string>> filter = null,
    Expression<Func<string>> orderby = null,
    Expression<Func<string>> expand = null,
    Expression<Func<string>> fetchXml = null,
    Expression<Func<int>> top = null,
    Expression<Func<string>> skiptoken = null,
    Expression<Func<string>> partitionId = null)
```

**Parameters:**
- `connectionId` **[ConnectionName]** - Dataverse connection ID
- `organization` **[DynamicValues("GetOrganizations")]** - Organization URL
- `entityName` **[DynamicValues("GetEntityListEnum")]** - Entity logical name (e.g., "accounts", "contacts")
- `select` - OData $select query (optional)
- `filter` - OData $filter query (optional)
- `orderby` - OData $orderby query (optional)
- `expand` - OData $expand query (optional)
- `fetchXml` - FetchXML query (optional)
- `top` - Maximum records to return (optional)
- `skiptoken` - Pagination token (optional)
- `partitionId` - Partition ID for data isolation (optional)

**Returns:** `IOutputWorkflowAction<EntityItemList>`

**Usage:**
```csharp
var accounts = connector.ListRecords(
    organization: () => "org123.crm.dynamics.com",
    entityName: () => "accounts",
    filter: () => "revenue gt 1000000",
    orderby: () => "name asc",
    top: () => 50
);
```

**API:** `GET /api/data/v9.1/{entityName}`

---

### CreateRecord

Create a new record in a Dataverse entity.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "commondataservice")]
public static IOutputWorkflowAction<JToken> CreateRecord(
    [ConnectionName] string connectionId,
    [DynamicValues("GetOrganizations")] Expression<Func<string>> organization,
    [DynamicValues("GetEntityListEnum")] Expression<Func<string>> entityName,
    Expression<Func<JToken>> item)
```

**Parameters:**
- `connectionId` **[ConnectionName]** - Dataverse connection ID
- `organization` **[DynamicValues("GetOrganizations")]** - Organization URL
- `entityName` **[DynamicValues("GetEntityListEnum")]** - Entity logical name
- `item` - Record data as JSON

**Returns:** `IOutputWorkflowAction<JToken>` - Created record with ID

**Usage:**
```csharp
var newAccount = connector.CreateRecord(
    organization: () => "org123.crm.dynamics.com",
    entityName: () => "accounts",
    item: () => new {
        name = "Contoso Ltd",
        revenue = 5000000,
        industrycode = 1
    }
);
```

**API:** `POST /api/data/v9.1/{entityName}`

---

### GetItemCodeless

Retrieve a specific record by its unique identifier.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "commondataservice")]
public static IOutputWorkflowAction<JToken> GetItemCodeless(
    [ConnectionName] string connectionId,
    [DynamicValues("GetOrganizations")] Expression<Func<string>> organization,
    [DynamicValues("GetEntityListEnum")] Expression<Func<string>> entityName,
    Expression<Func<string>> recordId,
    Expression<Func<string>> select = null,
    Expression<Func<string>> expand = null,
    Expression<Func<string>> partitionId = null)
```

**Parameters:**
- `connectionId` **[ConnectionName]** - Dataverse connection ID
- `organization` **[DynamicValues("GetOrganizations")]** - Organization URL
- `entityName` **[DynamicValues("GetEntityListEnum")]** - Entity logical name
- `recordId` - Unique ID of the record
- `select` - OData $select (optional)
- `expand` - OData $expand (optional)
- `partitionId` - Partition ID (optional)

**Returns:** `IOutputWorkflowAction<JToken>` - Record data

**Usage:**
```csharp
var account = connector.GetItemCodeless(
    organization: () => "org123.crm.dynamics.com",
    entityName: () => "accounts",
    recordId: () => "12345678-1234-1234-1234-123456789012",
    select: () => "name,revenue,industrycode"
);
```

**API:** `GET /api/data/v9.1/{entityName}({recordId})`

---

### UpdateRecord

Update an existing record in a Dataverse entity.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "commondataservice")]
public static IOutputWorkflowAction<JToken> UpdateRecord(
    [ConnectionName] string connectionId,
    [DynamicValues("GetOrganizations")] Expression<Func<string>> organization,
    [DynamicValues("GetEntityListEnum")] Expression<Func<string>> entityName,
    Expression<Func<string>> recordId,
    Expression<Func<JToken>> item)
```

**Parameters:**
- `connectionId` **[ConnectionName]** - Dataverse connection ID
- `organization` **[DynamicValues("GetOrganizations")]** - Organization URL
- `entityName` **[DynamicValues("GetEntityListEnum")]** - Entity logical name
- `recordId` - ID of record to update
- `item` - Updated field values as JSON

**Returns:** `IOutputWorkflowAction<JToken>` - Updated record

**Usage:**
```csharp
var updated = connector.UpdateRecord(
    organization: () => "org123.crm.dynamics.com",
    entityName: () => "accounts",
    recordId: () => "12345678-1234-1234-1234-123456789012",
    item: () => new { revenue = 6000000 }
);
```

**API:** `PATCH /api/data/v9.1/{entityName}({recordId})`

---

### DeleteRecord

Delete a record from a Dataverse entity.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "commondataservice")]
public static IWorkflowAction DeleteRecord(
    [ConnectionName] string connectionId,
    [DynamicValues("GetOrganizations")] Expression<Func<string>> organization,
    [DynamicValues("GetEntityListEnum")] Expression<Func<string>> entityName,
    Expression<Func<string>> recordId,
    Expression<Func<string>> partitionId = null)
```

**Parameters:**
- `connectionId` **[ConnectionName]** - Dataverse connection ID
- `organization` **[DynamicValues("GetOrganizations")]** - Organization URL
- `entityName` **[DynamicValues("GetEntityListEnum")]** - Entity logical name
- `recordId` - ID of record to delete
- `partitionId` - Partition ID (optional)

**Returns:** `IWorkflowAction` - No output

**Usage:**
```csharp
connector.DeleteRecord(
    organization: () => "org123.crm.dynamics.com",
    entityName: () => "accounts",
    recordId: () => "12345678-1234-1234-1234-123456789012"
);
```

**API:** `DELETE /api/data/v9.1/{entityName}({recordId})`

---

### UpdateEntityFileImageFieldContent

Update file or image field content for an entity record.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "commondataservice")]
public static IWorkflowAction UpdateEntityFileImageFieldContent(
    [ConnectionName] string connectionId,
    [DynamicValues("GetOrganizations")] Expression<Func<string>> organization,
    [DynamicValues("GetEntityListEnum")] Expression<Func<string>> entityName,
    Expression<Func<string>> recordId,
    [DynamicValues("GetAttributeFiltersCodeless")] Expression<Func<string>> fileImageFieldName,
    Expression<Func<string>> xMsFileName,
    Expression<Func<string>> item)
```

**Parameters:**
- `connectionId` **[ConnectionName]** - Dataverse connection ID
- `organization` **[DynamicValues("GetOrganizations")]** - Organization URL
- `entityName` **[DynamicValues("GetEntityListEnum")]** - Entity logical name
- `recordId` - Record ID
- `fileImageFieldName` **[DynamicValues("GetAttributeFiltersCodeless")]** - Field name
- `xMsFileName` - File name
- `item` - File content (base64 encoded)

**Returns:** `IWorkflowAction`

**Usage:**
```csharp
connector.UpdateEntityFileImageFieldContent(
    organization: () => "org123.crm.dynamics.com",
    entityName: () => "accounts",
    recordId: () => "12345678-1234-1234-1234-123456789012",
    fileImageFieldName: () => "entityimage",
    xMsFileName: () => "logo.png",
    item: () => base64Content
);
```

**API:** `PATCH /api/data/v9.1/{entityName}({recordId})/{fieldName}`

---

### GetEntityFileImageFieldContent

Retrieve file or image field content from an entity record.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "commondataservice")]
public static IOutputWorkflowAction<string> GetEntityFileImageFieldContent(
    [ConnectionName] string connectionId,
    [DynamicValues("GetOrganizations")] Expression<Func<string>> organization,
    [DynamicValues("GetEntityListEnum")] Expression<Func<string>> entityName,
    Expression<Func<string>> recordId,
    [DynamicValues("GetAttributeFiltersCodeless")] Expression<Func<string>> fileImageFieldName,
    Expression<Func<string>> size = null)
```

**Parameters:**
- `connectionId` **[ConnectionName]** - Dataverse connection ID
- `organization` **[DynamicValues("GetOrganizations")]** - Organization URL
- `entityName` **[DynamicValues("GetEntityListEnum")]** - Entity logical name
- `recordId` - Record ID
- `fileImageFieldName` **[DynamicValues("GetAttributeFiltersCodeless")]** - Field name
- `size` - Image size (optional, for image fields)

**Returns:** `IOutputWorkflowAction<string>` - File content (base64)

**Usage:**
```csharp
var fileContent = connector.GetEntityFileImageFieldContent(
    organization: () => "org123.crm.dynamics.com",
    entityName: () => "accounts",
    recordId: () => "12345678-1234-1234-1234-123456789012",
    fileImageFieldName: () => "entityimage"
);
```

**API:** `GET /api/data/v9.1/{entityName}({recordId})/{fieldName}`

---

### PerformUnboundAction

Execute an unbound action (not tied to a specific record).

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "commondataservice")]
public static IOutputWorkflowAction<JToken> PerformUnboundAction(
    [ConnectionName] string connectionId,
    [DynamicValues("GetOrganizations")] Expression<Func<string>> organization,
    [DynamicValues("GetUnboundActions")] Expression<Func<string>> actionName,
    Expression<Func<JToken>> item)
```

**Parameters:**
- `connectionId` **[ConnectionName]** - Dataverse connection ID
- `organization` **[DynamicValues("GetOrganizations")]** - Organization URL
- `actionName` **[DynamicValues("GetUnboundActions")]** - Action name
- `item` - Action parameters as JSON

**Returns:** `IOutputWorkflowAction<JToken>` - Action result

**Usage:**
```csharp
var result = connector.PerformUnboundAction(
    organization: () => "org123.crm.dynamics.com",
    actionName: () => "WhoAmI",
    item: () => new { }
);
```

**API:** `POST /api/data/v9.1/{actionName}`

---

### PerformBoundAction

Execute a bound action (tied to a specific record).

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "commondataservice")]
public static IOutputWorkflowAction<JToken> PerformBoundAction(
    [ConnectionName] string connectionId,
    [DynamicValues("GetOrganizations")] Expression<Func<string>> organization,
    [DynamicValues("GetEntityListEnum")] Expression<Func<string>> entityName,
    [DynamicValues("GetBoundActions")] Expression<Func<string>> actionName,
    Expression<Func<string>> recordId,
    Expression<Func<JToken>> item)
```

**Parameters:**
- `connectionId` **[ConnectionName]** - Dataverse connection ID
- `organization` **[DynamicValues("GetOrganizations")]** - Organization URL
- `entityName` **[DynamicValues("GetEntityListEnum")]** - Entity logical name
- `actionName` **[DynamicValues("GetBoundActions")]** - Action name
- `recordId` - Record ID to execute action on
- `item` - Action parameters as JSON

**Returns:** `IOutputWorkflowAction<JToken>` - Action result

**Usage:**
```csharp
var result = connector.PerformBoundAction(
    organization: () => "org123.crm.dynamics.com",
    entityName: () => "accounts",
    actionName: () => "CalculateRollupField",
    recordId: () => "12345678-1234-1234-1234-123456789012",
    item: () => new { FieldName = "totalrevenue" }
);
```

**API:** `POST /api/data/v9.1/{entityName}({recordId})/Microsoft.Dynamics.CRM.{actionName}`

---

### AssociateEntities

Create a relationship between two entity records.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "commondataservice")]
public static IWorkflowAction AssociateEntities(
    [ConnectionName] string connectionId,
    [DynamicValues("GetOrganizations")] Expression<Func<string>> organization,
    [DynamicValues("GetEntityListEnum")] Expression<Func<string>> entityName,
    Expression<Func<string>> recordId,
    [DynamicValues("GetEntityRelationships")] Expression<Func<string>> associationEntityRelationship,
    Expression<Func<AssociateEntityRequest>> item)
```

**Parameters:**
- `connectionId` **[ConnectionName]** - Dataverse connection ID
- `organization` **[DynamicValues("GetOrganizations")]** - Organization URL
- `entityName` **[DynamicValues("GetEntityListEnum")]** - Primary entity
- `recordId` - Primary record ID
- `associationEntityRelationship` **[DynamicValues("GetEntityRelationships")]** - Relationship name
- `item` - Association details

**Returns:** `IWorkflowAction`

**Usage:**
```csharp
connector.AssociateEntities(
    organization: () => "org123.crm.dynamics.com",
    entityName: () => "accounts",
    recordId: () => "account-id",
    associationEntityRelationship: () => "contact_customer_accounts",
    item: () => new AssociateEntityRequest { 
        RelatedEntityId = "contact-id" 
    }
);
```

**API:** `POST /api/data/v9.1/{entityName}({recordId})/{relationship}/$ref`

---

### DisassociateEntities

Remove a relationship between two entity records.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "commondataservice")]
public static IWorkflowAction DisassociateEntities(
    [ConnectionName] string connectionId,
    [DynamicValues("GetOrganizations")] Expression<Func<string>> organization,
    [DynamicValues("GetEntityListEnum")] Expression<Func<string>> entityName,
    Expression<Func<string>> recordId,
    [DynamicValues("GetEntityRelationships")] Expression<Func<string>> associationEntityRelationship,
    Expression<Func<string>> id)
```

**Parameters:**
- `connectionId` **[ConnectionName]** - Dataverse connection ID
- `organization` **[DynamicValues("GetOrganizations")]** - Organization URL
- `entityName` **[DynamicValues("GetEntityListEnum")]** - Primary entity
- `recordId` - Primary record ID
- `associationEntityRelationship` **[DynamicValues("GetEntityRelationships")]** - Relationship name
- `id` - Related record ID to disassociate

**Returns:** `IWorkflowAction`

**Usage:**
```csharp
connector.DisassociateEntities(
    organization: () => "org123.crm.dynamics.com",
    entityName: () => "accounts",
    recordId: () => "account-id",
    associationEntityRelationship: () => "contact_customer_accounts",
    id: () => "contact-id"
);
```

**API:** `DELETE /api/data/v9.1/{entityName}({recordId})/{relationship}({id})/$ref`

---

### GetRelevantRows

Search for relevant records using Dataverse search with ranking.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "commondataservice")]
public static IOutputWorkflowAction<SearchOutput> GetRelevantRows(
    [ConnectionName] string connectionId,
    [DynamicValues("GetOrganizations")] Expression<Func<string>> organization,
    Expression<Func<SearchRequestBody>> searchRequest)
```

**Parameters:**
- `connectionId` **[ConnectionName]** - Dataverse connection ID
- `organization` **[DynamicValues("GetOrganizations")]** - Organization URL
- `searchRequest` - Search query parameters

**Returns:** `IOutputWorkflowAction<SearchOutput>` - Ranked search results

**Usage:**
```csharp
var results = connector.GetRelevantRows(
    organization: () => "org123.crm.dynamics.com",
    searchRequest: () => new SearchRequestBody {
        search = "contoso",
        entities = new[] { "accounts", "contacts" },
        top = 10
    }
);
```

**API:** `POST /api/data/v9.1/search`

---

### ExecuteChangeset

Execute multiple operations in a single batch transaction.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "commondataservice")]
public static IWorkflowAction ExecuteChangeset(
    [ConnectionName] string connectionId)
```

**Parameters:**
- `connectionId` **[ConnectionName]** - Dataverse connection ID

**Returns:** `IWorkflowAction`

**Usage:**
```csharp
// Batch operations executed atomically
connector.ExecuteChangeset();
```

**API:** `POST /api/data/v9.1/$batch`

---

## Triggers

### WhenSubscribeWebhookTrigger

Trigger a workflow when entity records change in Dataverse.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "commondataservice")]
public static IWorkflowTrigger WhenSubscribeWebhookTrigger(
    [ConnectionName] string connectionId,
    [DynamicValues("GetOrganizations")] Expression<Func<string>> organization,
    Expression<Func<CallbackRegistration>> subscriptionRequest)
```

**Parameters:**
- `connectionId` **[ConnectionName]** - Dataverse connection ID
- `organization` **[DynamicValues("GetOrganizations")]** - Organization URL
- `subscriptionRequest` - Webhook registration details

**Returns:** `IWorkflowTrigger`

**Usage:**
```csharp
var trigger = WorkflowActions.ManagedTriggers
    .Commondataservice("cds-connection")
    .WhenSubscribeWebhookTrigger(
        organization: () => "org123.crm.dynamics.com",
        subscriptionRequest: () => new CallbackRegistration {
            EntityName = "accounts",
            Scope = 4, // Organization
            Message = 1 // Create
        }
    );
```

**API:** `POST /api/data/v9.1/callbackregistrations`

---

### WhenBusinessEventsTrigger

Trigger a workflow on business events in Dataverse.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "commondataservice")]
public static IWorkflowTrigger WhenBusinessEventsTrigger(
    [ConnectionName] string connectionId,
    [DynamicValues("GetOrganizations")] Expression<Func<string>> organization,
    [DynamicValues("GetCatalogs")] Expression<Func<string>> catalog,
    [DynamicValues("GetEntityListEnum")] Expression<Func<string>> category,
    Expression<Func<WhenAnActionIsPerformedSubscriptionRequest>> subscriptionRequest)
```

**Parameters:**
- `connectionId` **[ConnectionName]** - Dataverse connection ID
- `organization` **[DynamicValues("GetOrganizations")]** - Organization URL
- `catalog` **[DynamicValues("GetCatalogs")]** - Event catalog
- `category` **[DynamicValues("GetEntityListEnum")]** - Event category
- `subscriptionRequest` - Event subscription details

**Returns:** `IWorkflowTrigger`

**Usage:**
```csharp
var trigger = WorkflowActions.ManagedTriggers
    .Commondataservice("cds-connection")
    .WhenBusinessEventsTrigger(
        organization: () => "org123.crm.dynamics.com",
        catalog: () => "BusinessEvents",
        category: () => "accounts",
        subscriptionRequest: () => new WhenAnActionIsPerformedSubscriptionRequest()
    );
```

**API:** Webhook-based business event trigger

---

## DynamicValues Operations

The CDS connector uses several DynamicValues operations to populate dropdowns:

### GetOrganizations
**Purpose:** Lists available Dataverse organizations  
**Used by:** All methods

### GetEntityListEnum
**Purpose:** Lists entity logical names (tables)  
**Used by:** Most CRUD operations  
**Hierarchy:** Organization → Entities

### GetEntityRelationships
**Purpose:** Lists relationships for an entity  
**Used by:** AssociateEntities, DisassociateEntities  
**Hierarchy:** Organization → Entity → Relationships

### GetBoundActions
**Purpose:** Lists bound actions for an entity  
**Used by:** PerformBoundAction  
**Hierarchy:** Organization → Entity → Actions

### GetUnboundActions
**Purpose:** Lists unbound actions  
**Used by:** PerformUnboundAction  
**Hierarchy:** Organization → Actions

### GetAttributeFiltersCodeless
**Purpose:** Lists file/image fields for an entity  
**Used by:** File/Image operations  
**Hierarchy:** Organization → Entity → Fields

### GetCatalogs
**Purpose:** Lists business event catalogs  
**Used by:** WhenBusinessEventsTrigger  
**Hierarchy:** Organization → Catalogs

---

## Complete Usage Example

```csharp
using Microsoft.Azure.Workflows.Sdk.Agents;
using Microsoft.Azure.Workflows.Sdk.Agents.Connectors.Commondataservice;

// Create CDS connector
var cds = WorkflowActions.ManagedConnectors
    .Commondataservice("cds-connection");

// List accounts with filter
var accounts = cds.ListRecords(
    organization: () => "org123.crm.dynamics.com",
    entityName: () => "accounts",
    filter: () => "revenue gt 1000000",
    orderby: () => "name asc",
    top: () => 10
);

// Create a new account
var newAccount = cds.CreateRecord(
    organization: () => "org123.crm.dynamics.com",
    entityName: () => "accounts",
    item: () => new {
        name = "Contoso Ltd",
        revenue = 5000000
    }
);

// Update the account
var updated = cds.UpdateRecord(
    organization: () => "org123.crm.dynamics.com",
    entityName: () => "accounts",
    recordId: () => "account-id",
    item: () => new { revenue = 6000000 }
);

// Set up webhook trigger
var trigger = WorkflowActions.ManagedTriggers
    .Commondataservice("cds-connection")
    .WhenSubscribeWebhookTrigger(
        organization: () => "org123.crm.dynamics.com",
        subscriptionRequest: () => new CallbackRegistration {
            EntityName = "accounts",
            Scope = 4,
            Message = 1
        }
    );
```

---

## Summary

### Common Data Service Connector Provides:
- ✅ **13 Actions** - Complete CRUD, file management, business logic, search
- ✅ **2 Triggers** - Entity webhooks and business events
- ✅ **Complex DynamicValues Hierarchy** - Multi-level dropdown navigation
- ✅ **OData Query Support** - Advanced filtering and pagination
- ✅ **Batch Operations** - Transactional changesets
- ✅ **Relationship Management** - Associate/disassociate entities

### Common Use Cases:
- **CRM Data Management** - Accounts, contacts, leads, opportunities
- **Custom Business Apps** - Custom entity CRUD operations
- **Data Integration** - Sync with external systems
- **Business Process Automation** - Trigger workflows on data changes
- **File Management** - Store and retrieve documents/images
- **Advanced Queries** - FetchXML and OData filtering

### Change Summary (v1.141.0.9 → v1.141.0.10):
- **Total Methods:** 2 → 15 (+13 methods, +650%)
- **Actions:** 0 → 13 (all new)
- **Triggers:** 2 → 2 (unchanged count, but enhanced)
- **Breaking Changes:** None

### Integration Points:
- **Microsoft Dataverse** - Full Web API v9.1 support
- **Power Platform** - Native integration
- **Dynamics 365** - CE, Sales, Service Cloud
- **Custom Solutions** - Model-driven apps

---

**Version:** 1.141.0.10  
**Status:** ✅ Production Ready  
**Breaking Changes:** None  
**Last Updated:** November 6, 2025

---

_For complete SDK documentation, see [README.md](README.md). For AgentBuilder integration, see [AGENT_BUILDER_GUIDE.md](AGENT_BUILDER_GUIDE.md)._
