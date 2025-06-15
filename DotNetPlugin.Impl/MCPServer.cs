using DotNetPlugin.NativeBindings.SDK;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web.Script.Serialization;
using System.Threading.Tasks;
using DotNetPlugin.Mcp;
//https://modelcontextprotocol.io/docs/concepts/prompts
//https://prasanthmj.github.io/ai/mcp-go/
namespace DotNetPlugin
{
    class SimpleMcpServer
    {
        private readonly McpHttpServer _httpServer;
        private readonly McpCommandDispatcher _commandDispatcher;
        private readonly Dictionary<string, MethodInfo> _commands = new Dictionary<string, MethodInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly Type _targetType;
        public bool IsActivelyDebugging = false;
        public bool OutputPlugingDebugInformation = true;

        public SimpleMcpServer(Type commandSourceType)
        {
            _targetType = commandSourceType;
            string IPAddress = "+";
            string port = "3001";
            Console.WriteLine("MCP server listening on " + IPAddress + ":" + port);

            _httpServer = new McpHttpServer(IPAddress, port);
            _httpServer.RequestReceived += OnRequest;

            _commandDispatcher = new McpCommandDispatcher(commandSourceType);

            foreach (var method in commandSourceType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var attr = method.GetCustomAttribute<CommandAttribute>();
                if (attr != null)
                    _commands[attr.Name] = method;
            }
        }

        private void OutputDebugInforamtion()
        {
            if (OutputPlugingDebugInformation)
            { }
        }

