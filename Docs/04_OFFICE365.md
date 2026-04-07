# Office 365 Connector - Complete Reference

**Namespace:** `Microsoft.Azure.Workflows.Sdk.Agents.Connectors.Office365`  
**Connector Name:** `office365`  
**Type:** API Management  
**SDK Version:** 1.141.0.10  
**Breaking Changes:** ⚠️ YES - Input enums throughout

---

## 📋 Table of Contents

### Quick Links
- [Overview](#overview)
- [Actions](#actions)
- [Triggers](#triggers)
- [Input Types](#input-types)
- [Summary](#summary)

### Email Operations (26 Actions)
- SendMailV2, SendMailV3, SendMailWithOptions
- GetEmailsV2, GetEmailsV3, GetEmail, GetEmailsV4
- ReplyToV2, ReplyToV3, ForwardEmailV2, ForwardEmailV3
- DeleteEmailV2, MoveEmailV2, MoveEmailV3
- MarkAsReadOrUnreadV3, FlagEmailV2, FlagEmailV3
- SetUpAutomaticRepliesV2, ExportEmailV2
- GetAttachment, GetAttachmentV2

### Calendar Operations (20 Actions)
- CalendarGetEvents, CalendarGetEventsV2, CalendarGetEventsV3, CalendarGetEventsV4
- CalendarGetEventV2, CalendarGetEventV3, CalendarGetEventV4
- CalendarPostEventV2, CalendarPostEventV3, CalendarPostEventV4
- CalendarUpdateEventV2, CalendarUpdateEventV3, CalendarUpdateEventV4
- CalendarDeleteEventV2, CalendarDeleteEventV3, CalendarDeleteEventV4
- CalendarRespondToEvent, CalendarRespondToEventV2, CalendarRespondToEventV3
- FindMeetingTimes, FindMeetingTimesV2

### Contact Operations (10 Actions)
- ContactsGetContacts, ContactsGetContactsV2, ContactsGetContactsV3
- ContactsGetContactV2, ContactsGetContactV3
- ContactsPostContactV2, ContactsPostContactV3
- ContactsUpdateContactV2, ContactsUpdateContactV3
- ContactsDeleteContactV2, ContactsDeleteContactV3

### Folder & Management (6 Actions)
- GetFolders, GetFoldersV2
- CreateFolder, CreateFolderV2
- GetMailTips
- GetMyProfile

### Email Triggers (9 Triggers)
- WhenOnNewEmailV2, WhenOnNewEmailV3, WhenOnNewEmailV4
- WhenOnFlaggedEmailV2, WhenOnFlaggedEmailV3
- WhenOnNewMentionMeEmailV2, WhenOnNewMentionMeEmailV3
- WhenOnEmailArrives (legacy), WhenOnEmailArrivesV2

### Calendar Triggers (9 Triggers)
- WhenCalendarGetOnNewItemsV2, WhenCalendarGetOnNewItemsV3
- WhenCalendarGetOnUpdatedItemsV2, WhenCalendarGetOnUpdatedItemsV3
- WhenCalendarGetOnChangedItemsV2, WhenCalendarGetOnChangedItemsV3

---

## Overview

The Office 365 connector is the most comprehensive connector, providing extensive email, calendar, and contact operations through Microsoft Graph API with multiple API versions for backward compatibility.

**Access via:**
```csharp
using Microsoft.Azure.Workflows.Sdk.Agents;
using Microsoft.Azure.Workflows.Sdk.Agents.Connectors.Office365;

var connector = WorkflowActions.ManagedConnectors.Office365("office365-connection");
```

**Total Methods:** 91 (73 Actions, 18 Triggers)  
**Change from v1.141.0.9:** +41 methods (+82%)  
**Breaking Changes:** ⚠️ Input enums extensively used  
**API Versions:** V2, V3, V4 (progressive enhancements)

---

## Input Types

### Importance Enums
```csharp
public enum GetEmailsV2importanceInput
{
    Low, Normal, High
}
```

### Sensitivity Enums
```csharp
public enum SendMailV2sensitivityInput
{
    Normal, Personal, Private, Confidential
}
```

### Calendar Response Enums
```csharp
public enum CalendarRespondToEventresponseInput
{
    Accept, Tentative, Decline
}
```

### Show As Enums
```csharp
public enum CalendarPostEventV2showAsInput
{
    Free, Tentative, Busy, Oof, WorkingElsewhere, Unknown
}
```

### Read Status Enums
```csharp
public enum MarkAsReadOrUnreadV3isReadInput
{
    [EnumMember(Value = "true")] True,
    [EnumMember(Value = "false")] False
}
```

### Flag Status Enums
```csharp
public enum FlagEmailV2flagStatusInput
{
    NotFlagged, Complete, Flagged
}
```

---

## Actions - Email Operations

### SendMailV3

Send an email (latest version with enhanced features).

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "office365")]
public static IWorkflowAction SendMailV3(
    [ConnectionName] string connectionId,
    Expression<Func<ClientSendMessage>> item)
```

**Parameters:**
- `item` - Email details (to, subject, body, cc, bcc, attachments, importance, sensitivity)

**Usage:**
```csharp
connector.SendMailV3(
    item: () => new ClientSendMessage {
        To = "recipient@example.com",
        Subject = "Important Update",
        Body = "<p>HTML body content</p>",
        Importance = "High",
        Sensitivity = "Normal"
    }
);
```

**API:** `POST /v1.0/me/sendMail`

---

### GetEmailsV4

Get emails with advanced filtering (latest version).

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "office365")]
public static IOutputWorkflowAction<BatchResponseClientReceiveMessage> GetEmailsV4(
    [ConnectionName] string connectionId,
    Expression<Func<string>> folderPath = null,
    Expression<Func<string>> to = null,
    Expression<Func<string>> cc = null,
    Expression<Func<string>> from = null,
    Expression<Func<GetEmailsV4importanceInput>> importance = null,
    Expression<Func<GetEmailsV4sensitivityInput>> sensitivity = null,
    Expression<Func<bool>> fetchOnlyWithAttachment = null,
    Expression<Func<string>> subjectFilter = null,
    Expression<Func<bool>> fetchOnlyUnread = null,
    Expression<Func<bool>> fetchOnlyFlagged = null,
    Expression<Func<bool>> includeAttachments = null,
    Expression<Func<string>> searchQuery = null,
    Expression<Func<int>> top = null,
    Expression<Func<int>> skip = null)
```

**Parameters:** Comprehensive filtering options including:
- Folder, recipients, sender, importance, sensitivity
- Attachment, read, and flag filters
- Search query, pagination

**Usage:**
```csharp
var emails = connector.GetEmailsV4(
    folderPath: () => "Inbox",
    from: () => "boss@example.com",
    importance: () => GetEmailsV4importanceInput.High,
    fetchOnlyUnread: () => true,
    top: () => 50
);
```

**API:** `GET /v1.0/me/messages`

---

### ReplyToV3

Reply to an email (latest version).

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "office365")]
public static IWorkflowAction ReplyToV3(
    [ConnectionName] string connectionId,
    Expression<Func<string>> messageId,
    Expression<Func<ClientSendMessage>> item)
```

**Parameters:**
- `messageId` - ID of email to reply to
- `item` - Reply content (body, comment)

**Usage:**
```csharp
connector.ReplyToV3(
    messageId: () => "AAMkAD...",
    item: () => new ClientSendMessage {
        Body = "Thank you for the update.",
        Comment = "Adding additional context..."
    }
);
```

**API:** `POST /v1.0/me/messages/{messageId}/reply`

---

### ForwardEmailV3

Forward an email (latest version).

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "office365")]
public static IWorkflowAction ForwardEmailV3(
    [ConnectionName] string connectionId,
    Expression<Func<string>> messageId,
    Expression<Func<ClientSendMessage>> item)
```

**Parameters:**
- `messageId` - ID of email to forward
- `item` - Forward details (to, comment)

**Usage:**
```csharp
connector.ForwardEmailV3(
    messageId: () => "AAMkAD...",
    item: () => new ClientSendMessage {
        To = "team@example.com",
        Comment = "FYI - please review"
    }
);
```

**API:** `POST /v1.0/me/messages/{messageId}/forward`

---

### MarkAsReadOrUnreadV3

Mark an email as read or unread.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "office365")]
public static IWorkflowAction MarkAsReadOrUnreadV3(
    [ConnectionName] string connectionId,
    Expression<Func<string>> messageId,
    Expression<Func<MarkAsReadOrUnreadV3isReadInput>> isRead)
