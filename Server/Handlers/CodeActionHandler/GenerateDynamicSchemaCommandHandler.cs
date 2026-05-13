using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

using MediatR;

using Newtonsoft.Json.Linq;

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;

using SdkLspServer.Services.Api;
using SdkLspServer.Services.Connections;
using SdkLspServer.Services.Telemetry;

namespace SdkLspServer.Handlers.CodeActionHandler;

/// <summary>
/// Handles the "sdklsp.generateDynamicSchemaClass" command triggered when the user
/// clicks the Code Action. Does the schema fetch + class generation + writes the file
/// to disk and updates the source document to use the generated type.
/// </summary>
internal class GenerateDynamicSchemaCommandHandler : ExecuteCommandHandlerBase
{
    /// <summary>
    /// The command identifier used in CodeAction.Command and registered with the LSP server.
    /// </summary>
    public const string CommandName = "sdklsp.generateDynamicSchemaClass";

    private readonly BufferManager bufferManager;
    private readonly DynamicSchemaFetcher schemaFetcher;
    private readonly ITelemetryService telemetry;

    public GenerateDynamicSchemaCommandHandler(
        BufferManager bufferManager,
        ConnectionsService connectionsService,
        ApiService apiService,
        ITelemetryService telemetryService)
    {
        this.bufferManager = bufferManager;
        this.schemaFetcher = new DynamicSchemaFetcher(connectionsService, apiService);
        this.telemetry = telemetryService;
    }

    [RequiresAssemblyFiles]
    public override async Task<Unit> Handle(ExecuteCommandParams request, CancellationToken cancellationToken)
    {
        if (!string.Equals(request.Command, CommandName, StringComparison.Ordinal))
        {
            return Unit.Value;
        }

        try
        {
            // Deserialize the command arguments
            var args = request.Arguments?
                .FirstOrDefault()?
                .ToObject<GenerateCommandArgs>();

            if (args == null)
            {
                await Console.Error.WriteLineAsync("[GenerateCommand] No arguments provided");
                return Unit.Value;
            }

            await Console.Error.WriteLineAsync(
                $"[GenerateCommand] User clicked: generating {args.ClassName} for {args.ConnectorName}:{args.OperationId}");

            // Fetch the schema from the connector
            JsonElement? schema = await schemaFetcher.FetchSchemaAsync(
                args.ConnectorName,
                args.OperationId,
                cancellationToken);

            if (schema == null)
            {
                await Console.Error.WriteLineAsync(
                    $"[GenerateCommand] Schema unavailable for {args.TypeName}. Connection may need re-authorization.");
                return Unit.Value;
            }

            // Read the source document — try buffer first, then disk
            string? documentText = bufferManager.GetBuffer(args.DocumentUri);
            string? sourceFilePath = null;

            if (string.IsNullOrEmpty(documentText))
            {
                var docUri = DocumentUri.Parse(args.DocumentUri);
                if (docUri.Scheme == "file")
                {
                    sourceFilePath = docUri.GetFileSystemPath();
                    if (File.Exists(sourceFilePath))
                    {
                        documentText = await File.ReadAllTextAsync(sourceFilePath, cancellationToken);
                    }
                }
            }
            else
            {
                var docUri = DocumentUri.Parse(args.DocumentUri);
                if (docUri.Scheme == "file")
                {
                    sourceFilePath = docUri.GetFileSystemPath();
                }
            }

            // Determine namespace from the source document
            string targetNamespace = !string.IsNullOrEmpty(documentText)
                ? DetectNamespace(documentText) ?? "GeneratedTypes"
                : "GeneratedTypes";

            // Generate the class
            string generatedCode = SchemaToClassGenerator.GenerateClass(
                className: args.ClassName,
                baseClassName: args.TypeName,
                baseClassNamespace: args.TypeNamespace ?? string.Empty,
                schema: schema.Value,
                targetNamespace: targetNamespace);

            // Determine target file path
            string targetFilePath;
            if (!string.IsNullOrEmpty(sourceFilePath))
            {
                targetFilePath = Path.Combine(
                    Path.GetDirectoryName(sourceFilePath)!,
                    $"{args.ClassName}.cs");
            }
            else
            {
                targetFilePath = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    $"{args.ClassName}.cs");
            }

            // Write the generated class file
            await File.WriteAllTextAsync(targetFilePath, generatedCode, cancellationToken);

            await Console.Error.WriteLineAsync(
                $"[GenerateCommand] Created {targetFilePath} ({generatedCode.Split('\n').Length} lines)");

            // Update the source document: replace `new ...DynamicXyzRequest()` with `new ClassName()`
            if (!string.IsNullOrEmpty(documentText) && !string.IsNullOrEmpty(sourceFilePath))
            {
                string updatedText = ApplySourceCodeUpdate(
                    documentText,
                    args.TypeName,
                    args.TypeNamespace,
                    args.ClassName);

                if (!string.Equals(updatedText, documentText, StringComparison.Ordinal))
                {
                    await File.WriteAllTextAsync(sourceFilePath, updatedText, cancellationToken);
                    await Console.Error.WriteLineAsync(
                        $"[GenerateCommand] Updated {sourceFilePath}: replaced 'new ...{args.TypeName}()' with 'new {args.ClassName}()'");
                }
            }

            telemetry.TrackEvent("CodeAction_DynamicSchema_Generated", new Dictionary<string, string>
            {
                { "Connector", args.ConnectorName },
                { "OperationId", args.OperationId },
                { "GeneratedClass", args.ClassName },
                { "FilePath", Path.GetFileName(targetFilePath) },
            });
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
            await Console.Error.WriteLineAsync($"[GenerateCommand] Error: {ex.Message}\n{ex.StackTrace}");
            telemetry.TrackException(ex, new Dictionary<string, string>
            {
                { "Handler", "GenerateDynamicSchemaCommand" },
            });
        }

