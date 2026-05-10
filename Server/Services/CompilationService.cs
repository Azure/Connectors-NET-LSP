//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SdkLspServer.Services;

/// <summary>
/// Shared service that creates and caches Roslyn <see cref="CSharpCompilation"/> instances.
/// Compilations are cached per document URI and reused when the source text has not changed.
/// Only the latest version per URI is retained to bound memory usage.
/// </summary>
public sealed class CompilationService
{
    private static readonly ConcurrentDictionary<string, List<string>> NuGetReferenceCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly SdkIndex? sdkIndex;

    // Cache: one entry per document URI, evicts previous version on text change.
    // Validity is checked via (textLength, textHashCode, projectDirectory) to avoid hash-only collisions
    // and ensure NuGet references match.
    private readonly ConcurrentDictionary<string, (int Length, int Hash, string? ProjectDirectory, CSharpCompilation Compilation, SemanticModel Model)> cache = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="CompilationService"/> class.
    /// </summary>
    /// <param name="sdkIndex">The SDK index providing assembly references. May be null if the SDK failed to load.</param>
    public CompilationService(SdkIndex? sdkIndex)
    {
        this.sdkIndex = sdkIndex;
    }

    /// <summary>
    /// Gets or creates a Roslyn compilation and semantic model for the given syntax tree.
    /// Returns a cached result when the document text has not changed.
    /// Callers must pass the same <paramref name="syntaxTree"/> they use for syntax node lookups
    /// so that the returned <see cref="SemanticModel"/> is compatible with those nodes.
    /// </summary>
    /// <param name="documentUri">The URI of the document being compiled.</param>
    /// <param name="syntaxTree">The caller's parsed syntax tree. The returned semantic model is for this tree.</param>
    /// <param name="filePath">Optional file path used to resolve NuGet project references.</param>
    public (CSharpCompilation Compilation, SemanticModel Model) GetCompilation(
        Uri documentUri,
        SyntaxTree syntaxTree,
        string? filePath = null)
    {
        string uriKey = documentUri.ToString();
        Microsoft.CodeAnalysis.Text.SourceText sourceText = syntaxTree.GetText();
        int textLength = sourceText.Length;
        int textHash = sourceText.ToString().GetHashCode(StringComparison.Ordinal);
        string? projectDirectory = !string.IsNullOrEmpty(filePath) ? FindProjectDirectory(filePath) : null;

        if (this.cache.TryGetValue(uriKey, out var cached) &&
            cached.Length == textLength &&
            cached.Hash == textHash &&
            string.Equals(cached.ProjectDirectory, projectDirectory, StringComparison.OrdinalIgnoreCase))
        {
            // Fast path: if the caller passed the exact same SyntaxTree instance,
            // return the cached compilation and semantic model directly.
            if (ReferenceEquals(syntaxTree, cached.Compilation.SyntaxTrees.First()))
            {
                return (cached.Compilation, cached.Model);
            }

            // The cached compilation used a tree with identical text.
            // Replace the tree in the compilation so the returned SemanticModel
            // belongs to the caller's SyntaxTree instance.
            CSharpCompilation replaced = cached.Compilation.ReplaceSyntaxTree(
                cached.Compilation.SyntaxTrees.First(),
                syntaxTree);
            SemanticModel model = replaced.GetSemanticModel(syntaxTree);
            return (replaced, model);
        }

        var references = new List<MetadataReference>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddCoreReferences(references, seenPaths);
        this.AddSdkReferences(references, seenPaths);

        if (!string.IsNullOrEmpty(filePath))
        {
            AddProjectNuGetReferences(references, seenPaths, filePath);
        }

        var compilation = CSharpCompilation.Create(
            "LspAnalysis",
            [syntaxTree],
            references);

        SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree);

        // Store with the current tree; replaces any prior entry for this URI.
        this.cache[uriKey] = (textLength, textHash, projectDirectory, compilation, semanticModel);

