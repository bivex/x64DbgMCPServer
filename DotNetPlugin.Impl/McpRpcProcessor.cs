using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Web.Script.Serialization;
using DotNetPlugin.NativeBindings.SDK;
using DotNetPlugin.Mcp;

namespace DotNetPlugin.Mcp
{
    internal class McpRpcProcessor
    {
        private readonly McpCommandDispatcher _commandDispatcher;
        private readonly Action<string, object, object> _sendSseResult;
        private readonly Action<string, object, int, string, object> _sendSseError;
        private readonly Func<string, bool> _isSseSessionValid;
        private readonly JavaScriptSerializer _jsonSerializer = new JavaScriptSerializer();
        private readonly Func<bool> _outputPluginDebugInformation;
        private readonly Func<bool> _isActivelyDebugging;

        public McpRpcProcessor(
            McpCommandDispatcher commandDispatcher,
            Action<string, object, object> sendSseResult,
            Action<string, object, int, string, object> sendSseError,
            Func<string, bool> isSseSessionValid,
            Func<bool> outputPluginDebugInformation,
            Func<bool> isActivelyDebugging)
        {
            _commandDispatcher = commandDispatcher;
            _sendSseResult = sendSseResult;
            _sendSseError = sendSseError;
            _isSseSessionValid = isSseSessionValid;
            _outputPluginDebugInformation = outputPluginDebugInformation;
            _isActivelyDebugging = isActivelyDebugging;
        }

        private void LogRpcCall(string sessionId, object rpcId, string method)
        {
            if (_outputPluginDebugInformation())
            {
                Console.WriteLine("RPC Call | Session: {0}, ID: {1}, Method: {2}", sessionId, rpcId, method);
            }
        }

        internal void ProcessRpcRequest(string sessionId, string requestBody)
        {
            object rpcId = null;
            string method = null;

            try
            {
                if (string.IsNullOrWhiteSpace(requestBody))
                {
                    throw new JsonException("Request body is empty or whitespace.");
                }
                var json = _jsonSerializer.Deserialize<Dictionary<string, object>>(requestBody);

                if (json == null)
                {
                    throw new JsonException("Failed to deserialize JSON body.");
                }

                json.TryGetValue("id", out rpcId);
                if (!json.TryGetValue("method", out object methodObj) || !(methodObj is string) || string.IsNullOrWhiteSpace((string)methodObj))
                {
                    var errorMsg = "Invalid JSON RPC: Missing or invalid 'method'.";
                    Console.WriteLine("Error processing request for session {0}: {1}", sessionId, errorMsg);
                    if (rpcId != null)
                    {
                        _sendSseError(sessionId, rpcId, -32600, errorMsg, null);
                    }
                    return;
                }
                method = (string)methodObj;

                DispatchRpcMethod(sessionId, rpcId, method, json);
            }
            catch (JsonException jsonEx)
            {
                Console.WriteLine("JSON Error processing request for session {0}: {1}", sessionId, jsonEx.Message);
                _sendSseError(sessionId, rpcId, -32700, $"Parse error: Invalid JSON received. ({jsonEx.Message})", null);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error processing method '{0}' for session {1}: {2}", method ?? "unknown", sessionId, ex);
                _sendSseError(sessionId, rpcId, -32603, $"Internal error processing method '{method ?? "unknown"}': {ex.Message}", null);
            }
        }

        private void DispatchRpcMethod(string sessionId, object rpcId, string method, Dictionary<string, object> json)
        {
            LogRpcCall(sessionId, rpcId, method);

            switch (method)
            {
                case "rpc.discover":
                case "tools/list":
                    HandleToolsList(sessionId, rpcId);
                    break;
                case "initialize":
                    HandleInitialize(sessionId, rpcId, json);
                    break;
                case "notifications/initialized":
                    if (_outputPluginDebugInformation())
                    {
                        Console.WriteLine("Notification 'initialized' received for session {0}.", sessionId);
                    }
                    break;
                case "tools/call":
                    HandleToolCall(sessionId, rpcId, json);
                    break;
                case "prompts/list":
                    HandlePromptsList(sessionId, rpcId);
                    break;
                case "prompts/get":
                    HandlePromptsGet(sessionId, rpcId, json);
                    break;
                case "resources/list":
                    HandleResourcesList(sessionId, rpcId);
                    break;
                default:
                    // Assuming _commands is now handled externally or via _commandDispatcher directly
                    Console.WriteLine("Unknown method '{0}' received for session {1}", method, sessionId);
                    _sendSseError(sessionId, rpcId, -32601, $"Method not found: {method}", null);
                    break;
            }
        }

