using System.Text.Json.Serialization;

namespace SdkLspServer.Services.CodeLens;

/// <summary>
/// Configuration for CodeLens command names. Allows the VS Code extension client
/// to specify which commands the CodeLens actions should invoke, decoupling the
/// LSP server from any specific VS Code extension.
/// </summary>
public class CodeLensConfig
{
    /// <summary>
    /// The VS Code command name invoked when the user clicks a "Create connection" CodeLens.
    /// Defaults to a generic command name; the client extension should register this command.
    /// </summary>
    [JsonPropertyName("openConnectionViewCommand")]
    public string OpenConnectionViewCommand { get; set; } = "connectorSdk.openConnectionView";

    /// <summary>
    /// Updates configuration values from another instance, preserving existing
    /// values for properties not provided.
    /// </summary>
    public void UpdateFrom(CodeLensConfig? other)
    {
        if (other == null)
        {
            return;
        }

        if (!string.IsNullOrEmpty(other.OpenConnectionViewCommand))
        {
            OpenConnectionViewCommand = other.OpenConnectionViewCommand;
        }
    }
}