        return (compilation, semanticModel);
    }

    /// <summary>
    /// Creates a compilation containing only SDK and core references, without any user source text.
    /// Used for metadata-only analysis such as dynamic operations discovery.
    /// </summary>
    public CSharpCompilation CreateSdkMetadataCompilation()
    {
        var references = new List<MetadataReference>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddCoreReferences(references, seenPaths);
        this.AddSdkReferences(references, seenPaths);

        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText("// SDK metadata compilation");
        return CSharpCompilation.Create(
            "SdkMetadataCompilation",
            [syntaxTree],
            references);
    }

    /// <summary>
    /// Adds essential .NET core framework and Microsoft Extensions references required for Roslyn compilation.
    /// </summary>
    /// <param name="references">The collection of metadata references to populate with core assemblies.</param>
    /// <param name="seenPaths">A set tracking already processed assembly paths to prevent duplicate references.</param>
    internal static void AddCoreReferences(List<MetadataReference> references, HashSet<string> seenPaths)
    {
        TryAddReference(references, seenPaths, typeof(string).Assembly.Location);
        TryAddReference(references, seenPaths, typeof(object).Assembly.Location);
        TryAddReference(references, seenPaths, typeof(System.Console).Assembly.Location);
        TryAddReference(references, seenPaths, typeof(System.Linq.Enumerable).Assembly.Location);
        TryAddReference(references, seenPaths, typeof(System.Threading.Tasks.Task).Assembly.Location);
        TryAddReference(references, seenPaths, typeof(System.Action).Assembly.Location);

        TryAddTrustedReference(references, seenPaths, "netstandard.dll");
        TryAddTrustedReference(references, seenPaths, "System.Runtime.dll");
        TryAddTrustedReference(references, seenPaths, "System.Collections.dll");
        TryAddTrustedReference(references, seenPaths, "Microsoft.Extensions.Primitives.dll");
        TryAddTrustedReference(references, seenPaths, "Microsoft.Extensions.Configuration.Abstractions.dll");
        TryAddTrustedReference(references, seenPaths, "Microsoft.Extensions.DependencyInjection.Abstractions.dll");
        TryAddTrustedReference(references, seenPaths, "Microsoft.Extensions.Logging.Abstractions.dll");
        TryAddTrustedReference(references, seenPaths, "Microsoft.Extensions.Options.dll");
        TryAddTrustedReference(references, seenPaths, "Microsoft.Extensions.Hosting.Abstractions.dll");
        TryAddTrustedReference(references, seenPaths, "Microsoft.Extensions.Hosting.dll");
        TryAddTrustedReference(references, seenPaths, "Microsoft.Extensions.DependencyInjection.dll");
        TryAddTrustedReference(references, seenPaths, "Microsoft.Extensions.Configuration.dll");
        TryAddTrustedReference(references, seenPaths, "Microsoft.Extensions.Logging.dll");
    }

    /// <summary>
    /// Adds a metadata reference for the specified assembly path, deduplicating by normalized path.
    /// </summary>
    /// <param name="references">The collection of metadata references to add to.</param>
    /// <param name="seenPaths">A set tracking already processed assembly paths to prevent duplicates.</param>
    /// <param name="assemblyPath">The path to the assembly. Null or whitespace values are ignored.</param>
    internal static void TryAddReference(List<MetadataReference> references, HashSet<string> seenPaths, string? assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath))
        {
            return;
        }

        string normalized = Path.GetFullPath(assemblyPath);
        if (!File.Exists(normalized) || !seenPaths.Add(normalized))
        {
            return;
        }

        try
        {
            references.Add(MetadataReference.CreateFromFile(normalized));
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
        }
    }

    /// <summary>
    /// Resolves a trusted platform assembly by file name and adds it as a metadata reference.
    /// </summary>
    internal static void TryAddTrustedReference(List<MetadataReference> references, HashSet<string> seenPaths, string assemblyFile)
    {
        string? path = TryGetTrustedAssemblyPath(assemblyFile);
        TryAddReference(references, seenPaths, path);
    }

    /// <summary>
    /// Attempts to locate a trusted platform assembly by its file name.
    /// Searches the TRUSTED_PLATFORM_ASSEMBLIES list, runtime directory, and core library directory.
    /// </summary>
    /// <param name="assemblyFile">The file name of the assembly to locate (e.g., "System.Runtime.dll").</param>
    internal static string? TryGetTrustedAssemblyPath(string assemblyFile)
    {
        if (string.IsNullOrWhiteSpace(assemblyFile))
        {
            return null;
        }

        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpaList)
        {
            foreach (string candidate in tpaList.Split(Path.PathSeparator))
            {
                try
                {
                    if (string.Equals(Path.GetFileName(candidate), assemblyFile, StringComparison.OrdinalIgnoreCase))
                    {
                        return candidate;
                    }
                }
                catch (Exception ex) when (!ex.IsFatal())
                {
                }
            }
        }

        try
        {
            string runtimeDir = RuntimeEnvironment.GetRuntimeDirectory();
            if (!string.IsNullOrEmpty(runtimeDir))
            {
                string candidate = Path.Combine(runtimeDir, assemblyFile);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
        }

        try
        {
            string coreLibPath = typeof(object).Assembly.Location;
            if (!string.IsNullOrEmpty(coreLibPath))
            {
                string? directory = Path.GetDirectoryName(coreLibPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    string candidate = Path.Combine(directory, assemblyFile);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
        }

        return null;
    }

    /// <summary>
    /// Adds NuGet package assembly references from the project containing the document.
    /// Parses obj/project.assets.json to discover compile-time assembly paths.
    /// </summary>
    internal static int AddProjectNuGetReferences(List<MetadataReference> references, HashSet<string> seenPaths, string documentFilePath)
    {
        string? projectDir = FindProjectDirectory(documentFilePath);
        if (projectDir == null)
        {
            return 0;
        }

        // TODO(#9): NuGetReferenceCache never invalidates when project.assets.json changes
        // (e.g., after dotnet restore, package updates, or branch switches). Consider watching
        // the assets file for changes and clearing the cache entry for the affected project.
        List<string> assemblyPaths = NuGetReferenceCache.GetOrAdd(projectDir, ResolveNuGetAssemblyPaths);

        int added = 0;
        foreach (string assemblyPath in assemblyPaths)
        {
            int before = references.Count;
            TryAddReference(references, seenPaths, assemblyPath);
            if (references.Count > before)
            {
                added++;
            }
        }

        return added;
    }

    /// <summary>
    /// Walks up from a file path to find the directory containing a .csproj file.
    /// </summary>
    internal static string? FindProjectDirectory(string filePath)
    {
        string? directory = Path.GetDirectoryName(filePath);
        while (!string.IsNullOrEmpty(directory))
        {
            try
            {
                if (Directory.EnumerateFiles(directory, "*.csproj").Any())
                {
                    return directory;
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                // Skip directories we can't read (permissions, network errors, etc.)
            }

            directory = Path.GetDirectoryName(directory);
        }

        return null;
    }

    /// <summary>
    /// Parses obj/project.assets.json to resolve NuGet package compile-time assembly paths.
    /// </summary>
    private static List<string> ResolveNuGetAssemblyPaths(string projectDirectory)
    {
        var result = new List<string>();
        string assetsPath = Path.Combine(projectDirectory, "obj", "project.assets.json");
        if (!File.Exists(assetsPath))
        {
            return result;
        }

        try
        {
            using FileStream stream = File.OpenRead(assetsPath);
            using JsonDocument doc = JsonDocument.Parse(stream);
            JsonElement root = doc.RootElement;

            // Get package folders (NuGet cache locations)
            var packageFolders = new List<string>();
            if (root.TryGetProperty("packageFolders", out JsonElement foldersElement))
            {
                foreach (JsonProperty folder in foldersElement.EnumerateObject())
                {
                    packageFolders.Add(folder.Name);
                }
            }

            if (packageFolders.Count == 0)
            {
                return result;
            }

            // Get libraries for path lookup
            var libraryPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("libraries", out JsonElement librariesElement))
            {
                foreach (JsonProperty lib in librariesElement.EnumerateObject())
                {
                    if (lib.Value.TryGetProperty("path", out JsonElement pathProp))
                    {
                        libraryPaths[lib.Name] = pathProp.GetString() ?? string.Empty;
                    }
                }
            }

            // Get targets - process first target framework
            if (root.TryGetProperty("targets", out JsonElement targetsElement))
            {
                foreach (JsonProperty target in targetsElement.EnumerateObject())
                {
                    foreach (JsonProperty package in target.Value.EnumerateObject())
                    {
                        if (!package.Value.TryGetProperty("compile", out JsonElement compileElement))
                        {
                            continue;
                        }

                        if (!libraryPaths.TryGetValue(package.Name, out string? libraryPath))
                        {
                            continue;
                        }

                        foreach (JsonProperty compileItem in compileElement.EnumerateObject())
                        {
                            string relativePath = compileItem.Name;
                            if (!relativePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            foreach (string packageFolder in packageFolders)
                            {
                                string fullPath = Path.Combine(packageFolder, libraryPath, relativePath);
                                if (File.Exists(fullPath))
                                {
                                    result.Add(fullPath);
                                    break;
                                }
                            }
                        }
                    }

                    // Only process first target framework
                    break;
                }
            }
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
        }

        return result;
    }

    private void AddSdkReferences(List<MetadataReference> references, HashSet<string> seenPaths)
    {
        if (this.sdkIndex == null)
        {
            return;
        }

        foreach (string assemblyPath in this.sdkIndex.AssemblyPaths)
        {
            try
            {
                if (File.Exists(assemblyPath))
                {
                    TryAddReference(references, seenPaths, assemblyPath);
                }
            }
            catch (Exception ex) when (!ex.IsFatal())
            {
                Console.Error.WriteLine($"[CompilationService] Failed to load assembly {assemblyPath}: {ex.Message}");
            }
        }
    }
}
