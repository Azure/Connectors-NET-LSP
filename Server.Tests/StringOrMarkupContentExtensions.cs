//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace SdkLspServer.Tests;

/// <summary>
/// Test helpers for <see cref="StringOrMarkupContent"/>, which is the type of
/// <see cref="Diagnostic.Message"/> in OmniSharp LanguageServer 0.19.11+.
/// </summary>
internal static class StringOrMarkupContentExtensions
{
    public static string GetText(this StringOrMarkupContent content) =>
        content.String ?? content.MarkupContent?.Value ?? string.Empty;

    public static bool Contains(this StringOrMarkupContent content, string value, StringComparison comparison) =>
        content.GetText().Contains(value, comparison);
}
