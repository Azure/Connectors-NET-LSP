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

    /// <summary>[ConnectorOperation] references a trigger operation not found in the SDK index.</summary>
    public const string ConnectorOperationUnknown = "CSDK009";

    // ---------------------------------------------------------------
    // CSDK100–CSDK199: Connection configuration
    // ---------------------------------------------------------------

    /// <summary>Connection parameter value does not match any configured connection.</summary>
    public const string ConnectionParameterValueInvalid = "CSDK100";

    /// <summary>Connection parameter uses a hard-coded string instead of a configuration reference.</summary>
    public const string ConnectionParameterHardCoded = "CSDK101";

    /// <summary>No connection configuration found for a connector that is used in this file.</summary>
    public const string NoConnectionConfigured = "CSDK102";

    /// <summary>Connection name referenced in code is missing from the connections configuration.</summary>
    public const string ConnectionMissing = "CSDK103";

    /// <summary>Multiple connections match a connector type, making auto-resolution ambiguous.</summary>
    public const string MultipleConnectionsAmbiguous = "CSDK104";

    /// <summary>Connection type does not match the connector type expected by the code context.</summary>
    public const string ConnectionTypeMismatch = "CSDK105";

    // ---------------------------------------------------------------
    // CSDK200–CSDK299: Trigger payload types
    // ---------------------------------------------------------------

    /// <summary>Deserialize&lt;T&gt; type does not match the expected trigger payload type for the operation.</summary>
    public const string TriggerPayloadTypeMismatch = "CSDK200";

    /// <summary>Deserialize&lt;T&gt; uses a weak type (object, Object, dynamic, JsonElement, JObject, JToken) when a typed payload exists.</summary>
    public const string TriggerPayloadWeakType = "CSDK201";

    /// <summary>Generic argument type in Deserialize&lt;T&gt; is not found in the SDK type list.</summary>
    public const string TriggerPayloadTypeNotFound = "CSDK202";

    /// <summary>Operation name in [ConnectorTriggerMetadata] does not map to a known trigger payload type.</summary>
    public const string TriggerPayloadOperationUnmapped = "CSDK203";

    /// <summary>Type used in Deserialize&lt;T&gt; does not follow the TriggerPayload naming convention.</summary>
    public const string TriggerPayloadNotPayloadType = "CSDK204";

    // ---------------------------------------------------------------
    // CSDK300–CSDK399: DynamicValues / DynamicSchema
    // ---------------------------------------------------------------

    /// <summary>
    /// A string literal passed to a <c>[DynamicValues]</c> parameter does not match
    /// any of the values returned by the dynamic values API. Emitted only when the
    /// API has been called (e.g., via hover or completion) and the result is cached.
    /// </summary>
    public const string DynamicValuesInvalidValue = "CSDK300";

    // ---------------------------------------------------------------
    // CSDK400–CSDK499: SDK usage patterns
    // ---------------------------------------------------------------

    /// <summary>Info-level diagnostic when a C# file contains no reference to the SDK namespace.</summary>
    public const string NoSdkUsageDetected = "CSDK400";

    /// <summary><c>[ConnectorOperation]</c> attribute value doesn't match any known operation in the SDK index.</summary>
    public const string ConnectorOperationValueUnknown = "CSDK401";

    /// <summary>Input type used where output type is expected, or vice versa.</summary>
    public const string WrongPayloadTypeDirection = "CSDK402";

    /// <summary>Catching <c>ConnectorException</c> without checking <c>StatusCode</c> property.</summary>
    public const string ConnectorExceptionWithoutStatusCode = "CSDK403";

    /// <summary>Async connector method called without <c>await</c> keyword.</summary>
    public const string AsyncConnectorCallWithoutAwait = "CSDK404";

    /// <summary><c>CancellationToken</c> available in scope but not passed to connector API call.</summary>
    public const string CancellationTokenNotPassed = "CSDK405";

    /// <summary>Connector response property accessed without null check.</summary>
    public const string ResponseWithoutNullCheck = "CSDK406";

    /// <summary>Binary content operation result not disposed with <c>using</c> statement.</summary>
    public const string BinaryContentWithoutUsing = "CSDK407";
}
