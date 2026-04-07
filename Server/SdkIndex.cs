using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace SdkLspServer;

/// <summary>
/// Represents an indexed SDK extracted from a NuGet package. It holds lists of
/// assemblies and type names discovered in the package. The index is loaded
/// eagerly on startup to avoid reflection during request handling.
/// </summary>
public sealed class SdkIndex
{
    /// <summary>Gets the source .nupkg path used to create this index.</summary>
    public string SourceNupkgPath { get; }

    /// <summary>Gets the root directory where the nupkg contents were extracted.</summary>
    public string ExtractRoot { get; }

    /// <summary>Gets list of discovered assembly DLL paths.</summary>
    public ImmutableArray<string> AssemblyPaths { get; }

    /// <summary>Gets list of fully-qualified type names found in those assemblies.</summary>
    public ImmutableArray<string> TypeNames { get; }

    /// <summary>Gets ConnectorNames constants (e.g., "Office365" → "office365").</summary>
    public ImmutableArray<SdkConstant> ConnectorNameConstants { get; }

    /// <summary>Gets all TriggerOperations constants grouped by connector name.</summary>
    public ImmutableDictionary<string, ImmutableArray<SdkConstant>> TriggerOperationsByConnector { get; }

    /// <summary>Gets a human‑readable summary describing how many assemblies and types were loaded.</summary>
    public string Summary => $"{AssemblyPaths.Length} assemblies, {TypeNames.Length} types";

    private SdkIndex(
        string nupkg,
        string root,
        IEnumerable<string> assemblies,
        IEnumerable<string> types,
        IEnumerable<SdkConstant> connectorNames,
        IDictionary<string, ImmutableArray<SdkConstant>> triggerOps)
    {
        SourceNupkgPath = nupkg;
        ExtractRoot = root;
        AssemblyPaths = assemblies.ToImmutableArray();
        TypeNames = types.ToImmutableArray();
        ConnectorNameConstants = connectorNames.ToImmutableArray();
        TriggerOperationsByConnector = ImmutableDictionary.CreateRange(StringComparer.OrdinalIgnoreCase, triggerOps);
    }

    /// <summary>
    /// Gets trigger operations for a specific connector name (case-insensitive).
    /// </summary>
    public ImmutableArray<SdkConstant> GetTriggerOperations(string connectorName)
    {
        return TriggerOperationsByConnector.TryGetValue(connectorName, out ImmutableArray<SdkConstant> ops)
            ? ops
            : ImmutableArray<SdkConstant>.Empty;
    }

    /// <summary>
    /// Gets all trigger operations from all connectors.
    /// </summary>
    public IEnumerable<SdkConstant> GetAllTriggerOperations()
    {
        return TriggerOperationsByConnector.Values.SelectMany(ops => ops);
    }

    /// <summary>
    /// Maps an operation name to its corresponding TriggerPayload type name.
    /// Convention: {Connector}{OperationName}TriggerPayload.
    /// </summary>
    public string? GetPayloadTypeForOperation(string connectorName, string operationName)
    {
        if (!TriggerOperationsByConnector.TryGetValue(connectorName, out ImmutableArray<SdkConstant> ops) || ops.IsEmpty)
        {
            return null;
        }

        // Extract connector prefix from class name: "Office365TriggerOperations" → "Office365"
        string className = ops[0].ClassName;
        int trigIdx = className.IndexOf("TriggerOperations", StringComparison.Ordinal);
        if (trigIdx <= 0)
        {
            return null;
        }

        string connectorPrefix = className.Substring(0, trigIdx);
        string expectedName = $"{connectorPrefix}{operationName}TriggerPayload";
        return TypeNames.FirstOrDefault(t =>
            t.EndsWith(expectedName, StringComparison.Ordinal));
    }

    /// <summary>
    /// Attempts to create an index from the given .nupkg path. If the path
    /// is null or the file does not exist, this returns null. Extraction and
    /// reflection are performed in a separate thread to avoid blocking the
    /// calling thread.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public static async Task<SdkIndex?> TryCreateAsync(string? nupkgPath)
    {
        if (string.IsNullOrWhiteSpace(nupkgPath) || !File.Exists(nupkgPath))
        {
            return null;
        }

        // Generate a temporary directory to extract contents into.
        string extractRoot = Path.Combine(Path.GetTempPath(), "sdk-lsp-server", Path.GetFileNameWithoutExtension(nupkgPath) + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractRoot);

        try
        {
            List<string> assemblies = await NupkgLoader.ExtractAndFindAssembliesAsync(nupkgPath, extractRoot);

            // Load metadata and collect type names and constants without executing code
            MetadataReader.DiscoveryResult discovery = MetadataReader.DiscoverTypes(assemblies);
            if (discovery.Failures.Count > 0)
            {
                await Console.Error.WriteLineAsync("[SdkLspServer] Failed to read some assemblies:\n  " +
                    string.Join("\n  ", discovery.Failures.Select(f => $"{f.Path}: {f.Error}")));
            }

            await Console.Error.WriteLineAsync($"[SdkLspServer] SDK index: {discovery.Types.Count} types, " +
                $"{discovery.ConnectorNames.Count} connector names, " +
                $"{discovery.TriggerOperations.Values.Sum(v => v.Length)} trigger operations across " +
                $"{discovery.TriggerOperations.Count} connectors");

            return new SdkIndex(
                nupkgPath,
                extractRoot,
                assemblies,
                discovery.Types,
                discovery.ConnectorNames,
                discovery.TriggerOperations);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[SdkLspServer] Indexing failed: {ex}");
            return null;
        }
    }