        private static void ExecuteCommand(string command)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo("cmd.exe", "/c " + command)
                {
                    Verb = "runas",
                    CreateNoWindow = true,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                }
            };
            process.Start();
            process.WaitForExit();
        }

        private bool _isRunning = false;

        public void Start()
        {
            if (_isRunning)
            {
                Console.WriteLine("MCP server is already running.");
                return;
            }

            try
            {
                _httpServer.Start();
                _isRunning = true;
                Console.WriteLine("MCP server started. CurrentlyDebugging: " + Bridge.DbgIsDebugging() + " IsRunning: " + Bridge.DbgIsRunning());
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to start MCP server: " + ex.Message);
            }
        }

        public void Stop()
        {
            if (!_isRunning)
            {
                Console.WriteLine("MCP server is already stopped.");
                _isRunning = false;
                return;
            }

            try
            {
                _httpServer.Stop();
                _isRunning = false;
                Console.WriteLine("MCP server stopped.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to stop MCP server: " + ex.Message);
            }
        }

    public static void PrettyPrintJson(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            string prettyJson = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            var compact = string.Join(Environment.NewLine,
            prettyJson.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd()));

            Console.WriteLine(compact.Replace("{", "").Replace("}", "").Replace("\r", ""));
        }
        catch (JsonException ex)
        {
            Console.WriteLine("Invalid JSON: " + ex.Message);
        }
    }

        static bool pDebug = false;
        private static readonly Dictionary<string, StreamWriter> _sseSessions = new Dictionary<string, StreamWriter>();
        
        private async Task OnRequest(HttpListenerContext ctx)
        {
            if (pDebug)
            {
                Console.WriteLine("=== Incoming Request ===");
                Console.WriteLine($"Method: {ctx.Request.HttpMethod}");
                Console.WriteLine($"URL: {ctx.Request.Url}");
                Console.WriteLine($"Headers:");
                foreach (string key in ctx.Request.Headers)
                {
                    Console.WriteLine($"  {key}: {ctx.Request.Headers[key]}");
                }
                Console.WriteLine("=========================");
            }
            string requestBody = null;

            if (ctx.Request.HttpMethod == "POST")
            {
                var path = ctx.Request.Url.AbsolutePath.ToLowerInvariant();

                if (path.StartsWith("/message"))
                {
                    var sessionId = ctx.Request.QueryString["sessionId"];
                    bool sessionIsValid = false;
                    lock (_sseSessions)
                    {
                        sessionIsValid = !string.IsNullOrWhiteSpace(sessionId) && _sseSessions.ContainsKey(sessionId);
                    }

                    if (!sessionIsValid)
                    {
                        Console.WriteLine($"Bad request for /message: Invalid or missing sessionId '{sessionId}'");
                        ctx.Response.StatusCode = 400;
                        byte[] badReqBuffer = Encoding.UTF8.GetBytes("Invalid or missing sessionId.");
                        ctx.Response.ContentType = "text/plain; charset=utf-8";
                        ctx.Response.ContentLength64 = badReqBuffer.Length;
                        try
                        {
                            ctx.Response.OutputStream.Write(badReqBuffer, 0, badReqBuffer.Length);
                        }
                        catch (Exception writeEx) { Console.WriteLine($"Error writing 400 response: {writeEx.Message}"); }
                        finally { ctx.Response.OutputStream.Close(); }
                        return;
                    }

                    if (ctx.Request.HasEntityBody)
                    {
                        using (var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding))
                        {
                            requestBody = await reader.ReadToEndAsync();
                            if (pDebug) { Debug.WriteLine("jsonBody:" + requestBody); }
                        }
                    }
                    else
                    {
                        if (pDebug) { Console.WriteLine("No body."); }
                    }

                    try
                    {
                        ctx.Response.StatusCode = 202;
                        ctx.Response.ContentType = "text/plain; charset=utf-8";
                        byte[] buffer = Encoding.UTF8.GetBytes("Accepted");
                        ctx.Response.ContentLength64 = buffer.Length;
                        await ctx.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                        await ctx.Response.OutputStream.FlushAsync();
                    }
                    catch (Exception acceptEx)
                    {
                        Console.WriteLine($"Error sending 202 Accepted: {acceptEx.Message}");
                        CleanupSseSession(sessionId);
                        return;
                    }

                    Dictionary<string, object> json = null;
                    object rpcId = null;
                    string method = null;

                    try
                    {
                        if (string.IsNullOrWhiteSpace(requestBody))
                        {
                            throw new JsonException("Request body is empty or whitespace.");
                        }
                        json = _jsonSerializer.Deserialize<Dictionary<string, object>>(requestBody);

                        if (json == null)
                        {
                            throw new JsonException("Failed to deserialize JSON body.");
                        }

                        json.TryGetValue("id", out rpcId);
                        if (!json.TryGetValue("method", out object methodObj) || !(methodObj is string))
                        {
                            var errorMsg = "Invalid JSON RPC: Missing or invalid 'method'.";
                            Console.WriteLine($"Error processing request for session {sessionId}: {errorMsg}");
                            if (rpcId != null) SendSseError(sessionId, rpcId, -32600, errorMsg);
                            return;
                        }
                        method = (string)methodObj;

                        if (pDebug) { Console.WriteLine($"RPC Call | Session: {sessionId}, ID: {rpcId}, Method: {method}"); }

                        if (method == "rpc.discover")
                        {
                            HandleToolsList(sessionId, rpcId);
                        }
                        else if (method == "initialize")
                        {
                            HandleInitialize(sessionId, rpcId, json);
                        }
                        else if (method == "notifications/initialized")
                        {
                            if (pDebug) { Console.WriteLine($"Notification 'initialized' received for session {sessionId}."); }
                        }
                        else if (method == "tools/list")
                        {
                            HandleToolsList(sessionId, rpcId);
                        }
                        else if (method == "tools/call")
                        {
                            HandleToolCall(sessionId, rpcId, json);
                        }
                        else if (method == "prompts/list")
                        {
                            HandlePromptsList(sessionId, rpcId);
                        }
                        else if (method == "prompts/get")
                        {
                            HandlePromptsGet(sessionId, rpcId, json);
                        }
                        else if (method == "resources/list")
                        {
                            HandleResourcesList(sessionId, rpcId);
                        }
                        else if (_commands.TryGetValue(method, out var methodInfo))
                        {
                            Console.WriteLine($"Warning: Received legacy-style direct command call '{method}' for session {sessionId}. Consider using 'tools/call'.");
                            SendSseError(sessionId, rpcId, -32601, $"Direct command calls are deprecated. Use 'tools/call' for method '{method}'.");
                        }
                        else
                        {
                            Console.WriteLine($"Unknown method '{method}' received for session {sessionId}");
                            SendSseError(sessionId, rpcId, -32601, $"Method not found: {method}");
                        }
                    }
                    catch (JsonException jsonEx)
                    {
                        Console.WriteLine($"JSON Error processing request for session {sessionId}: {jsonEx.Message}");
                        SendSseError(sessionId, rpcId, -32700, $"Parse error: Invalid JSON received. ({jsonEx.Message})");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error processing method '{method ?? "unknown"}' for session {sessionId}: {ex}");
                        SendSseError(sessionId, rpcId, -32603, $"Internal error processing method '{method ?? "unknown"}': {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine($"POST request to unknown path: {path}");
                    ctx.Response.StatusCode = 404;
                    ctx.Response.OutputStream.Close();
                }
            }
            else if (ctx.Request.HttpMethod == "GET")
            {
                var path = ctx.Request.Url.AbsolutePath.ToLowerInvariant();

                if (path.EndsWith("/sse/") || path.EndsWith("/sse"))
                {
                    ctx.Response.ContentType = "text/event-stream; charset=utf-8";
                    ctx.Response.StatusCode = 200;
                    ctx.Response.SendChunked = true;
                    ctx.Response.KeepAlive = true;
                    ctx.Response.Headers.Add("Cache-Control", "no-cache");
                    ctx.Response.Headers.Add("X-Accel-Buffering", "no");

                    string sessionId = "";
                    try
                    {
                        using (var rng = RandomNumberGenerator.Create())
                        {
                            byte[] randomBytes = new byte[16];
                            rng.GetBytes(randomBytes);
                            sessionId = Convert.ToBase64String(randomBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
                        }

                        var writer = new StreamWriter(ctx.Response.OutputStream, new UTF8Encoding(false), 1024, leaveOpen: true)
                        {
                            AutoFlush = true
                        };

                        bool added = false;
                        lock (_sseSessions)
                        {
                            if (!_sseSessions.ContainsKey(sessionId))
                            {
                                _sseSessions[sessionId] = writer;
                                added = true;
                            }
                            else
                            {
                                Console.WriteLine($"WARNING: Session ID collision detected for {sessionId}");
                            }
                        }

                        if (added)
                        {
                            Console.WriteLine($"SSE session started: {sessionId}");
                            string messagePath = $"/message?sessionId={sessionId}";
                            await writer.WriteAsync($"event: endpoint\n");
                            await writer.WriteAsync($"data: {messagePath}\n\n");
                        }
                        else
                        {
                            ctx.Response.StatusCode = 500;
                            writer?.Dispose();
                            ctx.Response.OutputStream.Close();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error establishing SSE session or sending handshake: {ex.Message}");
                        try
                        {
                            if (ctx.Response.StatusCode == 200)
                            {
                                ctx.Response.StatusCode = 500;
                            }
                        }
                        catch (Exception statusEx)
                        {
                            Console.WriteLine($"Could not set error status code for SSE setup failure: {statusEx.Message}");
                        }

                        try { ctx.Response.OutputStream.Close(); } catch { }
                        if (!string.IsNullOrEmpty(sessionId)) { CleanupSseSession(sessionId); }
                    }
                }
                else if (path.EndsWith("/discover") || path.EndsWith("/mcp/"))
                {
                    Console.WriteLine("Handling legacy GET /discover or /mcp/");
                    var toolList = new List<object>();
                    lock (_commands)
                    {
                        foreach (var cmd in _commands)
                        {
                            var methodInfo = cmd.Value;
                            var attribute = methodInfo.GetCustomAttribute<CommandAttribute>();
                            if (attribute != null && !attribute.X64DbgOnly && (!attribute.DebugOnly /* || Debugger.IsAttached *//* || Add Bridge checks if needed */))
                            {
                                toolList.Add(new
                                {
                                    name = cmd.Key,
                                    parameters = methodInfo.GetParameters().Select(p => $"{p.ParameterType.Name}").ToArray()
                                });
                            }
                        }
                    }

                    var legacyResponse = new
                    {
                        jsonrpc = "2.0",
                        id = (string)null,
                        result = toolList
                    };
                    var json = _jsonSerializer.Serialize(legacyResponse);
                    var buffer = Encoding.UTF8.GetBytes(json);
                    ctx.Response.ContentType = "application/json; charset=utf-8";
                    ctx.Response.ContentLength64 = buffer.Length;
                    try
                    {
                        await ctx.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                    }
                    catch (Exception writeEx) { Console.WriteLine($"Error writing discover response: {writeEx.Message}"); }
                    finally { ctx.Response.OutputStream.Close(); }
                }
                else
                {
                    Console.WriteLine($"GET request to unknown path: {path}");
                    ctx.Response.StatusCode = 404;
                    ctx.Response.OutputStream.Close();
                }
            }
            else
            {
                Console.WriteLine($"Unsupported HTTP method: {ctx.Request.HttpMethod}");
                ctx.Response.StatusCode = 405;
                ctx.Response.AddHeader("Allow", "GET, POST");
                ctx.Response.OutputStream.Close();
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
            SendSseResult(sessionId, id, result);
        }

        private void HandleToolsList(string sessionId, object id)
        {
            bool isDebuggerAttached = Bridge.DbgIsDebugging();
            var toolsList = _commandDispatcher.GetToolsList(isDebuggerAttached, IsActivelyDebugging);
            SendSseResult(sessionId, id, new { tools = toolsList.ToArray() });
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

                if (!paramsDict.TryGetValue("name", out object nameObj) || !(nameObj is string) || string.IsNullOrWhiteSpace((string)nameObj))
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

                if (pDebug) { Console.WriteLine($"Tool Call: {toolName} with args: {(arguments.Count > 0 ? _jsonSerializer.Serialize(arguments) : "None")}"); }

                var result = _commandDispatcher.HandleToolCall(toolName, arguments, IsActivelyDebugging);
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
            SendSseResult(sessionId, id, callResult);
        }

        private void HandlePromptsList(string sessionId, object id)
        {
            if (pDebug) { Console.WriteLine($"Handling prompts/list for session {sessionId}"); }
            var promptsList = new List<PromptInfo>();
            try
            {
                lock (_prompts)
                {
                        promptsList.AddRange(_prompts);
                }

                var result = new PromptsListResult { prompts = promptsList };
                SendSseResult(sessionId, id, result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling prompts/list for session {sessionId}: {ex}");
                SendSseError(sessionId, id, -32603, $"Internal error handling prompts/list: {ex.Message}");
            }
        }

        private void HandlePromptsGet(string sessionId, object id, Dictionary<string, object> json)
        {
            string promptName = null;
            Dictionary<string, object> arguments = null;
            PromptInfo promptInfo = null;

            try
            {
                if (!json.TryGetValue("params", out object paramsObj) || !(paramsObj is Dictionary<string, object> paramsDict))
                {
                    throw new ArgumentException("Invalid or missing 'params' object for prompts/get");
                }

                if (!paramsDict.TryGetValue("name", out object nameObj) || !(nameObj is string) || string.IsNullOrWhiteSpace((string)nameObj))
                {
                    throw new ArgumentException("Invalid or missing 'name' in prompts/get params");
                }
                promptName = (string)nameObj;

                if (paramsDict.TryGetValue("arguments", out object argsObj) && argsObj is Dictionary<string, object> argsDict)
                {
                    arguments = argsDict;
                }
                else
                {
                    arguments = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                }

                if (pDebug) { Console.WriteLine($"Handling prompts/get: {promptName} with args: {(arguments.Count > 0 ? _jsonSerializer.Serialize(arguments) : "None")}"); }

                lock (_prompts)
                {
                    promptInfo = _prompts.FirstOrDefault(p => p.name.Equals(promptName, StringComparison.OrdinalIgnoreCase));
                }

                if (promptInfo == null)
                {
                    SendSseError(sessionId, id, -32601, $"Prompt not found: {promptName}");
                    return;
                }

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

                var result = new PromptGetResult
                {
                    description = promptInfo.description,
                    messages = generatedMessages
                };
                SendSseResult(sessionId, id, result);

            }
            catch (ArgumentException argEx)
            {
                Console.WriteLine($"Argument Error handling prompts/get for '{promptName ?? "unknown"}' (Session: {sessionId}): {argEx.Message}");
                SendSseError(sessionId, id, -32602, $"Invalid parameters: {argEx.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling prompts/get for '{promptName ?? "unknown"}' (Session: {sessionId}): {ex}");
                SendSseError(sessionId, id, -32603, $"Internal error processing prompt '{promptName ?? "unknown"}': {ex.Message}");
            }
        }

        private void HandleResourcesList(string sessionId, object id)
        {
            if (pDebug) { Console.WriteLine($"Handling resources/list for session {sessionId}"); }
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
                SendSseResult(sessionId, id, result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling resources/list for session {sessionId}: {ex}");
                SendSseError(sessionId, id, -32603, $"Internal error handling resources/list: {ex.Message}");
            }
        }

        private void SendSseResult(string sessionId, object id, object result)
        {
            var response = new JsonRpcResponse<object> { id = id, result = result };
            string jsonData = _jsonSerializer.Serialize(response);
            SendData(sessionId, jsonData);
        }

        private void SendSseError(string sessionId, object id, int code, string message, object data = null)
        {
            var errorPayload = new JsonRpcError { code = code, message = message, data = data };
            var response = new JsonRpcErrorResponse { id = id, error = errorPayload };
            string jsonData = _jsonSerializer.Serialize(response);
            SendData(sessionId, jsonData);
        }

        private void SendData(string sessionId, string jsonData)
        {
            try
            {
                StreamWriter writer;
                bool sessionExists;
                lock (_sseSessions)
                {
                    sessionExists = _sseSessions.TryGetValue(sessionId, out writer);
                }

                if (sessionExists && writer != null)
                {
                    lock (writer)
                    {
                        writer.Write($"data: {jsonData}\n\n");
                        writer.Flush();
                        if (pDebug) { Console.WriteLine($"SSE >>> Session {sessionId}: {jsonData}"); }
                    }
                }
                else
                {
                    Console.WriteLine($"Error: SSE Session {sessionId} not found or writer is null when trying to send data.");
                }
            }
            catch (ObjectDisposedException)
            {
                Console.WriteLine($"SSE Session {sessionId} writer was disposed. Cleaning up.");
                CleanupSseSession(sessionId);
            }
            catch (IOException ioEx)
            {
                Console.WriteLine($"SSE Write Error for session {sessionId}: {ioEx.Message}. Cleaning up.");
                CleanupSseSession(sessionId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error sending SSE data for session {sessionId}: {ex}");
                CleanupSseSession(sessionId);
            }
        }

        private void CleanupSseSession(string sessionId)
        {
            lock (_sseSessions)
            {
                if (_sseSessions.TryGetValue(sessionId, out StreamWriter writer))
                {
                    try
                    {
                        writer?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error disposing writer for session {sessionId}: {ex.Message}");
                    }
                    finally
                    {
                        _sseSessions.Remove(sessionId);
                        Console.WriteLine($"Removed SSE session {sessionId}.");
                    }
                }
            }
        }

        private static readonly JavaScriptSerializer _jsonSerializer = new JavaScriptSerializer();

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

        private readonly Dictionary<string, ResourceInfo> _resources = new Dictionary<string, ResourceInfo>(StringComparer.OrdinalIgnoreCase)
        {
            {"/files/config.json", new ResourceInfo {
                uri = "/files/config.json", name = "Configuration File", description = "Server-side configuration in JSON format", mimeType = "application/json"
            }},
            {"/images/logo.png", new ResourceInfo {
                uri = "/images/logo.png", name = "Logo Image", description = "Company logo", mimeType = "image/png"
            }}
        };

        private readonly Dictionary<string, ResourceTemplateInfo> _resourceTemplates = new Dictionary<string, ResourceTemplateInfo>(StringComparer.OrdinalIgnoreCase)
        {
            {"/logs/{date}", new ResourceTemplateInfo {
                uriTemplate = "/logs/{date}", name = "Log File by Date", description = "Retrieve logs for a specific date (YYYY-MM-DD)", mimeType = "text/plain"
            }}
        };

        public class PromptArgument
        {
            public string name { get; set; }
            public string description { get; set; }
            public bool? required { get; set; }
        }

        public class PromptContentTemplate
        {
            public string type { get; set; } = "text";
            public string text { get; set; }
        }

        public class PromptMessageTemplate
        {
            public string role { get; set; }
            public PromptContentTemplate content { get; set; }
        }

        public class PromptInfo
        {
            public string name { get; set; }
            public string description { get; set; }
            public List<PromptArgument> arguments { get; set; }
            public List<PromptMessageTemplate> messageTemplates { get; set; }
        }

        public class ResourceInfo
        {
            public string uri { get; set; }
            public string name { get; set; }
            public string description { get; set; }
            public string mimeType { get; set; }
        }

        public class ResourceTemplateInfo
        {
            public string uriTemplate { get; set; }
            public string name { get; set; }
            public string description { get; set; }
            public string mimeType { get; set; }
        }

        public class JsonRpcResponse<T>
        {
            public string jsonrpc { get; set; } = "2.0";
            public object id { get; set; }
            public T result { get; set; }
        }

        public class JsonRpcErrorResponse
        {
            public string jsonrpc { get; set; } = "2.0";
            public object id { get; set; }
            public JsonRpcError error { get; set; }
        }

        public class JsonRpcError
        {
            public int code { get; set; }
            public string message { get; set; }
            public object data { get; set; }
        }

        public class PromptsListResult
        {
            public List<PromptInfo> prompts { get; set; }
        }

        public class PromptGetResult
        {
            public string description { get; set; }
            public List<object> messages { get; set; }
        }

        public class ResourceListResult
        {
            public List<object> resources { get; set; }
        }

        public class FinalPromptMessage
        {
            public string role { get; set; }
            public FinalPromptContent content { get; set; }
        }

        public class FinalPromptContent
        {
            public string type { get; set; }
            public string text { get; set; }
        }
    }
}