```

**Parameters:**
- `messageId` - Email ID
- `isRead` - True or False

**Usage:**
```csharp
connector.MarkAsReadOrUnreadV3(
    messageId: () => "AAMkAD...",
    isRead: () => MarkAsReadOrUnreadV3isReadInput.True
);
```

**API:** `PATCH /v1.0/me/messages/{messageId}`

---

### FlagEmailV3

Flag an email for follow-up.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "office365")]
public static IWorkflowAction FlagEmailV3(
    [ConnectionName] string connectionId,
    Expression<Func<string>> messageId,
    Expression<Func<FlagEmailV3flagStatusInput>> flagStatus,
    Expression<Func<DateTime>> startDateTime = null,
    Expression<Func<DateTime>> dueDateTime = null)
```

**Parameters:**
- `messageId` - Email ID
- `flagStatus` - NotFlagged, Complete, or Flagged
- `startDateTime` - Flag start date (optional)
- `dueDateTime` - Flag due date (optional)

**Usage:**
```csharp
connector.FlagEmailV3(
    messageId: () => "AAMkAD...",
    flagStatus: () => FlagEmailV3flagStatusInput.Flagged,
    dueDateTime: () => DateTime.Now.AddDays(3)
);
```

**API:** `PATCH /v1.0/me/messages/{messageId}`

