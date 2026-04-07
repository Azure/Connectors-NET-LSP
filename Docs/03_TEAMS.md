# Microsoft Teams Connector - Complete Reference

**Namespace:** `Microsoft.Azure.Workflows.Sdk.Agents.Connectors.Teams`  
**Connector Name:** `teams`  
**Type:** API Management  
**SDK Version:** 1.141.0.10  
**Breaking Changes:** ⚠️ YES - Input enums for several methods

---

## 📋 Table of Contents

### Quick Links
- [Overview](#overview)
- [Actions](#actions)
- [Triggers](#triggers)
- [Input Types](#input-types)
- [Complete Usage Example](#complete-usage-example)
- [Summary](#summary)

### Team Management (Actions)
- [CreateATeam](#createateam) - Create a new team
- [GetAllTeams](#getallteams) - List all teams
- [GetTeam](#getteam) - Get team details
- [AddMemberToTeam](#addmembertoteam) - Add member to team

### Channel Operations (Actions)
- [GetChannelsForGroup](#getchannelsforgroup) - List channels in team
- [CreateChannel](#createchannel) - Create new channel
- [GetMessagesFromChannel](#getmessagesfromchannel) - Get channel messages
- [PostMessageToConversation](#postmessagetoconversation) - Post message
- [ReplyWithMessageToConversation](#replywithmessagetoconversation) - Reply to message
- [PostCardToConversation](#postcardtoconversation) - Post adaptive card
- [PostCardAndWaitForResponse](#postcardandwaitforresponse) - Interactive card
- [ReplyWithCardToConversation](#replywithcardtoconversation) - Reply with card
- [UpdateCardInConversation](#updatecardinconversation) - Update existing card

### Chat Operations (Actions)
- [GetChats](#getchats) - List chats
- [CreateChat](#createchat) - Create new chat
- [GetMessageDetails](#getmessagedetails) - Get message info
- [ListMembers](#listmembers) - List conversation members

### Tag Management (Actions)
- [GetTags](#gettags) - List team tags
- [CreateTag](#createtag) - Create new tag
- [AddMemberToTag](#addmembertotag) - Add member to tag
- [GetTagMembers](#gettagmembers) - List tag members
- [DeleteTag](#deletetag) - Delete a tag
- [DeleteTagMember](#deletetagmember) - Remove member from tag
- [AtMentionTag](#atmentiontas) - Get @mention for tag

### Meeting Operations (Actions)
- [CreateTeamsMeeting](#createteamsmeeting) - Schedule meeting

### Mention & Notification (Actions)
- [AtMentionUser](#atmentionuser) - Get @mention for user
- [PostFeedNotification](#postfeednotification) - Send feed notification
- [SubscribeUserMessageWithOptions](#subscribusermessagewithoptions) - Subscribe to messages

### Advanced (Actions)
- [HttpRequest](#httprequest) - Make custom Graph API call

### Message Webhooks (Triggers)
- [WhenWebhookNewMessageTrigger](#whenwebhooknewmessagetrigger) - New message in channel/chat
- [WhenWebhookChatMessageTrigger](#whenwebhookchatmessagetrigger) - New chat message
- [WhenWebhookAtMentionTrigger](#whenwebhookatmentiontrigger) - When @mentioned
- [WhenWebhookKeywordTrigger](#whenwebhookkeywordtrigger) - Keyword detection

### Membership Webhooks (Triggers)
- [WhenOnGroupMembershipAdd](#whenongroupershipadd) - Member added to team
- [WhenOnGroupMembershipRemoval](#whenongroupmembershipremoval) - Member removed from team

---

## Overview

The Microsoft Teams connector provides comprehensive integration with Teams, including team/channel management, messaging, meetings, tags, and real-time webhooks.

**Access via:**
```csharp
using Microsoft.Azure.Workflows.Sdk.Agents;
using Microsoft.Azure.Workflows.Sdk.Agents.Connectors.Teams;

var connector = WorkflowActions.ManagedConnectors.Teams("teams-connection");
```

**Total Methods:** 35 (29 Actions, 6 Triggers)  
**Change from v1.141.0.9:** +29 methods (+500%+ expansion)  
**Breaking Changes:** ⚠️ Input enums for chat type, thread type, notification type, and more

---

## Input Types

### GetChatschatTypeInput
```csharp
public enum GetChatschatTypeInput
{
    [EnumMember(Value = "oneOnOne")]
    OneOnOne,
    [EnumMember(Value = "group")]
    Group
}
```

### GetChatstopicInput
```csharp
public enum GetChatstopicInput
{
    [EnumMember(Value = "all")]
    All,
    [EnumMember(Value = "topic")]
    Topic
}
```

### CreateChannelbodyInput / CreateTagbodyInput / AddMemberToTagbodyInput
Complex input classes for creating channels, tags, and tag memberships.

### PostFeedNotificationposterInput
```csharp
public enum PostFeedNotificationposterInput
{
    [EnumMember(Value = "Flowbot")]
    Flowbot,
    [EnumMember(Value = "User")]
    User
}
```

### PostFeedNotificationnotificationTypeInput
```csharp
public enum PostFeedNotificationnotificationTypeInput
{
    [EnumMember(Value = "feedMessaging")]
    FeedMessaging
}
```

### GetMessageDetailsthreadTypeInput / ListMembersthreadTypeInput
```csharp
public enum GetMessageDetailsthreadTypeInput
{
    [EnumMember(Value = "channel")]
    Channel,
    [EnumMember(Value = "chat")]
    Chat
}
```

### WebhookAtMentionTriggerthreadTypeInput / WebhookKeywordTriggerthreadTypeInput / WebhookNewMessageTriggerthreadTypeInput
```csharp
public enum WebhookAtMentionTriggerthreadTypeInput
{
    [EnumMember(Value = "channel")]
    Channel,
    [EnumMember(Value = "chat")]
    Chat,
    [EnumMember(Value = "all")]
    All
}
```

### CreateTeamsMeetingcalendaridInput / ReplyWithMessageToConversationposterInput / ReplyWithCardToConversationposterInput
Complex input enums for meetings and replies.

---

## Actions

### GetAllTeams

List all teams the user is a member of.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "teams")]
public static IOutputWorkflowAction<GetAllTeamsResponse> GetAllTeams(
    [ConnectionName] string connectionId)
```

**Returns:** `IOutputWorkflowAction<GetAllTeamsResponse>` - List of teams

**Usage:**
```csharp
var teams = connector.GetAllTeams();
```

**API:** `GET /beta/me/joinedTeams`

---

### GetChannelsForGroup

Get all channels in a team.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "teams")]
public static IOutputWorkflowAction<GetChannelsForGroupResponse> GetChannelsForGroup(
    [ConnectionName] string connectionId,
    [DynamicValues("GetAllTeams")] Expression<Func<string>> groupId)
```

**Parameters:**
- `groupId` **[DynamicValues("GetAllTeams")]** - Team ID

**Returns:** `IOutputWorkflowAction<GetChannelsForGroupResponse>`

**Usage:**
```csharp
var channels = connector.GetChannelsForGroup(
    groupId: () => "team-id-123"
);
```

**API:** `GET /beta/groups/{groupId}/channels`

---

### CreateChannel

Create a new channel in a team.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "teams")]
public static IOutputWorkflowAction<CreateChannelResponse> CreateChannel(
    [ConnectionName] string connectionId,
    [DynamicValues("GetAllTeams")] Expression<Func<string>> groupId,
    Expression<Func<CreateChannelbodyInput>> body)
```

**Parameters:**
- `groupId` **[DynamicValues("GetAllTeams")]** - Team ID
- `body` - Channel details (name, description, type)

**Returns:** `IOutputWorkflowAction<CreateChannelResponse>`

**Usage:**
```csharp
var channel = connector.CreateChannel(
    groupId: () => "team-id",
    body: () => new CreateChannelbodyInput {
        displayName = "New Channel",
        description = "Channel description"
    }
);
```

**API:** `POST /beta/groups/{groupId}/channels`

---

### GetChats

Get user's chat conversations.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "teams")]
public static IOutputWorkflowAction<GetChatsResponse> GetChats(
    [ConnectionName] string connectionId,
    Expression<Func<GetChatschatTypeInput>> chatType,
    Expression<Func<GetChatstopicInput>> topic)
```

**Parameters:**
- `chatType` - OneOnOne or Group
- `topic` - All or Topic

**Returns:** `IOutputWorkflowAction<GetChatsResponse>`

**Usage:**
```csharp
var chats = connector.GetChats(
    chatType: () => GetChatschatTypeInput.Group,
    topic: () => GetChatstopicInput.All
);
```

**API:** `GET /flowbot/actions/listchats/chattypes/{chatType}/topic/{topic}/expandmembers/false`

---

### CreateTeamsMeeting

Schedule a Teams meeting.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "teams")]
public static IOutputWorkflowAction<NewMeetingRespone> CreateTeamsMeeting(
    [ConnectionName] string connectionId,
    Expression<Func<CreateTeamsMeetingcalendaridInput>> calendarid,
    Expression<Func<NewMeeting>> item)
```

**Parameters:**
- `calendarid` - Calendar ID
- `item` - Meeting details (subject, start, end, attendees)

**Returns:** `IOutputWorkflowAction<NewMeetingRespone>`

**Usage:**
```csharp
var meeting = connector.CreateTeamsMeeting(
    calendarid: () => CreateTeamsMeetingcalendaridInput.Default,
    item: () => new NewMeeting {
        subject = "Team Sync",
        start = new DateTime(2025, 11, 6, 10, 0, 0),
        end = new DateTime(2025, 11, 6, 11, 0, 0)
    }
);
```

**API:** `POST /v1.0/me/calendars/{calendarId}/events`

---

### GetTags

List all tags in a team.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "teams")]
public static IOutputWorkflowAction<GetTagsResponseSchema> GetTags(
    [ConnectionName] string connectionId,
    [DynamicValues("GetAllTeams")] Expression<Func<string>> groupId)
```

**Parameters:**
- `groupId` **[DynamicValues("GetAllTeams")]** - Team ID

**Returns:** `IOutputWorkflowAction<GetTagsResponseSchema>`

**Usage:**
```csharp
var tags = connector.GetTags(
    groupId: () => "team-id"
);
```

**API:** `GET /beta/teams/{groupId}/tags`

---

### CreateTag

Create a new tag in a team.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "teams")]
public static IOutputWorkflowAction<CreateTagResponseSchema> CreateTag(
    [ConnectionName] string connectionId,
    [DynamicValues("GetAllTeams")] Expression<Func<string>> groupId,
    Expression<Func<CreateTagbodyInput>> body)
```

**Parameters:**
- `groupId` **[DynamicValues("GetAllTeams")]** - Team ID
- `body` - Tag details (name, description)

**Returns:** `IOutputWorkflowAction<CreateTagResponseSchema>`

**Usage:**
```csharp
var tag = connector.CreateTag(
    groupId: () => "team-id",
    body: () => new CreateTagbodyInput {
        displayName = "Marketing Team"
    }
);
```

**API:** `POST /beta/teams/{groupId}/tags`

---

### PostMessageToConversation

Post a message to a channel or chat.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "teams")]
public static IOutputWorkflowAction<PostToConversationResponse> PostMessageToConversation(
    [ConnectionName] string connectionId,
    [DynamicValues("GetAllTeams")] Expression<Func<string>> teamId,
    [DynamicValues("GetChannelsForGroup")] Expression<Func<string>> channelId,
    Expression<Func<JToken>> body)
```

**Parameters:**
- `teamId` **[DynamicValues("GetAllTeams")]** - Team ID
- `channelId` **[DynamicValues("GetChannelsForGroup")]** - Channel ID
- `body` - Message content

**Returns:** `IOutputWorkflowAction<PostToConversationResponse>`

**Usage:**
```csharp
var message = connector.PostMessageToConversation(
    teamId: () => "team-id",
    channelId: () => "channel-id",
    body: () => new {
        body = new {
            content = "Hello, Team!",
            contentType = "text"
        }
    }
);
```

**API:** `POST /beta/teams/{teamId}/channels/{channelId}/messages`

---

### PostCardToConversation

Post an Adaptive Card to a channel.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "teams")]
public static IOutputWorkflowAction<PostToConversationResponse> PostCardToConversation(
    [ConnectionName] string connectionId,
    [DynamicValues("GetAllTeams")] Expression<Func<string>> teamId,
    [DynamicValues("GetChannelsForGroup")] Expression<Func<string>> channelId,
    Expression<Func<JToken>> body)
```

**Parameters:**
- `teamId` **[DynamicValues("GetAllTeams")]** - Team ID
- `channelId` **[DynamicValues("GetChannelsForGroup")]** - Channel ID
- `body` - Adaptive Card JSON

**Returns:** `IOutputWorkflowAction<PostToConversationResponse>`

**Usage:**
```csharp
var card = connector.PostCardToConversation(
    teamId: () => "team-id",
    channelId: () => "channel-id",
    body: () => adaptiveCardJson
);
```

**API:** `POST /beta/teams/{teamId}/channels/{channelId}/messages`

---

### GetMessagesFromChannel

Retrieve messages from a channel.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "teams")]
public static IOutputWorkflowAction<GetMessagesFromChannelResponse> GetMessagesFromChannel(
    [ConnectionName] string connectionId,
    [DynamicValues("GetAllTeams")] Expression<Func<string>> groupId,
    [DynamicValues("GetChannelsForGroup")] Expression<Func<string>> channelId)
```

**Parameters:**
- `groupId` **[DynamicValues("GetAllTeams")]** - Team ID
- `channelId` **[DynamicValues("GetChannelsForGroup")]** - Channel ID

**Returns:** `IOutputWorkflowAction<GetMessagesFromChannelResponse>`

**Usage:**
```csharp
var messages = connector.GetMessagesFromChannel(
    groupId: () => "team-id",
    channelId: () => "channel-id"
);
```

**API:** `GET /beta/teams/{groupId}/channels/{channelId}/messages`

---

## Triggers

### WhenWebhookNewMessageTrigger

Trigger when a new message is posted.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "teams")]
public static IOutputWorkflowTrigger<JToken> WhenWebhookNewMessageTrigger(
    [ConnectionName] string connectionId,
    Expression<Func<WebhookNewMessageTriggerthreadTypeInput>> threadType,
    Expression<Func<JToken>> requestBody)
```

**Parameters:**
- `threadType` - Channel, Chat, or All
- `requestBody` - Webhook configuration

**Returns:** `IOutputWorkflowTrigger<JToken>`

**Usage:**
```csharp
var trigger = WorkflowActions.ManagedTriggers
    .Teams("teams-connection")
    .WhenWebhookNewMessageTrigger(
        threadType: () => WebhookNewMessageTriggerthreadTypeInput.Channel,
        requestBody: () => webhookConfig
    );
```

---

### WhenWebhookAtMentionTrigger

Trigger when user is @mentioned.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "teams")]
public static IOutputWorkflowTrigger<JToken> WhenWebhookAtMentionTrigger(
    [ConnectionName] string connectionId,
    Expression<Func<WebhookAtMentionTriggerthreadTypeInput>> threadType,
    Expression<Func<JToken>> requestBody)
```

**Parameters:**
- `threadType` - Channel, Chat, or All
- `requestBody` - Webhook configuration

**Returns:** `IOutputWorkflowTrigger<JToken>`

**Usage:**
```csharp
var trigger = WorkflowActions.ManagedTriggers
    .Teams("teams-connection")
    .WhenWebhookAtMentionTrigger(
        threadType: () => WebhookAtMentionTriggerthreadTypeInput.All,
        requestBody: () => webhookConfig
    );
```

---

### WhenOnGroupMembershipAdd

Trigger when a member is added to a team.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "teams")]
public static IOutputWorkflowTrigger<JToken> WhenOnGroupMembershipAdd(
    [ConnectionName] string connectionId,
    [DynamicValues("GetAllTeams")] Expression<Func<string>> groupId)
```

**Parameters:**
- `groupId` **[DynamicValues("GetAllTeams")]** - Team ID to monitor

**Returns:** `IOutputWorkflowTrigger<JToken>`

**Usage:**
```csharp
var trigger = WorkflowActions.ManagedTriggers
    .Teams("teams-connection")
    .WhenOnGroupMembershipAdd(
        groupId: () => "team-id"
    );
```

---

### WhenOnGroupMembershipRemoval

Trigger when a member is removed from a team.

**Signature:**
```csharp
[ConnectorOperation(Type = ConnectorType.ApiManagement, ConnectorName = "teams")]
public static IOutputWorkflowTrigger<JToken> WhenOnGroupMembershipRemoval(
    [ConnectionName] string connectionId,
    [DynamicValues("GetAllTeams")] Expression<Func<string>> groupId)
```

**Parameters:**
- `groupId` **[DynamicValues("GetAllTeams")]** - Team ID to monitor

**Returns:** `IOutputWorkflowTrigger<JToken>`

**Usage:**
```csharp
var trigger = WorkflowActions.ManagedTriggers
    .Teams("teams-connection")
    .WhenOnGroupMembershipRemoval(
        groupId: () => "team-id"
    );
```

---

## Complete Usage Example

```csharp
using Microsoft.Azure.Workflows.Sdk.Agents;
using Microsoft.Azure.Workflows.Sdk.Agents.Connectors.Teams;

// Create Teams connector
var teams = WorkflowActions.ManagedConnectors
    .Teams("teams-connection");

// Get all teams
var myTeams = teams.GetAllTeams();

// Get channels
var channels = teams.GetChannelsForGroup(
    groupId: () => "team-id"
);

// Post message
var message = teams.PostMessageToConversation(
    teamId: () => "team-id",
    channelId: () => "channel-id",
    body: () => new {
        body = new {
            content = "Hello, Team!",
            contentType = "text"
        }
    }
);

// Create meeting
var meeting = teams.CreateTeamsMeeting(
    calendarid: () => CreateTeamsMeetingcalendaridInput.Default,
    item: () => new NewMeeting {
        subject = "Weekly Sync",
        start = DateTime.Now.AddDays(1),
        end = DateTime.Now.AddDays(1).AddHours(1)
    }
);

// Set up webhook
var trigger = WorkflowActions.ManagedTriggers
    .Teams("teams-connection")
    .WhenWebhookNewMessageTrigger(
        threadType: () => WebhookNewMessageTriggerthreadTypeInput.Channel,
        requestBody: () => webhookConfig
    );
```

---

## Summary

### Teams Connector Provides:
- ✅ **29 Actions** - Teams, channels, messages, meetings, tags
- ✅ **6 Triggers** - Messages, mentions, membership changes
- ✅ **Adaptive Cards** - Rich interactive messages
- ✅ **Tag Management** - Organize team members
- ✅ **Meeting Scheduling** - Create Teams meetings
- ✅ **Real-time Webhooks** - Instant notifications

### Common Use Cases:
- **Team Collaboration** - Automate team/channel creation
- **Notifications** - Send alerts to channels
- **Meeting Automation** - Schedule recurring meetings
- **Member Management** - Track membership changes
- **Interactive Workflows** - Adaptive Card responses
- **Chat Bots** - Automated chat responses

### Change Summary (v1.141.0.9 → v1.141.0.10):
- **Total Methods:** 6 → 35 (+29 methods, +483%)
- **Actions:** 4 → 29 (+25 new actions)
- **Triggers:** 2 → 6 (+4 new triggers)
- **Breaking Changes:** ⚠️ Input enums for multiple parameters

### DynamicValues Operations:
- `GetAllTeams` - Lists user's teams
- `GetChannelsForGroup` - Lists team channels
- `GetTags` - Lists team tags

---

**Version:** 1.141.0.10  
**Status:** ✅ Production Ready  
**Breaking Changes:** ⚠️ YES - Input parameter enums  
**Last Updated:** November 6, 2025

---

_For complete SDK documentation, see [README.md](README.md). For AgentBuilder integration, see [AGENT_BUILDER_GUIDE.md](AGENT_BUILDER_GUIDE.md)._
