# Outlook Connector - Complete Reference

**Namespace:** `Microsoft.Azure.Workflows.Sdk.Agents.Connectors.Outlook`  
**Connector Name:** `outlook`  
**Type:** API Management  
**SDK Version:** 1.141.0.10  
**Breaking Changes:** ⚠️ YES - Input enums for importance, sensitivity, and more

---

## 📋 Table of Contents

### Quick Links
- [Overview](#overview)
- [Actions](#actions)
- [Triggers](#triggers)
- [Input Types](#input-types)
- [Summary](#summary)

### Email Operations (Actions)
- [SendMail](#sendmail) - Send email
- [SendMailV2](#sendmailv2) - Send email (v2)
- [SendMailWithOptions](#sendmailwithoptions) - Send email with response options
- [GetEmailsV2](#getemailsv2) - Get emails with filtering
- [GetEmail](#getemail) - Get single email
- [ReplyToEmailV3](#replytoemailv3) - Reply to email
- [ForwardEmailV3](#forwardemailv3) - Forward email
- [DeleteEmailV2](#deleteemailv2) - Delete email
- [MoveEmailV2](#moveemailv2) - Move email to folder
- [MarkAsReadOrUnreadV3](#markasreadorunreadv3) - Mark email read/unread
- [FlagEmailV2](#flagemailv2) - Flag email
- [SetUpAutomaticRepliesV2](#setupautomaticrepliesv2) - Configure out-of-office

### Calendar Operations (Actions)
- [CalendarGetEvents](#calendargetevents) - Get calendar events
- [CalendarGetEventV2](#calendargeteventv2) - Get single event
- [CalendarPostEventV2](#calendarposteventv2) - Create event
- [CalendarUpdateEventV2](#calendarupdateeventv2) - Update event
- [CalendarDeleteEventV2](#calendardeleteeventv2) - Delete event
- [CalendarRespondToEvent](#calendarrespondtoevent) - RSVP to event

### Contact Operations (Actions)
- [ContactsGetContactV2](#contactsgetcontactv2) - Get contact
- [ContactsGetContacts](#contactsgetcontacts) - List contacts
- [ContactsPostContactV2](#contactspostcontactv2) - Create contact
- [ContactsUpdateContactV2](#contactsupdatecontactv2) - Update contact
- [ContactsDeleteContactV2](#contactsdeletecontactv2) - Delete contact

### Folder Operations (Actions)
- [GetFolders](#getfolders) - List mail folders
- [CreateFolder](#createfolder) - Create mail folder

### Email Triggers
- [WhenOnNewEmailV2](#whenonnewemailv2) - New email received
- [WhenOnFlaggedEmailV2](#whenonflaggedemailv2) - Email flagged
- [WhenOnNewMentionMeEmailV2](#whenonnewmentionmeemailv2) - @mentioned in email

### Calendar Triggers
- [WhenCalendarGetOnNewItemsV2](#whencalendargetonnewitemsv2) - New calendar event
- [WhenCalendarGetOnUpdatedItemsV2](#whencalendargetonupdateditemsv2) - Event updated
- [WhenCalendarGetOnChangedItemsV2](#whencalendargetonchangeditemsv2) - Event changed

---

## Overview

The Outlook connector provides comprehensive email, calendar, and contact management through Microsoft Graph API.

**Access via:**
```csharp
using Microsoft.Azure.Workflows.Sdk.Agents;
using Microsoft.Azure.Workflows.Sdk.Agents.Connectors.Outlook;

var connector = WorkflowActions.ManagedConnectors.Outlook("outlook-connection");
```

**Total Methods:** 33 (26 Actions, 7 Triggers)  
**Change from v1.141.0.9:** +26 methods  
**Breaking Changes:** ⚠️ Input enums for importance, sensitivity, flags, etc.

---

## Input Types

### GetEmailsV2importanceInput / OnNewEmailV2importanceInput
```csharp
public enum GetEmailsV2importanceInput
{
    Low,
    Normal,
    High
}
```

### GetEmailsV2sensitivityInput
```csharp
public enum GetEmailsV2sensitivityInput
{
    Normal,
    Personal,
    Private,
    Confidential
}
```

### MarkAsReadOrUnreadV3isReadInput
```csharp
public enum MarkAsReadOrUnreadV3isReadInput
{
    [EnumMember(Value = "true")]
    True,
    [EnumMember(Value = "false")]
    False
}
```

### FlagEmailV2flagStatusInput
```csharp
public enum FlagEmailV2flagStatusInput
{
    NotFlagged,
    Complete,
    Flagged
}
```

### CalendarRespondToEventcommentInput
```csharp
public enum CalendarRespondToEventcommentInput
{
    [EnumMember(Value = "Accept")]
    Accept,
    [EnumMember(Value = "Tentative")]
    Tentative,
    [EnumMember(Value = "Decline")]
    Decline
}
```

---

## Actions

### SendMailV2

Send an email message.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "outlook")]
public static IWorkflowAction SendMailV2(
    [ConnectionName] string connectionId,
    Expression<Func<ClientSendMessage>> item)
```

**Parameters:**
- `item` - Email message details (to, subject, body, attachments)

**Returns:** `IWorkflowAction`

**Usage:**
```csharp
connector.SendMailV2(
    item: () => new ClientSendMessage {
        To = "recipient@example.com",
        Subject = "Test Email",
        Body = "Email body content",
        Importance = "Normal"
    }
);
```

**API:** `POST /v2/Mail`

---

### GetEmailsV2

Get emails from mailbox with filtering options.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "outlook")]
public static IOutputWorkflowAction<BatchResponseClientReceiveMessage> GetEmailsV2(
    [ConnectionName] string connectionId,
    Expression<Func<string>> folderPath = null,
    Expression<Func<string>> to = null,
    Expression<Func<string>> cc = null,
    Expression<Func<string>> toOrCc = null,
    Expression<Func<string>> from = null,
    Expression<Func<GetEmailsV2importanceInput>> importance = null,
    Expression<Func<bool>> fetchOnlyWithAttachment = null,
    Expression<Func<string>> subjectFilter = null,
    Expression<Func<bool>> fetchOnlyUnread = null,
    Expression<Func<bool>> includeAttachments = null,
    Expression<Func<string>> searchQuery = null,
    Expression<Func<int>> top = null)
```

**Parameters:**
- `folderPath` - Folder to search (e.g., "Inbox", "Sent Items")
- `to` - Filter by recipient
- `from` - Filter by sender
- `importance` - Low, Normal, or High
- `fetchOnlyWithAttachment` - Only emails with attachments
- `fetchOnlyUnread` - Only unread emails
- `top` - Maximum number of emails to return

**Returns:** `IOutputWorkflowAction<BatchResponseClientReceiveMessage>`

**Usage:**
```csharp
var emails = connector.GetEmailsV2(
    folderPath: () => "Inbox",
    from: () => "sender@example.com",
    fetchOnlyUnread: () => true,
    importance: () => GetEmailsV2importanceInput.High,
    top: () => 50
);
```

**API:** `GET /v2/Mail`

---

### ReplyToEmailV3

Reply to an email.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "outlook")]
public static IWorkflowAction ReplyToEmailV3(
    [ConnectionName] string connectionId,
    Expression<Func<string>> messageId,
    Expression<Func<ClientSendMessage>> item)
```

**Parameters:**
- `messageId` - ID of email to reply to
- `item` - Reply message content

**Returns:** `IWorkflowAction`

**Usage:**
```csharp
connector.ReplyToEmailV3(
    messageId: () => "message-id-123",
    item: () => new ClientSendMessage {
        Body = "Reply content",
        Comment = "Additional comment"
    }
);
```

**API:** `POST /v2/Mail/{messageId}/Reply`

---

### ForwardEmailV3

Forward an email.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "outlook")]
public static IWorkflowAction ForwardEmailV3(
    [ConnectionName] string connectionId,
    Expression<Func<string>> messageId,
    Expression<Func<ClientSendMessage>> item)
```

**Parameters:**
- `messageId` - ID of email to forward
- `item` - Forward details (to, comment)

**Returns:** `IWorkflowAction`

**Usage:**
```csharp
connector.ForwardEmailV3(
    messageId: () => "message-id-123",
    item: () => new ClientSendMessage {
        To = "recipient@example.com",
        Comment = "FYI"
    }
);
```

**API:** `POST /v2/Mail/{messageId}/Forward`

---

### DeleteEmailV2

Delete an email.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "outlook")]
public static IWorkflowAction DeleteEmailV2(
    [ConnectionName] string connectionId,
    Expression<Func<string>> messageId)
```

**Parameters:**
- `messageId` - ID of email to delete

**Returns:** `IWorkflowAction`

**Usage:**
```csharp
connector.DeleteEmailV2(
    messageId: () => "message-id-123"
);
```

**API:** `DELETE /v2/Mail/{messageId}`

---

### CalendarGetEvents

Get calendar events.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "outlook")]
public static IOutputWorkflowAction<CalendarEventListClientReceive> CalendarGetEvents(
    [ConnectionName] string connectionId,
    [DynamicValues("CalendarGetTables")] Expression<Func<string>> table = null,
    Expression<Func<int>> top = null,
    Expression<Func<string>> filter = null)
```

**Parameters:**
- `table` **[DynamicValues("CalendarGetTables")]** - Calendar ID
- `top` - Maximum number of events
- `filter` - OData filter query

**Returns:** `IOutputWorkflowAction<CalendarEventListClientReceive>`

**Usage:**
```csharp
var events = connector.CalendarGetEvents(
    table: () => "calendar-id",
    top: () => 50,
    filter: () => "start/dateTime ge '2025-11-06'"
);
```

**API:** `GET /v2/Calendar/events`

---

### CalendarPostEventV2

Create a new calendar event.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "outlook")]
public static IOutputWorkflowAction<ClientReceiveCalendarEvent> CalendarPostEventV2(
    [ConnectionName] string connectionId,
    [DynamicValues("CalendarGetTables")] Expression<Func<string>> table,
    Expression<Func<ClientSendCalendarEvent>> item)
```

**Parameters:**
- `table` **[DynamicValues("CalendarGetTables")]** - Calendar ID
- `item` - Event details (subject, start, end, location, attendees)

**Returns:** `IOutputWorkflowAction<ClientReceiveCalendarEvent>`

**Usage:**
```csharp
var event = connector.CalendarPostEventV2(
    table: () => "calendar-id",
    item: () => new ClientSendCalendarEvent {
        Subject = "Team Meeting",
        Start = DateTime.Now.AddDays(1),
        End = DateTime.Now.AddDays(1).AddHours(1),
        Location = "Conference Room A"
    }
);
```

**API:** `POST /v2/Calendar/events`

---

### ContactsGetContacts

List contacts.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "outlook")]
public static IOutputWorkflowAction<ContactListClientReceive> ContactsGetContacts(
    [ConnectionName] string connectionId,
    [DynamicValues("ContactGetFolders")] Expression<Func<string>> table = null,
    Expression<Func<int>> top = null,
    Expression<Func<string>> filter = null)
```

**Parameters:**
- `table` **[DynamicValues("ContactGetFolders")]** - Contact folder ID
- `top` - Maximum contacts to return
- `filter` - OData filter

**Returns:** `IOutputWorkflowAction<ContactListClientReceive>`

**Usage:**
```csharp
var contacts = connector.ContactsGetContacts(
    table: () => "default-contacts",
    top: () => 100
);
```

**API:** `GET /v2/Contacts`

---

### ContactsPostContactV2

Create a new contact.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "outlook")]
public static IOutputWorkflowAction<ClientReceiveContact> ContactsPostContactV2(
    [ConnectionName] string connectionId,
    [DynamicValues("ContactGetFolders")] Expression<Func<string>> table,
    Expression<Func<ClientSendContact>> item)
```

**Parameters:**
- `table` **[DynamicValues("ContactGetFolders")]** - Contact folder ID
- `item` - Contact details (name, email, phone)

**Returns:** `IOutputWorkflowAction<ClientReceiveContact>`

**Usage:**
```csharp
var contact = connector.ContactsPostContactV2(
    table: () => "default-contacts",
    item: () => new ClientSendContact {
        GivenName = "John",
        Surname = "Doe",
        EmailAddresses = new[] {
            new EmailAddress { Address = "john.doe@example.com" }
        }
    }
);
```

**API:** `POST /v2/Contacts`

---

## Triggers

### WhenOnNewEmailV2

Trigger when a new email is received.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "outlook")]
public static IOutputWorkflowTrigger<TriggerBatchResponseClientReceiveMessage> WhenOnNewEmailV2(
    [ConnectionName] string connectionId,
    Expression<Func<string>> folderPath = null,
    Expression<Func<string>> to = null,
    Expression<Func<string>> cc = null,
    Expression<Func<string>> toOrCc = null,
    Expression<Func<string>> from = null,
    Expression<Func<OnNewEmailV2importanceInput>> importance = null,
    Expression<Func<bool>> fetchOnlyWithAttachment = null,
    Expression<Func<bool>> includeAttachments = null,
    Expression<Func<string>> subjectFilter = null)
```

**Parameters:**
- `folderPath` - Folder to monitor (optional)
- `from` - Filter by sender (optional)
- `importance` - Filter by importance (optional)
- `fetchOnlyWithAttachment` - Only trigger on emails with attachments (optional)
- `includeAttachments` - Include attachment content (optional)

**Returns:** `IOutputWorkflowTrigger<TriggerBatchResponseClientReceiveMessage>`

**Usage:**
```csharp
var trigger = WorkflowActions.ManagedTriggers
    .Outlook("outlook-connection")
    .WhenOnNewEmailV2(
        folderPath: () => "Inbox",
        importance: () => OnNewEmailV2importanceInput.High,
        fetchOnlyWithAttachment: () => false
    );
```

**API:** Poll-based trigger checking for new emails

---

### WhenCalendarGetOnNewItemsV2

Trigger when a new calendar event is created.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "outlook")]
public static IOutputWorkflowTrigger<CalendarEventListWithActionType> WhenCalendarGetOnNewItemsV2(
    [ConnectionName] string connectionId,
    [DynamicValues("CalendarGetTables")] Expression<Func<string>> table,
    Expression<Func<int>> incomingDays = null)
```

**Parameters:**
- `table` **[DynamicValues("CalendarGetTables")]** - Calendar ID
- `incomingDays` - Days ahead to monitor (optional)

**Returns:** `IOutputWorkflowTrigger<CalendarEventListWithActionType>`

**Usage:**
```csharp
var trigger = WorkflowActions.ManagedTriggers
    .Outlook("outlook-connection")
    .WhenCalendarGetOnNewItemsV2(
        table: () => "calendar-id",
        incomingDays: () => 7
    );
```

**API:** Poll-based trigger checking for new events

---

### WhenCalendarGetOnChangedItemsV2

Trigger when calendar events change.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "outlook")]
public static IOutputWorkflowTrigger<CalendarEventListWithActionType> WhenCalendarGetOnChangedItemsV2(
    [ConnectionName] string connectionId,
    [DynamicValues("CalendarGetTables")] Expression<Func<string>> table,
    Expression<Func<int>> incomingDays = null,
    Expression<Func<int>> pastDays = null)
```

**Parameters:**
- `table` **[DynamicValues("CalendarGetTables")]** - Calendar ID
- `incomingDays` - Days ahead to monitor (optional)
- `pastDays` - Days in past to monitor (optional)

**Returns:** `IOutputWorkflowTrigger<CalendarEventListWithActionType>`

**Usage:**
```csharp
var trigger = WorkflowActions.ManagedTriggers
    .Outlook("outlook-connection")
    .WhenCalendarGetOnChangedItemsV2(
        table: () => "calendar-id",
        incomingDays: () => 7,
        pastDays: () => 1
    );
```

**API:** Poll-based trigger checking for changed events

---

## Complete Usage Example

```csharp
using Microsoft.Azure.Workflows.Sdk.Agents;
using Microsoft.Azure.Workflows.Sdk.Agents.Connectors.Outlook;

var outlook = WorkflowActions.ManagedConnectors
    .Outlook("outlook-connection");

// Send email
outlook.SendMailV2(
    item: () => new ClientSendMessage {
        To = "recipient@example.com",
        Subject = "Automated Email",
        Body = "<p>Email content</p>",
        Importance = "High"
    }
);

// Get unread emails
var emails = outlook.GetEmailsV2(
    folderPath: () => "Inbox",
    fetchOnlyUnread: () => true,
    top: () => 10
);

// Create calendar event
var meeting = outlook.CalendarPostEventV2(
    table: () => "calendar-id",
    item: () => new ClientSendCalendarEvent {
        Subject = "Team Sync",
        Start = DateTime.Now.AddDays(1),
        End = DateTime.Now.AddDays(1).AddHours(1)
    }
);

// Monitor new emails
var trigger = WorkflowActions.ManagedTriggers
    .Outlook("outlook-connection")
    .WhenOnNewEmailV2(
        folderPath: () => "Inbox",
        importance: () => OnNewEmailV2importanceInput.High
    );
```

---

## Summary

### Outlook Connector Provides:
- ✅ **26 Actions** - Email, calendar, contacts, folders
- ✅ **7 Triggers** - New emails, calendar changes, flags
- ✅ **Rich Filtering** - OData queries, importance, attachments
- ✅ **Attachments** - Send and receive file attachments
- ✅ **Calendar Management** - CRUD operations, RSVP
- ✅ **Contact Management** - Full CRUD operations

### Common Use Cases:
- **Email Automation** - Auto-reply, forwarding, filing
- **Calendar Sync** - Meeting coordination, reminders
- **Contact Management** - CRM synchronization
- **Email Processing** - Sentiment analysis, categorization
- **Notifications** - Alert on important emails
- **Out-of-Office** - Automatic replies

### Change Summary (v1.141.0.9 → v1.141.0.10):
- **Total Methods:** 7 → 33 (+26 methods, +371%)
- **Actions:** 4 → 26 (+22 new actions)
- **Triggers:** 3 → 7 (+4 new triggers)
- **Breaking Changes:** ⚠️ Input enums

### DynamicValues Operations:
- `CalendarGetTables` - Lists calendars
- `ContactGetFolders` - Lists contact folders

---

**Version:** 1.141.0.10  
**Status:** ✅ Production Ready  
**Breaking Changes:** ⚠️ YES - Input enums  
**Last Updated:** November 6, 2025

---

_For complete SDK documentation, see [README.md](README.md). For AgentBuilder integration, see [AGENT_BUILDER_GUIDE.md](AGENT_BUILDER_GUIDE.md)._