---

## Actions - Calendar Operations

### CalendarPostEventV4

Create a calendar event (latest version).

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "office365")]
public static IOutputWorkflowAction<ClientReceiveCalendarEvent> CalendarPostEventV4(
    [ConnectionName] string connectionId,
    [DynamicValues("CalendarGetTables")] Expression<Func<string>> table,
    Expression<Func<ClientSendCalendarEvent>> item)
```

**Parameters:**
- `table` **[DynamicValues("CalendarGetTables")]** - Calendar ID
- `item` - Event details (subject, start, end, location, attendees, body, importance, sensitivity, show as, is all day, recurrence, reminder)

**Usage:**
```csharp
var event = connector.CalendarPostEventV4(
    table: () => "AAMkAD...",
    item: () => new ClientSendCalendarEvent {
        Subject = "Project Review",
        Start = new DateTime(2025, 11, 7, 14, 0, 0),
        End = new DateTime(2025, 11, 7, 15, 0, 0),
        Location = "Conference Room A",
        RequiredAttendees = "team@example.com",
        Body = "Quarterly project review meeting",
        ShowAs = "Busy",
        Importance = "High"
    }
);
```

**API:** `POST /v1.0/me/calendars/{table}/events`

---

### CalendarGetEventsV4

Get calendar events with filtering (latest version).

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "office365")]
public static IOutputWorkflowAction<CalendarEventListClientReceive> CalendarGetEventsV4(
    [ConnectionName] string connectionId,
    [DynamicValues("CalendarGetTables")] Expression<Func<string>> table = null,
    Expression<Func<int>> top = null,
    Expression<Func<int>> skip = null,
    Expression<Func<string>> filter = null,
    Expression<Func<string>> orderBy = null)
```

**Parameters:**
- `table` **[DynamicValues("CalendarGetTables")]** - Calendar ID (optional)
- `top` - Maximum events to return
- `skip` - Number of events to skip (pagination)
- `filter` - OData filter query
- `orderBy` - Sort order

**Usage:**
```csharp
var events = connector.CalendarGetEventsV4(
    table: () => "calendar-id",
    filter: () => "start/dateTime ge '2025-11-06T00:00:00'",
    orderBy: () => "start/dateTime asc",
    top: () => 50
);
```

**API:** `GET /v1.0/me/calendars/{table}/events`

---

### CalendarRespondToEventV3

Respond to a calendar event invitation (latest version).

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "office365")]
public static IWorkflowAction CalendarRespondToEventV3(
    [ConnectionName] string connectionId,
    [DynamicValues("CalendarGetTables")] Expression<Func<string>> table,
    Expression<Func<string>> eventId,
    Expression<Func<CalendarRespondToEventV3responseInput>> response,
    Expression<Func<string>> comment = null)