        private void HandleInitialize(string sessionId, object id, Dictionary<string, object> json)
        {
            var result = new
            {
                protocolVersion = "2024-11-05",
                capabilities = new
                {
                    tools = new { },
                    prompts = new { },
                    resources = new { }
                },
                serverInfo = new { name = "AgentSmithers-X64DbgMcpServer", version = "1.0.0" },
                instructions = "Welcome to the .NET x64Dbg MCP Server!"
            };
            _sendSseResult(sessionId, id, result);
        }

        private void HandleToolsList(string sessionId, object id)
        {
            bool isDebuggerAttached = Bridge.DbgIsDebugging();
            var toolsList = _commandDispatcher.GetToolsList(isDebuggerAttached, _isActivelyDebugging());
            _sendSseResult(sessionId, id, new { tools = toolsList.ToArray() });
        }

        private void HandleToolCall(string sessionId, object id, Dictionary<string, object> json)
        {
            string toolName = null;
            Dictionary<string, object> arguments = null;
            string resultText = "An error occurred processing the tool call.";
            bool isError = true;

            try
            {
                if (!json.TryGetValue("params", out object paramsObj) || !(paramsObj is Dictionary<string, object> paramsDict))
                {
                    throw new ArgumentException("Invalid or missing 'params' object for tools/call");
                }

                if (!paramsDict.TryGetValue("name", out object nameObj) || !(nameObj is string)
                        || string.IsNullOrWhiteSpace((string)nameObj))
                {
                    throw new ArgumentException("Invalid or missing 'name' in tools/call params");
                }
                toolName = (string)nameObj;

                if (paramsDict.TryGetValue("arguments", out object argsObj) && argsObj is Dictionary<string, object> argsDict)
                {
                    arguments = argsDict;
                }
                else
                {
                    arguments = new Dictionary<string, object>();
                }

                if (_outputPluginDebugInformation())
                {
                    var serializedArgs = arguments.Count > 0 ? _jsonSerializer.Serialize(arguments) : "None";
                    Console.WriteLine("Tool Call: {0} with args: {1}", toolName, serializedArgs);
                }

                var result = _commandDispatcher.HandleToolCall(toolName, arguments, _isActivelyDebugging());
                resultText = result?.ToString() ?? $"{toolName} executed successfully.";
                isError = false;
            }
            catch (TargetInvocationException tie)
            {
                resultText = $"Error executing tool '{toolName}': {(tie.InnerException?.Message ?? tie.Message)}";
                isError = true;
                Console.WriteLine($"Execution Error in {toolName}: {(tie.InnerException ?? tie)}");
            }
            catch (Exception ex)
            {
                resultText = $"Error processing tool call for '{toolName ?? "unknown"}': {ex.Message}";
                isError = true;
                Console.WriteLine($"Error during tool call {toolName ?? "unknown"}: {ex}");
            }

            var toolContent = new[] { new { type = "text", text = resultText } };
            var callResult = new { content = toolContent, isError = isError };
            _sendSseResult(sessionId, id, callResult);
        }

        private void HandlePromptsList(string sessionId, object id)
        {
            if (_outputPluginDebugInformation())
            {
                Console.WriteLine("Handling prompts/list for session {0}", sessionId);
            }
            var promptsList = new List<PromptInfo>();
            try
            {
                lock (_prompts)
                {
                    promptsList.AddRange(_prompts);
                }

                var result = new PromptsListResult { prompts = promptsList };
                _sendSseResult(sessionId, id, result);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error handling prompts/list for session {0}: {1}", sessionId, ex);
                _sendSseError(sessionId, id, -32603, $"Internal error handling prompts/list: {ex.Message}", null);
            }
        }

        private void HandlePromptsGet(string sessionId, object id, Dictionary<string, object> json)
        {
            string promptName = null;
            Dictionary<string, object> arguments = null;
            PromptInfo promptInfo = null;

            try
            {
                (promptName, arguments) = GetPromptRequestParameters(json, sessionId, id);

                promptInfo = GetPromptInfo(promptName, sessionId, id);
                if (promptInfo == null)
                {
                    return; // Error already sent by GetPromptInfo
                }

                ValidatePromptArguments(promptInfo, arguments, promptName, sessionId, id);

                var generatedMessages = GeneratePromptMessages(promptInfo, arguments);

                var result = new PromptGetResult
                {
                    description = promptInfo.description,
                    messages = generatedMessages
                };
                _sendSseResult(sessionId, id, result);

            }
            catch (ArgumentException argEx)
            {
                Console.WriteLine(
                    "Argument Error handling prompts/get for '{0}' (Session: {1}): {2}",
                    promptName ?? "unknown", sessionId, argEx.Message);
                _sendSseError(sessionId, id, -32602, $"Invalid parameters: {argEx.Message}", null);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error handling prompts/get for '{0}' (Session: {1}): {2}",
                    promptName ?? "unknown", sessionId, ex);
                _sendSseError(sessionId, id, -32603, $"Internal error processing prompt '{promptName ?? "unknown"}': {ex.Message}", null);
            }
        }

