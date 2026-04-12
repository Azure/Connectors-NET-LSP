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

    /// <summary>ConnectorName value does not match any known connector.</summary>
    public const string UnknownConnectorName = "CSDK001";

    /// <summary>ConnectorName is close to a known connector (possible typo).</summary>
    public const string ConnectorNameTypo = "CSDK002";

    /// <summary>ConnectorName matches a known connector but with wrong casing.</summary>
    public const string ConnectorNameCasing = "CSDK003";

    /// <summary>A trigger attribute is missing required ConnectorName.</summary>
    public const string TriggerMetadataMissingConnectorName = "CSDK004";

    /// <summary>A trigger attribute is missing required OperationName.</summary>
    public const string TriggerMetadataMissingOperationName = "CSDK005";

    /// <summary>A trigger attribute is on a method whose signature does not match the trigger callback pattern.</summary>
    public const string TriggerMetadataSignatureMismatch = "CSDK006";

    /// <summary>OperationName does not match any known trigger operation for the connector.</summary>
    public const string UnknownOperationName = "CSDK007";

    /// <summary>OperationName is specified but the connector has no trigger operations.</summary>
    public const string OperationNameNoTriggers = "CSDK008";

    /// <summary>[ConnectorOperation] references an operation not found in the SDK index.</summary>
    public const string ConnectorOperationUnknown = "CSDK009";

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

    /// <summary>Info-level diagnostic when a C# file contains no reference to the SDK namespace.</summary>
    public const string NoSdkUsageDetected = "CSDK400";
}