```

**Parameters:**
- `table` **[DynamicValues("CalendarGetTables")]** - Calendar ID
- `eventId` - Event ID to respond to
- `response` - Accept, Tentative, or Decline
- `comment` - Optional comment

**Usage:**
```csharp
connector.CalendarRespondToEventV3(
    table: () => "calendar-id",
    eventId: () => "event-id",
    response: () => CalendarRespondToEventV3responseInput.Accept,
    comment: () => "Looking forward to it!"
);
```

**API:** `POST /v1.0/me/calendars/{table}/events/{eventId}/accept|tentativelyAccept|decline`

---

### FindMeetingTimesV2

Find available meeting times for attendees.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "office365")]
public static IOutputWorkflowAction<FindMeetingTimesResponse> FindMeetingTimesV2(
    [ConnectionName] string connectionId,
    Expression<Func<FindMeetingTimesRequest>> item)
```

**Parameters:**
- `item` - Meeting requirements (attendees, time constraints, duration, location preference)

**Usage:**
```csharp
var suggestions = connector.FindMeetingTimesV2(
    item: () => new FindMeetingTimesRequest {
        Attendees = new[] { "person1@example.com", "person2@example.com" },
        TimeConstraint = new TimeConstraint {
            Start = DateTime.Now.AddDays(1),
            End = DateTime.Now.AddDays(7)
        },
        MeetingDuration = "PT1H", // 1 hour
        MinimumAttendeePercentage = 100
    }
);
```

**API:** `POST /v1.0/me/findMeetingTimes`

---

## Actions - Contact Operations

### ContactsPostContactV3

Create a new contact (latest version).

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "office365")]
public static IOutputWorkflowAction<ClientReceiveContact> ContactsPostContactV3(
    [ConnectionName] string connectionId,
    [DynamicValues("ContactGetFolders")] Expression<Func<string>> table,
    Expression<Func<ClientSendContact>> item)
```

**Parameters:**
- `table` **[DynamicValues("ContactGetFolders")]** - Contact folder ID
- `item` - Contact details (name, email, phone, address, company, job title)

**Usage:**
```csharp
var contact = connector.ContactsPostContactV3(
    table: () => "default-contacts",
    item: () => new ClientSendContact {
        GivenName = "Jane",
        Surname = "Smith",
        EmailAddresses = new[] {
            new EmailAddress { Address = "jane.smith@example.com", Name = "Work" }
        },
        BusinessPhones = new[] { "+1-555-0100" },
        JobTitle = "Marketing Director",
        CompanyName = "Contoso Ltd"
    }
);
```

**API:** `POST /v1.0/me/contactFolders/{table}/contacts`

---

### ContactsGetContactsV3

List contacts with filtering (latest version).

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "office365")]
public static IOutputWorkflowAction<ContactListClientReceive> ContactsGetContactsV3(
    [ConnectionName] string connectionId,
    [DynamicValues("ContactGetFolders")] Expression<Func<string>> table = null,
    Expression<Func<int>> top = null,
    Expression<Func<int>> skip = null,
    Expression<Func<string>> filter = null,
    Expression<Func<string>> orderBy = null)
```

**Parameters:**
- `table` **[DynamicValues("ContactGetFolders")]** - Contact folder ID
- `top` - Maximum contacts
- `skip` - Pagination offset
- `filter` - OData filter
- `orderBy` - Sort order

**Usage:**
```csharp
var contacts = connector.ContactsGetContactsV3(
    table: () => "default-contacts",
    filter: () => "companyName eq 'Contoso Ltd'",
    orderBy: () => "surname asc",
    top: () => 100
);
```

**API:** `GET /v1.0/me/contactFolders/{table}/contacts`

---

## Actions - Management & Utilities

### GetMyProfile

Get the current user's profile information.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "office365")]
public static IOutputWorkflowAction<UserProfile> GetMyProfile(
    [ConnectionName] string connectionId)
```

**Returns:** `IOutputWorkflowAction<UserProfile>` - User profile data

**Usage:**
```csharp
var profile = connector.GetMyProfile();
```

**API:** `GET /v1.0/me`

---

### GetMailTips

Get mail tips for recipients (auto-replies, mailbox full, etc.).

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "office365")]
public static IOutputWorkflowAction<MailTipsResponse> GetMailTips(
    [ConnectionName] string connectionId,
    Expression<Func<MailTipsRequest>> item)
```

**Parameters:**
- `item` - Request with recipient email addresses

**Usage:**
```csharp
var mailTips = connector.GetMailTips(
    item: () => new MailTipsRequest {
        EmailAddresses = new[] { "recipient@example.com" },
        MailTipsOptions = "automaticReplies,mailboxFullStatus"
    }
);
```

**API:** `POST /v1.0/me/getMailTips`

