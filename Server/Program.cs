using System.Diagnostics;

using Microsoft.Extensions.DependencyInjection;

using OmniSharp.Extensions.LanguageServer.Protocol.Window;
using OmniSharp.Extensions.LanguageServer.Server;

using SdkLspServer.Handlers;
using SdkLspServer.Handlers.CodeActionHandler;
using SdkLspServer.Handlers.CompletionHandler;
using SdkLspServer.Handlers.HoverHandler;
using SdkLspServer.Services.Api;
using SdkLspServer.Services.CodeLens;
using SdkLspServer.Services.Connections;
using SdkLspServer.Services.Telemetry;

using ZiggyCreatures.Caching.Fusion;

namespace SdkLspServer;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        ITelemetryService? telemetryService = null;

        try
        {
            // Log server version and location at startup for debugging stale-binary issues
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            string version = assembly.GetName().Version?.ToString() ?? "unknown";
            string location = assembly.Location;
            await Console.Error.WriteLineAsync($"[SdkLspServer] Version {version} from {location}");

            // Support for debugger attachment at startup
            if (args.Contains("--wait-for-debugger"))
            {
                await Console.Error.WriteLineAsync($"[SdkLspServer] Waiting for debugger to attach... (PID: {Environment.ProcessId})");
                while (!Debugger.IsAttached)
                {
                    await Task.Delay(100);
                }

                await Console.Error.WriteLineAsync("[SdkLspServer] Debugger attached, continuing...");
            }

            // Determine the SDK path from command line or local SDK folder
            SdkPathResult sdkResolution = ResolveSdkPath(args);
            string sdkSource = sdkResolution.Source;

            // Index the provided SDK. When --sdk-assembly is used, index directly from DLL(s)
            // without nupkg extraction. Otherwise, extract and index the nupkg.
            SdkIndex? index = null;
            if (sdkResolution.AssemblyPaths != null)
            {
                index = await SdkIndex.TryCreateFromAssembliesAsync(sdkResolution.AssemblyPaths);
            }
            else if (!string.IsNullOrEmpty(sdkResolution.NupkgPath))
            {
                index = await SdkIndex.TryCreateAsync(sdkResolution.NupkgPath);
            }

            if (index is null)
            {
                await Console.Error.WriteLineAsync($"[SdkLspServer] Could not load SDK from: {sdkResolution.DisplayPath}");
            }

            // Create config instance that will be populated from initializationOptions
            ApiServiceConfig apiConfig = new();

            // Create CodeLens config for configurable command names
            CodeLensConfig codeLensConfig = new();

            // Create connections service for runtime updates
            ConnectionsService connectionsService = new();

            LanguageServer server = await LanguageServer.From((options) =>
            {
                options
                    .WithInput(Console.OpenStandardInput())
                    .WithOutput(Console.OpenStandardOutput())
                    .WithServices(services =>
                    {
                        ConfigureServices(services);

                        // Make SdkIndex available for dependency injection
                        if (index != null)
                        {
                            services.AddSingleton(index);
                        }
                        else
                        {
                            services.AddSingleton(typeof(SdkIndex), provider => null!);
                        }

                        // Register ConnectionsService as singleton
                        services.AddSingleton(connectionsService);

                        // Make ApiServiceConfig available for dependency injection
                        services.AddSingleton(apiConfig);

                        // Make CodeLensConfig available for dependency injection
                        services.AddSingleton(codeLensConfig);
                    })

                     // Register custom notification handler for API config updates
                     .OnNotification("custom/updateApiConfig", async (ApiServiceConfig? updateConfig) =>
                     {
                         try
                         {
                             apiConfig.UpdateFrom(updateConfig);
                         }
                         catch (Exception ex)
                         {
                             await Console.Error.WriteLineAsync($"[SdkLspServer] ❌ Failed to update apiConfig: {ex.Message}");
                         }
                     })

                     // Register custom notification handler for connections updates
                     .OnNotification("custom/updateConnections", async (ConnectionsConfig? updatedConnections) =>
                     {
                         try
                         {
                             connectionsService.UpdateConnections(updatedConnections);
                             int count = connectionsService.GetConnectionCount();
                             await Console.Error.WriteLineAsync($"[SdkLspServer] ✅ Connections updated via notification: {count} connection(s)");
                         }
                         catch (Exception ex)
                         {
                             await Console.Error.WriteLineAsync($"[SdkLspServer] ❌ Failed to update connections: {ex.Message}");
                         }
                     })

                    // Additional handlers for enhanced SDK support.
                    .OnInitialize(async (s, request, ct) =>
                    {
                        // Get telemetry service from DI
                        var telemetryService = s.Services.GetService(typeof(ITelemetryService)) as ITelemetryService;

                        // 🎯 Parse initializationOptions
                        try
                        {
                            InitializationOptionsWrapper? initOptions = System.Text.Json.JsonSerializer.Deserialize<InitializationOptionsWrapper>(
                                request.InitializationOptions?.ToString() ?? "{}");

                            // Handle API Config
                            if (initOptions?.ApiConfig != null)
                            {
                                apiConfig.BaseUrl = initOptions.ApiConfig.BaseUrl;
                                apiConfig.SubscriptionId = initOptions.ApiConfig.SubscriptionId;
                                apiConfig.ResourceGroup = initOptions.ApiConfig.ResourceGroup;
                                apiConfig.BearerToken = initOptions.ApiConfig.BearerToken;
                            }

                            // Handle CodeLens Config
                            if (initOptions?.CodeLens != null)
                            {
                                codeLensConfig.UpdateFrom(initOptions.CodeLens);
                            }

                            // Handle Connections Config
                            if (initOptions?.Connections != null)
                            {
                                connectionsService.UpdateConnections(initOptions.Connections);
                                int count = connectionsService.GetConnectionCount();
                                await Console.Error.WriteLineAsync($"[SdkLspServer] ✅ Connections loaded from initializationOptions: {count} connection(s)");
                                s.Window.ShowInfo($"✅ Loaded {count} connection(s)");
                            }
                            else
                            {
                                await Console.Error.WriteLineAsync("[SdkLspServer] ⚠️  No connections provided in initializationOptions");
                            }

                            // Handle Telemetry Config
                            if (telemetryService != null && initOptions?.Telemetry != null)
                            {
                                telemetryService.Initialize(initOptions.Telemetry);

                                // Track initialization event
                                telemetryService.TrackEvent("LSP_Server_Initialized", new Dictionary<string, string>
                                {
                                { "SdkLoaded", (index != null).ToString() },
                                { "ConnectionsCount", connectionsService.GetConnectionCount().ToString() },
                                { "HasApiConfig", (initOptions.ApiConfig != null).ToString() },
                                });
                            }
                            else if (telemetryService != null)
                            {
                                await Console.Error.WriteLineAsync("[SdkLspServer] ⚠️  No telemetry config provided in initializationOptions");
                            }
                        }
                        catch (Exception ex)
                        {
                            telemetryService?.TrackException(ex, new Dictionary<string, string>
                            {
                            { "Phase", "Initialization" },
                            });
                        }

                        // Report whether the SDK index was loaded successfully.
                        if (index is null)
                        {
                            s.Window.ShowError($"SDK failed to load (source: {sdkSource}). Provide a .nupkg path via --sdk /path/file.nupkg or a DLL path via --sdk-assembly /path/file.dll.");

                            telemetryService?.TrackEvent("SDK_Load_Failed", new Dictionary<string, string>
                            {
                            { "Source", sdkSource },
                            });
                        }
                        else
                        {
                            telemetryService?.TrackEvent("SDK_Load_Success", new Dictionary<string, string>
                            {
                            { "Source", sdkSource },
                            { "Summary", index.Summary ?? "unknown" },
                            });
                        }

                        s.Window.ShowInfo($"Loaded SDK: {index?.Summary} (source: {sdkSource})");
                    })
                    .WithHandler<TextDocumentSyncHandler>()
                    .WithHandler<HoverHandler>()
                    .WithHandler<CodeLensHandler>()
                    .WithHandler<CompletionHandler>()
                    .WithHandler<DynamicSchemaCodeActionHandler>()
                    .WithHandler<GenerateDynamicSchemaCommandHandler>();
            });

            // Track server started event
            telemetryService = server.Services.GetService(typeof(ITelemetryService)) as ITelemetryService;
            telemetryService?.TrackEvent("LSP_Server_Started");

            await Console.Error.WriteLineAsync("[SdkLspServer] Server started successfully");

            // Wait for the server to exit. This call blocks until the client shuts
            // down the server.
            await server.WaitForExit;

            // Track server shutdown and flush telemetry
            telemetryService?.TrackEvent("LSP_Server_Shutdown");
            telemetryService?.Flush();
            await Task.Delay(1000); // Give telemetry time to flush

            await Console.Error.WriteLineAsync("[SdkLspServer] Server shutdown complete");
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[SdkLspServer] ❌ Fatal error: {ex.Message}");
            await Console.Error.WriteLineAsync($"[SdkLspServer] Stack trace: {ex.StackTrace}");

            telemetryService?.TrackException(ex, new Dictionary<string, string>
            {
                { "Phase", "Startup" },
                { "Fatal", "true" },
            });
            telemetryService?.Flush();

            throw;
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<BufferManager>();

        // Add HttpClient for making dynamic API calls
        services.AddHttpClient("SdkLspClient", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("User-Agent", "SdkLspServer/1.0");
        });

        // Register FusionCache with default options optimized for LSP scenarios
        services.AddFusionCache()
            .WithDefaultEntryOptions(options =>
            {
                // Default cache duration: 5 minutes
                options.Duration = TimeSpan.FromMinutes(5);

                // Enable fail-safe mode: serve stale data for up to 1 hour if API fails
                options.IsFailSafeEnabled = true;
                options.FailSafeMaxDuration = TimeSpan.FromHours(1);
                options.FailSafeThrottleDuration = TimeSpan.FromSeconds(30);

                // Factory timeouts: 10 seconds for API calls
                options.FactorySoftTimeout = TimeSpan.FromSeconds(10);
                options.FactoryHardTimeout = TimeSpan.FromSeconds(15);
                options.AllowTimedOutFactoryBackgroundCompletion = false;
            });

        // Register ApiService for handlers to use
        services.AddSingleton<ApiService>();

        // Register TelemetryService for telemetry tracking
        services.AddSingleton<ITelemetryService, TelemetryService>();

        // Register shared LSPStore for cross-handler communication (includes DynamicData slice)
        services.AddSingleton<Store.LSPStore>();
    }

    /// <summary>
    /// Resolved SDK path information returned by <see cref="ResolveSdkPath"/>.
    /// </summary>
    private sealed class SdkPathResult
    {
        /// <summary>Gets the nupkg path (mutually exclusive with AssemblyPaths).</summary>
        public string? NupkgPath { get; init; }

        /// <summary>Gets the assembly DLL paths (mutually exclusive with NupkgPath).</summary>
        public string[]? AssemblyPaths { get; init; }

        /// <summary>Gets a description of where the SDK was found.</summary>
        public string Source { get; init; } = "none";

        public bool IsAssembly => AssemblyPaths != null;

        public string DisplayPath => NupkgPath ?? (AssemblyPaths != null ? string.Join(", ", AssemblyPaths) : "(none)");
    }

    /// <summary>
    /// Parse command-line arguments to determine the path to the SDK
    /// .nupkg file or assembly DLL, falling back to a local SDK/ folder search.
    /// </summary>
    private static SdkPathResult ResolveSdkPath(string[] args)
    {
        // CLI: --sdk /path/to/sdk.nupkg or --sdk=/path/to/sdk.nupkg
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--sdk" && i + 1 < args.Length)
            {
                return new SdkPathResult { NupkgPath = args[i + 1], Source = "arg" };
            }

            if (args[i].StartsWith("--sdk=", StringComparison.Ordinal))
            {
                string nupkgValue = args[i]["--sdk=".Length..];
                if (string.IsNullOrEmpty(nupkgValue))
                {
                    return new SdkPathResult { Source = "arg-missing" };
                }

                return new SdkPathResult { NupkgPath = nupkgValue, Source = "arg" };
            }

            // --sdk-assembly /path/to/Assembly1.dll /path/to/Assembly2.dll ...
            if (args[i] == "--sdk-assembly" && i + 1 < args.Length)
            {
                // Collect all subsequent non-flag arguments as assembly paths
                var paths = new List<string>();
                for (int j = i + 1; j < args.Length && !args[j].StartsWith("--", StringComparison.Ordinal); j++)
                {
                    paths.Add(args[j]);
                }

                if (paths.Count == 0)
                {
                    return new SdkPathResult { Source = "arg-assembly-missing" };
                }

                return new SdkPathResult { AssemblyPaths = paths.ToArray(), Source = "arg-assembly" };
            }

            if (args[i].StartsWith("--sdk-assembly=", StringComparison.Ordinal))
            {
                string assemblyValue = args[i]["--sdk-assembly=".Length..];
                if (string.IsNullOrEmpty(assemblyValue))
                {
                    return new SdkPathResult { Source = "arg-assembly-missing" };
                }

                return new SdkPathResult { AssemblyPaths = [assemblyValue], Source = "arg-assembly" };
            }
        }

        // Fallback: search for a .nupkg inside an SDK folder up the tree
        string? candidate = TryFindNupkgInSdkFolder();
        return !string.IsNullOrWhiteSpace(candidate)
            ? new SdkPathResult { NupkgPath = candidate, Source = "sdk-folder" }
            : new SdkPathResult { Source = "none" };
    }

    private static string? TryFindNupkgInSdkFolder()
    {
        try
        {
            // Start from current working directory and walk up to root
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null)
            {
                var sdkDir = new DirectoryInfo(Path.Combine(dir.FullName, "SDK"));
                if (sdkDir.Exists)
                {
                    FileInfo[] nupkgs =
                    [
                        .. sdkDir.GetFiles("*.nupkg", SearchOption.TopDirectoryOnly)
                                                .OrderByDescending(f => f.LastWriteTimeUtc),
                    ];
                    if (nupkgs.Length > 0)
                    {
                        return nupkgs[0].FullName;
                    }
                }

                dir = dir.Parent;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}
