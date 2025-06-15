using System.Collections.Generic;

namespace DotNetPlugin.Mcp {
public class PromptArgument {
    public string name { get; set; }
    public string description { get; set; }
    public bool? required { get; set; }
}

public class PromptContentTemplate {
    public string type { get; set; } = "text";
    public string text { get; set; }
}

public class PromptMessageTemplate {
    public string role { get; set; }
    public PromptContentTemplate content { get; set; }
}

public class PromptInfo {
    public string name { get; set; }
    public string description { get; set; }
    public List<PromptArgument> arguments { get; set; }
    public List<PromptMessageTemplate> messageTemplates { get; set; }
}

public class ResourceInfo {
    public string uri { get; set; }
    public string name { get; set; }
    public string description { get; set; }
    public string mimeType { get; set; }
}

public class ResourceTemplateInfo {
    public string uriTemplate { get; set; }
    public string name { get; set; }
    public string description { get; set; }
    public string mimeType { get; set; }
}

public class JsonRpcResponse<T> {
    public string jsonrpc { get; set; } = "2.0";
    public object id { get; set; }
    public T result { get; set; }
}

public class JsonRpcErrorResponse {
    public string jsonrpc { get; set; } = "2.0";
    public object id { get; set; }
    public JsonRpcError error { get; set; }
}

public class JsonRpcError {
    public int code { get; set; }
    public string message { get; set; }
    public object data { get; set; }
}

public class PromptsListResult {
    public List<PromptInfo> prompts { get; set; }
}

public sealed class PromptGetResult {
    public string description { get; set; }
    public List<object> messages { get; set; } = new List<object>();
}

public sealed class ResourceListResult {
    public List<object> resources { get; set; } = new List<object>();
}

public sealed class FinalPromptMessage {
    public string role { get; set; }
    public FinalPromptContent content { get; set; }
}

public sealed class FinalPromptContent {
    public string type { get; set; } = "text";
    public string text { get; set; }
}
}