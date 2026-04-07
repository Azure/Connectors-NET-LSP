using System.Text.Json;

using SdkLspServer.Handlers.CodeActionHandler;

namespace SdkLspServer.Tests;

[TestClass]
public class SchemaToClassGeneratorTests
{
    [TestMethod]
    public void GenerateClass_SimpleStringProperties_ProducesCorrectOutput()
    {
        // Arrange
        string schemaJson = """
        {
            "type": "object",
            "properties": {
                "messageBody": {
                    "type": "string",
                    "description": "The body of the message."
                },
                "subject": {
                    "type": "string",
                    "description": "The subject line."
                }
            }
        }
        """;
        JsonElement schema = JsonDocument.Parse(schemaJson).RootElement;

        // Act
        string result = SchemaToClassGenerator.GenerateClass(
            className: "PostMessageInput",
            baseClassName: "DynamicPostMessageRequest",
            baseClassNamespace: "Microsoft.Azure.Connectors.DirectClient.Teams",
            schema: schema,
            targetNamespace: "MyApp.Models");

        // Assert
        Assert.IsTrue(result.Contains("public class PostMessageInput : DynamicPostMessageRequest"), message: "Should inherit from base class.");
        Assert.IsTrue(result.Contains("[JsonPropertyName(\"messageBody\")]"), message: "Should have JsonPropertyName for messageBody.");
        Assert.IsTrue(result.Contains("[JsonPropertyName(\"subject\")]"), message: "Should have JsonPropertyName for subject.");
        Assert.IsTrue(result.Contains("public string? MessageBody { get; set; }"), message: "Should have PascalCase property.");
        Assert.IsTrue(result.Contains("public string? Subject { get; set; }"), message: "Should have PascalCase property.");
        Assert.IsTrue(result.Contains("namespace MyApp.Models;"), message: "Should have target namespace.");
        Assert.IsTrue(result.Contains("using Microsoft.Azure.Connectors.DirectClient.Teams;"), message: "Should import base class namespace.");
    }

    [TestMethod]
    public void GenerateClass_SameNamespace_OmitsUsingDirective()
    {
        // Arrange
        string schemaJson = """
        {
            "type": "object",
            "properties": {
                "name": { "type": "string" }
            }
        }
        """;
        JsonElement schema = JsonDocument.Parse(schemaJson).RootElement;

        // Act
        string result = SchemaToClassGenerator.GenerateClass(
            className: "TestInput",
            baseClassName: "DynamicTestRequest",
            baseClassNamespace: "MyApp.Models",
            schema: schema,
            targetNamespace: "MyApp.Models");

        // Assert
        Assert.IsFalse(result.Contains("using MyApp.Models;"), message: "Should not import own namespace.");
    }

    [TestMethod]
    public void GenerateClass_IntegerAndBooleanTypes_MapsCorrectly()
    {
        // Arrange
        string schemaJson = """
        {
            "type": "object",
            "properties": {
                "retryCount": { "type": "integer" },
                "isEnabled": { "type": "boolean" },
                "threshold": { "type": "number" }
            }
        }
        """;
        JsonElement schema = JsonDocument.Parse(schemaJson).RootElement;

        // Act
        string result = SchemaToClassGenerator.GenerateClass(
            className: "ConfigInput",
            baseClassName: "DynamicConfigRequest",
            baseClassNamespace: "Sdk",
            schema: schema,
            targetNamespace: "App");

        // Assert
        Assert.IsTrue(result.Contains("public int? RetryCount { get; set; }"), message: "integer should map to int?.");
        Assert.IsTrue(result.Contains("public bool? IsEnabled { get; set; }"), message: "boolean should map to bool?.");
        Assert.IsTrue(result.Contains("public double? Threshold { get; set; }"), message: "number should map to double?.");
    }

    [TestMethod]
    public void GenerateClass_NestedObject_GeneratesNestedClass()
    {
        // Arrange
        string schemaJson = """
        {
            "type": "object",
            "properties": {
                "recipient": {
                    "type": "object",
                    "description": "The message recipient.",
                    "properties": {
                        "groupId": { "type": "string" },
                        "channelId": { "type": "string" }
                    }
                }
            }
        }
        """;
        JsonElement schema = JsonDocument.Parse(schemaJson).RootElement;

        // Act
        string result = SchemaToClassGenerator.GenerateClass(
            className: "PostMessageInput",
            baseClassName: "DynamicPostMessageRequest",
            baseClassNamespace: "Sdk.Teams",
            schema: schema,
            targetNamespace: "App");

        // Assert
        Assert.IsTrue(result.Contains("public Recipient? Recipient { get; set; }"), message: "Nested object should be a typed reference.");
        Assert.IsTrue(result.Contains("public class Recipient"), message: "Nested class should be generated.");
        Assert.IsTrue(result.Contains("[JsonPropertyName(\"groupId\")]"), message: "Nested properties should have JsonPropertyName.");
        Assert.IsTrue(result.Contains("public string? GroupId { get; set; }"), message: "Nested properties should be PascalCase.");
    }

