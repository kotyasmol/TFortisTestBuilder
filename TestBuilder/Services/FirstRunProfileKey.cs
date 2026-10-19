using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace TestBuilder.Services;

/// <summary>In-memory preparation belongs to a profile version, never to a production result.</summary>
internal static class FirstRunProfileKey
{
    public static string Create(string? path, string json)
    {
        var graph = JsonNode.Parse(json)!.AsObject();
        graph.Remove("updatedAt");
        graph.Remove("board");
        StripLayout(graph);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(graph.ToJsonString())));
        return (path == null ? "unsaved" : Path.GetFullPath(path)) + ":" + digest;
    }

    private static void StripLayout(JsonObject graph)
    {
        if (graph["nodes"] is not JsonArray nodes) return;
        foreach (var item in nodes)
        {
            if (item is not JsonObject node) continue;
            node.Remove("x"); node.Remove("y"); node.Remove("color"); node.Remove("boardElements");
            if (node["body"] is JsonObject body) StripLayout(body);
            if (node["bodyGraph"] is JsonObject bodyGraph) StripLayout(bodyGraph);
        }
    }
}
