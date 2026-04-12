//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;

namespace SdkLspServer.Diagnostics;

/// <summary>
/// Aggregates diagnostics from all registered <see cref="IDiagnosticValidator"/> implementations
/// and publishes them to the client via <c>textDocument/publishDiagnostics</c>.
/// Supports debouncing to avoid excessive work on rapid text changes.
/// </summary>
internal sealed class DiagnosticPublisher : IDisposable
{
    /// <summary>
    /// Default debounce interval applied when a document changes.
    /// </summary>
    public static readonly TimeSpan DefaultDebounceInterval = TimeSpan.FromMilliseconds(500);

    private readonly Action<PublishDiagnosticsParams> publishAction;
    private readonly IReadOnlyList<IDiagnosticValidator> validators;
    private readonly SdkIndex? sdkIndex;
    private readonly TimeSpan debounceInterval;

    /// <summary>
    /// Tracks the latest debounce CTS per document so that superseded requests are cancelled.
    /// </summary>
    private readonly Dictionary<string, CancellationTokenSource> pendingDebounce = new(StringComparer.Ordinal);
    private readonly object debounceLock = new();

    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiagnosticPublisher"/> class
    /// using an LSP server facade for publishing.
    /// </summary>
    /// <param name="router">The LSP server facade used to publish diagnostics to the client.</param>
    /// <param name="validators">The set of validators to run on each document.</param>
    /// <param name="sdkIndex">The SDK index for type look-ups. May be null.</param>
    /// <param name="debounceInterval">Optional override for the debounce interval (defaults to 500 ms).</param>
    public DiagnosticPublisher(
        ILanguageServerFacade router,
        IEnumerable<IDiagnosticValidator> validators,
        SdkIndex? sdkIndex,
        TimeSpan? debounceInterval = null)
        : this(
              publishAction: (router ?? throw new ArgumentNullException(nameof(router))).TextDocument.PublishDiagnostics,
              validators: validators,
              sdkIndex: sdkIndex,
              debounceInterval: debounceInterval)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DiagnosticPublisher"/> class
    /// using a custom publish action. Primarily used for unit testing.
    /// </summary>
    /// <param name="publishAction">The action to invoke to publish diagnostics.</param>
    /// <param name="validators">The set of validators to run on each document.</param>
    /// <param name="sdkIndex">The SDK index for type look-ups. May be null.</param>
    /// <param name="debounceInterval">Optional override for the debounce interval (defaults to 500 ms).</param>
    public DiagnosticPublisher(
        Action<PublishDiagnosticsParams> publishAction,
        IEnumerable<IDiagnosticValidator> validators,
        SdkIndex? sdkIndex,
        TimeSpan? debounceInterval = null)
    {
        this.publishAction = publishAction ?? throw new ArgumentNullException(nameof(publishAction));
        this.validators = (validators ?? throw new ArgumentNullException(nameof(validators))).ToList();
        this.sdkIndex = sdkIndex;
        this.debounceInterval = debounceInterval ?? DiagnosticPublisher.DefaultDebounceInterval;
    }

    /// <summary>
    /// Runs all validators immediately and publishes the aggregated diagnostics to the client.
    /// Use this for document open and save events where immediate feedback is appropriate.
    /// </summary>
    /// <param name="documentUri">The URI of the document to validate.</param>
    /// <param name="documentText">The full text of the document.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task PublishDiagnosticsAsync(
        DocumentUri documentUri,
        string documentText,
        CancellationToken cancellationToken)
    {
        var allDiagnostics = new List<Diagnostic>();

        foreach (IDiagnosticValidator validator in this.validators)
        {
            try
            {
                IReadOnlyList<Diagnostic> results = await validator
                    .ValidateAsync(documentUri, documentText, this.sdkIndex, cancellationToken)
                    .ConfigureAwait(continueOnCapturedContext: false);

                allDiagnostics.AddRange(results);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync(
                    $"[DiagnosticPublisher] Validator '{validator.GetType().Name}' threw: {ex.Message}");
            }
        }

        this.publishAction(new PublishDiagnosticsParams
        {
            Uri = documentUri,
            Diagnostics = new Container<Diagnostic>(allDiagnostics),
        });
    }

    /// <summary>
    /// Schedules a debounced diagnostic run for the given document.
    /// If a previous debounce is already pending for this document, it is cancelled
    /// and replaced by the new request.
    /// </summary>
    /// <param name="documentUri">The URI of the document to validate.</param>
    /// <param name="documentText">The full text of the document.</param>
    public void ScheduleDebouncedPublish(DocumentUri documentUri, string documentText)
    {
        string key = documentUri.ToString();

        CancellationTokenSource newCts = new();

        lock (this.debounceLock)
        {
            if (this.pendingDebounce.TryGetValue(key, out CancellationTokenSource? existingCts))
            {
                existingCts.Cancel();
                existingCts.Dispose();
            }

            this.pendingDebounce[key] = newCts;
        }

        _ = this.RunDebouncedAsync(documentUri, documentText, newCts.Token);
    }

    /// <summary>
    /// Clears all diagnostics for the given document by publishing an empty diagnostics array.
    /// Call this when a document is closed.
    /// </summary>
    /// <param name="documentUri">The URI of the document whose diagnostics should be cleared.</param>
    public void ClearDiagnostics(DocumentUri documentUri)
    {
        string key = documentUri.ToString();

        // Cancel any pending debounce for this document
        lock (this.debounceLock)
        {
            if (this.pendingDebounce.TryGetValue(key, out CancellationTokenSource? existingCts))
            {
                existingCts.Cancel();
                existingCts.Dispose();
                this.pendingDebounce.Remove(key);
            }
        }

        this.publishAction(new PublishDiagnosticsParams
        {
            Uri = documentUri,
            Diagnostics = new Container<Diagnostic>(),
        });
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;

        lock (this.debounceLock)
        {
            foreach (CancellationTokenSource cts in this.pendingDebounce.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }

            this.pendingDebounce.Clear();
        }
    }

    private async Task RunDebouncedAsync(
        DocumentUri documentUri,
        string documentText,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(this.debounceInterval, cancellationToken)
                .ConfigureAwait(continueOnCapturedContext: false);

            await this
                .PublishDiagnosticsAsync(documentUri, documentText, cancellationToken)
                .ConfigureAwait(continueOnCapturedContext: false);
        }
        catch (OperationCanceledException)
        {
            // Debounce was superseded or document was closed — expected.
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(
                $"[DiagnosticPublisher] Debounced publish failed: {ex.Message}");
        }
    }
}
