using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace SdkLspServer;

/// <summary>
/// Represents an indexed SDK loaded from a NuGet package or assembly DLLs. It
/// holds lists of assemblies and type names discovered from the SDK assemblies.
/// The index is loaded eagerly on startup to avoid reflection during request handling.
/// </summary>
public sealed class SdkIndex
{
    /// <summary>Gets a description of the source used to create this index (nupkg path, DLL path, or a count of assemblies).</summary>
    public string Source { get; }

    /// <summary>Gets the root directory containing the indexed assemblies (extraction directory for nupkg, containing directory for DLLs).</summary>
    public string RootDirectory { get; }

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

    /// <summary>
    /// Lazily-initialized frozen lookup set containing both fully-qualified, simple,
    /// and nested type names for O(1) existence checks. Built once per SdkIndex instance
    /// using Interlocked.CompareExchange for thread-safe single initialization.
    /// </summary>
    private FrozenSet<string>? typeNameLookupCache;

    /// <summary>
    /// Gets a pre-computed frozen lookup set of type names for fast existence checks.
    /// Contains both fully-qualified names (e.g., "Microsoft.Azure...Office365OnNewEmailTriggerPayload")
    /// and the rightmost simple name segment (e.g., "Office365OnNewEmailTriggerPayload").
    /// For nested types ("Outer+Inner"), only the innermost name ("Inner") is added.
    /// Thread-safe: uses Interlocked.CompareExchange to ensure a single instance.
    /// Immutable: returns FrozenSet which cannot be modified.
    /// </summary>
    public FrozenSet<string> TypeNameLookup
    {
        get
        {
            if (this.typeNameLookupCache is null)
            {
                var lookup = new HashSet<string>(StringComparer.Ordinal);
                foreach (string fullTypeName in this.TypeNames)
                {
                    lookup.Add(fullTypeName);
                    int lastSeparator = Math.Max(fullTypeName.LastIndexOf('.'), fullTypeName.LastIndexOf('+'));
                    if (lastSeparator >= 0)
                    {
                        lookup.Add(fullTypeName.Substring(lastSeparator + 1));
                    }
                }

                Interlocked.CompareExchange(ref this.typeNameLookupCache, lookup.ToFrozenSet(StringComparer.Ordinal), comparand: null);
            }

            return this.typeNameLookupCache!;
        }
    }

    private SdkIndex(
        string source,
        string root,
        IEnumerable<string> assemblies,
        IEnumerable<string> types,
        IEnumerable<SdkConstant> connectorNames,
        IDictionary<string, ImmutableArray<SdkConstant>> triggerOps)
    {
        Source = source;
        RootDirectory = root;
        AssemblyPaths = assemblies.ToImmutableArray();
        TypeNames = types.ToImmutableArray();
        ConnectorNameConstants = connectorNames.ToImmutableArray();
        TriggerOperationsByConnector = ImmutableDictionary.CreateRange(StringComparer.OrdinalIgnoreCase, triggerOps);
    }

    /// <summary>
    /// Creates an <see cref="SdkIndex"/> with explicit connector names and trigger operations.
    /// Intended for unit testing only.
    /// </summary>
    internal static SdkIndex CreateForTesting(
        IEnumerable<SdkConstant> connectorNames,
        IDictionary<string, ImmutableArray<SdkConstant>> triggerOperations,
        IEnumerable<string>? typeNames = null)
    {
        return new SdkIndex(
            source: "test",
            root: string.Empty,
            assemblies: Array.Empty<string>(),
            types: typeNames ?? Array.Empty<string>(),
            connectorNames: connectorNames,
            triggerOps: triggerOperations);
    }

    /// <summary>
    /// Gets trigger operations for a specific connector name (case-insensitive).
    /// </summary>
    public ImmutableArray<SdkConstant> GetTriggerOperations(string connectorName)
    {
        return TriggerOperationsByConnector.TryGetValue(connectorName, out ImmutableArray<SdkConstant> operations)
            ? operations
            : ImmutableArray<SdkConstant>.Empty;
    }

    /// <summary>
    /// Gets all trigger operations from all connectors.
    /// </summary>
    public IEnumerable<SdkConstant> GetAllTriggerOperations()
    {
        return TriggerOperationsByConnector.Values.SelectMany(operations => operations);
    }

