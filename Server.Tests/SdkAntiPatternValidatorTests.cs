//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

using System.Collections.Immutable;

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

using SdkLspServer.Diagnostics;
using SdkLspServer.Diagnostics.Validators;
using SdkLspServer.Services;

namespace SdkLspServer.Tests;

[TestClass]
public sealed class SdkAntiPatternValidatorTests
{
    /// <summary>
    /// Preamble that defines fake SDK types in the <c>Azure.Connectors.Sdk</c> namespace
    /// so that the semantic model resolves connector client methods during analysis.
    /// </summary>
    private const string SdkPreamble = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        namespace Azure.Connectors.Sdk
        {
            [AttributeUsage(AttributeTargets.Method)]
            public sealed class ConnectorOperationAttribute : Attribute
            {
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
            }
            public class ConnectorException : Exception
            {
                public int StatusCode { get; set; }
            }
        }
        namespace Azure.Connectors.Sdk.Office365
        {
            public class Office365Client
            {
                public virtual Task<object> SendEmailAsync(
                    string subject,
                    CancellationToken cancellationToken = default) => Task.FromResult<object>(null);

                public virtual Task<object> GetEmailAsync(
                    string emailId,
                    CancellationToken cancellationToken = default) => Task.FromResult<object>(null);

                public virtual void SyncOperation(string id) { }
            }
        }
        """;

    private static SdkIndex CreateMockSdkIndex()
    {
        return SdkIndex.CreateForTesting(
            connectorNames: new[]
            {
                new SdkConstant("Office365", "office365", "ConnectorNames", "Azure.Connectors.Sdk.ConnectorNames"),
            },
            triggerOperations: new Dictionary<string, ImmutableArray<SdkConstant>>
            {
                ["office365"] = ImmutableArray.Create(
                    new SdkConstant("OnNewEmail", "OnNewEmail", "Office365TriggerOperations", "Azure.Connectors.Sdk.Office365.Office365TriggerOperations")),
            },
            typeNames: new[]
            {
                "Azure.Connectors.Sdk.Office365.Office365SendEmailInput",
                "Azure.Connectors.Sdk.Office365.Office365SendEmailOutput",
            });
    }

    private static SdkAntiPatternValidator CreateValidator(SdkIndex? sdkIndex = null)
    {
        var compilationService = new CompilationService(sdkIndex);
        return new SdkAntiPatternValidator(compilationService);
    }

    // ---------------------------------------------------------------
    // CSDK401: [ConnectorOperation] unknown operation
    // ---------------------------------------------------------------
    [TestMethod]
    public async Task ValidateAsync_ConnectorOperationUnknownOperation_EmitsCSdk401Async()
    {
        // Arrange
        var sdkIndex = SdkAntiPatternValidatorTests.CreateMockSdkIndex();
        var validator = SdkAntiPatternValidatorTests.CreateValidator(sdkIndex);
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            [AttributeUsage(AttributeTargets.Method)]
            public sealed class ConnectorOperationAttribute : Attribute
            {
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
            }
            public class Test
            {
                [ConnectorOperation(ConnectorName = "office365", OperationName = "NonexistentOp")]
                public void MyMethod() { }
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Diagnostic? result = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.ConnectorOperationValueUnknown, StringComparison.Ordinal));
        Assert.IsNotNull(result, message: "Expected CSDK401 for unknown operation name.");
        Assert.AreEqual(DiagnosticSeverity.Warning, result.Severity);
        Assert.IsTrue(result.Message.Contains("NonexistentOp", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ValidateAsync_ConnectorOperationKnownOperation_NoCSdk401Async()
    {
        // Arrange
        var sdkIndex = SdkAntiPatternValidatorTests.CreateMockSdkIndex();
        var validator = SdkAntiPatternValidatorTests.CreateValidator(sdkIndex);
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            [AttributeUsage(AttributeTargets.Method)]
            public sealed class ConnectorOperationAttribute : Attribute
            {
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
            }
            public class Test
            {
                [ConnectorOperation(ConnectorName = "office365", OperationName = "OnNewEmail")]
                public void MyMethod() { }
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Assert.IsFalse(
            diagnostics.Any(diagnostic =>
                string.Equals(diagnostic.Code?.String, DiagnosticCodes.ConnectorOperationValueUnknown, StringComparison.Ordinal)),
            message: "Should not emit CSDK401 for a known operation.");
    }

    [TestMethod]
    public async Task ValidateAsync_ConnectorOperationPositionalArgument_EmitsCSdk401Async()
    {
        // Arrange
        var sdkIndex = SdkAntiPatternValidatorTests.CreateMockSdkIndex();
        var validator = SdkAntiPatternValidatorTests.CreateValidator(sdkIndex);
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            [AttributeUsage(AttributeTargets.Method)]
            public sealed class ConnectorOperationAttribute : Attribute
            {
                public ConnectorOperationAttribute(string operationName) { OperationName = operationName; }
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
            }
            public class Test
            {
                [ConnectorOperation("UnknownPositionalOp")]
                public void MyMethod() { }
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Diagnostic? result = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.ConnectorOperationValueUnknown, StringComparison.Ordinal));
        Assert.IsNotNull(result, message: "Expected CSDK401 for unknown positional operation name.");
    }

    // ---------------------------------------------------------------
    // CSDK402: Wrong payload type direction
    // ---------------------------------------------------------------
    [TestMethod]
    public async Task ValidateAsync_InputTypeForAwaitResult_EmitsCSdk402Async()
    {
        // Arrange
        var sdkIndex = SdkAntiPatternValidatorTests.CreateMockSdkIndex();
        var validator = SdkAntiPatternValidatorTests.CreateValidator(sdkIndex);
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System.Threading.Tasks;
            public class Office365SendEmailInput { }
            public class Office365SendEmailOutput { }
            public class Test
            {
                public async Task Run()
                {
                    Office365SendEmailInput result = await Task.FromResult(new Office365SendEmailInput());
                }
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Diagnostic? result2 = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.WrongPayloadTypeDirection, StringComparison.Ordinal));
        Assert.IsNotNull(result2, message: "Expected CSDK402 for Input type receiving await result.");
        Assert.AreEqual(DiagnosticSeverity.Information, result2.Severity);
        Assert.IsTrue(result2.Message.Contains("Office365SendEmailOutput", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ValidateAsync_OutputTypeForAwaitResult_NoCSdk402Async()
    {
        // Arrange
        var sdkIndex = SdkAntiPatternValidatorTests.CreateMockSdkIndex();
        var validator = SdkAntiPatternValidatorTests.CreateValidator(sdkIndex);
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System.Threading.Tasks;
            public class Office365SendEmailOutput { }
            public class Test
            {
                public async Task Run()
                {
                    Office365SendEmailOutput result = await Task.FromResult(new Office365SendEmailOutput());
                }
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Assert.IsFalse(
            diagnostics.Any(diagnostic =>
                string.Equals(diagnostic.Code?.String, DiagnosticCodes.WrongPayloadTypeDirection, StringComparison.Ordinal)),
            message: "Should not emit CSDK402 for correct Output type.");
    }

    // ---------------------------------------------------------------
    // CSDK403: ConnectorException without StatusCode
    // ---------------------------------------------------------------
    [TestMethod]
    public async Task ValidateAsync_CatchConnectorExceptionWithoutStatusCode_EmitsCSdk403Async()
    {
        // Arrange
        var validator = SdkAntiPatternValidatorTests.CreateValidator();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            public class ConnectorException : Exception
            {
                public int StatusCode { get; set; }
            }
            public class Test
            {
                public void Run()
                {
                    try { }
                    catch (ConnectorException ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                }
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex: null, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Diagnostic? result = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.ConnectorExceptionWithoutStatusCode, StringComparison.Ordinal));
        Assert.IsNotNull(result, message: "Expected CSDK403 for ConnectorException without StatusCode check.");
        Assert.AreEqual(DiagnosticSeverity.Warning, result.Severity);
        Assert.IsTrue(result.Message.Contains("StatusCode", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ValidateAsync_CatchConnectorExceptionWithStatusCode_NoCSdk403Async()
    {
        // Arrange
        var validator = SdkAntiPatternValidatorTests.CreateValidator();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            public class ConnectorException : Exception
            {
                public int StatusCode { get; set; }
            }
            public class Test
            {
                public void Run()
                {
                    try { }
                    catch (ConnectorException ex)
                    {
                        if (ex.StatusCode == 404)
                        {
                            Console.WriteLine("Not found.");
                        }
                    }
                }
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex: null, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Assert.IsFalse(
            diagnostics.Any(diagnostic =>
                string.Equals(diagnostic.Code?.String, DiagnosticCodes.ConnectorExceptionWithoutStatusCode, StringComparison.Ordinal)),
            message: "Should not emit CSDK403 when StatusCode is checked.");
    }

    // ---------------------------------------------------------------
    // CSDK404: Async connector call without await
    // ---------------------------------------------------------------
    [TestMethod]
    public async Task ValidateAsync_AsyncConnectorCallWithoutAwait_EmitsCSdk404Async()
    {
        // Arrange
        var sdkIndex = SdkAntiPatternValidatorTests.CreateMockSdkIndex();
        var validator = SdkAntiPatternValidatorTests.CreateValidator(sdkIndex);
        var uri = DocumentUri.From("file:///test.cs");
        string code = SdkAntiPatternValidatorTests.SdkPreamble + """
            namespace TestApp
            {
                public class MyFunction
                {
                    public void Run()
                    {
                        var client = new Azure.Connectors.Sdk.Office365.Office365Client();
                        client.SendEmailAsync("test");
                    }
                }
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Diagnostic? result = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.AsyncConnectorCallWithoutAwait, StringComparison.Ordinal));
        Assert.IsNotNull(result, message: "Expected CSDK404 for async connector call without await.");
        Assert.AreEqual(DiagnosticSeverity.Warning, result.Severity);
        Assert.IsTrue(result.Message.Contains("SendEmailAsync", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ValidateAsync_AsyncConnectorCallWithAwait_NoCSdk404Async()
    {
        // Arrange
        var sdkIndex = SdkAntiPatternValidatorTests.CreateMockSdkIndex();
        var validator = SdkAntiPatternValidatorTests.CreateValidator(sdkIndex);
        var uri = DocumentUri.From("file:///test.cs");
        string code = SdkAntiPatternValidatorTests.SdkPreamble + """
            namespace TestApp
            {
                public class MyFunction
                {
                    public async System.Threading.Tasks.Task Run()
                    {
                        var client = new Azure.Connectors.Sdk.Office365.Office365Client();
                        await client.SendEmailAsync("test");
                    }
                }
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Assert.IsFalse(
            diagnostics.Any(diagnostic =>
                string.Equals(diagnostic.Code?.String, DiagnosticCodes.AsyncConnectorCallWithoutAwait, StringComparison.Ordinal)),
            message: "Should not emit CSDK404 when await is used.");
    }

    [TestMethod]
    public async Task ValidateAsync_SyncConnectorCallWithoutAwait_NoCSdk404Async()
    {
        // Arrange
        var sdkIndex = SdkAntiPatternValidatorTests.CreateMockSdkIndex();
        var validator = SdkAntiPatternValidatorTests.CreateValidator(sdkIndex);
        var uri = DocumentUri.From("file:///test.cs");
        string code = SdkAntiPatternValidatorTests.SdkPreamble + """
            namespace TestApp
            {
                public class MyFunction
                {
                    public void Run()
                    {
                        var client = new Azure.Connectors.Sdk.Office365.Office365Client();
                        client.SyncOperation("test");
                    }
                }
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Assert.IsFalse(
            diagnostics.Any(diagnostic =>
                string.Equals(diagnostic.Code?.String, DiagnosticCodes.AsyncConnectorCallWithoutAwait, StringComparison.Ordinal)),
            message: "Should not emit CSDK404 for sync methods.");
    }

    // ---------------------------------------------------------------
    // CSDK405: CancellationToken not passed
    // ---------------------------------------------------------------
    [TestMethod]
    public async Task ValidateAsync_CancellationTokenNotPassed_EmitsCSdk405Async()
    {
        // Arrange
        var sdkIndex = SdkAntiPatternValidatorTests.CreateMockSdkIndex();
        var validator = SdkAntiPatternValidatorTests.CreateValidator(sdkIndex);
        var uri = DocumentUri.From("file:///test.cs");
        string code = SdkAntiPatternValidatorTests.SdkPreamble + """
            namespace TestApp
            {
                public class MyFunction
                {
                    public async System.Threading.Tasks.Task Run(System.Threading.CancellationToken cancellationToken)
                    {
                        var client = new Azure.Connectors.Sdk.Office365.Office365Client();
                        await client.SendEmailAsync("test");
                    }
                }
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Diagnostic? result = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.CancellationTokenNotPassed, StringComparison.Ordinal));
        Assert.IsNotNull(result, message: "Expected CSDK405 when CancellationToken is available but not passed.");
        Assert.AreEqual(DiagnosticSeverity.Warning, result.Severity);
        Assert.IsTrue(result.Message.Contains("cancellationToken", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ValidateAsync_CancellationTokenPassed_NoCSdk405Async()
    {
        // Arrange
        var sdkIndex = SdkAntiPatternValidatorTests.CreateMockSdkIndex();
        var validator = SdkAntiPatternValidatorTests.CreateValidator(sdkIndex);
        var uri = DocumentUri.From("file:///test.cs");
        string code = SdkAntiPatternValidatorTests.SdkPreamble + """
            namespace TestApp
            {
                public class MyFunction
                {
                    public async System.Threading.Tasks.Task Run(System.Threading.CancellationToken cancellationToken)
                    {
                        var client = new Azure.Connectors.Sdk.Office365.Office365Client();
                        await client.SendEmailAsync("test", cancellationToken);
                    }
                }
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Assert.IsFalse(
            diagnostics.Any(diagnostic =>
                string.Equals(diagnostic.Code?.String, DiagnosticCodes.CancellationTokenNotPassed, StringComparison.Ordinal)),
            message: "Should not emit CSDK405 when CancellationToken is forwarded.");
    }

    [TestMethod]
    public async Task ValidateAsync_NoCancellationTokenInScope_NoCSdk405Async()
    {
        // Arrange
        var sdkIndex = SdkAntiPatternValidatorTests.CreateMockSdkIndex();
        var validator = SdkAntiPatternValidatorTests.CreateValidator(sdkIndex);
        var uri = DocumentUri.From("file:///test.cs");
        string code = SdkAntiPatternValidatorTests.SdkPreamble + """
            namespace TestApp
            {
                public class MyFunction
                {
                    public async System.Threading.Tasks.Task Run()
                    {
                        var client = new Azure.Connectors.Sdk.Office365.Office365Client();
                        await client.SendEmailAsync("test");
                    }
                }
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Assert.IsFalse(
            diagnostics.Any(diagnostic =>
                string.Equals(diagnostic.Code?.String, DiagnosticCodes.CancellationTokenNotPassed, StringComparison.Ordinal)),
            message: "Should not emit CSDK405 when no CancellationToken is available.");
    }

    // ---------------------------------------------------------------
    // Edge cases
    // ---------------------------------------------------------------
    [TestMethod]
    public async Task ValidateAsync_EmptyDocument_ReturnsEmptyAsync()
    {
        // Arrange
        var validator = SdkAntiPatternValidatorTests.CreateValidator();
        var uri = DocumentUri.From("file:///test.cs");

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, string.Empty, sdkIndex: null, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Assert.AreEqual(0, diagnostics.Count, message: "Empty document should produce no diagnostics.");
    }

    [TestMethod]
    public async Task ValidateAsync_NullSdkIndex_SkipsSdkDependentChecksAsync()
    {
        // Arrange
        var validator = SdkAntiPatternValidatorTests.CreateValidator();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            [AttributeUsage(AttributeTargets.Method)]
            public sealed class ConnectorOperationAttribute : Attribute
            {
                public string OperationName { get; set; } = "";
            }
            public class Test
            {
                [ConnectorOperation(OperationName = "NonexistentOp")]
                public void MyMethod() { }
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex: null, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Assert.IsFalse(
            diagnostics.Any(diagnostic =>
                string.Equals(diagnostic.Code?.String, DiagnosticCodes.ConnectorOperationValueUnknown, StringComparison.Ordinal)),
            message: "Should not emit CSDK401 when SdkIndex is null.");
    }
}
