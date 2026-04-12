//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

using OmniSharp.Extensions.LanguageServer.Protocol;

namespace SdkLspServer.Diagnostics;

/// <summary>
/// Contract for diagnostic validators that analyze C# documents for SDK-related issues.
/// Each validator focuses on a specific category of diagnostics (e.g., attribute validation,
/// connection configuration, trigger payloads).
/// </summary>
internal interface IDiagnosticValidator
{
    /// <summary>
    /// Analyzes the given document and returns a list of diagnostics.
    /// </summary>
    /// <param name="documentUri">The URI of the document being analyzed.</param>
    /// <param name="documentText">The full text content of the document.</param>
    /// <param name="sdkIndex">The SDK index for type and assembly lookups. May be null if the SDK failed to load.</param>
    /// <param name="cancellationToken">Cancellation token to abort analysis.</param>
    Task<IReadOnlyList<OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic>> ValidateAsync(
        DocumentUri documentUri,
        string documentText,
        SdkIndex? sdkIndex,
        CancellationToken cancellationToken);
}
