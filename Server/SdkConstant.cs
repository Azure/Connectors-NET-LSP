namespace SdkLspServer;

/// <summary>
/// Represents a constant field discovered from a static class in the SDK assembly.
/// </summary>
/// <param name="FieldName">The C# field name (e.g., "Office365").</param>
/// <param name="Value">The constant string value (e.g., "office365").</param>
/// <param name="ClassName">The simple class name (e.g., "ConnectorNames").</param>
/// <param name="FullClassName">The fully-qualified class name including namespace.</param>
public sealed record SdkConstant(string FieldName, string Value, string ClassName, string FullClassName);
