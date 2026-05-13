using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace SdkLspServer.Handlers.CodeActionHandler;

/// <summary>
/// Reads [DynamicSchema("operationId")] attributes from SDK assemblies using
/// System.Reflection.Metadata (raw PE/IL metadata reading). This is orders of
/// magnitude faster than Roslyn compilation — typically under 10ms for a single DLL.
/// </summary>
internal static class DynamicSchemaCache
{
    /// <summary>
    /// Builds a complete cache from all SDK assemblies: type name → operationId.
    /// </summary>
    /// <returns>A dictionary mapping type names to operation IDs.</returns>
    public static Dictionary<string, string> Build(IEnumerable<string> assemblyPaths)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string assemblyPath in assemblyPaths)
        {
            var fromAssembly = ScanAssemblyForDynamicSchema(assemblyPath);
            foreach (var kvp in fromAssembly)
            {
                result[kvp.Key] = kvp.Value;
            }
        }

        return result;
    }

    /// <summary>
    /// Scans a single assembly for types with [DynamicSchema("operationId")] and returns
    /// a dictionary of simple type name → operationId.
    /// </summary>
    /// <returns>A dictionary mapping type names to operation IDs from the assembly.</returns>
    public static Dictionary<string, string> ScanAssemblyForDynamicSchema(string assemblyPath)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!File.Exists(assemblyPath))
        {
            return result;
        }

        try
        {
            using var stream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(stream);

            if (!peReader.HasMetadata)
            {
                return result;
            }

            MetadataReader reader = peReader.GetMetadataReader();

            // First, find the TypeRef or TypeDef handle for "DynamicSchemaAttribute"
            // so we can match it efficiently against custom attributes.
            HashSet<int> dynamicSchemaAttributeTokens = FindAttributeTypeTokens(reader, "DynamicSchemaAttribute");

            if (dynamicSchemaAttributeTokens.Count == 0)
            {
                return result;
            }

            // Scan all type definitions for custom attributes matching DynamicSchemaAttribute
            foreach (TypeDefinitionHandle typeDefHandle in reader.TypeDefinitions)
            {
                TypeDefinition typeDef = reader.GetTypeDefinition(typeDefHandle);

                foreach (CustomAttributeHandle attrHandle in typeDef.GetCustomAttributes())
                {
                    CustomAttribute attr = reader.GetCustomAttribute(attrHandle);

                    if (!IsAttributeMatch(reader, attr, dynamicSchemaAttributeTokens))
                    {
                        continue;
                    }

                    // Decode the constructor argument (operationId string)
                    string? operationId = DecodeStringConstructorArg(reader, attr);
                    if (!string.IsNullOrEmpty(operationId))
                    {
                        string typeName = reader.GetString(typeDef.Name);
                        result[typeName] = operationId;
                    }
                }
            }
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
            Console.Error.WriteLine($"[DynamicSchemaCache] Error scanning {Path.GetFileName(assemblyPath)}: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Finds all metadata tokens (TypeRef or TypeDef) that match the attribute name.
    /// </summary>
    private static HashSet<int> FindAttributeTypeTokens(MetadataReader reader, string attributeName)
    {
        var tokens = new HashSet<int>();

        // Check TypeRefs (attributes defined in other assemblies)
        foreach (TypeReferenceHandle typeRefHandle in reader.TypeReferences)
        {
            TypeReference typeRef = reader.GetTypeReference(typeRefHandle);
            if (string.Equals(reader.GetString(typeRef.Name), attributeName, StringComparison.Ordinal))
            {
                tokens.Add(MetadataTokens.GetToken(typeRefHandle));
            }
        }

        // Check TypeDefs (attributes defined in the same assembly)
        foreach (TypeDefinitionHandle typeDefHandle in reader.TypeDefinitions)
        {
            TypeDefinition typeDef = reader.GetTypeDefinition(typeDefHandle);
            if (string.Equals(reader.GetString(typeDef.Name), attributeName, StringComparison.Ordinal))
            {
                tokens.Add(MetadataTokens.GetToken(typeDefHandle));
            }
        }

        return tokens;
    }

    /// <summary>
    /// Checks whether a custom attribute's constructor belongs to one of the matched attribute types.
    /// </summary>
    private static bool IsAttributeMatch(MetadataReader reader, CustomAttribute attr, HashSet<int> targetTokens)
    {
        EntityHandle ctorHandle = attr.Constructor;

        if (ctorHandle.Kind == HandleKind.MemberReference)
        {
            MemberReference memberRef = reader.GetMemberReference((MemberReferenceHandle)ctorHandle);
            int parentToken = MetadataTokens.GetToken(memberRef.Parent);
            return targetTokens.Contains(parentToken);
        }

        if (ctorHandle.Kind == HandleKind.MethodDefinition)
        {
            MethodDefinition methodDef = reader.GetMethodDefinition((MethodDefinitionHandle)ctorHandle);
            int parentToken = MetadataTokens.GetToken(methodDef.GetDeclaringType());
            return targetTokens.Contains(parentToken);
        }

        return false;
    }

    /// <summary>
    /// Decodes the first string constructor argument from a custom attribute blob.
    /// The attribute blob format (ECMA-335 II.23.3) is:
    ///   Prolog (2 bytes: 0x0001) + FixedArgs + NamedArgs
    /// For DynamicSchemaAttribute(string operationId), FixedArgs is a single SerString.
    /// </summary>
    private static string? DecodeStringConstructorArg(MetadataReader reader, CustomAttribute attr)
    {
        try
        {
            BlobReader blobReader = reader.GetBlobReader(attr.Value);

            // Prolog: must be 0x0001
            if (blobReader.Length < 2)
            {
                return null;
            }

            ushort prolog = blobReader.ReadUInt16();
            if (prolog != 0x0001)
            {
                return null;
            }

            // Read SerString: PackedLen-prefixed UTF-8 string
            // Length encoding: single byte if < 0x80, else multi-byte
            return blobReader.ReadSerializedString();
        }
        catch
        {
            return null;
        }
    }
}
