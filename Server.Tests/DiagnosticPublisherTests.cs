//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

using System.Collections.Concurrent;

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

using SdkLspServer.Diagnostics;
using SdkLspServer.Diagnostics.Validators;

namespace SdkLspServer.Tests;

[TestClass]
public class DiagnosticPublisherTests
{
    /// <summary>
    /// Fake validator that returns a fixed set of diagnostics.
    /// </summary>
    private sealed class FakeValidator : IDiagnosticValidator
    {
        private readonly IReadOnlyList<Diagnostic> diagnostics;

        public FakeValidator(params Diagnostic[] diagnostics)
        {
            this.diagnostics = diagnostics;
        }

        public Task<IReadOnlyList<Diagnostic>> ValidateAsync(
            DocumentUri documentUri,
            string documentText,
            SdkIndex? sdkIndex,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(this.diagnostics);
        }
    }

    /// <summary>
    /// Fake validator that throws an exception to verify resilience.
    /// </summary>
    private sealed class ThrowingValidator : IDiagnosticValidator
    {
        public Task<IReadOnlyList<Diagnostic>> ValidateAsync(
            DocumentUri documentUri,
            string documentText,
            SdkIndex? sdkIndex,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException(message: "Simulated validator failure.");
        }
    }

    [TestMethod]
    public async Task PublishDiagnosticsAsync_WithNoValidators_PublishesEmptyDiagnostics()
    {
        // Arrange
        PublishDiagnosticsParams? captured = null;
        var publisher = new DiagnosticPublisher(
            publishAction: parameters => captured = parameters,
            validators: Array.Empty<IDiagnosticValidator>(),
            sdkIndex: null);
        var uri = DocumentUri.From("file:///test.cs");

        // Act
        await publisher
            .PublishDiagnosticsAsync(uri, "class Foo {}", CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Assert.IsNotNull(captured);
        Assert.AreEqual(0, captured.Diagnostics.Count());
    }

    [TestMethod]
    public async Task PublishDiagnosticsAsync_AggregatesDiagnosticsFromMultipleValidators()
    {
        // Arrange
        var diag1 = new Diagnostic
        {
            Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(new Position(0, 0), new Position(0, 5)),
            Severity = DiagnosticSeverity.Warning,
            Code = "TEST001",
            Source = "test",
            Message = "First diagnostic.",
        };
        var diag2 = new Diagnostic
        {
            Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(new Position(1, 0), new Position(1, 10)),
            Severity = DiagnosticSeverity.Error,
            Code = "TEST002",
            Source = "test",
            Message = "Second diagnostic.",
        };

        var validator1 = new FakeValidator(diag1);
        var validator2 = new FakeValidator(diag2);

        PublishDiagnosticsParams? captured = null;
        var publisher = new DiagnosticPublisher(
            publishAction: parameters => captured = parameters,
            validators: new IDiagnosticValidator[] { validator1, validator2 },
            sdkIndex: null);
        var uri = DocumentUri.From("file:///test.cs");

        // Act
        await publisher
            .PublishDiagnosticsAsync(uri, "class Foo {}", CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Assert.IsNotNull(captured);
        Assert.AreEqual(2, captured.Diagnostics.Count());
    }

    [TestMethod]
    public async Task PublishDiagnosticsAsync_ValidatorThrows_OtherValidatorsStillRun()
    {
        // Arrange
        var diag = new Diagnostic
        {
            Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(new Position(0, 0), new Position(0, 5)),
            Severity = DiagnosticSeverity.Information,
            Code = "TEST001",
            Source = "test",
            Message = "Surviving diagnostic.",
        };

        var throwingValidator = new ThrowingValidator();
        var goodValidator = new FakeValidator(diag);

        PublishDiagnosticsParams? captured = null;
        var publisher = new DiagnosticPublisher(
            publishAction: parameters => captured = parameters,
            validators: new IDiagnosticValidator[] { throwingValidator, goodValidator },
            sdkIndex: null);
        var uri = DocumentUri.From("file:///test.cs");

        // Act
        await publisher
            .PublishDiagnosticsAsync(uri, "class Foo {}", CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert — the good validator's diagnostic should still be published
        Assert.IsNotNull(captured);
        Assert.AreEqual(1, captured.Diagnostics.Count());
    }

    [TestMethod]
    public void ClearDiagnostics_PublishesEmptyArray()
    {
        // Arrange
        PublishDiagnosticsParams? captured = null;
        var publisher = new DiagnosticPublisher(
            publishAction: parameters => captured = parameters,
            validators: Array.Empty<IDiagnosticValidator>(),
            sdkIndex: null);
        var uri = DocumentUri.From("file:///test.cs");

        // Act
        publisher.ClearDiagnostics(uri);

        // Assert
        Assert.IsNotNull(captured);
        Assert.AreEqual(0, captured.Diagnostics.Count());
    }

    [TestMethod]
    public async Task SdkUsageValidator_NoSdkUsing_EmitsDiagnostic()
    {
        // Arrange
        var validator = new SdkUsageValidator();
        var uri = DocumentUri.From("file:///test.cs");
        string documentText = "using System;\n\nclass Foo { }";

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, documentText, sdkIndex: null, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Assert.AreEqual(1, diagnostics.Count);
        Assert.AreEqual(DiagnosticCodes.NoSdkUsageDetected, diagnostics[0].Code?.String);
        Assert.AreEqual(DiagnosticSeverity.Information, diagnostics[0].Severity);
        Assert.AreEqual(DiagnosticCodes.Source, diagnostics[0].Source);
    }

    [TestMethod]
    public async Task SdkUsageValidator_WithSdkUsing_NoDiagnostics()
    {
        // Arrange
        var validator = new SdkUsageValidator();
        var uri = DocumentUri.From("file:///test.cs");
        string documentText = "using Microsoft.Azure.Connectors.Sdk;\n\nclass Foo { }";

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, documentText, sdkIndex: null, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Assert.AreEqual(0, diagnostics.Count);
    }

    [TestMethod]
    public async Task SdkUsageValidator_EmptyDocument_NoDiagnostics()
    {
        // Arrange
        var validator = new SdkUsageValidator();
        var uri = DocumentUri.From("file:///test.cs");

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, string.Empty, sdkIndex: null, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Assert.AreEqual(0, diagnostics.Count);
    }

    [TestMethod]
    public async Task ScheduleDebouncedPublish_OnlyLastScheduledPublishFires()
    {
        // Arrange
        var publishedParams = new ConcurrentQueue<PublishDiagnosticsParams>();
        var publishSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publisher = new DiagnosticPublisher(
            publishAction: parameters =>
            {
                publishedParams.Enqueue(parameters);
                publishSignal.TrySetResult();
            },
            validators: Array.Empty<IDiagnosticValidator>(),
            sdkIndex: null,
            debounceInterval: TimeSpan.FromMilliseconds(50));
        var uri = DocumentUri.From("file:///test.cs");

        // Act — schedule three rapid changes; only the last should publish
        publisher.ScheduleDebouncedPublish(uri, "version1");
        publisher.ScheduleDebouncedPublish(uri, "version2");
        publisher.ScheduleDebouncedPublish(uri, "version3");

        // Wait for the publish signal or timeout
        await Task.WhenAny(publishSignal.Task, Task.Delay(5000))
            .ConfigureAwait(continueOnCapturedContext: false);

        // Small additional delay to confirm no extra publishes arrive
        await Task.Delay(100)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert — only one publish should have occurred (from the last schedule)
        Assert.AreEqual(1, publishedParams.Count, message: "Only the last debounced publish should fire.");
    }

    [TestMethod]
    public async Task ClearDiagnostics_CancelsPendingDebounce_NoDiagnosticsPublishedAfterClear()
    {
        // Arrange
        var publishedParams = new ConcurrentQueue<PublishDiagnosticsParams>();
        var publisher = new DiagnosticPublisher(
            publishAction: parameters => publishedParams.Enqueue(parameters),
            validators: Array.Empty<IDiagnosticValidator>(),
            sdkIndex: null,
            debounceInterval: TimeSpan.FromMilliseconds(100));
        var uri = DocumentUri.From("file:///test.cs");

        // Act — schedule a debounced publish, then immediately clear
        publisher.ScheduleDebouncedPublish(uri, "some text");
        publisher.ClearDiagnostics(uri);

        // Wait long enough for the debounce to have fired if it wasn't cancelled
        await Task.Delay(300)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert — only the ClearDiagnostics publish (empty array) should be present;
        // the debounced publish should have been cancelled
        Assert.AreEqual(1, publishedParams.Count, message: "Only the ClearDiagnostics call should publish.");
        publishedParams.TryPeek(out PublishDiagnosticsParams? clearParams);
        Assert.IsNotNull(clearParams);
        Assert.AreEqual(0, clearParams.Diagnostics.Count(), message: "ClearDiagnostics should publish an empty array.");
    }
}