        private (string promptName, Dictionary<string, object> arguments) GetPromptRequestParameters(Dictionary<string, object> json, string sessionId, object id)
        {
            if (!json.TryGetValue("params", out object paramsObj) || !(paramsObj is Dictionary<string, object> paramsDict))
            {
                throw new ArgumentException("Invalid or missing 'params' object for prompts/get");
            }

            if (!paramsDict.TryGetValue("name", out object nameObj) || !(nameObj is string)
                    || string.IsNullOrWhiteSpace((string)nameObj))
            {
                throw new ArgumentException("Invalid or missing 'name' in prompts/get params");
            }
            string promptName = (string)nameObj;

            Dictionary<string, object> arguments = null;
            if (paramsDict.TryGetValue("arguments", out object argsObj) && argsObj is Dictionary<string, object> argsDict)
            {
                arguments = argsDict;
            }
            else
            {
                arguments = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            }

            if (_outputPluginDebugInformation())
            {
                Console.WriteLine(
                    "Handling prompts/get: {0} with args: {1}",
                    promptName,
                    (arguments.Count > 0 ? _jsonSerializer.Serialize(arguments) : "None"));
            }
            return (promptName, arguments);
        }

        private PromptInfo GetPromptInfo(string promptName, string sessionId, object id)
        {
            PromptInfo promptInfo = null;
            lock (_prompts)
            {
                promptInfo = _prompts.FirstOrDefault(p => p.name.Equals(promptName, StringComparison.OrdinalIgnoreCase));
            }

            if (promptInfo == null)
            {
                _sendSseError(sessionId, id, -32601, $"Prompt not found: {promptName}", null);
            }
            return promptInfo;
        }

        private void ValidatePromptArguments(PromptInfo promptInfo, Dictionary<string, object> arguments, string promptName, string sessionId, object id)
        {
            if (promptInfo.arguments != null)
            {
                foreach (var requiredArg in promptInfo.arguments.Where(a => a.required == true))
                {
                    object argValue = null;
                    if (arguments == null || !arguments.TryGetValue(requiredArg.name, out argValue) || argValue == null)
                    {
                        throw new ArgumentException($"Missing required argument '{requiredArg.name}' for prompt '{promptName}'.");
                    }
                }
            }
        }

        private List<object> GeneratePromptMessages(PromptInfo promptInfo, Dictionary<string, object> arguments)
        {
            var generatedMessages = new List<object>();
            if (promptInfo.messageTemplates != null)
            {
                foreach (var template in promptInfo.messageTemplates)
                {
                    string originalText = template.content?.text;
                    string substitutedText = originalText ?? "";

                    if (!string.IsNullOrEmpty(substitutedText) && promptInfo.arguments != null)
                    {
                        foreach (var argDef in promptInfo.arguments)
                        {
                            string placeholder = $"{{{argDef.name}}}";
                            if (substitutedText.Contains(placeholder))
                            {
                                object argValueObj = null;
                                string actualArgValueString = "";

                                if (arguments != null && arguments.TryGetValue(argDef.name, out argValueObj) && argValueObj != null)
                                {
                                    actualArgValueString = Convert.ToString(argValueObj);
                                }
                                else if (argDef.required != true)
                                {
                                    actualArgValueString = "";
                                }

                                substitutedText = substitutedText.Replace(placeholder, actualArgValueString);
                            }
                        }

                        string maxLengthPlaceholder = "{maxLengthPlaceholder}";
                        if (substitutedText.Contains(maxLengthPlaceholder))
                        {
                            object maxLengthObj = null;
                            string maxLengthText = "";
                            if (arguments != null && arguments.TryGetValue("maxLength", out maxLengthObj) && maxLengthObj != null)
                            {
                                maxLengthText = $" (max length: {maxLengthObj})";
                            }
                            substitutedText = substitutedText.Replace(maxLengthPlaceholder, maxLengthText);
                        }
                    }

                    generatedMessages.Add(new FinalPromptMessage
                    {
                        role = template.role,
                        content = new FinalPromptContent { type = template.content?.type ?? "text", text = substitutedText }
                    });
                }
            }
            return generatedMessages;
        }

