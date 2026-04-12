using System.Reflection;

using MediatR;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;

using SdkLspServer.Diagnostics;

namespace SdkLspServer.Handlers;

internal class TextDocumentSyncHandler(ILanguageServerFacade router, BufferManager bufferManager, SdkIndex? sdkIndex, DiagnosticPublisher diagnosticPublisher)
    : IDidChangeTextDocumentHandler
{
    private readonly ILanguageServerFacade router = router;
    private readonly BufferManager bufferManager = bufferManager;
    private readonly SdkIndex? sdkIndex = sdkIndex;
    private readonly DiagnosticPublisher diagnosticPublisher = diagnosticPublisher;

    public TextDocumentSyncKind Change { get; } = TextDocumentSyncKind.Full;

    public TextDocumentSelector DocumentSelector { get; } = new TextDocumentSelector(
        new TextDocumentFilter()
        {
            Pattern = "**/*.cs",
        });

    public static TextDocumentAttributes GetTextDocumentAttributes(Uri uri)
    {
        return new TextDocumentAttributes(uri, "csharp");
    }

    public TextDocumentChangeRegistrationOptions GetRegistrationOptions(TextSynchronizationCapability capability, ClientCapabilities clientCapabilities)
    {
        return new TextDocumentChangeRegistrationOptions()
        {
            DocumentSelector = DocumentSelector,
            SyncKind = Change,
        };
    }

    public Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken cancellationToken)
    {
        string documentPath = request.TextDocument.Uri.ToString();
        string? text = request.ContentChanges.FirstOrDefault()?.Text;

        bufferManager.UpdateBuffer(documentPath, text ?? string.Empty);

        // If the change likely introduced SDK usage, ask client to refresh CodeLens
        if (!string.IsNullOrEmpty(text) && MightContainSdkUsage(text))
        {
            TryRequestCodeLensRefresh(router);
        }

        // Schedule debounced diagnostics on text change
        this.diagnosticPublisher.ScheduleDebouncedPublish(
            request.TextDocument.Uri,
            text ?? string.Empty);

        return Unit.Task;
    }

#pragma warning disable VSTHRD200 // LSP protocol handler names are defined by the framework
    public async Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken cancellationToken)
    {
        bufferManager.UpdateBuffer(request.TextDocument.Uri.ToString(), request.TextDocument.Text);
        if (!string.IsNullOrEmpty(request.TextDocument.Text) && MightContainSdkUsage(request.TextDocument.Text))
        {
            TryRequestCodeLensRefresh(router);
        }

        // Publish diagnostics immediately on document open
        await this.diagnosticPublisher
            .PublishDiagnosticsAsync(request.TextDocument.Uri, request.TextDocument.Text, cancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);

        return Unit.Value;
    }

    public async Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken cancellationToken)
    {
        // On save, trigger a refresh to ensure lenses appear immediately
        TryRequestCodeLensRefresh(router);

        // Publish diagnostics immediately on save
        string? text = this.bufferManager.GetBuffer(request.TextDocument.Uri.ToString());
        if (text != null)
        {
            await this.diagnosticPublisher
                .PublishDiagnosticsAsync(request.TextDocument.Uri, text, cancellationToken)
                .ConfigureAwait(continueOnCapturedContext: false);
        }

        return Unit.Value;
    }

    public Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken cancellationToken)
    {
        // Clear diagnostics and release buffer memory when a document is closed
        this.diagnosticPublisher.ClearDiagnostics(request.TextDocument.Uri);
        this.bufferManager.RemoveBuffer(request.TextDocument.Uri.ToString());
        return Unit.Task;
    }
