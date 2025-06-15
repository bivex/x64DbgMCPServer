using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DotNetPlugin.Mcp {
public class McpHttpServer {
    private readonly HttpListener _listener = new HttpListener();
    private bool _isRunning = false;

    public event Func<HttpListenerContext, Task> RequestReceived;

    public McpHttpServer ( string ipAddress, string port )
    {
        Console.WriteLine ( $"MCP HTTP server listening on {ipAddress}:{port}" );
        _listener.Prefixes.Add ( $"http://{ipAddress}:{port}/sse/" );
        _listener.Prefixes.Add ( $"http://{ipAddress}:{port}/message/" );
    }

    public void Start()
    {
        if ( _isRunning )
        {
            Console.WriteLine ( "MCP HTTP server is already running." );
            return;
        }

        try
        {
            _listener.Start();
            _listener.BeginGetContext ( OnRequest, null );
            _isRunning = true;
            Console.WriteLine ( "MCP HTTP server started." );
        }
        catch ( Exception ex )
        {
            Console.WriteLine ( $"Failed to start MCP HTTP server: {ex.Message}" );
        }
    }

    public void Stop()
    {
        if ( !_isRunning )
        {
            Console.WriteLine ( "MCP HTTP server is already stopped." );
            return;
        }

        try
        {
            _listener.Stop();
            _isRunning = false;
            Console.WriteLine ( "MCP HTTP server stopped." );
        }
        catch ( Exception ex )
        {
            Console.WriteLine ( $"Failed to stop MCP HTTP server: {ex.Message}" );
        }
    }

    private async void OnRequest ( IAsyncResult ar )
    {
        HttpListenerContext ctx = null;
        try
        {
            ctx = _listener.EndGetContext ( ar );
        }
        catch ( ObjectDisposedException )
        {
            Console.WriteLine ( "Listener was stopped." );
            return;
        }
        catch ( Exception ex )
        {
            Console.WriteLine ( $"Error getting listener context: {ex.Message}" );
            return;
        }

        try
        {
            if ( _listener.IsListening )
            {
                _listener.BeginGetContext ( OnRequest, null );
            }
        }
        catch ( ObjectDisposedException )
        {
            // Listener was stopped between EndGetContext and BeginGetContext, ignore.
        }
        catch ( Exception ex )
        {
            Console.WriteLine ( $"Error restarting listener loop: {ex.Message}" );
        }

        if ( RequestReceived != null )
        {
            await RequestReceived.Invoke ( ctx );
        }
        else
        {
            // If no handler, send a 501 Not Implemented response.
            ctx.Response.StatusCode = 501;
            byte[] buffer = Encoding.UTF8.GetBytes ( "Not Implemented" );
            ctx.Response.ContentLength64 = buffer.Length;
            ctx.Response.ContentType = "text/plain; charset=utf-8";
            ctx.Response.OutputStream.Write ( buffer, 0, buffer.Length );
            ctx.Response.OutputStream.Close();
        }
    }
}
}