    /// <summary>
    /// A helper for reading type names from a set of assemblies using a
    /// MetadataLoadContext. Reading types in this way avoids executing code
    /// from the assemblies. Failures are collected to report to stderr.
    /// </summary>
    private static class MetadataReader
    {
        public sealed class DiscoveryResult
        {
            public List<string> Types { get; } = new();

            public List<(string Path, string Error)> Failures { get; } = new();

            public List<SdkConstant> ConnectorNames { get; } = new();

            public Dictionary<string, ImmutableArray<SdkConstant>> TriggerOperations { get; } = new(StringComparer.OrdinalIgnoreCase);
        }

        public static DiscoveryResult DiscoverTypes(IEnumerable<string> assemblyPaths)
        {
            var result = new DiscoveryResult();

            try
            {
                IEnumerable<string> core = Array.Empty<string>();

                try
                {
                    string coreDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
                    if (!string.IsNullOrEmpty(coreDir) && Directory.Exists(coreDir))
                    {
                        core = Directory.GetFiles(coreDir, "*.dll", SearchOption.TopDirectoryOnly);
                    }
                }
                catch
                {
                    // Ignore and fall back to TPA probing below.
                }

                if (!core.Any())
                {
                    if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpaList)
                    {
                        core = tpaList.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                                      .Where(p => string.Equals(Path.GetExtension(p), ".dll", StringComparison.OrdinalIgnoreCase));
                    }
                }

                using var resolver = new System.Reflection.MetadataLoadContext(
                    new PathAssemblyResolver(core.Concat(assemblyPaths)));
                foreach (string asmPath in assemblyPaths)
                {
                    try
                    {
                        System.Reflection.Assembly asm = resolver.LoadFromAssemblyPath(asmPath);
                        foreach (Type t in asm.GetTypes())
                        {
                            if (!t.IsNested)
                            {
                                result.Types.Add(t.FullName ?? t.Name);
                            }

                            // Discover ConnectorNames constants
                            if (string.Equals(t.Name, "ConnectorNames", StringComparison.Ordinal) && t.IsAbstract && t.IsSealed)
                            {
                                foreach (FieldInfo field in t.GetFields(BindingFlags.Public | BindingFlags.Static))
                                {
                                    if (field.IsLiteral && field.FieldType.FullName == "System.String")
                                    {
                                        string? value = field.GetRawConstantValue() as string;
                                        if (value != null)
                                        {
                                            result.ConnectorNames.Add(new SdkConstant(field.Name, value, t.Name, t.FullName ?? t.Name));
                                        }
                                    }
                                }
                            }

                            // Discover *TriggerOperations constants
                            if (t.Name.EndsWith("TriggerOperations", StringComparison.Ordinal) && t.IsAbstract && t.IsSealed)
                            {
                                var ops = new List<SdkConstant>();
                                foreach (FieldInfo field in t.GetFields(BindingFlags.Public | BindingFlags.Static))
                                {
                                    if (field.IsLiteral && field.FieldType.FullName == "System.String")
                                    {
                                        string? value = field.GetRawConstantValue() as string;
                                        if (value != null)
                                        {
                                            ops.Add(new SdkConstant(field.Name, value, t.Name, t.FullName ?? t.Name));
                                        }
                                    }
                                }

                                if (ops.Count > 0)
                                {
                                    // Extract connector name from class name: "Office365TriggerOperations" → "Office365"
                                    string connectorPrefix = t.Name.Substring(0, t.Name.Length - "TriggerOperations".Length);

                                    // Map to ConnectorNames value (lowercase): "Office365" → "office365"
                                    string connectorKey = connectorPrefix.ToLowerInvariant();
                                    result.TriggerOperations[connectorKey] = ops.ToImmutableArray();
                                }
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        result.Failures.Add((asmPath, e.Message));
                    }
                }
            }
            catch (Exception e)
            {
                result.Failures.Add(("(resolver)", e.Message));
            }

            return result;
        }
    }
}
