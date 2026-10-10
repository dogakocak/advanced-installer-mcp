using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Server;

namespace AdvancedInstallerMcp.Tools;

[McpServerToolType]
public static class PingTool
{
    [McpServerTool(Name = "ping", ReadOnly = true, Idempotent = true)]
    [Description("Checks that the Advanced Installer MCP server is running. Returns 'pong' and the server version.")]
    public static string Ping()
    {
        var version = typeof(PingTool).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        return $"pong (advanced-installer-mcp {version})";
    }
}
