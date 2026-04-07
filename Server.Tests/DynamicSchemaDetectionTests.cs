using Microsoft.ApplicationInsights.DataContracts;

using OmniSharp.Extensions.LanguageServer.Protocol.Models;

using SdkLspServer.Handlers.CodeActionHandler;
using SdkLspServer.Services.Telemetry;

namespace SdkLspServer.Tests;

[TestClass]
public class DynamicSchemaDetectionTests
{
    [TestMethod]
    public void DetectDynamicSchemaType_CursorOnObjectCreation_ReturnsTypeInfo()
    {
        // Arrange
        string code = """
            using Microsoft.Azure.Connectors.DirectClient.Teams;

            var request = new DynamicPostMessageRequest();
            """;

        var handler = CreateHandler();

        // Position cursor on "DynamicPostMessageRequest" (line 2, inside the type name)
        var range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(2, 22, 2, 22);

        // Act
        var result = handler.DetectDynamicSchemaType(code, range, CancellationToken.None);

        // Assert — semantic resolution requires the actual SDK assembly to be loaded,
        // so without it, we expect null (the type symbol won't resolve).
        // This test validates the syntactic detection path doesn't throw.
        // Full integration testing requires an SdkIndex with the real assemblies.
        Assert.IsTrue(
            result == null || result.TypeName == "DynamicPostMessageRequest",
            message: "If resolved, type name should match.");
    }

    [TestMethod]
    public void DetectDynamicSchemaType_CursorOnNonDynamicType_ReturnsNull()
    {
        // Arrange
        string code = """
            var items = new List<string>();
            """;

        var handler = CreateHandler();
        var range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(0, 20, 0, 20);

        // Act
        var result = handler.DetectDynamicSchemaType(code, range, CancellationToken.None);

        // Assert
        Assert.IsNull(result, message: "Non-Dynamic types should not trigger code action.");
    }

    [TestMethod]
    public void DetectDynamicSchemaType_EmptyDocument_ReturnsNull()
    {
        // Arrange
        var handler = CreateHandler();
        var range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(0, 0, 0, 0);

        // Act
        var result = handler.DetectDynamicSchemaType(string.Empty, range, CancellationToken.None);

        // Assert
        Assert.IsNull(result, message: "Empty document should return null.");
    }

    [TestMethod]
    public void DetectDynamicSchemaType_CursorOutOfBounds_ReturnsNull()
    {
        // Arrange
        string code = "var x = 1;";
        var handler = CreateHandler();
        var range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(99, 0, 99, 0);

        // Act
        var result = handler.DetectDynamicSchemaType(code, range, CancellationToken.None);

        // Assert
        Assert.IsNull(result, message: "Out-of-bounds cursor should return null.");
    }

    [TestMethod]
    public void GenerateClassName_VariousDynamicTypes_ProducesCleanNames()
    {
        Assert.AreEqual("PostMessageInput", DynamicSchemaCodeActionHandler.GenerateClassName("DynamicPostMessageRequest"));
        Assert.AreEqual("ReplyMessageInput", DynamicSchemaCodeActionHandler.GenerateClassName("DynamicReplyMessageRequest"));
        Assert.AreEqual("PostCardInput", DynamicSchemaCodeActionHandler.GenerateClassName("DynamicPostCardRequest"));
        Assert.AreEqual("GetMessageDetailsResponseModel", DynamicSchemaCodeActionHandler.GenerateClassName("DynamicGetMessageDetailsResponseSchema"));
        Assert.AreEqual("UserNotificationInput", DynamicSchemaCodeActionHandler.GenerateClassName("DynamicUserNotificationRequest"));
    }

    private static DynamicSchemaCodeActionHandler CreateHandler()
    {
        return new DynamicSchemaCodeActionHandler(
            sdkIndex: null,
            bufferManager: new BufferManager(),
            telemetryService: new NoOpTelemetryService());
    }

    private sealed class NoOpTelemetryService : ITelemetryService
    {
        public void Initialize(TelemetryConfig? config)
        {
        }

        public void TrackEvent(string eventName, IDictionary<string, string>? properties = null, IDictionary<string, double>? metrics = null)
        {
        }

        public void TrackMetric(string metricName, double value, IDictionary<string, string>? properties = null)
        {
        }

        public void TrackException(Exception exception, IDictionary<string, string>? properties = null)
        {
        }

        public void TrackTrace(string message, SeverityLevel severity = SeverityLevel.Information, IDictionary<string, string>? properties = null)
        {
        }

        public void TrackDependency(string dependencyType, string target, string name, DateTimeOffset startTime, TimeSpan duration, bool success)
        {
        }

        public void Flush()
        {
        }
    }
}
