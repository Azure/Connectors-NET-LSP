using SdkLspServer.Handlers.CodeActionHandler;

namespace SdkLspServer.Tests;

[TestClass]
public class SourceCodeUpdateTests
{
    [TestMethod]
    public void ApplySourceCodeUpdate_FullyQualifiedType_ReplacedWithGeneratedClass()
    {
        // Arrange
        string source = """
            var messageRequest = new Azure.Connectors.Sdk.Teams.DynamicPostMessageRequest();
            messageRequest.AdditionalProperties["messageBody"] = JsonSerializer.SerializeToElement("hello");
            """;

        // Act
        string result = GenerateDynamicSchemaCommandHandler.ApplySourceCodeUpdate(
            source,
            dynamicTypeName: "DynamicPostMessageRequest",
            typeNamespace: "Azure.Connectors.Sdk.Teams",
            generatedClassName: "PostMessageInput");

        // Assert
        Assert.IsTrue(
            result.Contains("new PostMessageInput()", StringComparison.Ordinal),
            message: "Should replace the fully-qualified new expression.");
        Assert.IsFalse(
            result.Contains("DynamicPostMessageRequest", StringComparison.Ordinal),
            message: "Original type name should be gone.");
        Assert.IsTrue(
            result.Contains("AdditionalProperties", StringComparison.Ordinal),
            message: "AdditionalProperties access should remain unchanged.");
    }

    [TestMethod]
    public void ApplySourceCodeUpdate_ShortTypeName_ReplacedWithGeneratedClass()
    {
        // Arrange
        string source = """
            var request = new DynamicPostMessageRequest();
            """;

        // Act
        string result = GenerateDynamicSchemaCommandHandler.ApplySourceCodeUpdate(
            source,
            dynamicTypeName: "DynamicPostMessageRequest",
            typeNamespace: null,
            generatedClassName: "PostMessageInput");

        // Assert
        Assert.IsTrue(
            result.Contains("new PostMessageInput()", StringComparison.Ordinal),
            message: "Should replace short-form new expression.");
    }

    [TestMethod]
    public void ApplySourceCodeUpdate_MultipleInstances_AllReplaced()
    {
        // Arrange
        string source = """
            var a = new DynamicPostMessageRequest();
            var b = new DynamicPostMessageRequest();
            """;

        // Act
        string result = GenerateDynamicSchemaCommandHandler.ApplySourceCodeUpdate(
            source,
            dynamicTypeName: "DynamicPostMessageRequest",
            typeNamespace: null,
            generatedClassName: "PostMessageInput");

        // Assert
        int count = result.Split("new PostMessageInput()").Length - 1;
        Assert.AreEqual(2, count, message: "Both instances should be replaced.");
    }

    [TestMethod]
    public void ApplySourceCodeUpdate_NoMatch_ReturnsUnchanged()
    {
        // Arrange
        string source = """
            var items = new List<string>();
            """;

        // Act
        string result = GenerateDynamicSchemaCommandHandler.ApplySourceCodeUpdate(
            source,
            dynamicTypeName: "DynamicPostMessageRequest",
            typeNamespace: null,
            generatedClassName: "PostMessageInput");

        // Assert
        Assert.AreEqual(source, result, message: "Should return unchanged when no match.");
    }

    [TestMethod]
    public void ApplySourceCodeUpdate_DoesNotReplaceTypeInOtherContexts()
    {
        // Arrange — the type name appears as a parameter type and in a new expression
        string source = """
            public void Process(DynamicPostMessageRequest existingParam)
            {
                var request = new DynamicPostMessageRequest();
            }
            """;

        // Act
        string result = GenerateDynamicSchemaCommandHandler.ApplySourceCodeUpdate(
            source,
            dynamicTypeName: "DynamicPostMessageRequest",
            typeNamespace: null,
            generatedClassName: "PostMessageInput");

        // Assert — only `new DynamicPostMessageRequest()` should be replaced
        Assert.IsTrue(
            result.Contains("new PostMessageInput()", StringComparison.Ordinal),
            message: "new expression should be replaced.");
        Assert.IsTrue(
            result.Contains("DynamicPostMessageRequest existingParam", StringComparison.Ordinal),
            message: "Parameter type should remain unchanged — it's the base type, still valid.");
    }
}
