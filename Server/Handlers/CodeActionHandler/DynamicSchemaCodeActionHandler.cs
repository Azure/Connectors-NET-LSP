using System.Diagnostics.CodeAnalysis;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using Newtonsoft.Json.Linq;

using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

using SdkLspServer.Services.Telemetry;

namespace SdkLspServer.Handlers.CodeActionHandler;

/// <summary>
/// Provides a Code Action (lightbulb) when the cursor is on a [DynamicSchema] type.
/// Detection is fast (syntax + cached SRM metadata). No schema fetch or code generation
/// happens here — that's deferred to GenerateDynamicSchemaCommandHandler when the user clicks.
/// </summary>
public class DynamicSchemaCodeActionHandler(
    SdkIndex? sdkIndex,
    BufferManager bufferManager,
    ITelemetryService telemetryService) : CodeActionHandlerBase
{
    private readonly SdkIndex? sdkIndex = sdkIndex;
    private readonly BufferManager bufferManager = bufferManager;
    private readonly ITelemetryService telemetry = telemetryService;

    /// <summary>
    /// Cached mapping of type name → [DynamicSchema] operationId, built eagerly at construction
    /// using System.Reflection.Metadata (raw IL reading, ~5ms). Never blocks request handling.
    /// </summary>
    private readonly Dictionary<string, string> dynamicSchemaOperations = BuildCacheFromSdkIndex(sdkIndex);

    private readonly TextDocumentSelector documentSelector = new(
        new TextDocumentFilter { Pattern = "**/*.cs" });

    [RequiresAssemblyFiles]
    public override async Task<CommandOrCodeActionContainer?> Handle(CodeActionParams request, CancellationToken cancellationToken)
    {
        try
        {
            string documentPath = request.TextDocument.Uri.ToString();
            string? documentText = bufferManager.GetBuffer(documentPath);

            if (string.IsNullOrEmpty(documentText))
            {
                if (request.TextDocument.Uri.Scheme == "file")
                {
                    try
                    {
                        string filePath = request.TextDocument.Uri.GetFileSystemPath();
                        documentText = await File.ReadAllTextAsync(filePath, cancellationToken);
                    }
                    catch
                    {
                        return new CommandOrCodeActionContainer();
                    }
                }
                else
                {
                    return new CommandOrCodeActionContainer();
                }
            }

            DynamicSchemaTypeInfo? typeInfo = DetectDynamicSchemaType(documentText, request.Range, cancellationToken);
            if (typeInfo == null)
            {
                return new CommandOrCodeActionContainer();
            }

            string suggestedClassName = GenerateClassName(typeInfo.TypeName);

            // Return a Command-based CodeAction — no schema fetch, no code generation.
            // The actual work happens in GenerateDynamicSchemaCommandHandler when clicked.
            var codeAction = new CodeAction
            {
                Title = $"Generate typed class '{suggestedClassName}' from dynamic schema",
                Kind = CodeActionKind.Refactor,
                Command = new Command
                {
                    Name = GenerateDynamicSchemaCommandHandler.CommandName,
                    Title = $"Generate {suggestedClassName}",
                    Arguments = new JArray(JToken.FromObject(
                        new GenerateDynamicSchemaCommandHandler.GenerateCommandArgs
                        {
                            TypeName = typeInfo.TypeName,
                            TypeNamespace = typeInfo.TypeNamespace,
                            OperationId = typeInfo.OperationId,
                            ConnectorName = typeInfo.ConnectorName,
                            ClassName = suggestedClassName,
                            DocumentUri = documentPath,
                        })),
                },
            };

            return new CommandOrCodeActionContainer(new CommandOrCodeAction(codeAction));
        }
        catch (OperationCanceledException)
        {
            return new CommandOrCodeActionContainer();
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[CodeAction] Error: {ex.Message}");
            telemetry.TrackException(ex, new Dictionary<string, string>
            {
                { "Handler", "DynamicSchemaCodeAction" },
                { "Operation", "Handle" },
            });

            return new CommandOrCodeActionContainer();
        }
    }

    public override Task<CodeAction> Handle(CodeAction request, CancellationToken cancellationToken)
    {
        // Resolve is not used — the handler returns a Command-based CodeAction.
        return Task.FromResult(request);
    }

    protected override CodeActionRegistrationOptions CreateRegistrationOptions(
        CodeActionCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new CodeActionRegistrationOptions
        {
            DocumentSelector = documentSelector,
            CodeActionKinds = new Container<CodeActionKind>(CodeActionKind.Refactor),
            ResolveProvider = false,
        };
    }

    /// <summary>
    /// Fast detection: checks if the cursor is on a Dynamic* type from the SDK.
    /// Uses syntax tree parsing and a cached SdkIndex type name + [DynamicSchema] operationId lookup.
    /// Schema fetch and code generation happen in GenerateDynamicSchemaCommandHandler when clicked.
    /// </summary>
    internal DynamicSchemaTypeInfo? DetectDynamicSchemaType(
        string documentText,
        OmniSharp.Extensions.LanguageServer.Protocol.Models.Range range,
        CancellationToken cancellationToken)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(documentText, cancellationToken: cancellationToken);
        CompilationUnitSyntax root = tree.GetCompilationUnitRoot(cancellationToken);
        Microsoft.CodeAnalysis.Text.SourceText text = tree.GetText(cancellationToken);

        if (range.Start.Line >= text.Lines.Count)
        {
            return null;
        }

        Microsoft.CodeAnalysis.Text.TextLine line = text.Lines[range.Start.Line];
        int position = line.Start + range.Start.Character;
        if (position > text.Length)
        {
            return null;
        }

        SyntaxNode? node = root.FindNode(Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(position, position));

        // Walk up to find an object creation expression, type reference, or variable declaration
        ObjectCreationExpressionSyntax? objectCreation = node?.FirstAncestorOrSelf<ObjectCreationExpressionSyntax>();
        VariableDeclaratorSyntax? variableDeclarator = node?.FirstAncestorOrSelf<VariableDeclaratorSyntax>();
        IdentifierNameSyntax? identifierName = node?.FirstAncestorOrSelf<IdentifierNameSyntax>();

        string? typeName = null;
        string? qualifiedNamespace = null;

        // Case 1: Cursor on `new DynamicPostMessageRequest()` or `new Namespace.DynamicPostMessageRequest()`
        if (objectCreation != null)
        {
            (typeName, qualifiedNamespace) = ExtractTypeNameFromSyntax(objectCreation.Type);
        }

        // Case 2: Cursor on the variable name in `var messageRequest = new DynamicPostMessageRequest()`
        if (string.IsNullOrEmpty(typeName) && variableDeclarator?.Initializer?.Value is ObjectCreationExpressionSyntax initCreation)
        {
            (typeName, qualifiedNamespace) = ExtractTypeNameFromSyntax(initCreation.Type);
        }

        // Case 3: Cursor on a plain identifier that might be a type name
        if (string.IsNullOrEmpty(typeName) && identifierName != null)
        {
            typeName = identifierName.Identifier.Text;
        }

        if (string.IsNullOrEmpty(typeName) || !typeName.StartsWith("Dynamic", StringComparison.Ordinal))
        {
            return null;
        }

        // Fast SDK lookup: verify the type exists in the loaded SDK assemblies
        // and try to find its [DynamicSchema] operationId from cached metadata
        return LookupTypeInSdkIndex(typeName, qualifiedNamespace);
    }

    /// <summary>
    /// Extracts the simple type name and optional namespace from a type syntax node.
    /// </summary>
    private static (string? TypeName, string? Namespace) ExtractTypeNameFromSyntax(TypeSyntax? typeSyntax)
    {
        if (typeSyntax is IdentifierNameSyntax simple)
        {
            return (simple.Identifier.Text, null);
        }

        if (typeSyntax is QualifiedNameSyntax qualified)
        {
            return (qualified.Right.Identifier.Text, qualified.Left.ToString());
        }

        return (null, null);
    }

    /// <summary>
    /// Looks up a Dynamic* type in the SdkIndex by name. Uses pre-loaded assembly metadata
    /// to find [DynamicSchema] attributes without creating a Roslyn compilation.
    /// </summary>
    private DynamicSchemaTypeInfo? LookupTypeInSdkIndex(string typeName, string? qualifiedNamespace)
    {
        if (sdkIndex == null)
        {
            return null;
        }

        // Check if the type name exists in the SDK type list
        string? matchingFullName = sdkIndex.TypeNames
            .FirstOrDefault(t => t.EndsWith($".{typeName}", StringComparison.Ordinal) ||
                                  string.Equals(t, typeName, StringComparison.Ordinal));

        if (string.IsNullOrEmpty(matchingFullName))
        {
            return null;
        }

        // Infer connector from namespace: "Microsoft.Azure.Connectors.DirectClient.Teams" → "teams"
        string? connectorName = null;
        string typeNamespace = matchingFullName.Contains('.')
            ? matchingFullName[..matchingFullName.LastIndexOf('.')]
            : string.Empty;

        if (!string.IsNullOrEmpty(typeNamespace))
        {
            string[] nsParts = typeNamespace.Split('.');
            connectorName = nsParts[^1].ToLowerInvariant();
        }
        else if (!string.IsNullOrEmpty(qualifiedNamespace))
        {
            string[] nsParts = qualifiedNamespace.Split('.');
            connectorName = nsParts[^1].ToLowerInvariant();
        }

        if (string.IsNullOrEmpty(connectorName))
        {
            return null;
        }

        // Look up the [DynamicSchema] operationId using pre-loaded Roslyn metadata from SdkIndex
        string? operationId = FindDynamicSchemaOperationId(typeName);

        if (string.IsNullOrEmpty(operationId))
        {
            return null;
        }

        return new DynamicSchemaTypeInfo
        {
            TypeName = typeName,
            TypeNamespace = typeNamespace,
            OperationId = operationId,
            ConnectorName = connectorName,
        };
    }

    /// <summary>
    /// Finds the [DynamicSchema("operationId")] for a type from the pre-built cache.
    /// </summary>
    private string? FindDynamicSchemaOperationId(string typeName)
    {
        return dynamicSchemaOperations.TryGetValue(typeName, out string? operationId)
            ? operationId
            : null;
    }

    /// <summary>
    /// Builds the DynamicSchema cache eagerly from the SdkIndex assembly paths.
    /// Uses System.Reflection.Metadata (raw PE reading) — typically completes in under 10ms.
    /// </summary>
    private static Dictionary<string, string> BuildCacheFromSdkIndex(SdkIndex? sdkIndex)
    {
        if (sdkIndex == null)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var result = DynamicSchemaCache.Build(sdkIndex.AssemblyPaths);
        Console.Error.WriteLine($"[CodeAction] Cached {result.Count} [DynamicSchema] types from SDK");
        return result;
    }

    /// <summary>
    /// Generates a clean class name from the dynamic type name.
    /// E.g., "DynamicPostMessageRequest" → "PostMessageInput".
    /// </summary>
    internal static string GenerateClassName(string dynamicTypeName)
    {
        string name = dynamicTypeName;

        if (name.StartsWith("Dynamic", StringComparison.Ordinal))
        {
            name = name["Dynamic".Length..];
        }

        // Replace "Request" or "Schema" suffix with "Input" for clarity
        if (name.EndsWith("Request", StringComparison.Ordinal))
        {
            name = name[..^"Request".Length] + "Input";
        }
        else if (name.EndsWith("Schema", StringComparison.Ordinal))
        {
            name = name[..^"Schema".Length] + "Model";
        }

        return name;
    }

    /// <summary>
    /// Information about a detected [DynamicSchema] type at the cursor position.
    /// </summary>
    internal sealed class DynamicSchemaTypeInfo
    {
        public required string TypeName { get; init; }

        public string? TypeNamespace { get; init; }

        public required string OperationId { get; init; }

        public required string ConnectorName { get; init; }
    }
}