    [TestMethod]
    public void GenerateClass_ArrayType_MapsToList()
    {
        // Arrange
        string schemaJson = """
        {
            "type": "object",
            "properties": {
                "tags": {
                    "type": "array",
                    "items": { "type": "string" }
                }
            }
        }
        """;
        JsonElement schema = JsonDocument.Parse(schemaJson).RootElement;

        // Act
        string result = SchemaToClassGenerator.GenerateClass(
            className: "TagInput",
            baseClassName: "DynamicTagRequest",
            baseClassNamespace: "Sdk",
            schema: schema,
            targetNamespace: "App");

        // Assert
        Assert.IsTrue(result.Contains("public List<string>? Tags { get; set; }"), message: "Array of strings should map to List<string>?.");
    }

    [TestMethod]
    public void GenerateClass_ObjectWithoutProperties_MapsToJsonElement()
    {
        // Arrange
        string schemaJson = """
        {
            "type": "object",
            "properties": {
                "metadata": {
                    "type": "object"
                }
            }
        }
        """;
        JsonElement schema = JsonDocument.Parse(schemaJson).RootElement;

        // Act
        string result = SchemaToClassGenerator.GenerateClass(
            className: "MetaInput",
            baseClassName: "DynamicMetaRequest",
            baseClassNamespace: "Sdk",
            schema: schema,
            targetNamespace: "App");

        // Assert
        Assert.IsTrue(result.Contains("public JsonElement? Metadata { get; set; }"), message: "Object without properties should map to JsonElement?.");
    }

    [TestMethod]
    public void GenerateClass_DateTimeFormat_MapsToDateTimeOffset()
    {
        // Arrange
        string schemaJson = """
        {
            "type": "object",
            "properties": {
                "createdAt": {
                    "type": "string",
                    "format": "date-time"
                }
            }
        }
        """;
        JsonElement schema = JsonDocument.Parse(schemaJson).RootElement;

        // Act
        string result = SchemaToClassGenerator.GenerateClass(
            className: "EventInput",
            baseClassName: "DynamicEventRequest",
            baseClassNamespace: "Sdk",
            schema: schema,
            targetNamespace: "App");

        // Assert
        Assert.IsTrue(result.Contains("public DateTimeOffset? CreatedAt { get; set; }"), message: "date-time format should map to DateTimeOffset?.");
    }

    [TestMethod]
    public void GenerateClass_XmlDocFromDescription_IncludedInOutput()
    {
        // Arrange
        string schemaJson = """
        {
            "type": "object",
            "description": "A test object.",
            "properties": {
                "name": {
                    "type": "string",
                    "description": "The display name."
                }
            }
        }
        """;
        JsonElement schema = JsonDocument.Parse(schemaJson).RootElement;

        // Act
        string result = SchemaToClassGenerator.GenerateClass(
            className: "TestInput",
            baseClassName: "DynamicTestRequest",
            baseClassNamespace: "Sdk",
            schema: schema,
            targetNamespace: "App");

        // Assert
        Assert.IsTrue(result.Contains("/// A test object."), message: "Class-level description should be in XML doc.");
        Assert.IsTrue(result.Contains("/// The display name."), message: "Property-level description should be in XML doc.");
    }

    [TestMethod]
    public void GenerateClass_EmptyProperties_EmptyClass()
    {
        // Arrange
        string schemaJson = """
        {
            "type": "object",
            "properties": {}
        }
        """;
        JsonElement schema = JsonDocument.Parse(schemaJson).RootElement;

        // Act
        string result = SchemaToClassGenerator.GenerateClass(
            className: "EmptyInput",
            baseClassName: "DynamicEmptyRequest",
            baseClassNamespace: "Sdk",
            schema: schema,
            targetNamespace: "App");

        // Assert
        Assert.IsTrue(result.Contains("public class EmptyInput : DynamicEmptyRequest"), message: "Should still generate the class.");
        Assert.IsTrue(result.Contains("{"), message: "Should have opening brace.");
        Assert.IsTrue(result.Contains("}"), message: "Should have closing brace.");
    }

    [TestMethod]
    public void GenerateClass_XmsSummary_UsedAsDescription()
    {
        // Arrange
        string schemaJson = """
        {
            "type": "object",
            "properties": {
                "status": {
                    "type": "string",
                    "x-ms-summary": "Current status of the item."
                }
            }
        }
        """;
        JsonElement schema = JsonDocument.Parse(schemaJson).RootElement;

        // Act
        string result = SchemaToClassGenerator.GenerateClass(
            className: "StatusInput",
            baseClassName: "DynamicStatusRequest",
            baseClassNamespace: "Sdk",
            schema: schema,
            targetNamespace: "App");

        // Assert
        Assert.IsTrue(result.Contains("/// Current status of the item."), message: "x-ms-summary should be used as description.");
    }

