using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

using SdkLspServer.Services.Api;
using SdkLspServer.Services.Connections;
using SdkLspServer.Services.Telemetry;
using SdkLspServer.Store;
using SdkLspServer.Store.DynamicData;

namespace SdkLspServer.Handlers.HoverHandler;

public class HoverHandler(SdkIndex? sdkIndex, BufferManager bufferManager, ConnectionsService connectionsService, ApiService apiService, LSPStore lspStore, ITelemetryService telemetryService, Services.CompilationService compilationService) : HoverHandlerBase
{
    private static readonly SymbolDisplayFormat ShortTypeFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private readonly SdkIndex? sdkIndex = sdkIndex;
    private readonly BufferManager bufferManager = bufferManager;
    private readonly ConnectionsService connectionsService = connectionsService;
    private readonly ApiService apiService = apiService;
    private readonly LSPStore lspStore = lspStore;
    private readonly ITelemetryService telemetry = telemetryService;
    private readonly Services.CompilationService compilationService = compilationService;
    private int hoverRequestCount = 0;

    // Initialize the dynamic operations registry with SDK
    static HoverHandler()
    {
        // Static initialization will happen once per app domain
    }

    private void EnsureRegistryInitialized()
    {
        // Initialize registry with SDK index (lazy, thread-safe in registry)
        DynamicOperationsRegistry.Initialize(sdkIndex, compilationService);
    }

    private readonly TextDocumentSelector documentSelector = new(
    new TextDocumentFilter()
    {
        Pattern = "**/*.cs",
    });

    public static TextDocumentAttributes GetTextDocumentAttributes(Uri uri)
    {
        return new TextDocumentAttributes(uri, "csharp");
    }

