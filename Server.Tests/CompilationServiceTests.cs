using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using SdkLspServer.Services;

namespace SdkLspServer.Tests;

[TestClass]
public class CompilationServiceTests
{
    [TestMethod]
    public void GetCompilation_SameSourceText_ReturnsCachedCompilation()
    {
        // Arrange
        var service = new CompilationService(sdkIndex: null);
        var uri = new Uri("file:///test/document.cs");
        string sourceText = "class Foo { }";

        // Act
        (CSharpCompilation first, SemanticModel firstModel) = service.GetCompilation(uri, sourceText);
        (CSharpCompilation second, SemanticModel secondModel) = service.GetCompilation(uri, sourceText);

        // Assert — same instance returned from cache
        Assert.AreSame(first, second, "Expected cached compilation to be returned for identical source text.");
        Assert.AreSame(firstModel, secondModel, "Expected cached semantic model to be returned for identical source text.");
    }

    [TestMethod]
    public void GetCompilation_DifferentSourceText_ReturnsNewCompilation()
    {
        // Arrange
        var service = new CompilationService(sdkIndex: null);
        var uri = new Uri("file:///test/document.cs");
        string sourceTextA = "class Foo { }";
        string sourceTextB = "class Bar { }";

        // Act
        (CSharpCompilation first, SemanticModel _) = service.GetCompilation(uri, sourceTextA);
        (CSharpCompilation second, SemanticModel _) = service.GetCompilation(uri, sourceTextB);

        // Assert — different compilation because source text changed
        Assert.AreNotSame(first, second, "Expected a new compilation when source text changes.");
    }

    [TestMethod]
    public void GetCompilation_CoreReferencesArePresent()
    {
        // Arrange
        var service = new CompilationService(sdkIndex: null);
        var uri = new Uri("file:///test/document.cs");
        string sourceText = "using System; class Foo { void M() { Console.WriteLine(); } }";

        // Act
        (CSharpCompilation compilation, SemanticModel _) = service.GetCompilation(uri, sourceText);

        // Assert — compilation should have metadata references (core .NET assemblies)
        Assert.IsTrue(
            compilation.References.Any(),
            "Expected core metadata references to be present in compilation.");

        // Verify at least System.Runtime or mscorlib is in the references
        IEnumerable<string> referenceNames = compilation.References
            .OfType<PortableExecutableReference>()
            .Where(r => r.FilePath != null)
            .Select(r => Path.GetFileName(r.FilePath!));

        bool hasSystemRuntime = referenceNames.Any(name =>
            string.Equals(name, "System.Runtime.dll", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "System.Private.CoreLib.dll", StringComparison.OrdinalIgnoreCase));

        Assert.IsTrue(hasSystemRuntime, "Expected System.Runtime.dll or System.Private.CoreLib.dll in references.");
    }

    [TestMethod]
    public void GetCompilation_SemanticModelIsUsable()
    {
        // Arrange
        var service = new CompilationService(sdkIndex: null);
        var uri = new Uri("file:///test/document.cs");
        string sourceText = "class Foo { int Bar = 42; }";

        // Act
        (CSharpCompilation _, SemanticModel model) = service.GetCompilation(uri, sourceText);

        // Assert — the semantic model should be able to resolve syntax
        SyntaxTree tree = model.SyntaxTree;
        SyntaxNode root = tree.GetRoot();
        Assert.IsNotNull(root, "Expected syntax tree root to be non-null.");
    }

    [TestMethod]
    public void CreateSdkMetadataCompilation_WithoutSdkIndex_ReturnsCompilation()
    {
        // Arrange
        var service = new CompilationService(sdkIndex: null);

        // Act
        CSharpCompilation compilation = service.CreateSdkMetadataCompilation();

        // Assert
        Assert.IsNotNull(compilation, "Expected a compilation even without SDK index.");
        Assert.IsTrue(
            compilation.References.Any(),
            "Expected core metadata references in SDK metadata compilation.");
    }

    [TestMethod]
    public void GetCompilation_DifferentUris_SameText_ReturnsSeparateEntries()
    {
        // Arrange
        var service = new CompilationService(sdkIndex: null);
        var uriA = new Uri("file:///test/a.cs");
        var uriB = new Uri("file:///test/b.cs");
        string sourceText = "class Foo { }";

        // Act
        (CSharpCompilation first, SemanticModel _) = service.GetCompilation(uriA, sourceText);
        (CSharpCompilation second, SemanticModel _) = service.GetCompilation(uriB, sourceText);

        // Assert — different URIs should return different cache entries
        // (though contents are the same, they are separate documents)
        Assert.AreNotSame(first, second, "Expected separate compilations for different document URIs.");
    }
}