    [TestMethod]
    public void ToPascalCase_CamelCase_ConvertedCorrectly()
    {
        Assert.AreEqual("MessageBody", SchemaToClassGenerator.ToPascalCase("messageBody"));
        Assert.AreEqual("GroupId", SchemaToClassGenerator.ToPascalCase("groupId"));
        Assert.AreEqual("Id", SchemaToClassGenerator.ToPascalCase("id"));
        Assert.AreEqual("IsEnabled", SchemaToClassGenerator.ToPascalCase("isEnabled"));
    }

    [TestMethod]
    public void ToPascalCase_SnakeCase_ConvertedCorrectly()
    {
        Assert.AreEqual("MessageBody", SchemaToClassGenerator.ToPascalCase("message_body"));
        Assert.AreEqual("GroupId", SchemaToClassGenerator.ToPascalCase("group_id"));
    }

    [TestMethod]
    public void ToPascalCase_AlreadyPascalCase_Unchanged()
    {
        Assert.AreEqual("MessageBody", SchemaToClassGenerator.ToPascalCase("MessageBody"));
        Assert.AreEqual("Id", SchemaToClassGenerator.ToPascalCase("Id"));
    }

    [TestMethod]
    public void ToPascalCase_EmptyOrNull_ReturnsInput()
    {
        Assert.AreEqual(string.Empty, SchemaToClassGenerator.ToPascalCase(string.Empty));
        Assert.IsNull(SchemaToClassGenerator.ToPascalCase(null!));
    }

    [TestMethod]
    public void GenerateClass_DescriptionWithSpecialChars_EscapedInXmlDoc()
    {
        // Arrange
        string schemaJson = """
        {
            "type": "object",
            "properties": {
                "formula": {
                    "type": "string",
                    "description": "Use <b>bold</b> & \"quotes\" in HTML."
                }
            }
        }
        """;
        JsonElement schema = JsonDocument.Parse(schemaJson).RootElement;

        // Act
        string result = SchemaToClassGenerator.GenerateClass(
            className: "FormulaInput",
            baseClassName: "DynamicFormulaRequest",
            baseClassNamespace: "Sdk",
            schema: schema,
            targetNamespace: "App");

        // Assert
        Assert.IsTrue(result.Contains("&lt;b&gt;"), message: "Angle brackets should be XML-escaped.");
        Assert.IsTrue(result.Contains("&amp;"), message: "Ampersand should be XML-escaped.");
    }

    [TestMethod]
    public void GenerateClass_ArrayOfObjects_GeneratesItemClass()
    {
        // Arrange
        string schemaJson = """
        {
            "type": "object",
            "properties": {
                "attachments": {
                    "type": "array",
                    "items": {
                        "type": "object",
                        "properties": {
                            "name": { "type": "string" },
                            "size": { "type": "integer" }
                        }
                    }
                }
            }
        }
        """;
        JsonElement schema = JsonDocument.Parse(schemaJson).RootElement;

        // Act
        string result = SchemaToClassGenerator.GenerateClass(
            className: "FileInput",
            baseClassName: "DynamicFileRequest",
            baseClassNamespace: "Sdk",
            schema: schema,
            targetNamespace: "App");

        // Assert
        Assert.IsTrue(result.Contains("public List<AttachmentsItem>? Attachments { get; set; }"), message: "Array of objects should reference the generated item class.");
        Assert.IsTrue(result.Contains("public class AttachmentsItem"), message: "Item class should be generated.");
        Assert.IsTrue(result.Contains("public string? Name { get; set; }"), message: "Item class should have string property.");
        Assert.IsTrue(result.Contains("public int? Size { get; set; }"), message: "Item class should have int property.");
    }

    [TestMethod]
    public void GenerateClassName_DynamicPostMessageRequest_PostMessageInput()
    {
        Assert.AreEqual("PostMessageInput", DynamicSchemaCodeActionHandler.GenerateClassName("DynamicPostMessageRequest"));
    }

    [TestMethod]
    public void GenerateClassName_DynamicGetMessageDetailsResponseSchema_GetMessageDetailsResponseModel()
    {
        Assert.AreEqual("GetMessageDetailsResponseModel", DynamicSchemaCodeActionHandler.GenerateClassName("DynamicGetMessageDetailsResponseSchema"));
    }

    [TestMethod]
    public void GenerateClassName_NoDynamicPrefix_KeepsName()
    {
        Assert.AreEqual("CustomType", DynamicSchemaCodeActionHandler.GenerateClassName("CustomType"));
    }
}
