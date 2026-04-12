//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace SdkLspServer.Diagnostics.Validators;

/// <summary>
/// Emits an informational diagnostic when a C# file does not contain any
/// <c>using Microsoft.Azure.Connectors.Sdk</c> directives, indicating that
/// the file likely does not use the Connectors SDK.
/// </summary>
internal sealed class SdkUsageValidator : IDiagnosticValidator
{
    private const string SdkNamespace = "Microsoft.Azure.Connectors.Sdk";

    /// <inheritdoc/>
    public Task<IReadOnlyList<Diagnostic>> ValidateAsync(
        DocumentUri documentUri,
        string documentText,
        SdkIndex? sdkIndex,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<Diagnostic>();

        if (string.IsNullOrWhiteSpace(documentText))
        {
            return Task.FromResult<IReadOnlyList<Diagnostic>>(diagnostics);
        }

        // Check whether the document contains a using directive for the SDK namespace
        bool hasSdkUsing = documentText.Contains(SdkUsageValidator.SdkNamespace, StringComparison.Ordinal);

        if (!hasSdkUsing)
        {
            diagnostics.Add(new Diagnostic
            {
                Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                    new Position(0, 0),
                    new Position(0, 0)),
                Severity = DiagnosticSeverity.Information,
                Code = DiagnosticCodes.NoSdkUsageDetected,
                Source = DiagnosticCodes.Source,
                Message = "No SDK usage detected. Add 'using Microsoft.Azure.Connectors.Sdk;' to use the Connectors SDK.",
            });
        }

        return Task.FromResult<IReadOnlyList<Diagnostic>>(diagnostics);
    }
}