    /// <summary>
    /// Maps an operation name to its corresponding TriggerPayload type name.
    /// Convention: {Connector}{OperationName}TriggerPayload.
    /// </summary>
    public string? GetPayloadTypeForOperation(string connectorName, string operationName)
    {
        if (!TriggerOperationsByConnector.TryGetValue(connectorName, out ImmutableArray<SdkConstant> operations) || operations.IsEmpty)
        {
            return null;
        }

        // Extract connector prefix from class name: "Office365TriggerOperations" → "Office365"
        string className = operations[0].ClassName;
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

            return await BuildIndexAsync(nupkgPath, extractRoot, assemblies);
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
            await Console.Error.WriteLineAsync($"[SdkLspServer] Indexing failed: {ex}");
            return null;
        }
    }

    /// <summary>
    /// Attempts to create an index from one or more assembly DLL paths on disk
    /// (e.g., from the NuGet cache). No nupkg extraction is needed — the DLLs
    /// are read directly via MetadataLoadContext.
    /// </summary>
    /// <param name="assemblyPaths">One or more paths to SDK assembly DLLs.</param>
    public static async Task<SdkIndex?> TryCreateFromAssembliesAsync(params string[] assemblyPaths)
    {
        if (assemblyPaths is null || assemblyPaths.Length == 0)
        {
            return null;
        }

        // Normalize to absolute paths so RootDirectory is always usable.
        // Per-path exception handling ensures invalid characters don't crash the server.
        var validPaths = new List<string>();
        foreach (string assemblyPath in assemblyPaths)
        {
            if (string.IsNullOrWhiteSpace(assemblyPath))
            {
                continue;
            }

            try
            {
                string fullPath = Path.GetFullPath(assemblyPath);
                if (File.Exists(fullPath))
                {
                    validPaths.Add(fullPath);
                }
            }
            catch (Exception ex) when (!ex.IsFatal())
            {
                continue;
            }
        }

        if (validPaths.Count == 0)
        {
            return null;
        }

        try
        {
            string sourceDescription = validPaths.Count == 1
                ? validPaths[0]
                : $"{validPaths.Count} assemblies";

            // Use the common parent directory when assemblies span multiple directories,
            // or the containing directory when there is only one.
            string rootDirectory = validPaths.Count == 1
                ? Path.GetDirectoryName(validPaths[0]) ?? string.Empty
                : FindCommonParentDirectory(validPaths);

            return await BuildIndexAsync(sourceDescription, rootDirectory, validPaths);
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
            await Console.Error.WriteLineAsync($"[SdkLspServer] Assembly indexing failed: {ex}");
            return null;
        }
    }

    /// <summary>
    /// Finds the deepest common parent directory for a list of file paths.
    /// </summary>
    private static string FindCommonParentDirectory(List<string> paths)
    {
        if (paths.Count == 0)
        {
            return string.Empty;
        }

        string[] segments = Path.GetDirectoryName(paths[0])?.Split(Path.DirectorySeparatorChar) ?? [];
        foreach (string filePath in paths.Skip(1))
        {
            string[] otherSegments = Path.GetDirectoryName(filePath)?.Split(Path.DirectorySeparatorChar) ?? [];
            int commonLength = Math.Min(segments.Length, otherSegments.Length);
            int matchCount = 0;
            for (int i = 0; i < commonLength; i++)
            {
                if (!string.Equals(segments[i], otherSegments[i], StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                matchCount++;
            }

            segments = segments[..matchCount];
        }

        string result = segments.Length > 0 ? string.Join(Path.DirectorySeparatorChar, segments) : string.Empty;

        // Ensure drive-only roots are valid paths (e.g., "C:" -> "C:\\")
        if (result.Length == 2 && result[1] == ':')
        {
            result += Path.DirectorySeparatorChar;
        }

        // Handle Unix root: empty result from splitting "/" paths means root is "/"
        if (string.IsNullOrEmpty(result) && paths.All(filePath => Path.IsPathRooted(filePath)))
        {
            result = Path.GetPathRoot(paths[0]) ?? string.Empty;
        }

        return result;
    }

    /// <summary>
    /// Shared indexing logic used by both nupkg and direct-assembly creation paths.
    /// </summary>
    /// <param name="sourceDescription">Human-readable description of the SDK source (nupkg path, DLL path, or assembly count).</param>
    /// <param name="root">Root directory containing the indexed assemblies.</param>
    /// <param name="assemblies">Paths to the assembly DLLs to index.</param>
    private static async Task<SdkIndex?> BuildIndexAsync(string sourceDescription, string root, List<string> assemblies)
    {
        // Load metadata and collect type names and constants without executing code
        MetadataReader.DiscoveryResult discovery = MetadataReader.DiscoverTypes(assemblies);
        if (discovery.Failures.Count > 0)
        {
            await Console.Error.WriteLineAsync("[SdkLspServer] Failed to read some assemblies:\n  " +
                string.Join("\n  ", discovery.Failures.Select(failure => $"{failure.Path}: {failure.Error}")));
        }

        await Console.Error.WriteLineAsync($"[SdkLspServer] SDK index: {discovery.Types.Count} types, " +
            $"{discovery.ConnectorNames.Count} connector names, " +
            $"{discovery.TriggerOperations.Values.Sum(operations => operations.Length)} trigger operations across " +
            $"{discovery.TriggerOperations.Count} connectors");

        return new SdkIndex(
            sourceDescription,
            root,
            assemblies,
            discovery.Types,
            discovery.ConnectorNames,
            discovery.TriggerOperations);
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
