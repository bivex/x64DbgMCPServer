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
namespace DotNetPlugin {
internal sealed class SimpleMcpServer {
    private readonly McpHttpServer _httpServer;
    private readonly McpCommandDispatcher _commandDispatcher;
    private readonly Dictionary<string, MethodInfo> _commands = new Dictionary<string, MethodInfo>
    ( StringComparer.OrdinalIgnoreCase );
    private readonly Type _targetType;
    private bool _isActivelyDebugging = false;
    private bool _outputPluginDebugInformation = true;

    internal bool IsActivelyDebugging
    {
        get => _isActivelyDebugging;
        set => _isActivelyDebugging = value;
    }
    private bool OutputPluginDebugInformation => _outputPluginDebugInformation;

    internal McpCommandDispatcher CommandDispatcher => _commandDispatcher;

    private readonly McpRpcProcessor _rpcProcessor;
    private readonly SseSessionManager _sseSessionManager = new SseSessionManager();

    internal SimpleMcpServer ( Type commandSourceType )
    {
        _targetType = commandSourceType;
        string IPAddress = "+";
        string port = "3001";
        Console.WriteLine ( "MCP server listening on " + IPAddress + ":" + port );

        _httpServer = new McpHttpServer ( IPAddress, port );
        _httpServer.RequestReceived += OnRequest;

        _commandDispatcher = new McpCommandDispatcher ( commandSourceType );

        foreach ( var method in commandSourceType.GetMethods ( BindingFlags.Static | BindingFlags.Public |
                  BindingFlags.NonPublic ) )
        {
            var attr = method.GetCustomAttribute<CommandAttribute>();
            if ( attr != null )
            { _commands[attr.Name] = method; }
        }

        _rpcProcessor = new McpRpcProcessor (
            _commandDispatcher,
            SendSseResult,
            (sessionId, id, code, message, data) => SendSseError(sessionId, id, code, message, data),
            _sseSessionManager.IsSseSessionValid,
            () => OutputPluginDebugInformation,
            () => IsActivelyDebugging );
    }

    private void OutputDebugInforamtion()
    {
        if ( OutputPluginDebugInformation )
        { }
    }

    private static void ExecuteCommand ( string command )
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo ( "cmd.exe", "/c " + command )
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

    internal void Start()
    {
        if ( _isRunning )
        {
            Console.WriteLine ( "MCP server is already running." );
            return;
        }

        try
        {
            _httpServer.Start();
            _isRunning = true;
            Console.WriteLine ( "MCP server started. CurrentlyDebugging: " + Bridge.DbgIsDebugging() + " IsRunning: " +
                                Bridge.DbgIsRunning() );
        }
        catch ( Exception ex )
        {
            Console.WriteLine ( "Failed to start MCP server: " + ex.Message );
        }
    }

    internal void Stop()
    {
        if ( !_isRunning )
        {
            Console.WriteLine ( "MCP server is already stopped." );
            _isRunning = false;
            return;
        }

        try
        {
            _httpServer.Stop();
            _isRunning = false;
            Console.WriteLine ( "MCP server stopped." );
        }
        catch ( Exception ex )
        {
            Console.WriteLine ( "Failed to stop MCP server: " + ex.Message );
        }
    }

    internal static void PrettyPrintJson ( string json )
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse ( json );
            string prettyJson = JsonSerializer.Serialize ( doc.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true
            } );

            var compact = string.Join ( Environment.NewLine,
                                        prettyJson.Split ( new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries ).Select ( line => line.TrimEnd() ) );