        private void HandleResourcesList(string sessionId, object id)
        {
            if (_outputPluginDebugInformation())
            {
                Console.WriteLine("Handling resources/list for session {0}", sessionId);
            }
            var resourcesList = new List<object>();

            try
            {
                lock (_resources)
                {
                    foreach (var kvp in _resources)
                    {
                        resourcesList.Add(kvp.Value);
                    }
                }

                lock (_resourceTemplates)
                {
                    foreach (var kvp in _resourceTemplates)
                    {
                        resourcesList.Add(kvp.Value);
                    }
                }

                var result = new ResourceListResult { resources = resourcesList };
                _sendSseResult(sessionId, id, result);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error handling resources/list for session {0}: {1}", sessionId, ex);
                _sendSseError(sessionId, id, -32603, $"Internal error handling resources/list: {ex.Message}", null);
            }
        }

        // Nested classes for data structures
        private readonly List<PromptInfo> _prompts = new List<PromptInfo>
        {
            new PromptInfo {
                name = "X64DbgPrompt",
                description = "Prompt used as a default to ask the AI to use the X64Dbg functionality",
                arguments = new List<PromptArgument> { },
                messageTemplates = new List<PromptMessageTemplate> {
                    new PromptMessageTemplate {
                        role = "user",
                        content = new PromptContentTemplate { type = "text", text = "You are an AI assistant with access to an MCP (Model Context Protocol) server. Your goal is to complete tasks by calling the available commands on this server." }
                    }
                }
            }
        };

        private readonly Dictionary<string, ResourceInfo> _resources = new Dictionary<string, ResourceInfo>
        ( StringComparer.OrdinalIgnoreCase )
        {
            {"/files/config.json", new ResourceInfo
                {
                    uri = "/files/config.json", name = "Configuration File", description = "Server-side configuration in JSON format", mimeType = "application/json"
                }
            },
            {
                "/images/logo.png", new ResourceInfo {
                    uri = "/images/logo.png", name = "Logo Image", description = "Company logo", mimeType = "image/png"
                }
            }
        };

        private readonly Dictionary<string, ResourceTemplateInfo> _resourceTemplates = new
        Dictionary<string, ResourceTemplateInfo> ( StringComparer.OrdinalIgnoreCase )
        {
            {"/logs/{date}", new ResourceTemplateInfo
                {
                    uriTemplate = "/logs/{date}", name = "Log File by Date", description = "Retrieve logs for a specific date (YYYY-MM-DD)", mimeType = "text/plain"
                }
            }
        };

        private class PromptArgument
        {
            public string name { get; set; }
            public string description { get; set; }
            public bool? required { get; set; }
        }

        private class PromptContentTemplate
        {
            public string type { get; set; } = "text";
            public string text { get; set; }
        }

        private class PromptMessageTemplate
        {
            public string role { get; set; }
            public PromptContentTemplate content { get; set; }
        }

        private class PromptInfo
        {
            public string name { get; set; }
            public string description { get; set; }
            public List<PromptArgument> arguments { get; set; }
            public List<PromptMessageTemplate> messageTemplates { get; set; }
        }

        private class ResourceInfo
        {
            public string uri { get; set; }
            public string name { get; set; }
            public string description { get; set; }
            public string mimeType { get; set; }
        }

        private class ResourceTemplateInfo
        {
            public string uriTemplate { get; set; }
            public string name { get; set; }
            public string description { get; set; }
            public string mimeType { get; set; }
        }

        private class JsonRpcResponse<T>
        {
            public string jsonrpc { get; set; } = "2.0";
            public object id { get; set; }
            public T result { get; set; }
        }

        private class JsonRpcErrorResponse
        {
            public string jsonrpc { get; set; } = "2.0";
            public object id { get; set; }
            public JsonRpcError error { get; set; }
        }

        private class JsonRpcError
        {
            public int code { get; set; }
            public string message { get; set; }
            public object data { get; set; }
        }

        private class PromptsListResult
        {
            public List<PromptInfo> prompts { get; set; }
        }

        private sealed class PromptGetResult
        {
            public string description { get; set; }
            public List<object> messages { get; set; } = new List<object>();
        }

        private sealed class ResourceListResult
        {
            public List<object> resources { get; set; } = new List<object>();
        }

        private sealed class FinalPromptMessage
        {
            public string role { get; set; }
            public FinalPromptContent content { get; set; }
        }

        private sealed class FinalPromptContent
        {
            public string type { get; set; } = "text";
            public string text { get; set; }
        }
    }
} 