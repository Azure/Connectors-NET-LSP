//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace SdkLspServer.Diagnostics;

/// <summary>
/// Defines diagnostic code ranges for all SDK diagnostic validators.
/// Each category has a reserved range to avoid collisions as new validators are added.
/// </summary>
internal static class DiagnosticCodes
{
    /// <summary>
    /// The source identifier used in all diagnostics published by this server.
    /// </summary>
    public const string Source = "Connectors SDK";

    // ---------------------------------------------------------------
    // CSDK001–CSDK099: Attribute validation
    // ---------------------------------------------------------------

    /// <summary>Info-level diagnostic when a C# file contains no SDK using directives.</summary>
    public const string NoSdkUsageDetected = "CSDK001";

    // ---------------------------------------------------------------
    // CSDK100–CSDK199: Connection configuration
    // ---------------------------------------------------------------

    // (reserved for future validators)

    // ---------------------------------------------------------------
    // CSDK200–CSDK299: Trigger payload types
    // ---------------------------------------------------------------

    // (reserved for future validators)

    // ---------------------------------------------------------------
    // CSDK300–CSDK399: DynamicValues / DynamicSchema
    // ---------------------------------------------------------------

    // (reserved for future validators)

    // ---------------------------------------------------------------
    // CSDK400–CSDK499: SDK usage patterns
    // ---------------------------------------------------------------

    // (reserved for future validators)
}