---

### SetUpAutomaticRepliesV2

Configure out-of-office automatic replies.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "office365")]
public static IWorkflowAction SetUpAutomaticRepliesV2(
    [ConnectionName] string connectionId,
    Expression<Func<SetUpAutomaticRepliesV2statusInput>> status,
    Expression<Func<string>> internalMessage = null,
    Expression<Func<string>> externalMessage = null,
    Expression<Func<DateTime>> scheduledStartDateTime = null,
    Expression<Func<DateTime>> scheduledEndDateTime = null)
```

**Parameters:**
- `status` - Disabled, AlwaysEnabled, or Scheduled
- `internalMessage` - Message for internal senders
- `externalMessage` - Message for external senders
- `scheduledStartDateTime` - Start time (for scheduled)
- `scheduledEndDateTime` - End time (for scheduled)

**Usage:**
```csharp
connector.SetUpAutomaticRepliesV2(
    status: () => SetUpAutomaticRepliesV2statusInput.Scheduled,
    internalMessage: () => "I'm out of office until Monday.",
    externalMessage: () => "I'm currently unavailable. For urgent matters, please contact support@example.com.",
    scheduledStartDateTime: () => DateTime.Now,
    scheduledEndDateTime: () => DateTime.Now.AddDays(5)
);
```

**API:** `PATCH /v1.0/me/mailboxSettings/automaticRepliesSetting`

---

## Triggers - Email Monitoring

### WhenOnNewEmailV4

Trigger when a new email is received (latest version).

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "office365")]
public static IOutputWorkflowTrigger<TriggerBatchResponseClientReceiveMessage> WhenOnNewEmailV4(
    [ConnectionName] string connectionId,
    Expression<Func<string>> folderPath = null,
    Expression<Func<string>> to = null,
    Expression<Func<string>> cc = null,
    Expression<Func<string>> from = null,
    Expression<Func<OnNewEmailV4importanceInput>> importance = null,
    Expression<Func<OnNewEmailV4sensitivityInput>> sensitivity = null,
    Expression<Func<bool>> fetchOnlyWithAttachment = null,
    Expression<Func<bool>> fetchOnlyUnread = null,
    Expression<Func<bool>> fetchOnlyFlagged = null,
    Expression<Func<bool>> includeAttachments = null,
    Expression<Func<string>> subjectFilter = null)
```

**Parameters:** Comprehensive filtering similar to GetEmailsV4

**Usage:**
```csharp
var trigger = WorkflowActions.ManagedTriggers
    .Office365("office365-connection")
    .WhenOnNewEmailV4(
        folderPath: () => "Inbox",
        from: () => "important-sender@example.com",
        importance: () => OnNewEmailV4importanceInput.High,
        fetchOnlyWithAttachment: () => true
    );
```

**API:** Poll-based trigger checking for new emails

---

### WhenCalendarGetOnNewItemsV3

Trigger when new calendar events are created (latest version).

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "office365")]
public static IOutputWorkflowTrigger<CalendarEventListWithActionType> WhenCalendarGetOnNewItemsV3(
    [ConnectionName] string connectionId,
    [DynamicValues("CalendarGetTables")] Expression<Func<string>> table,
    Expression<Func<int>> incomingDays = null)
```

**Parameters:**
- `table` **[DynamicValues("CalendarGetTables")]** - Calendar ID to monitor
- `incomingDays` - Days ahead to check (optional)

**Usage:**
```csharp
var trigger = WorkflowActions.ManagedTriggers
    .Office365("office365-connection")
    .WhenCalendarGetOnNewItemsV3(
        table: () => "calendar-id",
        incomingDays: () => 14
    );
```

**API:** Poll-based trigger checking for new events

---

### WhenCalendarGetOnChangedItemsV3

Trigger when calendar events change (latest version).

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "office365")]
public static IOutputWorkflowTrigger<CalendarEventListWithActionType> WhenCalendarGetOnChangedItemsV3(
    [ConnectionName] string connectionId,
    [DynamicValues("CalendarGetTables")] Expression<Func<string>> table,
    Expression<Func<int>> incomingDays = null,
    Expression<Func<int>> pastDays = null)
```

**Parameters:**
- `table` **[DynamicValues("CalendarGetTables")]** - Calendar ID
- `incomingDays` - Days ahead to monitor
- `pastDays` - Days in past to monitor