        return Unit.Value;
    }

    protected override ExecuteCommandRegistrationOptions CreateRegistrationOptions(
        ExecuteCommandCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new ExecuteCommandRegistrationOptions
        {
            Commands = new Container<string>(CommandName),
        };
    }

    /// <summary>
    /// Replaces <c>new DynamicXyzRequest()</c> (or its fully-qualified form) with
    /// <c>new GeneratedClassName()</c> in the source document. Does not modify
    /// AdditionalProperties access — those still work via inheritance.
    /// </summary>
    /// <returns>The updated document text with dynamic type instantiations replaced.</returns>
    internal static string ApplySourceCodeUpdate(
        string documentText,
        string dynamicTypeName,
        string? typeNamespace,
        string generatedClassName)
    {
        string result = documentText;

        // Replace fully-qualified form: new Azure.Connectors.Sdk.Teams.DynamicPostMessageRequest()
        if (!string.IsNullOrEmpty(typeNamespace))
        {
            string fqPattern = $"new {typeNamespace}.{dynamicTypeName}()";
            result = result.Replace(fqPattern, $"new {generatedClassName}()", StringComparison.Ordinal);
        }

        // Replace short form: new DynamicPostMessageRequest()
        string shortPattern = $"new {dynamicTypeName}()";
        result = result.Replace(shortPattern, $"new {generatedClassName}()", StringComparison.Ordinal);

        return result;
    }

    private static string? DetectNamespace(string documentText)
    {
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(documentText);
        var root = (Microsoft.CodeAnalysis.CSharp.Syntax.CompilationUnitSyntax)tree.GetRoot();

        var fileScopedNs = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.FileScopedNamespaceDeclarationSyntax>()
            .FirstOrDefault();

        if (fileScopedNs != null)
        {
            return fileScopedNs.Name.ToString();
        }

        var blockNs = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.NamespaceDeclarationSyntax>()
            .FirstOrDefault();

        return blockNs?.Name.ToString();
    }

    /// <summary>
    /// Arguments passed via the Command when the user clicks the Code Action.
    /// </summary>
    public sealed class GenerateCommandArgs
    {
        public string TypeName { get; set; } = string.Empty;

        public string? TypeNamespace { get; set; }

        public string OperationId { get; set; } = string.Empty;

        public string ConnectorName { get; set; } = string.Empty;

        public string ClassName { get; set; } = string.Empty;

        public string DocumentUri { get; set; } = string.Empty;
    }
}