    [RequiresAssemblyFiles]
    public override async Task<Hover?> Handle(HoverParams request, CancellationToken cancellationToken)
    {
        // Ensure dynamic operations registry is initialized
        EnsureRegistryInitialized();

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Sample hover requests at 50%
        bool shouldTrack = (++hoverRequestCount % 2) == 0;

        if (shouldTrack)
        {
            telemetry.TrackEvent("Hover_Request_Started");
        }

        try
        {
            string documentPath = request.TextDocument.Uri.ToString();

            // Get the document content from BufferManager
            string? documentText = bufferManager.GetBuffer(documentPath);
            if (string.IsNullOrEmpty(documentText))
            {
                // Fallback: try to read file from disk if not in buffer
                if (request.TextDocument.Uri.Scheme == "file")
                {
                    try
                    {
                        string filePath = request.TextDocument.Uri.GetFileSystemPath();
                        documentText = await System.IO.File.ReadAllTextAsync(filePath, cancellationToken);
                    }
                    catch
                    {
                        return null;
                    }
                }
                else
                {
                    return null;
                }
            }

            // Analyze the symbol at the hover position
            string? documentFilePath = request.TextDocument.Uri.Scheme == "file"
                ? request.TextDocument.Uri.GetFileSystemPath()
                : null;

            string? symbolInfo = await AnalyzeSymbolAtPositionAsync(documentText, request.Position, documentFilePath, request.TextDocument.Uri, cancellationToken);

            if (string.IsNullOrEmpty(symbolInfo))
            {
                // Fallback to SDK summary if no specific symbol found
                if (sdkIndex != null)
                {
                    symbolInfo = $"**SDK Loaded**\n\n{sdkIndex.Summary}";
                }
                else
                {
                    return null;
                }
            }

            var markupContent = new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = symbolInfo,
            };

            stopwatch.Stop();

            if (shouldTrack)
            {
                telemetry.TrackMetric("Hover_ResponseTime_Ms", stopwatch.ElapsedMilliseconds);
                telemetry.TrackEvent("Hover_Request_Completed", new Dictionary<string, string>
                {
                    { "HasSymbolInfo", (!string.IsNullOrEmpty(symbolInfo)).ToString() },
                });
            }

            return new Hover
            {
                Contents = new MarkedStringsOrMarkupContent(markupContent),
            };
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
            telemetry.TrackException(ex, new Dictionary<string, string>
            {
                { "Handler", "Hover" },
                { "Operation", "Handle" },
            });

            return null;
        }
    }

    /// <summary>
    /// Extracts parameter documentation from XML documentation comments for a specified parameter name.
    /// </summary>
    /// <param name="xml">The XML documentation string to parse. Can be null or whitespace.</param>
    /// <param name="paramName">The name of the parameter to extract documentation for.</param>
    /// <returns>
    /// The trimmed documentation text for the specified parameter if found in the XML,
    /// otherwise returns "Parameter" as a default value. Returns empty string if XML is null or whitespace.
    /// </returns>
    private static string ExtractParameterDocumentation(string? xml, string paramName)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return string.Empty;
        }

        try
        {
            string paramTag = $"<param name=\"{paramName}\">";
            int paramStart = xml.IndexOf(paramTag);
            if (paramStart >= 0)
            {
                int contentStart = paramStart + paramTag.Length;
                int paramEnd = xml.IndexOf("</param>", contentStart);
                if (paramEnd > contentStart)
                {
                    return xml[contentStart..paramEnd].Trim();
                }
            }
        }
        catch
        {
            // Ignore XML parsing errors
        }

        return "Parameter";
    }

    /// <summary>
    /// Determines if a parameter is connection-related.
    /// </summary>
    private static bool IsConnectionParameter(IParameterSymbol parameter)
    {
        // Check parameter attributes
        return parameter.GetAttributes().Any(a =>
            string.Equals(a.AttributeClass?.Name, "ConnectionNameAttribute", StringComparison.Ordinal) ||
            string.Equals(a.AttributeClass?.Name, "ConnectionName", StringComparison.Ordinal)) ||
            parameter.Name.Contains("connection", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Finds the position after any trailing comma for comment insertion.
    /// </summary>
    /// <summary>
    /// Checks if the argument needs a trailing comma.
    /// </summary>
    private static bool NeedsTrailingComma(ArgumentSyntax argumentSyntax)
    {
        if (argumentSyntax.Parent is not ArgumentListSyntax argumentList)
        {
            return false;
        }

        int currentIndex = argumentList.Arguments.IndexOf(argumentSyntax);

        // If this is the last argument, no comma needed
        if (currentIndex == argumentList.Arguments.Count - 1)
        {
            return false;
        }

        // Check if there's already a trailing comma
        SyntaxToken? trailingComma = argumentList.Arguments.GetSeparator(currentIndex);

        // If no comma exists but there are more arguments, we need one
        return !trailingComma.HasValue || trailingComma.Value.IsMissing;
    }

    private static (int Line, int Character) GetCommentInsertPosition(ArgumentSyntax argumentSyntax)
    {
        // Get the argument list to check for trailing comma
        if (argumentSyntax.Parent is not ArgumentListSyntax argumentList)
        {
            // Fallback to end of argument
            FileLinePositionSpan argLineSpan = argumentSyntax.GetLocation().GetLineSpan();
            return (argLineSpan.EndLinePosition.Line, argLineSpan.EndLinePosition.Character);
        }

        // Find the index of the current argument
        int currentIndex = argumentList.Arguments.IndexOf(argumentSyntax);

        // Check if there's a comma token after this argument
        SyntaxToken? trailingComma = argumentList.Arguments.GetSeparator(currentIndex);

        if (trailingComma.HasValue && !trailingComma.Value.IsMissing)
        {
            // Insert after the comma
            FileLinePositionSpan commaLineSpan = trailingComma.Value.GetLocation().GetLineSpan();
            return (commaLineSpan.EndLinePosition.Line, commaLineSpan.EndLinePosition.Character);
        }
        else
        {
            // No trailing comma, insert at end of argument
            FileLinePositionSpan argLineSpan = argumentSyntax.GetLocation().GetLineSpan();
            return (argLineSpan.EndLinePosition.Line, argLineSpan.EndLinePosition.Character);
        }
    }

    /// <summary>
    /// Checks whether there is code (next argument or closing paren) on the same line
    /// after where a trailing comment would be inserted. If so, a // comment would shadow it.
    /// </summary>
    private static bool HasCodeAfterOnSameLine(ArgumentSyntax argumentSyntax)
    {
        if (argumentSyntax.Parent is not ArgumentListSyntax argumentList)
        {
            return false;
        }

        (int commentLine, _) = GetCommentInsertPosition(argumentSyntax);

        int currentIndex = argumentList.Arguments.IndexOf(argumentSyntax);
        if (currentIndex < argumentList.Arguments.Count - 1)
        {
            // There's a next argument — check if it starts on the same line as the comment
            int nextArgLine = argumentList.Arguments[currentIndex + 1].GetLocation().GetLineSpan().StartLinePosition.Line;
            return nextArgLine == commentLine;
        }

        // Last argument — check if the closing paren is on the same line
        int closeParenLine = argumentList.CloseParenToken.GetLocation().GetLineSpan().StartLinePosition.Line;
        return closeParenLine == commentLine;
    }

    private static string CreateInsertValueCommandUri(ArgumentSyntax argumentSyntax, string valueToInsert, bool isConnection, string? description = null)
    {
        try
        {
            Console.Error.WriteLine($"[CreateInsertValueCommandUri] Starting - valueToInsert='{valueToInsert}', isConnection={isConnection}");

            // Strip quotes if the value has them (e.g., "value" -> value)
            string cleanValue = valueToInsert.Trim();
            if (cleanValue.StartsWith("\"") && cleanValue.EndsWith("\""))
            {
                cleanValue = cleanValue[1..^1];
            }

            LiteralExpressionSyntax? stringLiteral = null;

            // Handle two cases: lambda expressions and direct string literals
            if (argumentSyntax.Expression is LambdaExpressionSyntax lambda)
            {
                Console.Error.WriteLine("[CreateInsertValueCommandUri] Case 1: Lambda expression");

                // Case 1: Lambda expression (e.g., parameterName: x => "value")
                stringLiteral = lambda.DescendantNodes()
                    .OfType<LiteralExpressionSyntax>()
                    .FirstOrDefault(l => l.IsKind(SyntaxKind.StringLiteralExpression));
            }
            else if (argumentSyntax.Expression is LiteralExpressionSyntax literal &&
                     literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                Console.Error.WriteLine("[CreateInsertValueCommandUri] Case 2: Direct string literal");

                // Case 2: Direct string literal (e.g., connectionName: "myConnection")
                stringLiteral = literal;
            }

            int startLine, startChar, endLine, endChar;
            string baseNewText;

            if (stringLiteral != null)
            {
                // String literal found: replace content inside the quotes
                Microsoft.CodeAnalysis.Location location = stringLiteral.GetLocation();
                FileLinePositionSpan lineSpan = location.GetLineSpan();
                startLine = lineSpan.StartLinePosition.Line;
                startChar = lineSpan.StartLinePosition.Character + 1; // Skip opening quote
                endLine = lineSpan.EndLinePosition.Line;
                endChar = lineSpan.EndLinePosition.Character - 1; // Skip closing quote
                baseNewText = cleanValue;

                Console.Error.WriteLine($"[CreateInsertValueCommandUri] String literal at Line {startLine}, Char {startChar}-{endChar}");
                Console.Error.WriteLine($"[CreateInsertValueCommandUri] String literal value: '{stringLiteral.Token.ValueText}'");
            }
            else
            {
                // No string literal (variable, method call, etc.): replace the entire expression with a quoted string
                Console.Error.WriteLine("[CreateInsertValueCommandUri] No string literal found - replacing entire expression");
                Microsoft.CodeAnalysis.Location location = argumentSyntax.Expression.GetLocation();
                FileLinePositionSpan lineSpan = location.GetLineSpan();
                startLine = lineSpan.StartLinePosition.Line;
                startChar = lineSpan.StartLinePosition.Character;
                endLine = lineSpan.EndLinePosition.Line;
                endChar = lineSpan.EndLinePosition.Character;
                var escapedValue = cleanValue.Replace("\\", "\\\\").Replace("\"", "\\\"");
                baseNewText = $"\"{escapedValue}\"";

                Console.Error.WriteLine($"[CreateInsertValueCommandUri] Expression at Line {startLine}, Char {startChar}-{endChar}");
            }

            // Check if we need to add a trailing comma
            bool needsComma = NeedsTrailingComma(argumentSyntax);

            // NOTE(daviburg): For string literals, the replacement range is inside the quotes
            // (startChar+1, endChar-1). Appending a comma to baseNewText would place it inside
            // the string. Instead, extend the range to include the closing quote and append the
            // comma after it.
            string valueWithComma;
            if (needsComma && stringLiteral != null)
            {
                // Extend range to include closing quote, then newText ends with quote + comma
                endChar = endChar + 1;
                valueWithComma = baseNewText + "\",";
            }
            else
            {
                valueWithComma = needsComma ? baseNewText + "," : baseNewText;
            }

            Console.Error.WriteLine($"[CreateInsertValueCommandUri] needsComma={needsComma}, final value='{valueWithComma}'");

            // Add inline comment only when it won't shadow subsequent code on the same line.
            // A // comment comments out everything to end-of-line, so if the next argument
            // or closing paren is on the same line, inserting a comment would break the call.
            bool shouldAddComment = !isConnection
                && !string.IsNullOrEmpty(description)
                && !HasCodeAfterOnSameLine(argumentSyntax);

            if (shouldAddComment)
            {
                // Add both value replacement and comment
                (int commentLine, int commentChar) = GetCommentInsertPosition(argumentSyntax);

                // Always use a space before the comment
                string commentText = $" // {description}";

                // Create a custom command with both edits
                object[] edits =
                [
                    new
                    {
                        range = new
                        {
                            start = new { line = startLine, character = startChar },
                            end = new { line = endLine, character = endChar },
                        },
                        newText = valueWithComma,
                    },
                    new
                    {
                        range = new
                        {
                            start = new { line = commentLine, character = commentChar },
                            end = new { line = commentLine, character = commentChar },
                        },
                        newText = commentText,
                    },
                ];

                var commandArgs = new
                {
                    edits,
                };

                string argsJson = System.Text.Json.JsonSerializer.Serialize(commandArgs);
                string encodedArgs = Uri.EscapeDataString(argsJson);

                return $"command:sdklsp.applyEdits?{encodedArgs}";
            }
            else
            {
                // No comment - just replace the value (with comma if needed)
                object[] edits =
                [
                    new
                    {
                        range = new
                        {
                            start = new { line = startLine, character = startChar },
                            end = new { line = endLine, character = endChar },
                        },
                        newText = valueWithComma,
                    },
                ];

                var commandArgs = new
                {
                    edits,
                };

                string argsJson = System.Text.Json.JsonSerializer.Serialize(commandArgs);
                string encodedArgs = Uri.EscapeDataString(argsJson);

                return $"command:sdklsp.applyEdits?{encodedArgs}";
            }
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
            Console.Error.WriteLine($"[CreateInsertValueCommandUri] Error: {ex.Message}");
            Console.Error.WriteLine($"[CreateInsertValueCommandUri] Stack: {ex.StackTrace}");
            return "#";
        }
    }

    private static string FormatParameterInfo(IParameterSymbol parameter)
    {
        var result = new StringBuilder();
        result.AppendLine("```csharp");
        result.Append($"{parameter.Type.ToDisplayString(ShortTypeFormat)} {parameter.Name}");
        if (parameter.HasExplicitDefaultValue)
        {
            result.Append($" = {parameter.ExplicitDefaultValue}");
        }

        result.AppendLine();
        result.AppendLine("```");
        result.AppendLine("(parameter)");
        return result.ToString();
    }

    [RequiresAssemblyFiles("Calls System.Reflection.Assembly.Location")]
    private async Task<string?> AnalyzeSymbolAtPositionAsync(string documentText, Position position, string? documentFilePath, DocumentUri documentUri, CancellationToken cancellationToken)
    {
        try
        {
            // Parse the document
            SyntaxTree tree = CSharpSyntaxTree.ParseText(documentText, cancellationToken: cancellationToken);
            CompilationUnitSyntax root = tree.GetCompilationUnitRoot(cancellationToken: cancellationToken);
            Microsoft.CodeAnalysis.Text.SourceText text = await tree.GetTextAsync(cancellationToken);

            // Find the position in the document
            if (position.Line >= text.Lines.Count)
            {
                return null;
            }

            Microsoft.CodeAnalysis.Text.TextLine line = text.Lines[position.Line];
            if (position.Character >= line.Span.Length)
            {
                return null;
            }

            int absolutePosition = line.Start + position.Character;
            SyntaxNode? node = root.FindNode(Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(absolutePosition, absolutePosition));

            // Try to find a more specific node if we're looking at an identifier
            if (node is IdentifierNameSyntax && node.Parent != null)
            {
                node = node.Parent;
            }

            // For method calls, we want the invocation expression or member access
            if (node?.Parent is InvocationExpressionSyntax invocation)
            {
                node = invocation;
            }
            else if (node?.Parent is MemberAccessExpressionSyntax memberAccess)
            {
                await Console.Error.WriteLineAsync("[HoverHandler] Found member access, using that instead");
                node = memberAccess;
            }

            if (node == null)
            {
                return null;
            }

            // Create compilation first as it's needed for both parameter context and symbol analysis
            (CSharpCompilation compilation, SemanticModel semanticModel) = this.compilationService
                .GetCompilation(
                    documentUri.ToUri(),
                    documentText,
                    documentFilePath);

            // Check for compilation errors
            System.Collections.Immutable.ImmutableArray<Microsoft.CodeAnalysis.Diagnostic> diagnostics = compilation.GetDiagnostics(cancellationToken);
            var errors = diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToList();
            var warnings = diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Warning).ToList();

            if (errors.Count != 0)
            {
                await Console.Error.WriteLineAsync($"[HoverHandler] Compilation has {errors.Count} error(s), {warnings.Count} warning(s)");
                await Console.Error.WriteLineAsync("[HoverHandler] This may prevent proper symbol resolution!");

                foreach (Microsoft.CodeAnalysis.Diagnostic? error in errors.Take(5))
                {
                    FileLinePositionSpan location = error.Location.GetLineSpan();
                    await Console.Error.WriteLineAsync($"[HoverHandler] Error {error.Id} at line {location.StartLinePosition.Line}: {error.GetMessage()}");
                }

                if (errors.Count > 5)
                {
                    await Console.Error.WriteLineAsync($"[HoverHandler] ... and {errors.Count - 5} more errors");
                }
            }
            else
            {
                await Console.Error.WriteLineAsync($"[HoverHandler] Compilation successful with {warnings.Count} warning(s)");
            }

            // Check if we're hovering over a parameter in a method call first
            await Console.Error.WriteLineAsync($"[HoverHandler] Checking for parameter context on node: {node.GetType().Name} '{node}'");
            await Console.Error.WriteLineAsync($"[HoverHandler] Node parent: {node.Parent?.GetType().Name} '{node.Parent}'");
            await Console.Error.WriteLineAsync($"[HoverHandler] Node grandparent: {node.Parent?.Parent?.GetType().Name} '{node.Parent?.Parent}'");

            HoverHandlerTypes.ParameterContext? parameterContext = await GetParameterContextAsync(node, semanticModel);
            if (parameterContext != null)
            {
                await Console.Error.WriteLineAsync("[HoverHandler] ✅ Parameter context found:");
                await Console.Error.WriteLineAsync($"  - Method: {parameterContext.Method?.Name ?? "NULL"}");
                await Console.Error.WriteLineAsync($"  - MethodContainingType: {parameterContext.Method?.ContainingType?.Name ?? "NULL"}");
                await Console.Error.WriteLineAsync($"  - Parameter: {parameterContext.Parameter?.Name ?? "NULL"}");
                await Console.Error.WriteLineAsync($"  - ParameterName: {parameterContext.ParameterName ?? "NULL"}");
                await Console.Error.WriteLineAsync($"  - IsConnectionParameter: {parameterContext.IsConnectionParameter}");
                await Console.Error.WriteLineAsync($"  - ConnectionName: {parameterContext.ConnectionName ?? "NULL"}");
                await Console.Error.WriteLineAsync($"  - ArgumentIndex: {parameterContext.ArgumentIndex}");

                if (parameterContext.Parameter != null)
                {
                    await Console.Error.WriteLineAsync($"  - Parameter.Type: {parameterContext.Parameter.Type?.Name ?? "NULL"}");
                    await Console.Error.WriteLineAsync($"  - Parameter Attributes: {parameterContext.Parameter.GetAttributes().Length}");
                    foreach (AttributeData attr in parameterContext.Parameter.GetAttributes())
                    {
                        await Console.Error.WriteLineAsync($"    * {attr.AttributeClass?.Name ?? "NULL"}");
                    }
                }

                return await FormatParameterWithPossibleValuesAsync(parameterContext);
            }
            else
            {
                await Console.Error.WriteLineAsync($"[HoverHandler] ❌ No parameter context found for node: {node.GetType().Name}");
            }

            // Try to get symbol information
            SymbolInfo symbolInfo = semanticModel.GetSymbolInfo(node, cancellationToken: cancellationToken);
            ISymbol? symbol = symbolInfo.Symbol;

            await Console.Error.WriteLineAsync("[HoverHandler] Symbol lookup result:");
            await Console.Error.WriteLineAsync($"  - symbol is null: {symbol == null}");
            await Console.Error.WriteLineAsync($"  - CandidateSymbols count: {symbolInfo.CandidateSymbols.Length}");
            await Console.Error.WriteLineAsync($"  - CandidateReason: {symbolInfo.CandidateReason}");

            if (symbol != null)
            {
                await Console.Error.WriteLineAsync($"[HoverHandler] Resolved symbol: {symbol.Name} ({symbol.Kind})");
                return await FormatSymbolInfoAsync(symbol);
            }

            // If direct symbol lookup failed, try candidate symbols
            if (symbolInfo.CandidateSymbols.Length > 0)
            {
                ISymbol? candidateSymbol = symbolInfo.CandidateSymbols.FirstOrDefault();
                if (candidateSymbol != null)
                {
                    await Console.Error.WriteLineAsync($"[HoverHandler] Candidate symbol: {candidateSymbol.Name} ({candidateSymbol.Kind})");
                    return await FormatSymbolInfoAsync(candidateSymbol);
                }
            }

            // If no symbol found, try to get type information
            TypeInfo typeInfo = semanticModel.GetTypeInfo(node, cancellationToken: cancellationToken);
            if (typeInfo.Type != null)
            {
                await Console.Error.WriteLineAsync($"[HoverHandler] Using type info: {typeInfo.Type.Name}");
                return await FormatTypeInfoAsync(typeInfo.Type);
            }

            // Try to find a more specific node if we're at a generic position
            SyntaxNode? parent = node.Parent;
            while (parent != null && parent != root)
            {
                SymbolInfo parentSymbolInfo = semanticModel.GetSymbolInfo(parent, cancellationToken: cancellationToken);
                if (parentSymbolInfo.Symbol != null)
                {
                    await Console.Error.WriteLineAsync($"[HoverHandler] Using parent symbol: {parentSymbolInfo.Symbol.Name}");
                    return await FormatSymbolInfoAsync(parentSymbolInfo.Symbol);
                }

                parent = parent.Parent;
            }

            await Console.Error.WriteLineAsync("[HoverHandler] No symbol information found for node");
            return null;
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
            await Console.Error.WriteLineAsync($"[HoverHandler] ❌ Exception in AnalyzeSymbolAtPositionAsync: {ex.GetType().Name}");
            await Console.Error.WriteLineAsync($"[HoverHandler] Message: {ex.Message}");
            await Console.Error.WriteLineAsync($"[HoverHandler] StackTrace: {ex.StackTrace}");
            return null;
        }
    }

    private async Task<string> FormatSymbolInfoAsync(ISymbol symbol)
    {
        try
        {
            var parts = new List<string>();

            // Format based on symbol type following LSP standard format
            switch (symbol)
            {
                case IMethodSymbol method:
                    parts.Add(await FormatMethodInfoAsync(method));
                    break;

                case IPropertySymbol property:
                    parts.Add(FormatPropertyInfo(property));
                    break;

                case IFieldSymbol field:
                    parts.Add(FormatFieldInfo(field));
                    break;

                case ILocalSymbol local:
                    parts.Add(FormatLocalVariableInfo(local));
                    break;

                case IParameterSymbol parameter:
                    parts.Add(FormatParameterInfo(parameter));
                    break;

                case ITypeSymbol type:
                    parts.Add(await FormatTypeInfoAsync(type));
                    break;

                default:
                    parts.Add($"**{symbol.Kind}**: `{symbol.Name}`");
                    break;
            }

            // Add SDK-specific information following LSP standards
            AddSdkInformation(symbol, parts);

            return string.Join("\n\n", parts);
        }
        catch (Exception)
        {
            // Return basic symbol info as fallback
            return symbol != null
                ? $"```csharp\n{symbol.Kind} {symbol.Name}\n```\n\n*Error loading detailed documentation*"
                : "*Error: Symbol is null*";
        }
    }

    private static async Task<string> FormatMethodInfoAsync(IMethodSymbol method)
    {
        try
        {
            var result = new StringBuilder();
            result.AppendLine("```csharp");

            // Show accessibility + return type + method name with simplified parameters
            string paramList = string.Join(
                ", ",
                method.Parameters.Select(p => $"{SimplifyTypeForDisplay(p.Type)} {p.Name}"));
            string access = GetAccessibilityString(method.DeclaredAccessibility);
            string returnType = method.ReturnsVoid ? "void" : SimplifyTypeForDisplay(method.ReturnType);
            result.AppendLine($"{access}{returnType} {method.Name}({paramList})");
            result.AppendLine();
            result.AppendLine("```");

            // 2. Documentation summary
            string? documentation = method.GetDocumentationCommentXml();
            if (!string.IsNullOrWhiteSpace(documentation))
            {
                string? summary = ExtractSummaryFromDocumentation(documentation);
                if (!string.IsNullOrEmpty(summary))
                {
                    result.AppendLine();
                    result.AppendLine(summary);
                }

                // Concise summary only (omit verbose parameter/returns sections for brevity)
            }

            return result.ToString();
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
            await Console.Error.WriteLineAsync($"[FormatMethodInfo] Error formatting method {method.Name}: {ex.Message}");
            return $"```csharp\n{method.Name}(...)\n```\n\n(Error formatting method signature)";
        }
    }

    private static string SimplifyTypeForDisplay(ITypeSymbol type)
    {
        ITypeSymbol t = UnwrapCommonWrapperTypes(type);
        return t.ToDisplayString(ShortTypeFormat);
    }

    private static ITypeSymbol UnwrapCommonWrapperTypes(ITypeSymbol type)
    {
        try
        {
            // Unwrap Nullable<T> to T? is handled by ShortTypeFormat; no change needed

            // Unwrap Expression<Func<...>> to the Func's return type
            if (type is INamedTypeSymbol nts)
            {
                if (string.Equals(nts.Name, "Expression", StringComparison.Ordinal)
                    && string.Equals(nts.ContainingNamespace?.ToDisplayString(), "System.Linq.Expressions", StringComparison.Ordinal)
                    && nts.TypeArguments.Length == 1)
                {
                    ITypeSymbol inner = nts.TypeArguments[0];
                    if (inner is INamedTypeSymbol func
                        && string.Equals(func.Name, "Func", StringComparison.Ordinal)
                        && string.Equals(func.ContainingNamespace?.ToDisplayString(), "System", StringComparison.Ordinal)
                        && func.TypeArguments.Length >= 1)
                    {
                        // Func<T1, T2, ..., TResult> -> TResult
                        return UnwrapCommonWrapperTypes(func.TypeArguments.Last());
                    }

                    // Expression<T> where T is not Func — just unwrap one level
                    return UnwrapCommonWrapperTypes(inner);
                }

                // Direct Func<...> parameters: map to return type
                if (string.Equals(nts.Name, "Func", StringComparison.Ordinal)
                    && string.Equals(nts.ContainingNamespace?.ToDisplayString(), "System", StringComparison.Ordinal)
                    && nts.TypeArguments.Length >= 1)
                {
                    return UnwrapCommonWrapperTypes(nts.TypeArguments.Last());
                }
            }
        }
        catch
        {
            // If anything goes wrong, fall back to original type
        }

        return type;
    }

    /// <summary>
    /// Formats property information following LSP standards.
    /// </summary>
    private static string FormatPropertyInfo(IPropertySymbol property)
    {
        var result = new StringBuilder();
        result.AppendLine("```csharp");
        string type = property.Type.ToDisplayString(ShortTypeFormat);
        string accessors = property switch
        {
            { GetMethod: not null, SetMethod: not null } => " { get; set; }",
            { GetMethod: not null } => " { get; }",
            { SetMethod: not null } => " { set; }",
            _ => string.Empty,
        };
        result.AppendLine($"{type} {property.Name}{accessors}");
        result.AppendLine();
        result.AppendLine("```");

        // Add documentation
        string? documentation = property.GetDocumentationCommentXml();
        if (!string.IsNullOrWhiteSpace(documentation))
        {
            string? summary = ExtractSummaryFromDocumentation(documentation);
            if (!string.IsNullOrEmpty(summary))
            {
                result.AppendLine();
                result.AppendLine(summary);
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Formats field information following LSP standards.
    /// </summary>
    private static string FormatFieldInfo(IFieldSymbol field)
    {
        var result = new StringBuilder();
        result.AppendLine("```csharp");
        string modifiers = field switch
        {
            { IsConst: true } => "const ",
            { IsReadOnly: true } => "readonly ",
            _ => string.Empty,
        };
        string type = field.Type.ToDisplayString(ShortTypeFormat);
        result.AppendLine($"{modifiers}{type} {field.Name}");
        result.AppendLine();
        result.AppendLine("```");

        // Add documentation
        string? documentation = field.GetDocumentationCommentXml();
        if (!string.IsNullOrWhiteSpace(documentation))
        {
            string? summary = ExtractSummaryFromDocumentation(documentation);
            if (!string.IsNullOrEmpty(summary))
            {
                result.AppendLine();
                result.AppendLine(summary);
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Formats local variable information (simplified format).
    /// </summary>
    private static string FormatLocalVariableInfo(ILocalSymbol local)
    {
        string type = local.Type.ToDisplayString(ShortTypeFormat);
        return $"```csharp\n{type} {local.Name}\n```\n\n(local variable)";
    }

    /// <summary>
    /// Formats type information following LSP standards.
    /// </summary>
    private static string FormatTypeInfo(ITypeSymbol type)
    {
        var result = new StringBuilder();
        result.AppendLine("```csharp");
        string kind = type.TypeKind.ToString().ToLowerInvariant();
        string name = type.ToDisplayString(ShortTypeFormat);
        result.AppendLine($"{kind} {name}");
        result.AppendLine();
        result.AppendLine("```");

        // Add namespace information
        if (type.ContainingNamespace?.IsGlobalNamespace == false)
        {
            result.AppendLine();
            result.AppendLine($"**Namespace:** `{type.ContainingNamespace.ToDisplayString()}`");
        }

        // Add documentation
        string? documentation = type.GetDocumentationCommentXml();
        if (!string.IsNullOrWhiteSpace(documentation))
        {
            string? summary = ExtractSummaryFromDocumentation(documentation);
            if (!string.IsNullOrEmpty(summary))
            {
                result.AppendLine();
                result.AppendLine(summary);
            }
        }

        return result.ToString();
    }

    private static Task<string> FormatTypeInfoAsync(ITypeSymbol type)
    {
        return Task.FromResult(FormatTypeInfo(type));
    }

    private static string GetAccessibilityString(Accessibility accessibility)
    {
        return accessibility switch
        {
            Accessibility.Public => "public ",
            Accessibility.Private => "private ",
            Accessibility.Protected => "protected ",
            Accessibility.Internal => "internal ",
            Accessibility.ProtectedOrInternal => "protected internal ",
            Accessibility.ProtectedAndInternal => "private protected ",
            _ => string.Empty,
        };
    }

    private void AddSdkInformation(ISymbol symbol, List<string> parts)
    {
        bool isFromSdk = IsSymbolFromSdk(symbol);
        if (isFromSdk)
        {
            parts.Add("---"); // Separator line following LSP standards
            parts.Add("**Connector SDK**");

            // Add usage example for SDK methods
            if (symbol is IMethodSymbol method && method.Name.Contains("Add"))
            {
                string example = GenerateMethodExample(method);
                if (!string.IsNullOrEmpty(example))
                {
                    parts.Add("**Example:**");
                    parts.Add($"```csharp\n{example}\n```");
                }
            }
        }
    }

    private string GenerateMethodExample(IMethodSymbol method)
    {
        if (method.Parameters.Length > 0)
        {
            string instanceName = method.ContainingType?.Name.ToLower().Replace("builder", string.Empty) ?? "agent";
            return $"{instanceName}.{method.Name}({string.Join(", ", method.Parameters.Select(GenerateParameterExample))});";
        }

        return string.Empty;
    }

    private string GenerateParameterExample(IParameterSymbol param)
    {
        return param.Type.Name switch
        {
            "String" => $"\"{param.Name}\"",
            "Int32" => "0",
            "Boolean" => "true",
            "Action" => $"{param.Name} => {{ /* Configure {param.Name} */ }}",
            _ => $"/* {param.Name} */",
        };
    }

    private static string ExtractSummaryFromDocumentation(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return string.Empty;
        }

        try
        {
            int summaryStart = xml.IndexOf("<summary>");
            int summaryEnd = xml.IndexOf("</summary>");
            if (summaryStart >= 0 && summaryEnd > summaryStart)
            {
                string summary = xml.Substring(summaryStart + 9, summaryEnd - summaryStart - 9);
                return summary.Trim().Replace("\n", " ").Replace("  ", " ");
            }
        }
        catch
        {
            // Ignore XML parsing errors
        }

        return string.Empty;
    }

    private bool IsSymbolFromSdk(ISymbol symbol)
    {
        if (sdkIndex == null)
        {
            return false;
        }

        string? assemblyName = symbol.ContainingAssembly?.Name;
        if (assemblyName == null)
        {
            return false;
        }

        // Check if any SDK assembly matches
        return sdkIndex.AssemblyPaths.Any(path =>
            System.IO.Path.GetFileNameWithoutExtension(path)
                .Equals(assemblyName, StringComparison.OrdinalIgnoreCase));
    }

    protected override HoverRegistrationOptions CreateRegistrationOptions(HoverCapability capability, ClientCapabilities clientCapabilities)
    {
        return new HoverRegistrationOptions
        {
            DocumentSelector = documentSelector,
        };
    }

    /// <summary>
    /// Helper class to represent parameter context information.
    /// </summary>
    private async Task<HoverHandlerTypes.ParameterContext?> GetParameterContextAsync(SyntaxNode node, SemanticModel semanticModel)
    {
        try
        {
            // Check if we're inside an argument of a method call
            // We need to look for the closest ArgumentSyntax ancestor, as the hover might be on:
            // - A string literal: "connectionName"
            // - An identifier: variableName
            // - A member access: obj.Property
            // First, try the direct approach
            ArgumentSyntax? argument = node.FirstAncestorOrSelf<ArgumentSyntax>();

            // If that didn't work, walk up the tree more carefully
            if (argument == null)
            {
                SyntaxNode? current = node;
                for (int depth = 0; current != null && depth < 10; depth++)
                {
                    if (current is ArgumentSyntax arg)
                    {
                        argument = arg;
                        break;
                    }

                    // Stop if we hit a method declaration or other boundary
                    if (current is MethodDeclarationSyntax or ClassDeclarationSyntax)
                    {
                        break;
                    }

                    current = current.Parent;
                }
            }

            if (argument == null)
            {
                // Fallback: check if we're inside an ArgumentListSyntax at an empty position
                // (e.g., cursor between ( and , in GetAllTablesAsync(|, cancellationToken))
                ArgumentListSyntax? emptyArgList = node.FirstAncestorOrSelf<ArgumentListSyntax>();

                // Also check parent nodes — the cursor token may be on the InvocationExpressionSyntax
                if (emptyArgList == null)
                {
                    SyntaxNode? current = node;
                    for (int scan = 0; current != null && scan < 5; scan++)
                    {
                        if (current is ArgumentListSyntax al)
                        {
                            emptyArgList = al;
                            break;
                        }

                        if (current is InvocationExpressionSyntax inv)
                        {
                            emptyArgList = inv.ArgumentList;
                            break;
                        }

                        current = current.Parent;
                    }
                }

                if (emptyArgList != null)
                {
                    InvocationExpressionSyntax? emptyInvocation = emptyArgList.Parent as InvocationExpressionSyntax;
                    if (emptyInvocation != null)
                    {
                        SymbolInfo emptySymbolInfo = semanticModel.GetSymbolInfo(emptyInvocation);
                        IMethodSymbol? emptyMethod = emptySymbolInfo.Symbol as IMethodSymbol
                            ?? emptySymbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();

                        if (emptyMethod != null && emptyMethod.Parameters.Length > 0)
                        {
                            // Determine argument index from cursor position relative to commas
                            int emptyArgIndex = 0;
                            foreach (SyntaxToken separator in emptyArgList.Arguments.GetSeparators())
                            {
                                if (node.SpanStart >= separator.SpanStart)
                                {
                                    emptyArgIndex++;
                                }
                            }

                            if (emptyArgIndex < emptyMethod.Parameters.Length)
                            {
                                IParameterSymbol emptyParam = emptyMethod.Parameters[emptyArgIndex];
                                await Console.Error.WriteLineAsync($"[GetParameterContext] Empty arg fallback: method={emptyMethod.Name}, param={emptyParam.Name}, index={emptyArgIndex}");

                                // Create a synthetic empty argument for the context so downstream code
                                // doesn't need to handle null ArgumentSyntax.
                                ArgumentSyntax emptyArg = emptyArgList.Arguments.Count > emptyArgIndex
                                    ? emptyArgList.Arguments[emptyArgIndex]
                                    : SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(
                                        SyntaxKind.StringLiteralExpression,
                                        SyntaxFactory.Literal(string.Empty)));

                                return new HoverHandlerTypes.ParameterContext
                                {
                                    Method = emptyMethod,
                                    Parameter = emptyParam,
                                    ArgumentIndex = emptyArgIndex,
                                    ArgumentSyntax = emptyArg,
                                    ParameterName = emptyParam.Name,
                                    IsConnectionParameter = emptyParam.Name.Contains("connection", StringComparison.OrdinalIgnoreCase),
                                };
                            }
                        }
                    }
                }

                return null;
            }

            ArgumentListSyntax? argumentList = argument.FirstAncestorOrSelf<ArgumentListSyntax>();
            if (argumentList == null)
            {
                return null;
            }

            InvocationExpressionSyntax? invocation = argumentList.FirstAncestorOrSelf<InvocationExpressionSyntax>();
            if (invocation == null)
            {
                return null;
            }

            // Get the method being called
            SymbolInfo symbolInfo = semanticModel.GetSymbolInfo(invocation);

            IMethodSymbol? method = TryResolveMethodSymbol(invocation, semanticModel, symbolInfo);

            if (method is null)
            {
                // Try to extract parameter information from the syntax
                int fallbackArgumentIndex = argumentList.Arguments.IndexOf(argument);

                string? namedArgument = argument.NameColon?.Name.Identifier.ValueText;
                bool namedSuggestsConnection = namedArgument?.Contains("connection", StringComparison.OrdinalIgnoreCase) == true;

                var fallbackContext = new HoverHandlerTypes.ParameterContext
                {
                    Method = null,
                    Parameter = null,
                    ArgumentIndex = fallbackArgumentIndex,
                    ArgumentSyntax = argument,
                    ParameterName = namedArgument,
                    IsConnectionParameter = namedSuggestsConnection,
                };

                // Check if this looks like a connection parameter based on the string value
                if (argument.Expression is LiteralExpressionSyntax literal &&
                    literal.Token.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralToken))
                {
                    string stringValue = literal.Token.ValueText;

                    // Get current connections
                    ConnectionsConfig? connections = connectionsService.GetConnections();

                    // Check if this string value matches any known connection (managed or DirectClient)
                    bool isConnectionKey = ConnectionsHelper.ContainsConnection(connections, stringValue);

                    // Also check if this string value matches any connection ID
                    string? matchedConnectionKey = null;
                    if (!isConnectionKey && connections?.ManagedApiConnections != null)
                    {
                        foreach (KeyValuePair<string, ManagedApiConnection> kvp in connections.ManagedApiConnections)
                        {
                            string connectionId = kvp.Value.Connection?.Id ?? string.Empty;
                            if (connectionId.Contains(stringValue, StringComparison.OrdinalIgnoreCase))
                            {
                                matchedConnectionKey = kvp.Key;
                                break;
                            }
                        }
                    }

                    fallbackContext.ConnectionName = stringValue;

                    if (isConnectionKey || matchedConnectionKey != null)
                    {
                        fallbackContext.IsConnectionParameter = true;
                        fallbackContext.ConnectionName = matchedConnectionKey ?? stringValue;

                        telemetry.TrackEvent("Connection_Parameter_Detected", new Dictionary<string, string>
                        {
                            { "ConnectionKey", matchedConnectionKey ?? stringValue },
                        });
                    }
                }

                return fallbackContext.IsConnectionParameter || !string.IsNullOrEmpty(fallbackContext.ParameterName) ? fallbackContext : null;
            }

            // Find which parameter this argument corresponds to
            int argumentIndex = argumentList.Arguments.IndexOf(argument);

            string? argumentName = argument.NameColon?.Name.Identifier.ValueText;
            if (!string.IsNullOrEmpty(argumentName))
            {
                IParameterSymbol? byName = method.Parameters.FirstOrDefault(p => string.Equals(p.Name, argumentName, StringComparison.Ordinal));
                if (byName != null)
                {
                    argumentIndex = byName.Ordinal;
                }
            }

            IParameterSymbol? parameter = null;
            if (argumentIndex >= 0 && argumentIndex < method.Parameters.Length)
            {
                parameter = method.Parameters[argumentIndex];
            }

            bool nameSuggestsConnection = argumentName?.Contains("connection", StringComparison.OrdinalIgnoreCase) == true;

            if (parameter == null)
            {
                if (string.IsNullOrEmpty(argumentName))
                {
                    return null;
                }

                var fallbackContext = new HoverHandlerTypes.ParameterContext
                {
                    Method = method,
                    Parameter = null,
                    ArgumentIndex = Math.Max(argumentIndex, 0),
                    ArgumentSyntax = argument,
                    ParameterName = argumentName,
                    IsConnectionParameter = nameSuggestsConnection,
                };

                if (argument.Expression is LiteralExpressionSyntax literal && literal.Token.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralToken))
                {
                    fallbackContext.ConnectionName = literal.Token.ValueText;
                }

                return fallbackContext;
            }

            var resolvedContext = new HoverHandlerTypes.ParameterContext
            {
                Method = method,
                Parameter = parameter,
                ArgumentIndex = argumentIndex,
                ArgumentSyntax = argument,
                ParameterName = parameter.Name,
                IsConnectionParameter = IsConnectionParameter(parameter) || nameSuggestsConnection,
            };

            if (resolvedContext.IsConnectionParameter && argument.Expression is LiteralExpressionSyntax literalArg &&
                literalArg.Token.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralToken))
            {
                resolvedContext.ConnectionName = literalArg.Token.ValueText;
            }

            return resolvedContext;
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
            await Console.Error.WriteLineAsync($"[ParameterContext] Exception: {ex}");
            return null;
        }
    }

    /// <summary>
    /// Formats parameter information with possible values based on context.
    /// Supports three paths: Connection parameters, Dynamic parameters, and Standard parameters.
    /// </summary>
    private async Task<string> FormatParameterWithPossibleValuesAsync(HoverHandlerTypes.ParameterContext context)
    {
        var result = new StringBuilder();

        // Build parameter signature section
        AppendParameterSignature(result, context);

        // Determine parameter type: Connection, Dynamic, or Standard
        bool isConnection = IsConnectionParameterContext(context);
        bool isDynamic = !isConnection && HasDynamicValuesAttribute(context);

        await Console.Error.WriteLineAsync($"[FormatParameterWithPossibleValues] isConnection={isConnection}, isDynamic={isDynamic}");

        if (isConnection)
        {
            // Path 1: Connection parameters
            var connectionValues = GetConnectionParameterValues().ToList();
            await Console.Error.WriteLineAsync($"[FormatParameterWithPossibleValues] Connection path - found {connectionValues.Count} connections");
            AppendParameterValues(result, connectionValues, context, isConnection: true);
        }
        else if (isDynamic)
        {
            // Path 2: Dynamic parameters
            await Console.Error.WriteLineAsync("[FormatParameterWithPossibleValues] Dynamic path - fetching values");
            List<HoverHandlerTypes.ParameterValue> dynamicValues = await FetchDynamicParameterValuesAsync(context);
            int valueCount = dynamicValues?.Count ?? 0;
            await Console.Error.WriteLineAsync($"[FormatParameterWithPossibleValues] Dynamic path - found {valueCount} values");

            if (dynamicValues != null)
            {
                AppendParameterValues(result, dynamicValues, context, isConnection: false);
            }
        }
        else
        {
            // Path 3: Standard parameters - show C# documentation style hover
            await Console.Error.WriteLineAsync("[FormatParameterWithPossibleValues] Standard parameter path");
            AppendStandardParameterDocumentation(result, context);
        }

        return result.ToString();
    }

    /// <summary>
    /// Appends the parameter signature section to the result.
    /// </summary>
    private static void AppendParameterSignature(StringBuilder result, HoverHandlerTypes.ParameterContext context)
    {
        string parameterLabel = context.Parameter?.Name ?? context.ParameterName ?? "parameter";
        string parameterType = context.Parameter?.Type != null
            ? SimplifyTypeForDisplay(context.Parameter.Type)
            : string.Empty;

        result.AppendLine("```csharp");
        result.AppendLine($"{parameterType} {parameterLabel}");
        result.AppendLine("```");
        result.AppendLine();
    }

    /// <summary>
    /// Determines if the parameter context represents a connection parameter.
    /// </summary>
    private static bool IsConnectionParameterContext(HoverHandlerTypes.ParameterContext context)
    {
        bool result = false;
        string reason = string.Empty;

        if (context.IsConnectionParameter)
        {
            result = true;
            reason = "context.IsConnectionParameter=true";
        }
        else if (context.Parameter != null && IsConnectionParameter(context.Parameter))
        {
            result = true;
            reason = "IsConnectionParameter(parameter)=true";
        }
        else
        {
            string parameterLabel = context.Parameter?.Name ?? context.ParameterName ?? string.Empty;
            if (parameterLabel.Contains("connection", StringComparison.OrdinalIgnoreCase))
            {
                result = true;
                reason = $"parameter name '{parameterLabel}' contains 'connection'";
            }
        }

        Console.Error.WriteLine($"[IsConnectionParameterContext] Result={result}, Reason={reason}, ParameterName={context.Parameter?.Name ?? context.ParameterName ?? "NULL"}");
        return result;
    }

    /// <summary>
    /// Checks if a parameter context has the DynamicValues attribute.
    /// </summary>
    private static bool HasDynamicValuesAttribute(HoverHandlerTypes.ParameterContext context)
    {
        if (context.Parameter == null)
        {
            Console.Error.WriteLine($"[HasDynamicValuesAttribute] No parameter symbol, trying inference from name: {context.ParameterName}");

            // Fallback: infer from parameter name patterns
            string? inferred = InferOperationFromParameterName(context.ParameterName);
            Console.Error.WriteLine($"[HasDynamicValuesAttribute] Inferred operation: {inferred ?? "NULL"}");
            return !string.IsNullOrEmpty(inferred);
        }

        (bool hasDynamicValues, string? operationName) = GetDynamicValuesAttribute(context.Parameter);
        Console.Error.WriteLine($"[HasDynamicValuesAttribute] Parameter={context.Parameter.Name}, hasDynamicValues={hasDynamicValues}, operationName={operationName ?? "NULL"}");
        return hasDynamicValues;
    }

    /// <summary>
    /// Appends standard C# parameter documentation following LSP and IntelliSense conventions.
    /// This is used for parameters that are neither connection nor dynamic values parameters.
    /// </summary>
    private static void AppendStandardParameterDocumentation(StringBuilder result, HoverHandlerTypes.ParameterContext context)
    {
        // Extract XML documentation for the parameter
        string? paramDoc = ExtractParameterDocFromContext(context);

        if (!string.IsNullOrEmpty(paramDoc))
        {
            result.AppendLine(paramDoc);
            result.AppendLine();
        }

        // Add parameter metadata
        if (context.Parameter != null)
        {
            // Check if this is an Input enum parameter and show available values
            ITypeSymbol? paramType = context.Parameter.Type;
            if (paramType != null)
            {
                // Unwrap Expression<Func<T>> to get the actual type
                ITypeSymbol unwrappedType = UnwrapExpressionFuncType(paramType);

                // Check if it's an enum, especially an Input enum
                if (unwrappedType is INamedTypeSymbol namedType &&
                    namedType.TypeKind == TypeKind.Enum &&
                    namedType.Name.EndsWith("Input", StringComparison.Ordinal))
                {
                    AppendEnumValues(result, namedType);
                }
            }

            // Show default value if present
            if (context.Parameter.HasExplicitDefaultValue)
            {
                string defaultValue = context.Parameter.ExplicitDefaultValue?.ToString() ?? "null";
                result.AppendLine($"**Default value:** `{defaultValue}`");
                result.AppendLine();
            }

            // Show parameter attributes if any
            var attributes = context.Parameter.GetAttributes()
                .Where(a => a.AttributeClass?.Name.Contains("Compiler", StringComparison.Ordinal) == false)
                .ToList();

            if (attributes.Count > 0)
            {
                result.AppendLine("**Attributes:**");
                foreach (AttributeData? attr in attributes)
                {
                    string attrName = attr.AttributeClass?.Name.Replace("Attribute", string.Empty) ?? "Unknown";
                    result.AppendLine($"- `[{attrName}]`");
                }

                result.AppendLine();
            }

            // Show parameter modifiers (ref, out, in, params)
            if (context.Parameter.RefKind != RefKind.None)
            {
                string refKindStr = context.Parameter.RefKind switch
                {
                    RefKind.Ref => "ref",
                    RefKind.Out => "out",
                    RefKind.In => "in",
                    RefKind.RefReadOnlyParameter => "ref readonly",
                    _ => string.Empty,
                };

                if (!string.IsNullOrEmpty(refKindStr))
                {
                    result.AppendLine($"**Modifier:** `{refKindStr}`");
                    result.AppendLine();
                }
            }

            if (context.Parameter.IsParams)
            {
                result.AppendLine("**Variable arguments:** This parameter accepts a variable number of arguments (`params`)");
                result.AppendLine();
            }

            // Show nullability information for reference types
            if (context.Parameter.Type.IsReferenceType)
            {
                string nullability = context.Parameter.NullableAnnotation switch
                {
                    NullableAnnotation.Annotated => "Nullable",
                    NullableAnnotation.NotAnnotated => "Non-nullable",
                    _ => string.Empty,
                };

                if (!string.IsNullOrEmpty(nullability))
                {
                    result.AppendLine($"**Nullability:** {nullability}");
                    result.AppendLine();
                }
            }
        }

        // Add containing method context
        if (context.Method != null)
        {
            result.AppendLine("---");
            result.AppendLine();
            result.AppendLine($"**Method:** `{context.Method.Name}`");

            if (context.Method.ContainingType != null)
            {
                result.AppendLine($"**Containing type:** `{context.Method.ContainingType.ToDisplayString(ShortTypeFormat)}`");
            }
        }
    }

    /// <summary>
    /// Fetches dynamic values for parameters with DynamicValues attribute.
    /// </summary>
    private async Task<List<HoverHandlerTypes.ParameterValue>> FetchDynamicParameterValuesAsync(HoverHandlerTypes.ParameterContext context)
    {
        var values = new List<HoverHandlerTypes.ParameterValue>();

        // Handle fallback case where we don't have method/parameter symbols
        if (context.Method == null || context.Parameter == null)
        {
            values.AddRange(await FetchDynamicValuesFromInferenceAsync(context));
        }
        else
        {
            values.AddRange(await FetchDynamicValuesFromAttributeAsync(context));
        }

        return values;
    }

    /// <summary>
    /// Attempts to fetch dynamic values by inferring operation from parameter name.
    /// </summary>
    private async Task<List<HoverHandlerTypes.ParameterValue>> FetchDynamicValuesFromInferenceAsync(HoverHandlerTypes.ParameterContext context)
    {
        var values = new List<HoverHandlerTypes.ParameterValue>();

        // Look for lambda expressions which are common with DynamicValues parameters
        if (context.ArgumentSyntax.Expression is SimpleLambdaExpressionSyntax or ParenthesizedLambdaExpressionSyntax)
        {
            string? inferredOperation = InferOperationFromParameterName(context.ParameterName);
            if (!string.IsNullOrEmpty(inferredOperation))
            {
                IEnumerable<HoverHandlerTypes.ParameterValue>? dynamicValues = await FetchDynamicValuesAsync(inferredOperation, context);
                if (dynamicValues != null)
                {
                    values.AddRange(dynamicValues);
                }
            }
        }

        return values;
    }

    /// <summary>
    /// Fetches dynamic values using the DynamicValues attribute on the parameter.
    /// </summary>
    private async Task<List<HoverHandlerTypes.ParameterValue>> FetchDynamicValuesFromAttributeAsync(HoverHandlerTypes.ParameterContext context)
    {
        var values = new List<HoverHandlerTypes.ParameterValue>();

        (bool hasDynamicValues, string? operationName) = GetDynamicValuesAttribute(context.Parameter!);

        if (hasDynamicValues && !string.IsNullOrEmpty(operationName))
        {
            IEnumerable<HoverHandlerTypes.ParameterValue>? dynamicValues = await FetchDynamicValuesAsync(operationName, context);
            if (dynamicValues != null)
            {
                values.AddRange(dynamicValues);
            }
        }

        return values;
    }

    /// <summary>
    /// Finds the connection parameter ArgumentSyntax in the invocation.
    /// </summary>
    private static ArgumentSyntax? FindConnectionParameterArgument(HoverHandlerTypes.ParameterContext context)
    {
        try
        {
            InvocationExpressionSyntax? invocation = context.ArgumentSyntax.FirstAncestorOrSelf<InvocationExpressionSyntax>();
            if (invocation?.ArgumentList == null)
            {
                return null;
            }

            ArgumentListSyntax argumentList = invocation.ArgumentList;

            // If we have method symbol info, find the connection parameter by attribute
            if (context.Method != null)
            {
                IParameterSymbol? connectionParameter = context.Method.Parameters
                    .FirstOrDefault(p => IsConnectionParameter(p));

                if (connectionParameter != null)
                {
                    int connectionParameterIndex = connectionParameter.Ordinal;
                    if (connectionParameterIndex < argumentList.Arguments.Count)
                    {
                        ArgumentSyntax foundArg = argumentList.Arguments[connectionParameterIndex];
                        Console.Error.WriteLine($"[FindConnectionParameterArgument] Returning argument at index {connectionParameterIndex}");
                        return foundArg;
                    }
                }
            }

            // Fallback: Look for an argument with a name containing "connection"
            foreach (ArgumentSyntax arg in argumentList.Arguments)
            {
                string? argName = arg.NameColon?.Name.Identifier.ValueText;

                if (argName?.Contains("connection", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return arg;
                }
            }

            // Last fallback: Look for the first string literal argument (likely the connection)
            foreach (ArgumentSyntax arg in argumentList.Arguments)
            {
                if (arg.Expression is LiteralExpressionSyntax literal &&
                    literal.Token.IsKind(SyntaxKind.StringLiteralToken))
                {
                    return arg;
                }
            }

            return null;
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
            Console.Error.WriteLine($"[FindConnectionParameterArgument] Error: {ex.Message}");
            Console.Error.WriteLine($"[FindConnectionParameterArgument] Stack: {ex.StackTrace}");
            return null;
        }
    }

    /// <summary>
    /// Appends parameter values section with clickable links.
    /// </summary>
    private static void AppendParameterValues(StringBuilder result, List<HoverHandlerTypes.ParameterValue> values, HoverHandlerTypes.ParameterContext context, bool isConnection)
    {
        string sectionTitle = isConnection ? "**Possible values:**" : "**Dynamic values:**";
        result.AppendLine(sectionTitle);

        Console.Error.WriteLine($"[AppendParameterValues] isConnection={isConnection}, values.Count={values.Count}");

        if (values.Count != 0)
        {
            result.AppendLine();

            // For connection parameters, find the actual connection argument to insert into
            ArgumentSyntax targetArgument = context.ArgumentSyntax;
            if (isConnection)
            {
                ArgumentSyntax? connectionArg = FindConnectionParameterArgument(context);
                if (connectionArg != null)
                {
                    targetArgument = connectionArg;
                }
            }

            foreach (HoverHandlerTypes.ParameterValue value in values)
            {
                string commandUri = CreateInsertValueCommandUri(targetArgument, value.Value, isConnection, value.Description);
                result.AppendLine($"- [{value.Description}]({commandUri})");
            }
        }
        else if (!isConnection)
        {
            result.AppendLine();
            result.AppendLine("*No values returned — check the **Connector SDK LSP** output channel for details.*");
        }
    }

    /// <summary>
    /// Extracts parameter documentation from context.
    /// </summary>
    private static string ExtractParameterDocFromContext(HoverHandlerTypes.ParameterContext context)
    {
        if (context.Method == null || context.Parameter == null)
        {
            return string.Empty;
        }

        string? methodDoc = context.Method.GetDocumentationCommentXml();
        return ExtractParameterDocumentation(methodDoc, context.Parameter.Name ?? string.Empty);
    }

    /// <summary>
    /// Gets possible connection values from all loaded connections (managed API and DirectClient).
    /// </summary>
    private IEnumerable<HoverHandlerTypes.ParameterValue> GetConnectionParameterValues()
    {
        ConnectionsConfig? connections = connectionsService.GetConnections();

        return ConnectionsHelper.GetAllConnections(connections)
            .Select(entry => new HoverHandlerTypes.ParameterValue
            {
                Value = $"\"{entry.Key}\"",
                Description = entry.Key,
            });
    }

    /// <summary>
    /// Infers an operation name from common parameter name patterns.
    /// </summary>
    private static string? InferOperationFromParameterName(string? parameterName)
    {
        if (string.IsNullOrEmpty(parameterName))
        {
            return null;
        }

        string lower = parameterName.ToLowerInvariant();

        // Common patterns for Microsoft Forms
        if (lower.Contains("formid") || lower.Contains("form_id"))
        {
            return "ListForms";
        }

        // Common patterns for Teams
        return lower.Contains("teamid") || lower.Contains("team_id")
            ? "GetAllTeams"
            : lower.Contains("channelid") || lower.Contains("channel_id") ? "GetChannelsForGroup" : null;
    }

    private static IMethodSymbol? TryResolveMethodSymbol(InvocationExpressionSyntax invocation, SemanticModel semanticModel, SymbolInfo initialSymbolInfo)
    {
        if (initialSymbolInfo.Symbol is IMethodSymbol direct)
        {
            return direct;
        }

        IMethodSymbol? candidate = initialSymbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
        if (candidate != null)
        {
            return candidate;
        }

        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            // Try to get type info of the expression being accessed
            _ = semanticModel.GetTypeInfo(memberAccess.Expression);

            SymbolInfo nameInfo = semanticModel.GetSymbolInfo(memberAccess.Name);

            if (nameInfo.Symbol is IMethodSymbol nameMethod)
            {
                return nameMethod;
            }

            candidate = nameInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
            if (candidate != null)
            {
                return candidate;
            }

            SymbolInfo memberInfo = semanticModel.GetSymbolInfo(memberAccess);
            if (memberInfo.Symbol is IMethodSymbol memberMethod)
            {
                return memberMethod;
            }

            candidate = memberInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
            if (candidate != null)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Tries to get connector operation information from a method's attributes.
    /// This is a simplified version of the existing method in CodeLensHandler.
    /// </summary>
    private static (string? ConnectorType, string? ConnectorName) TryGetConnectorOperationInfo(IMethodSymbol method)
    {
        string? connectorType = null;
        string? connectorName = null;

        foreach (AttributeData attr in method.GetAttributes())
        {
            string name = attr.AttributeClass?.Name ?? string.Empty;
            if (name.Equals("ConnectorOperationAttribute", StringComparison.Ordinal) ||
                name.Equals("ConnectorOperation", StringComparison.Ordinal))
            {
                // Named arguments first: Type and ConnectorName
                try
                {
                    foreach (KeyValuePair<string, TypedConstant> kvp in attr.NamedArguments)
                    {
                        string key = kvp.Key;

                        if (key.Equals("ConnectorName", StringComparison.Ordinal) ||
                            key.Equals("Connector", StringComparison.Ordinal) ||
                            key.EndsWith("ConnectorName", StringComparison.Ordinal))
                        {
                            if (kvp.Value.Value is string s && !string.IsNullOrWhiteSpace(s))
                            {
                                connectorName = s;
                            }
                        }
                        else if (key.Equals("Type", StringComparison.Ordinal) ||
                                 key.Equals("ConnectorType", StringComparison.Ordinal) ||
                                 key.EndsWith("Type", StringComparison.Ordinal))
                        {
                            connectorType = GetEnumNameOrValue(kvp.Value);
                        }
                    }
                }
                catch
                { /* ignore logging failures */
                }

                break;
            }
        }

        return (connectorType, connectorName);
    }

    /// <summary>
    /// Gets enum name or value from a typed constant.
    /// </summary>
    private static string? GetEnumNameOrValue(TypedConstant constant)
    {
        try
        {
            if (constant.IsNull)
            {
                return null;
            }

            if (constant.Type is INamedTypeSymbol enumType && enumType.TypeKind == TypeKind.Enum)
            {
                object? val = constant.Value;
                foreach (IFieldSymbol member in enumType.GetMembers().OfType<IFieldSymbol>())
                {
                    if (member.HasConstantValue && Equals(member.ConstantValue, val))
                    {
                        return member.Name;
                    }
                }
            }

            return constant.Value?.ToString();
        }
        catch
        {
            return null;
        }
    }

    // Helper functions
    private static (bool HasDynamicValues, string? OperationName) GetDynamicValuesAttribute(IParameterSymbol parameter)
    {
        foreach (AttributeData attr in parameter.GetAttributes())
        {
            string attrName = attr.AttributeClass?.Name ?? string.Empty;

            if (attrName.Equals("DynamicValuesAttribute", StringComparison.Ordinal) ||
                attrName.Equals("DynamicValues", StringComparison.Ordinal))
            {
                // The operation name is the first constructor argument
                if (attr.ConstructorArguments.Length > 0)
                {
                    string? operationName = attr.ConstructorArguments[0].Value?.ToString();
                    if (!string.IsNullOrEmpty(operationName))
                    {
                        return (true, operationName);
                    }
                }
            }
        }

        return (false, null);
    }

    /// <summary>
    /// Fetches dynamic values from an API endpoint based on the operation name.
    /// </summary>
    private async Task<IEnumerable<HoverHandlerTypes.ParameterValue>?> FetchDynamicValuesAsync(
        string operationName,
        HoverHandlerTypes.ParameterContext context)
    {
        try
        {
            await Console.Error.WriteLineAsync($"[DynamicValues] Fetching values for operation '{operationName}'");

            // Try to get connector information from the method if available
            string? connectorName = null;
            string? connectorType = null;

            if (context.Method != null)
            {
                (connectorType, connectorName) = TryGetConnectorOperationInfo(context.Method);
            }

            // If we couldn't get connector from method attributes, try to infer from context
            if (string.IsNullOrEmpty(connectorName))
            {
                connectorName = InferConnectorNameFromOperation(operationName, context);
            }

            if (string.IsNullOrEmpty(connectorName))
            {
                await Console.Error.WriteLineAsync($"[DynamicValues] Could not determine connector name for operation '{operationName}'");
                return null;
            }

            // Extract connection name from the invocation
            string? connectionName = ExtractConnectionName(context);

            // Validate the extracted connection name actually exists in connections config.
            // For DirectClient SDK, ExtractConnectionName may return a method argument value
            // (e.g., a site URL) instead of a connection key name.
            if (!string.IsNullOrEmpty(connectionName) &&
                !DynamicValuesHelper.IsValidConnectionKey(connectionsService, connectionName))
            {
                await Console.Error.WriteLineAsync($"[DynamicValues] Extracted value is not a known connection key — clearing for fallback");
                connectionName = null;
            }

            // Fallback for DirectClient SDK: connection is configured via DI, not in method call.
            // Only auto-resolve when exactly one connection matches the connector type. See issue #21.
            if (string.IsNullOrEmpty(connectionName))
            {
                connectionName = DynamicValuesHelper.ResolveConnectionByConnectorType(connectionsService, connectorName);
                if (!string.IsNullOrEmpty(connectionName))
                {
                    await Console.Error.WriteLineAsync($"[DynamicValues] Resolved connection '{connectionName}' by connector type '{connectorName}' (single match)");
                }
                else
                {
                    int matchCount = DynamicValuesHelper.GetConnectionCountForConnector(connectionsService, connectorName);
                    if (matchCount > 1)
                    {
                        await Console.Error.WriteLineAsync($"[DynamicValues] {matchCount} connections match connector '{connectorName}' — cannot determine which client instance uses");
                    }
                }
            }

            if (string.IsNullOrEmpty(connectionName))
            {
                await Console.Error.WriteLineAsync($"[DynamicValues] No connection found for connector '{connectorName}'");
                return null;
            }

            // Check if values are already in the LSP store
            List<DynamicValueItem>? cachedValues = lspStore.DynamicData.Get(connectorName, operationName, connectionName);
            if (cachedValues != null)
            {
                return cachedValues.Select(v => new HoverHandlerTypes.ParameterValue
                {
                    Value = v.Value,
                    Description = v.Description,
                });
            }

            // Build API URL based on connector, operation and api service config
            string? apiUrl = BuildDynamicApiUrl(connectorName, operationName, context, connectionName);
            DynamicOperationMetadata? metadata = DynamicOperationsRegistry.GetOperationMetadata(connectorName, operationName);

            if (string.IsNullOrEmpty(apiUrl))
            {
                await Console.Error.WriteLineAsync($"[DynamicValues] Could not build API URL for '{connectorName}:{operationName}'");
                return null;
            }

            if (metadata == null)
            {
                await Console.Error.WriteLineAsync($"[DynamicValues] Could not find metadata for '{connectorName}:{operationName}' — check DynamicOperationsMetadata.json");
                return null;
            }

            // Determine if this is a DirectClient connection (calls operation path directly)
            // vs ARM management connection (uses /dynamicInvoke POST wrapper)
            ConnectionsConfig? conns = connectionsService.GetConnections();
            string? runtimeUrl = ConnectionsHelper.GetDirectClientRuntimeUrl(conns, connectionName);
            bool isDirectClient = !string.IsNullOrEmpty(runtimeUrl);

            IEnumerable<HoverHandlerTypes.ParameterValue>? values;

            // TODO: Thread the hover request CancellationToken through the full call chain
            // to FetchDynamicValuesDirectAsync and the ARM invoke path. Currently uses default.
            if (isDirectClient)
            {
                // DirectClient: call the operation path directly with the correct HTTP method
                values = await FetchDynamicValuesDirectAsync(connectorName, apiUrl, metadata);
            }
            else
            {
                // ARM management: use the /dynamicInvoke POST wrapper
                HoverHandlerTypes.DynamicInvokePayload? payload = BuildDynamicApiPayload(connectorName, operationName, context);
                if (payload == null)
                {
                    await Console.Error.WriteLineAsync($"[DynamicValues] Could not build payload for '{connectorName}:{operationName}'");
                    return null;
                }

                values = await FetchDynamicValuesViaInvokeAsync(connectorName, apiUrl, payload);
            }

            // Store the values in the LSP store for later use by CompletionHandler
            if (values != null)
            {
                var storeItems = values.Select(v => new DynamicValueItem
                {
                    Value = v.Value,
                    Description = v.Description,
                }).ToList();
                lspStore.DynamicData.Set(connectorName, operationName, connectionName, storeItems);
            }

            return values;
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
            await Console.Error.WriteLineAsync($"[DynamicValues] Error fetching values for '{operationName}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Infers the connector name from the operation name and context when method attributes aren't available.
    /// </summary>
    private static string? InferConnectorNameFromOperation(string operationName, HoverHandlerTypes.ParameterContext context)
    {
        // Common operation name patterns
        if (operationName.Contains("Form", StringComparison.OrdinalIgnoreCase))
        {
            return "microsoftforms";
        }

        if (operationName.Contains("Team", StringComparison.OrdinalIgnoreCase))
        {
            return "teams";
        }

        if (operationName.Contains("Mail", StringComparison.OrdinalIgnoreCase) ||
            operationName.Contains("Outlook", StringComparison.OrdinalIgnoreCase))
        {
            return "outlook";
        }

        if (operationName.Contains("Office", StringComparison.OrdinalIgnoreCase))
        {
            return "office365";
        }

        if (operationName.Contains("Weather", StringComparison.OrdinalIgnoreCase))
        {
            return "msnweather";
        }

        // Try to infer from the containing type name (e.g., SharePointOnlineClient → sharepointonline)
        if (context.Method?.ContainingType != null)
        {
            string containingType = context.Method.ContainingType.Name;

            // DirectClient SDK generates classes named {ConnectorApiName}Client or {ConnectorApiName}Extensions
            if (containingType.EndsWith("Client", StringComparison.Ordinal) && containingType.Length > "Client".Length)
            {
                return containingType.Substring(0, containingType.Length - "Client".Length).ToLowerInvariant();
            }

            if (containingType.EndsWith("Extensions", StringComparison.Ordinal) && containingType.Length > "Extensions".Length)
            {
                return containingType.Substring(0, containingType.Length - "Extensions".Length).ToLowerInvariant();
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts the connection name from the parameter context by looking for the parameter with [ConnectionName] attribute.
    /// </summary>
    private static string? ExtractConnectionName(HoverHandlerTypes.ParameterContext context)
    {
        return ExtractConnectionNameFromMethodCall(context);
    }

    private async Task<IEnumerable<HoverHandlerTypes.ParameterValue>?> FetchDynamicValuesDirectAsync(string connectorName, string apiUrl, DynamicOperationMetadata metadata, CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            await Console.Error.WriteLineAsync($"[FetchDynamicValuesDirect] {metadata.Method.ToUpperInvariant()} {apiUrl}");

            HoverHandlerTypes.DynamicValuesListResponse? listResponse;

            if (string.Equals(metadata.Method, "get", StringComparison.OrdinalIgnoreCase))
            {
                listResponse = await apiService.GetJsonAsync<HoverHandlerTypes.DynamicValuesListResponse>(apiUrl, cancellationToken);
            }
            else
            {
                // For POST operations, send the query parameters as the body
                object payload = metadata.QueryParameters.Count > 0 ? metadata.QueryParameters : new { };
                listResponse = await apiService.PostJsonAsync<HoverHandlerTypes.DynamicValuesListResponse>(apiUrl, payload, cancellationToken);
            }

            if (listResponse?.Value == null || listResponse.Value.Count == 0)
            {
                await Console.Error.WriteLineAsync("[FetchDynamicValuesDirect] No values in response");
                return null;
            }

            stopwatch.Stop();

            await Console.Error.WriteLineAsync($"[FetchDynamicValuesDirect] Got {listResponse.Value.Count} values in {stopwatch.ElapsedMilliseconds}ms");

            return listResponse.Value.Select(item => new HoverHandlerTypes.ParameterValue
            {
                Value = $"\"{item.Name}\"",
                Description = item.DisplayName ?? item.Name ?? "Unknown",
            });
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
            await Console.Error.WriteLineAsync($"[FetchDynamicValuesDirect] Error for {connectorName}: {ex.Message}");
            return null;
        }
    }

    private async Task<IEnumerable<HoverHandlerTypes.ParameterValue>?> FetchDynamicValuesViaInvokeAsync(string connectorName, string apiUrl, HoverHandlerTypes.DynamicInvokePayload payload)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            await Console.Error.WriteLineAsync("[FetchDynamicValuesAsync] About to call API...");
            await Console.Error.WriteLineAsync($"[FetchDynamicValuesAsync] API Config - BearerToken is null: {apiService.Config.BearerToken == null}");
            await Console.Error.WriteLineAsync($"[FetchDynamicValuesAsync] API Config - BearerToken length: {apiService.Config.BearerToken?.Length ?? 0}");

            // Call Azure dynamicInvoke API with the payload
            // The body is an array directly, not wrapped in an object
            HoverHandlerTypes.DynamicInvokeResponse<List<HoverHandlerTypes.FormItem>>? dynamicResponse = await apiService.PostJsonAsync<HoverHandlerTypes.DynamicInvokeResponse<List<HoverHandlerTypes.FormItem>>>(apiUrl, payload, CancellationToken.None);

            await Console.Error.WriteLineAsync("[FetchDynamicValuesAsync] API call completed");
            await Console.Error.WriteLineAsync("[FetchDynamicValuesAsync] Response received:");
            await Console.Error.WriteLineAsync($"  - dynamicResponse is null: {dynamicResponse == null}");

            if (dynamicResponse != null)
            {
                await Console.Error.WriteLineAsync($"  - dynamicResponse.Response is null: {dynamicResponse.Response == null}");

                if (dynamicResponse.Response != null)
                {
                    await Console.Error.WriteLineAsync($"  - dynamicResponse.Response.StatusCode: {dynamicResponse.Response.StatusCode ?? "NULL"}");
                    await Console.Error.WriteLineAsync($"  - dynamicResponse.Response.Body is null: {dynamicResponse.Response.Body == null}");

                    if (dynamicResponse.Response.Body != null)
                    {
                        await Console.Error.WriteLineAsync($"  - dynamicResponse.Response.Body.Count: {dynamicResponse.Response.Body.Count}");
                    }
                }
            }

            // Extract the body from the nested response structure
            List<HoverHandlerTypes.FormItem>? forms = dynamicResponse?.Response?.Body;

            if (forms == null || forms.Count == 0)
            {
                await Console.Error.WriteLineAsync("[FetchDynamicValuesAsync] ❌ No forms data in response");
                return null;
            }

            stopwatch.Stop();

            await Console.Error.WriteLineAsync($"[FetchDynamicValuesAsync] ✅ Successfully fetched {forms.Count} forms");

            telemetry.TrackEvent("DynamicValues_Fetched", new Dictionary<string, string>
            {
                { "ConnectorName", connectorName },
                { "ValueCount", forms.Count.ToString() },
            });
            telemetry.TrackMetric("DynamicValues_FetchTime_Ms", stopwatch.ElapsedMilliseconds);

            // Convert forms to parameter values for IntelliSense
            return forms.Select(form =>
            {
                return new HoverHandlerTypes.ParameterValue
                {
                    Value = $"\"{form.Id}\"",
                    Description = form.Title ?? "Untitled Form",
                };
            });
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
            await Console.Error.WriteLineAsync($"[FetchDynamicValuesAsync] ❌ Exception caught: {ex.GetType().Name}");
            await Console.Error.WriteLineAsync($"[FetchDynamicValuesAsync] Error for {connectorName}: {ex.Message}");
            await Console.Error.WriteLineAsync($"[FetchDynamicValuesAsync] Stack trace: {ex.StackTrace}");

            telemetry.TrackException(ex, new Dictionary<string, string>
            {
                { "Handler", "Hover" },
                { "Operation", "FetchDynamicValues" },
                { "ConnectorName", connectorName },
            });

            return null;
        }
    }

    /// <summary>
    /// Builds the API URL for fetching dynamic values based on connector and operation.
    /// Resolves the ARM connection resource name for both managed API and DirectClient connections.
    /// </summary>
    private string? BuildDynamicApiUrl(string connectorName, string operationName, HoverHandlerTypes.ParameterContext context, string? resolvedConnectionName = null)
    {
        ApiServiceConfig apiConfiguration = apiService.Config;

        // Use pre-resolved connection name if available, otherwise extract from syntax
        string? connectionName = resolvedConnectionName ?? ExtractConnectionNameFromMethodCall(context);

        if (string.IsNullOrEmpty(connectionName))
        {
            return null;
        }

        ConnectionsConfig? connections = connectionsService.GetConnections();

        // For DirectClient connections, call the operation path directly on the runtime URL.
        // API Hub runtime URLs don't support /dynamicInvoke — that's an ARM management pattern.
        string? runtimeUrl = ConnectionsHelper.GetDirectClientRuntimeUrl(connections, connectionName);
        if (!string.IsNullOrEmpty(runtimeUrl))
        {
            DynamicOperationMetadata? metadata = DynamicOperationsRegistry.GetOperationMetadata(connectorName, operationName);
            string operationPath = metadata?.Path ?? $"/{operationName}";
            return $"{runtimeUrl.TrimEnd('/')}{operationPath}";
        }

        // For managed API connections, use the ARM management URL pattern.
        string? armConnectionName = ConnectionsHelper.ResolveArmConnectionName(connections, connectionName);

        if (string.IsNullOrEmpty(armConnectionName))
        {
            return null;
        }

        return $"{apiConfiguration.BaseUrl}/subscriptions/{apiConfiguration.SubscriptionId}/resourceGroups/{apiConfiguration.ResourceGroup}/providers/Microsoft.Web/connections/{armConnectionName}/dynamicInvoke?api-version={apiConfiguration.EffectiveApiVersion}";
    }

    /// <summary>
    /// Extracts the connection name from the method call by looking for the parameter with [ConnectionName] attribute.
    /// If method symbol is not available, falls back to extracting the first string literal argument.
    /// For method chains like .Microsoftforms("conn").GetFormResponseById(...), this looks up the chain.
    /// </summary>
    private static string? ExtractConnectionNameFromMethodCall(HoverHandlerTypes.ParameterContext context)
    {
        // If we already have a connection name in the context, use it
        if (!string.IsNullOrEmpty(context.ConnectionName))
        {
            Console.Error.WriteLine($"[ExtractConnectionName] Using connection from context: {context.ConnectionName}");
            return context.ConnectionName;
        }

        // Get the invocation expression from the argument syntax
        InvocationExpressionSyntax? invocation = context.ArgumentSyntax.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation == null)
        {
            Console.Error.WriteLine("[ExtractConnectionName] No invocation found");
            return null;
        }

        Console.Error.WriteLine($"[ExtractConnectionName] Current invocation: {invocation.Expression}");

        // Try to extract from current invocation first
        string? connectionName = ExtractConnectionFromInvocation(invocation, context);
        if (!string.IsNullOrEmpty(connectionName))
        {
            Console.Error.WriteLine($"[ExtractConnectionName] Found in current invocation: {connectionName}");
            return connectionName;
        }

        // For method chains like .Microsoftforms("conn").GetFormResponseById(...)
        // Walk up the member access chain to find the connection parameter
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            Console.Error.WriteLine($"[ExtractConnectionName] Walking up member access chain from: {memberAccess.Name}");

            // The expression on the left might be another invocation
            if (memberAccess.Expression is InvocationExpressionSyntax parentInvocation)
            {
                Console.Error.WriteLine($"[ExtractConnectionName] Found parent invocation: {parentInvocation.Expression}");

                // Look for string literal in parent invocation (e.g., Microsoftforms("conn"))
                connectionName = ExtractFirstStringLiteral(parentInvocation);
                if (!string.IsNullOrEmpty(connectionName))
                {
                    Console.Error.WriteLine($"[ExtractConnectionName] Found in parent invocation: {connectionName}");
                    return connectionName;
                }
            }
        }

        Console.Error.WriteLine("[ExtractConnectionName] No connection found in invocation chain");
        return null;
    }

    /// <summary>
    /// Extracts connection from a single invocation (does not traverse chain).
    /// </summary>
    private static string? ExtractConnectionFromInvocation(InvocationExpressionSyntax invocation, HoverHandlerTypes.ParameterContext context)
    {
        ArgumentListSyntax argumentList = invocation.ArgumentList;
        if (argumentList == null || argumentList.Arguments.Count == 0)
        {
            return null;
        }

        // If we have the method symbol, use it to find the connection parameter
        if (context.Method != null)
        {
            Console.Error.WriteLine($"[ExtractConnectionFromInvocation] Method: {context.Method.Name}, Total parameters: {context.Method.Parameters.Length}");

            // Find the parameter with [ConnectionName] attribute
            IParameterSymbol? connectionParameter = context.Method.Parameters.FirstOrDefault(p => IsConnectionParameter(p));

            if (connectionParameter != null)
            {
                int connectionParameterIndex = connectionParameter.Ordinal;
                if (connectionParameterIndex < argumentList.Arguments.Count)
                {
                    ArgumentSyntax connectionArgument = argumentList.Arguments[connectionParameterIndex];
                    return ExtractConnectionNameFromArgument(connectionArgument);
                }
            }
        }

        // Fallback: Look for the first string literal (likely the connection name)
        return ExtractFirstStringLiteral(invocation);
    }

    /// <summary>
    /// Extracts the first string literal from an invocation's arguments.
    /// </summary>
    private static string? ExtractFirstStringLiteral(InvocationExpressionSyntax invocation)
    {
        if (invocation?.ArgumentList == null)
        {
            return null;
        }

        foreach (ArgumentSyntax arg in invocation.ArgumentList.Arguments)
        {
            if (arg.Expression is LiteralExpressionSyntax literal &&
                literal.Token.IsKind(SyntaxKind.StringLiteralToken))
            {
                return literal.Token.ValueText;
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts the connection name from an argument expression.
    /// </summary>
    private static string? ExtractConnectionNameFromArgument(ArgumentSyntax connectionArgument)
    {
        // Extract the string literal value from the argument
        if (connectionArgument.Expression is LiteralExpressionSyntax literal &&
            literal.Token.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralToken))
        {
            return literal.Token.ValueText;
        }

        // Handle invocation expressions (like method calls that return a string)
        if (connectionArgument.Expression is InvocationExpressionSyntax)
        {
            Console.Error.WriteLine("[ExtractConnectionName] Connection parameter is a method call, cannot extract static value");
            return null;
        }

        // Handle identifier (variable reference)
        if (connectionArgument.Expression is IdentifierNameSyntax identifier)
        {
            Console.Error.WriteLine($"[ExtractConnectionName] Connection parameter is a variable: {identifier.Identifier.ValueText}");

            // We could potentially trace this variable, but for now just return null
            return null;
        }

        return null;
    }

    /// <summary>
    /// Builds the payload object for the dynamicInvoke API call based on connector and operation.
    /// The payload contains the API details (path, method, query parameters) for the dynamic operation.
    /// </summary>
    private static HoverHandlerTypes.DynamicInvokePayload? BuildDynamicApiPayload(string connectorName, string operationName, HoverHandlerTypes.ParameterContext context)
    {
        Console.Error.WriteLine($"[BuildDynamicApiPayload] Building payload for connector: {connectorName}, operation: {operationName}");

        // Get API details for the operation from registry
        DynamicOperationMetadata? metadata = DynamicOperationsRegistry.GetOperationMetadata(connectorName, operationName);

        if (metadata == null)
        {
            Console.Error.WriteLine($"[BuildDynamicApiPayload] No metadata found for {connectorName}:{operationName}");
            return null;
        }

        Console.Error.WriteLine($"[BuildDynamicApiPayload] Found metadata - Path: {metadata.Path}, Method: {metadata.Method}");

        // Build the payload object with nested request structure
        var payload = new HoverHandlerTypes.DynamicInvokePayload
        {
            Request = new HoverHandlerTypes.DynamicInvokeRequest
            {
                Method = metadata.Method.ToLowerInvariant(),
                Path = metadata.Path,
                Queries = metadata.QueryParameters.Count > 0 ? metadata.QueryParameters : null,
            },
        };

        Console.Error.WriteLine($"[BuildDynamicApiPayload] Generated payload: {System.Text.Json.JsonSerializer.Serialize(payload)}");

        return payload;
    }

    /// <summary>
    /// Appends enum values for Input enum types to the hover information.
    /// Shows both the C# enum member names and their EnumMember attribute values if present.
    /// </summary>
    private static void AppendEnumValues(StringBuilder result, INamedTypeSymbol enumType)
    {
        result.AppendLine("**Available Values:**");
        result.AppendLine();

        foreach (IFieldSymbol member in enumType.GetMembers().OfType<IFieldSymbol>())
        {
            if (!member.IsStatic || !member.IsConst)
            {
                continue;
            }

            // Get the EnumMember attribute value if present
            string? enumMemberValue = GetEnumMemberAttributeValue(member);

            if (!string.IsNullOrEmpty(enumMemberValue))
            {
                result.AppendLine($"- `{enumType.Name}.{member.Name}` → API Value: `\"{enumMemberValue}\"`");
            }
            else
            {
                result.AppendLine($"- `{enumType.Name}.{member.Name}`");
            }
        }

        result.AppendLine();
    }

    /// <summary>
    /// Extracts the Value property from an [EnumMember(Value = "...")] attribute if present.
    /// Returns null if the attribute is not found or has no Value property.
    /// </summary>
    private static string? GetEnumMemberAttributeValue(IFieldSymbol member)
    {
        foreach (AttributeData attr in member.GetAttributes())
        {
            string attrName = attr.AttributeClass?.Name ?? string.Empty;

            // Check for EnumMemberAttribute or EnumMember
            if (attrName.Equals("EnumMemberAttribute", StringComparison.Ordinal) ||
                attrName.Equals("EnumMember", StringComparison.Ordinal))
            {
                // Look for the Value named argument
                foreach (KeyValuePair<string, TypedConstant> namedArg in attr.NamedArguments)
                {
                    if (namedArg.Key == "Value" && namedArg.Value.Value is string value)
                    {
                        return value;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Unwraps Expression&lt;Func&lt;T&gt;&gt; to get the inner type T.
    /// This is used to extract enum types from parameters like Expression&lt;Func&lt;CurrentWeatherunitsInput&gt;&gt;.
    /// Note: This duplicates UnwrapCommonWrapperTypes but is kept separate for clarity and potential different behavior.
    /// </summary>
    private static ITypeSymbol UnwrapExpressionFuncType(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol nts)
        {
            return type;
        }

        // Check for Expression<Func<T>>
        if (nts.Name == "Expression" &&
            nts.ContainingNamespace?.ToDisplayString() == "System.Linq.Expressions" &&
            nts.TypeArguments.Length == 1)
        {
            ITypeSymbol inner = nts.TypeArguments[0];

            // Check if inner is Func<T>
            if (inner is INamedTypeSymbol func &&
                func.Name == "Func" &&
                func.ContainingNamespace?.ToDisplayString() == "System" &&
                func.TypeArguments.Length >= 1)
            {
                // Return the last type argument (return type of Func)
                return func.TypeArguments.Last();
            }

            // Expression<T> where T is not Func
            return inner;
        }

        // Direct Func<T> (not wrapped in Expression)
        return nts.Name == "Func" &&
            nts.ContainingNamespace?.ToDisplayString() == "System" &&
            nts.TypeArguments.Length >= 1
            ? nts.TypeArguments.Last()
            : type;
    }
}
