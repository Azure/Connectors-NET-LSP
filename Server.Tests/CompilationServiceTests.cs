//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using SdkLspServer.Services;

namespace SdkLspServer.Tests;

[TestClass]
public class CompilationServiceTests
{
    [TestMethod]
    public void GetCompilation_SameTree_ReturnsCachedCompilation()
    {
        // Arrange
        var service = new CompilationService(sdkIndex: null);
        var uri = new Uri("file:///test/document.cs");
        SyntaxTree tree = CSharpSyntaxTree.ParseText("class Foo { }");

        // Act
        (CSharpCompilation first, SemanticModel firstModel) = service.GetCompilation(uri, tree);
        (CSharpCompilation second, SemanticModel secondModel) = service.GetCompilation(uri, tree);

        // Assert — same instance returned via ReferenceEquals fast-path
        Assert.AreSame(first, second, "Expected cached compilation instance to be returned for same SyntaxTree.");
        Assert.AreSame(firstModel, secondModel, "Expected cached semantic model instance to be returned for same SyntaxTree.");
    }

    [TestMethod]
    public void GetCompilation_SameText_DifferentTree_ReturnsModelForCallerTree()
    {
        // Arrange
        var service = new CompilationService(sdkIndex: null);
        var uri = new Uri("file:///test/document.cs");
        SyntaxTree treeA = CSharpSyntaxTree.ParseText("class Foo { }");
        SyntaxTree treeB = CSharpSyntaxTree.ParseText("class Foo { }");

        // Act
        (CSharpCompilation _, SemanticModel modelA) = service.GetCompilation(uri, treeA);
        (CSharpCompilation _, SemanticModel modelB) = service.GetCompilation(uri, treeB);

        // Assert — semantic model must belong to the caller's tree instance
        Assert.AreSame(treeA, modelA.SyntaxTree, "SemanticModel should use the caller's SyntaxTree (A).");
        Assert.AreSame(treeB, modelB.SyntaxTree, "SemanticModel should use the caller's SyntaxTree (B).");
    }

    [TestMethod]
    public void GetCompilation_DifferentSourceText_ReturnsNewCompilation()
    {
        // Arrange
        var service = new CompilationService(sdkIndex: null);
        var uri = new Uri("file:///test/document.cs");
        SyntaxTree treeA = CSharpSyntaxTree.ParseText("class Foo { }");
        SyntaxTree treeB = CSharpSyntaxTree.ParseText("class Bar { }");

        // Act
        (CSharpCompilation first, SemanticModel _) = service.GetCompilation(uri, treeA);
        (CSharpCompilation second, SemanticModel _) = service.GetCompilation(uri, treeB);

        // Assert — different compilation because source text changed
        Assert.AreNotSame(first, second, "Expected a new compilation when source text changes.");
    }

    [TestMethod]
    public void GetCompilation_CoreReferencesArePresent()
    {
        // Arrange
        var service = new CompilationService(sdkIndex: null);
        var uri = new Uri("file:///test/document.cs");
        SyntaxTree tree = CSharpSyntaxTree.ParseText("using System; class Foo { void M() { Console.WriteLine(); } }");

        // Act
        (CSharpCompilation compilation, SemanticModel _) = service.GetCompilation(uri, tree);

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
    public void GetCompilation_SemanticModelUsesCallerTree()
    {
        // Arrange
        var service = new CompilationService(sdkIndex: null);
        var uri = new Uri("file:///test/document.cs");
        SyntaxTree tree = CSharpSyntaxTree.ParseText("class Foo { int Bar = 42; }");

        // Act
        (CSharpCompilation _, SemanticModel model) = service.GetCompilation(uri, tree);

        // Assert — the semantic model must reference the caller's tree
        Assert.AreSame(tree, model.SyntaxTree, "SemanticModel should reference the caller's SyntaxTree.");
        SyntaxNode root = model.SyntaxTree.GetRoot();
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
        SyntaxTree treeA = CSharpSyntaxTree.ParseText("class Foo { }");
        SyntaxTree treeB = CSharpSyntaxTree.ParseText("class Foo { }");

        // Act
        (CSharpCompilation first, SemanticModel _) = service.GetCompilation(uriA, treeA);
        (CSharpCompilation second, SemanticModel _) = service.GetCompilation(uriB, treeB);

        // Assert — different URIs should return different cache entries
        Assert.AreNotSame(first, second, "Expected separate compilations for different document URIs.");
    }

    [TestMethod]
    public void GetCompilation_EvictsPreviousEntry_SameUri()
    {
        // Arrange
        var service = new CompilationService(sdkIndex: null);
        var uri = new Uri("file:///test/document.cs");
        SyntaxTree treeV1 = CSharpSyntaxTree.ParseText("class V1 { }");
        SyntaxTree treeV2 = CSharpSyntaxTree.ParseText("class V2 { }");

        // Act — cache V1, then V2 for the same URI
        service.GetCompilation(uri, treeV1);
        (CSharpCompilation v2Compilation, SemanticModel _) = service.GetCompilation(uri, treeV2);

        // Re-request V1 — should NOT be cached (evicted by V2)
        SyntaxTree treeV1Again = CSharpSyntaxTree.ParseText("class V1 { }");
        (CSharpCompilation v1Again, SemanticModel _) = service.GetCompilation(uri, treeV1Again);

        // Assert — V1 and V2 should be different compilations (V1 was evicted)
        Assert.AreNotSame(v2Compilation, v1Again, "V1 should have been evicted when V2 was cached.");
    }
}