**Usage:**
```csharp
var trigger = WorkflowActions.ManagedTriggers
    .Office365("office365-connection")
    .WhenCalendarGetOnChangedItemsV3(
        table: () => "calendar-id",
        incomingDays: () => 7,
        pastDays: () => 1
    );
```

**API:** Poll-based trigger checking for event changes

---

## Complete Usage Example

```csharp
using Microsoft.Azure.Workflows.Sdk.Agents;
using Microsoft.Azure.Workflows.Sdk.Agents.Connectors.Office365;

var office365 = WorkflowActions.ManagedConnectors
    .Office365("office365-connection");

// Send email with attachments
office365.SendMailV3(
    item: () => new ClientSendMessage {
        To = "team@example.com",
        Subject = "Q4 Report",
        Body = "<h1>Quarterly Report</h1><p>Please review attached.</p>",
        Importance = "High",
        Attachments = new[] {
            new Attachment {
                Name = "Q4-Report.pdf",
                ContentBytes = pdfBytes
            }
        }
    }
);

// Get high-priority unread emails
var urgentEmails = office365.GetEmailsV4(
    importance: () => GetEmailsV4importanceInput.High,
    fetchOnlyUnread: () => true,
    top: () => 20
);

// Schedule meeting
var meeting = office365.CalendarPostEventV4(
    table: () => "primary-calendar",
    item: () => new ClientSendCalendarEvent {
        Subject = "Team Standup",
        Start = DateTime.Now.AddDays(1).Date.AddHours(9),
        End = DateTime.Now.AddDays(1).Date.AddHours(9).AddMinutes(30),
        RequiredAttendees = "team@example.com",
        IsReminderOn = true,
        ReminderMinutesBeforeStart = 15
    }
);

// Create contact
var contact = office365.ContactsPostContactV3(
    table: () => "default-contacts",
    item: () => new ClientSendContact {
        GivenName = "Alex",
        Surname = "Johnson",
        EmailAddresses = new[] {
            new EmailAddress { Address = "alex.johnson@partner.com" }
        },
        CompanyName = "Partner Corp"
    }
);

// Monitor important emails
var trigger = WorkflowActions.ManagedTriggers
    .Office365("office365-connection")
    .WhenOnNewEmailV4(
        importance: () => OnNewEmailV4importanceInput.High,
        includeAttachments: () => true
    );
```

---

## Summary

### Office 365 Connector Provides:
- ✅ **73 Actions** - Most comprehensive email/calendar/contact operations
- ✅ **18 Triggers** - Extensive monitoring capabilities
- ✅ **Multiple API Versions** - V2, V3, V4 for backward compatibility
- ✅ **Advanced Filtering** - OData queries, complex filters
- ✅ **Rich Calendar Features** - Recurrence, reminders, meeting finder
- ✅ **Attachment Support** - Full attachment CRUD
- ✅ **Mail Tips** - Auto-reply detection, mailbox status

### Common Use Cases:
- **Email Automation** - Complex routing, processing, archiving
- **Calendar Management** - Meeting scheduling, conflict resolution
- **Contact Sync** - CRM integration, contact management
- **Workflow Automation** - Approval workflows, notifications
- **Meeting Coordination** - Find meeting times, send invites
- **Out-of-Office Management** - Automatic reply configuration

### Change Summary (v1.141.0.9 → v1.141.0.10):
- **Total Methods:** 50 → 91 (+41 methods, +82%)
- **Actions:** 41 → 73 (+32 new actions)
- **Triggers:** 9 → 18 (+9 new triggers)
- **Breaking Changes:** ⚠️ Extensive Input enum usage
- **API Versions:** Added V3 and V4 for many operations

### API Version Guidance:
- **V2:** Legacy, stable
- **V3:** Enhanced features, recommended for most use cases
- **V4:** Latest, most features, best performance

### DynamicValues Operations:
- `CalendarGetTables` - Lists available calendars
- `ContactGetFolders` - Lists contact folders
- `GetFolders` - Lists mail folders

---

**Version:** 1.141.0.10  
**Status:** ✅ Production Ready  
**Breaking Changes:** ⚠️ YES - Extensive Input enums  
**Last Updated:** November 6, 2025

---

_For complete SDK documentation, see [README.md](README.md). For AgentBuilder integration, see [AGENT_BUILDER_GUIDE.md](AGENT_BUILDER_GUIDE.md)._