#pragma warning restore VSTHRD200

    private static bool ContainsGetManagedConnectors(ExpressionSyntax expression)
    {
        foreach (InvocationExpressionSyntax inv in expression.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
        {
            if (string.Equals(GetInvokedSimpleName(inv.Expression), "GetManagedConnectors", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void TryRequestCodeLensRefresh(ILanguageServerFacade router)
    {
        try
        {
            // Best-effort: try common shapes to send the standard LSP request
            const string methodName = "workspace/codeLens/refresh";

            // 1) Direct SendNotification(string)
            MethodInfo? sendNotif = router.GetType().GetMethod("SendNotification", [typeof(string)]);
            if (sendNotif != null)
            {
                sendNotif.Invoke(router, new object[] { methodName });
                return;
            }

            // 2) Client.SendNotification(string)
            PropertyInfo? clientProp = router.GetType().GetProperty("Client", BindingFlags.Public | BindingFlags.Instance);
            object? client = clientProp?.GetValue(router);
            if (client != null)
            {
                MethodInfo? clientSend = client.GetType().GetMethod("SendNotification", [typeof(string)]);
                if (clientSend != null)
                {
                    clientSend.Invoke(client, new object[] { methodName });
                    return;
                }
            }

            // 3) Workspace.RefreshCodeLens() if available in this version
            PropertyInfo? wsProp = router.GetType().GetProperty("Workspace", BindingFlags.Public | BindingFlags.Instance);
            object? workspace = wsProp?.GetValue(router);
            if (workspace != null)
            {
                MethodInfo? refresh = workspace.GetType().GetMethod("RefreshCodeLens", Type.EmptyTypes);
                if (refresh != null)
                {
                    refresh.Invoke(workspace, []);
                    return;
                }

                // 4) Workspace.SendNotification(string)
                MethodInfo? wsSendNotif = workspace.GetType().GetMethod("SendNotification", [typeof(string)]);
                if (wsSendNotif != null)
                {
                    wsSendNotif.Invoke(workspace, new object[] { methodName });
                    return;
                }
            }
        }
        catch
        {
            // Swallow – refresh is best-effort and optional
        }
    }

    private static string GetInvokedSimpleName(ExpressionSyntax expr)
    {
        return expr switch
        {
            IdentifierNameSyntax id => id.Identifier.ValueText,
            MemberAccessExpressionSyntax ma when ma.Name is IdentifierNameSyntax id2 => id2.Identifier.ValueText,
            _ => string.Empty,
        };
    }

    private bool MightContainSdkUsage(string text)
    {
        // Prefer a fast, semantic check via Roslyn. If that fails, fall back to a quick substring heuristic.
        try
        {
            if (MightContainSdkUsageRoslyn(text))
            {
                return true;
            }
        }
        catch
        {
            // ignore and try heuristic below
        }

        try
        {
            return !string.IsNullOrEmpty(text) && (
                text.Contains("GetManagedConnectors", StringComparison.Ordinal)
                || text.Contains("ConnectorOperation", StringComparison.Ordinal)
                || text.Contains("ConnectionName", StringComparison.Ordinal));
        }
        catch
        {
            return false;
        }
    }

    private bool MightContainSdkUsageRoslyn(string documentText)
    {
        // Parse
        SyntaxTree tree = CSharpSyntaxTree.ParseText(documentText);
        CompilationUnitSyntax root = tree.GetCompilationUnitRoot();

        // Quick syntactic short-circuit: if there are no invocations or member accesses, skip
        if (!root.DescendantNodes().Any(n => n is InvocationExpressionSyntax || n is MemberAccessExpressionSyntax))
        {
            return false;
        }

        // Create minimal compilation
        var references = new List<MetadataReference>();

        // Use AppContext.BaseDirectory for single-file deployment compatibility
        string baseDir = AppContext.BaseDirectory;

        try
        {
            // Try to add basic runtime references
            string runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location) ?? baseDir;

            // Add core runtime references if available
            string[] coreRefs =
            [
                Path.Combine(runtimeDir, "System.Runtime.dll"),
                Path.Combine(runtimeDir, "System.Console.dll"),
                Path.Combine(runtimeDir, "System.Linq.dll"),
                Path.Combine(runtimeDir, "mscorlib.dll"),
                Path.Combine(runtimeDir, "System.Private.CoreLib.dll"),
            ];

            foreach (string refPath in coreRefs)
            {
                if (File.Exists(refPath))
                {
                    references.Add(MetadataReference.CreateFromFile(refPath));
                }
            }

            // Fallback: use basic references from loaded assemblies if locations are available
            Assembly[] fallbackAssemblies = [typeof(string).Assembly, typeof(Console).Assembly, typeof(Enumerable).Assembly];
            foreach (Assembly? assembly in fallbackAssemblies)
            {
                if (!string.IsNullOrEmpty(assembly.Location) && File.Exists(assembly.Location))
                {
                    references.Add(MetadataReference.CreateFromFile(assembly.Location));
                }
            }
        }
        catch
        {
            // If all else fails, continue with empty references - Roslyn can still work for basic syntax analysis
        }

        if (sdkIndex != null)
        {
            foreach (string assemblyPath in sdkIndex.AssemblyPaths)
            {
                try
                {
                    if (File.Exists(assemblyPath))
                    {
                        references.Add(MetadataReference.CreateFromFile(assemblyPath));
                    }
                }
                catch
                {
                }
            }
        }

        var compilation = CSharpCompilation.Create(
            "CodeLensRefreshScan",
            [tree],
            references);

        SemanticModel semantic = compilation.GetSemanticModel(tree);

        // 1) Detect direct call to GetManagedConnectors()
        foreach (InvocationExpressionSyntax inv in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            string name = GetInvokedSimpleName(inv.Expression);
            if (string.Equals(name, "GetManagedConnectors", StringComparison.Ordinal))
            {
                return true;
            }

            SymbolInfo si = semantic.GetSymbolInfo(inv);
            IMethodSymbol? method = si.Symbol as IMethodSymbol ?? si.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
            if (method is null)
            {
                continue;
            }

            if (IsSymbolFromSdk(method))
            {
                return true;
            }

            if (method.GetAttributes().Any(a => string.Equals(a.AttributeClass?.Name, "ConnectorOperationAttribute", StringComparison.Ordinal)
                                             || string.Equals(a.AttributeClass?.Name, "ConnectorOperation", StringComparison.Ordinal)))
            {
                return true;
            }
        }

        // 2) Detect member access chains that include GetManagedConnectors() (e.g., GetManagedConnectors().Outlook.CurrentWeatherAsync)
        foreach (MemberAccessExpressionSyntax ma in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            if (ContainsGetManagedConnectors(ma.Expression))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsSymbolFromSdk(ISymbol symbol)
    {
        if (sdkIndex is null)
        {
            return false;
        }

        string? assemblyName = symbol.ContainingAssembly?.Name;
        return !string.IsNullOrEmpty(assemblyName)
            && sdkIndex.AssemblyPaths.Any(path =>
            Path.GetFileNameWithoutExtension(path)
                .Equals(assemblyName, StringComparison.OrdinalIgnoreCase));
    }
}
