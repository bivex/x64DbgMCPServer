using System;
using System.Text;
using System.Windows.Forms;
using DotNetPlugin.Mcp;
using DotNetPlugin.NativeBindings.SDK;
using x64DbgMCPServer.Properties;

namespace DotNetPlugin {
partial class Plugin {
    private McpCommandDispatcher _commandDispatcher;
    private SimpleMcpServer _mcpServer;

    protected override void SetupMenu ( Menus menus )
    {
        menus.Main
        .AddAndConfigureItem ( "&StartMCPServer", StartMCPServer ).SetIcon ( Resources.AboutIcon ).Parent
        .AddAndConfigureItem ( "&StopMCPServer", StopMCPServer ).SetIcon ( Resources.AboutIcon ).Parent
        .AddSeparator()
        .AddAndConfigureItem ( "&List Available Commands...", ListAvailableCommands ).SetIcon ( Resources.AboutIcon ).Parent
        .AddSeparator()
        .AddAndConfigureItem ( "&About...", OnAboutMenuItem ).SetIcon ( Resources.AboutIcon );
        //.AddAndConfigureItem("&CustomCommand", ExecuteCustomCommand).SetIcon(Resources.AboutIcon).Parent
        //.AddAndConfigureItem("&DotNetDumpProcess", OnDumpMenuItem).SetHotKey("CTRL+F12").Parent
        //.AddAndConfigureSubMenu("sub menu")
        //    .AddItem("sub menu entry1", menuItem => Console.WriteLine($"hEntry={menuItem.Id}"))
        //    .AddSeparator()
        //    .AddItem("sub menu entry2", menuItem => Console.WriteLine($"hEntry={menuItem.Id}"));
    }

    private void ListAvailableCommands(MenuItem menuItem)
    {
        if (_commandDispatcher == null)
        {
            MessageBox.Show(HostWindow, "The MCP server is not running.", "MCP Server", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var commands = _commandDispatcher.GetCommandList();
        if (commands.Count == 0)
        {
            MessageBox.Show(HostWindow, "No MCP commands are available.", "MCP Commands", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("Available MCP Commands:");
        sb.AppendLine("=======================");
        foreach (var command in commands)
        {
            sb.AppendLine($"{command.Key}: {command.Value}");
        }

        MessageBox.Show(HostWindow, sb.ToString(), "Available MCP Commands", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    public void OnAboutMenuItem ( MenuItem menuItem )
    {
        MessageBox.Show ( HostWindow, "x64DbgMCPServer Plugin For x64dbg\nCoded By AgentSmithers", "Info", MessageBoxButtons.OK,
                          MessageBoxIcon.Information );
    }

    public static void OnDumpMenuItem ( MenuItem menuItem )
    {
        if ( !Bridge.DbgIsDebugging() )
        {
            Console.WriteLine ( "You need to be debugging to use this Command" );
            return;
        }
        Bridge.DbgCmdExec ( "DotNetDumpProcess" );
    }

    public static void ExecuteCustomCommand ( MenuItem menuItem )
    {
        if ( !Bridge.DbgIsDebugging() )
        {
            Console.WriteLine ( "You need to be debugging to use this Command" );
            return;
        }
        Bridge.DbgCmdExec ( "DumpModuleToFile" );
    }
    public void StartMCPServer(MenuItem menuItem)
    {
        if (_mcpServer == null)
        {
            _mcpServer = new SimpleMcpServer(typeof(Plugin)); 
            _commandDispatcher = _mcpServer.CommandDispatcher; 
            _mcpServer.Start();
            MessageBox.Show(HostWindow, "MCP Server started.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            MessageBox.Show(HostWindow, "MCP Server is already running.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
    public void StopMCPServer(MenuItem menuItem)
    {
        if (_mcpServer != null)
        {
            _mcpServer.Stop();
            _mcpServer = null;
            _commandDispatcher = null;
            MessageBox.Show(HostWindow, "MCP Server stopped.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            MessageBox.Show(HostWindow, "MCP Server is not running.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
}