            Console.WriteLine ( compact.Replace ( "{", "" ).Replace ( "}", "" ).Replace ( "\r", "" ) );
        }
        catch ( JsonException ex )
        {
            Console.WriteLine ( "Invalid JSON: " + ex.Message );
        }
    }

    private static bool s_PDebug = false;

    private async Task OnRequest ( HttpListenerContext ctx )
    {
        if ( s_PDebug )
        {
            LogRequest ( ctx );
        }

        try
        {
            switch ( ctx.Request.HttpMethod )
            {
                case "POST":
                    await HandlePostRequest ( ctx );
                    break;
                case "GET":
                    await HandleGetRequest ( ctx );
                    break;
                default:
                    HandleUnsupportedMethod ( ctx );
                    break;
            }
        }
        catch ( Exception ex )
        {
            Console.WriteLine ( $"[FATAL] Unhandled exception in OnRequest: {ex}" );
            try
            {
                if ( ctx.Response.OutputStream.CanWrite )
                {
                    ctx.Response.StatusCode = ( int ) HttpStatusCode.InternalServerError;
                    ctx.Response.OutputStream.Close();
                }
            }
            catch ( Exception closeEx )
            {
                Console.WriteLine ( $"[FATAL] Could not close response stream after unhandled exception: {closeEx.Message}" );
            }
        }
    }

    private void LogRequest ( HttpListenerContext ctx )
    {
        Console.WriteLine ( "=== Incoming Request ===" );
        Console.WriteLine ( $"Method: {ctx.Request.HttpMethod}" );
        Console.WriteLine ( $"URL: {ctx.Request.Url}" );
        Console.WriteLine ( "Headers:" );
        foreach ( string key in ctx.Request.Headers )
        {
            Console.WriteLine ( $"  {key}: {ctx.Request.Headers[key]}" );
        }
        Console.WriteLine ( "=========================" );
    }

    private async Task HandlePostRequest ( HttpListenerContext ctx )
    {
        var path = ctx.Request.Url.AbsolutePath.ToLowerInvariant();
        if ( path.StartsWith ( "/message" ) )
        {
            await HandleMessageRequest ( ctx );
        }
        else
        {
            SendNotFound ( ctx, $"POST request to unknown path: {path}" );
        }
    }

    private async Task HandleGetRequest ( HttpListenerContext ctx )
    {
        var path = ctx.Request.Url.AbsolutePath.ToLowerInvariant();
        if ( path.EndsWith ( "/sse/" ) || path.EndsWith ( "/sse" ) )
        {
            await HandleSseSetupRequest ( ctx );
        }
        else if ( path.EndsWith ( "/discover" ) || path.EndsWith ( "/mcp/" ) )
        {
            await HandleLegacyDiscoverRequest ( ctx );
        }
        else
        {
            SendNotFound ( ctx, $"GET request to unknown path: {path}" );
        }
    }

    private async Task HandleMessageRequest ( HttpListenerContext ctx )
    {
        var sessionId = ctx.Request.QueryString["sessionId"];
        if ( !_sseSessionManager.IsSseSessionValid ( sessionId ) )
        {
            await SendBadRequestAsync ( ctx, $"Invalid or missing sessionId '{sessionId}'" );
            return;
        }

        string requestBody = await ReadRequestBodyAsync ( ctx );

        try
        {
            await SendAcceptedResponseAsync ( ctx );
        }
        catch ( Exception acceptEx )
        {
            Console.WriteLine ( $"Error sending 202 Accepted: {acceptEx.Message}" );
            _sseSessionManager.CleanupSseSession ( sessionId );
            return;
        }

        _rpcProcessor.ProcessRpcRequest ( sessionId, requestBody );
    }

    private async Task SendBadRequestAsync ( HttpListenerContext ctx, string logMessage )
    {
        Console.WriteLine ( $"Bad request for /message: {logMessage}" );
        ctx.Response.StatusCode = ( int ) HttpStatusCode.BadRequest;
        byte[] badReqBuffer = Encoding.UTF8.GetBytes ( "Invalid or missing sessionId." );
        ctx.Response.ContentType = "text/plain; charset=utf-8";
        ctx.Response.ContentLength64 = badReqBuffer.Length;
        try
        {
            await ctx.Response.OutputStream.WriteAsync ( badReqBuffer, 0, badReqBuffer.Length );
        }
        catch ( Exception writeEx )
        {
            Console.WriteLine ( $"Error writing 400 response: {writeEx.Message}" );
        }
        finally
        {
            ctx.Response.OutputStream.Close();
        }
    }

    private async Task<string> ReadRequestBodyAsync ( HttpListenerContext ctx )
    {
        if ( !ctx.Request.HasEntityBody )
        {
            if ( s_PDebug )
            {
                Console.WriteLine ( "No body." );
            }
            return null;
        }
        using ( var reader = new StreamReader ( ctx.Request.InputStream, ctx.Request.ContentEncoding ) )
        {
            var requestBody = await reader.ReadToEndAsync();
            if ( s_PDebug )
            {
                Debug.WriteLine ( "jsonBody:" + requestBody );
            }
            return requestBody;
        }
    }

    private async Task SendAcceptedResponseAsync ( HttpListenerContext ctx )
    {
        ctx.Response.StatusCode = ( int ) HttpStatusCode.Accepted;
        ctx.Response.ContentType = "text/plain; charset=utf-8";
        byte[] buffer = Encoding.UTF8.GetBytes ( "Accepted" );
        ctx.Response.ContentLength64 = buffer.Length;
        await ctx.Response.OutputStream.WriteAsync ( buffer, 0, buffer.Length );
        await ctx.Response.OutputStream.FlushAsync();
    }

    private async Task HandleSseSetupRequest ( HttpListenerContext ctx )
    {
        ctx.Response.ContentType = "text/event-stream; charset=utf-8";
        ctx.Response.StatusCode = ( int ) HttpStatusCode.OK;
        ctx.Response.SendChunked = true;
        ctx.Response.KeepAlive = true;
        ctx.Response.Headers.Add ( "Cache-Control", "no-cache" );
        ctx.Response.Headers.Add ( "X-Accel-Buffering", "no" );

        string sessionId = "";
        try
        {
            sessionId = _sseSessionManager.GenerateSessionId();
            var writer = new StreamWriter ( ctx.Response.OutputStream, new UTF8Encoding ( false ), 1024, leaveOpen: true )
            {
                AutoFlush = true
            };

            if ( _sseSessionManager.RegisterSseSession ( sessionId, writer ) )
            {
                Console.WriteLine ( $"SSE session started: {sessionId}" );
                string messagePath = $"/message?sessionId={sessionId}";
                await writer.WriteAsync ( $"event: endpoint\n" );
                await writer.WriteAsync ( $"data: {messagePath}\n\n" );
            }
            else
            {
                // This case is for a highly unlikely session ID collision
                ctx.Response.StatusCode = ( int ) HttpStatusCode.InternalServerError;
                writer?.Dispose();
                ctx.Response.OutputStream.Close();
            }
        }
        catch ( Exception ex )
        {
            Console.WriteLine ( $"Error establishing SSE session or sending handshake: {ex.Message}" );
            if ( ctx.Response.StatusCode == ( int ) HttpStatusCode.OK )
            {
                ctx.Response.StatusCode = ( int ) HttpStatusCode.InternalServerError;
            }
            ctx.Response.OutputStream.Close();
            if ( !string.IsNullOrEmpty ( sessionId ) )
            {
                _sseSessionManager.CleanupSseSession ( sessionId );
            }
        }
    }

    private async Task HandleLegacyDiscoverRequest ( HttpListenerContext ctx )
    {
        Console.WriteLine ( "Handling legacy GET /discover or /mcp/" );
        var toolList = new List<object>();
        lock ( _commands )
        {
            foreach ( var cmd in _commands )
            {
                var methodInfo = cmd.Value;
                var attribute = methodInfo.GetCustomAttribute<CommandAttribute>();
                if ( attribute != null && !attribute.X64DbgOnly && !attribute.DebugOnly )
                {
                    toolList.Add ( new
                    {
                        name = cmd.Key,
                        parameters = methodInfo.GetParameters().Select ( p => p.ParameterType.Name ).ToArray()
                    } );
                }
            }
        }

        var legacyResponse = new
        {
            jsonrpc = "2.0",
            id = ( string ) null,
            result = toolList
        };

        var json = _JsonSerializer.Serialize ( legacyResponse );
        var buffer = Encoding.UTF8.GetBytes ( json );
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.ContentLength64 = buffer.Length;

        try
        {
            await ctx.Response.OutputStream.WriteAsync ( buffer, 0, buffer.Length );
        }
        catch ( Exception writeEx )
        {
            Console.WriteLine ( $"Error writing discover response: {writeEx.Message}" );
        }
        finally
        {
            ctx.Response.OutputStream.Close();
        }
    }

    private void SendNotFound ( HttpListenerContext ctx, string logMessage )
    {
        Console.WriteLine ( logMessage );
        ctx.Response.StatusCode = ( int ) HttpStatusCode.NotFound;
        ctx.Response.OutputStream.Close();
    }

    private void HandleUnsupportedMethod ( HttpListenerContext ctx )
    {
        Console.WriteLine ( $"Unsupported HTTP method: {ctx.Request.HttpMethod}" );
        ctx.Response.StatusCode = ( int ) HttpStatusCode.MethodNotAllowed;
        ctx.Response.AddHeader ( "Allow", "GET, POST" );
        ctx.Response.OutputStream.Close();
    }

    private void SendSseResult ( string sessionId, object id, object result )
    {
        var response = new JsonRpcResponse<object> { id = id, result = result };
        string jsonData = _JsonSerializer.Serialize ( response );
        _sseSessionManager.SendData ( sessionId, jsonData );
    }

    private void SendSseError ( string sessionId, object id, int code, string message, object data = null )
    {
        var errorPayload = new JsonRpcError { code = code, message = message, data = data };
        var response = new JsonRpcErrorResponse { id = id, error = errorPayload };
        string jsonData = _JsonSerializer.Serialize ( response );
        _sseSessionManager.SendData ( sessionId, jsonData );
    }

    private static readonly JavaScriptSerializer _JsonSerializer = new JavaScriptSerializer();

    // Nested class for SSE session management
    private class SseSessionManager
    {
        private readonly Dictionary<string, StreamWriter> _sseSessions = new Dictionary<string, StreamWriter>();

        public string GenerateSessionId()
        {
            using ( var rng = RandomNumberGenerator.Create() )
            {
                byte[] randomBytes = new byte[16];
                rng.GetBytes ( randomBytes );
                return Convert.ToBase64String ( randomBytes ).TrimEnd ( '=' ).Replace ( '+', '-' ).Replace ( '/', '_' );
            }
        }

        public bool RegisterSseSession ( string sessionId, StreamWriter writer )
        {
            lock ( _sseSessions )
            {
                if ( _sseSessions.ContainsKey ( sessionId ) )
                {
                    Console.WriteLine ( $"WARNING: Session ID collision detected for {sessionId}" );
                    return false;
                }
                _sseSessions[sessionId] = writer;
                return true;
            }
        }

        public bool IsSseSessionValid ( string sessionId )
        {
            if ( string.IsNullOrWhiteSpace ( sessionId ) )
            {
                return false;
            }
            lock ( _sseSessions )
            {
                return _sseSessions.ContainsKey ( sessionId );
            }
        }

        public void SendData ( string sessionId, string jsonData )
        {
            try
            {
                StreamWriter writer;
                bool sessionExists;
                lock ( _sseSessions )
                {
                    sessionExists = _sseSessions.TryGetValue ( sessionId, out writer );
                }

                if ( sessionExists && writer != null )
                {
                    lock ( writer )
                    {
                        writer.Write ( $"data: {jsonData}\n\n" );
                        writer.Flush();
                        // Optionally add debug output here if needed
                    }
                }
                else
                {
                    Console.WriteLine ( $"Error: SSE Session {sessionId} not found or writer is null when trying to send data." );
                }
            }
            catch ( ObjectDisposedException )
            {
                Console.WriteLine ( $"SSE Session {sessionId} writer was disposed. Cleaning up." );
                CleanupSseSession ( sessionId );
            }
            catch ( IOException ioEx )
            {
                Console.WriteLine ( $"SSE Write Error for session {sessionId}: {ioEx.Message}. Cleaning up." );
                CleanupSseSession ( sessionId );
            }
            catch ( Exception ex )
            {
                Console.WriteLine ( $"Unexpected error sending SSE data for session {sessionId}: {ex}" );
                CleanupSseSession ( sessionId );
            }
        }

        public void CleanupSseSession ( string sessionId )
        {
            StreamWriter writer = null;
            lock ( _sseSessions )
            {
                if ( _sseSessions.TryGetValue ( sessionId, out writer ) )
                {
                    _sseSessions.Remove ( sessionId );
                    Console.WriteLine ( $"Removed SSE session {sessionId}." );
                }
            }
            if (writer != null)
            {
                try
                {
                    writer.Dispose();
                }
                catch ( Exception ex )
                {
                    Console.WriteLine ( $"Error disposing writer for session {sessionId}: {ex.Message}" );
                }
            }
        }
    }
}
}
