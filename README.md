# X64Dbg MCP Server (Model Context Protocol)

This project provides a lightweight, self-contained HTTP server plugin for x64dbg (and x96/x32dbg variants) built with C# on the .NET Framework. It serves as a bridge, enabling interactive communication between an MCP client (such as an LLM AI assistant) and the debugger. This allows you to remotely and programmatically send commands to inspect memory, disassemble code, query registers, manipulate labels and comments, and much more.

The plugin offers a clean project structure, a built-in command system, and a simple HTTP listener that exposes various debugger commands through a text-based API. This makes it an ideal starting point for integrating AI-assisted reverse engineering workflows.

## Features
*   ✅ **Cursor and MCP Client Compatible**: Designed for quick and easy integration with MCP-compatible clients like Cursor.
*   ✅ **Self-Hosted HTTP Command Interface**: No external web server frameworks (like ASP.NET Core) required, ensuring a lightweight footprint.
*   ✅ **Lightweight & Zero-Dependency Deployment**: Simple binary deployment for ease of use.
*   ✅ **Modular Command System**: Supports extensible commands with automatic parameter mapping.
*   ✅ **Direct Debugger Interaction**: Seamlessly interact with registers, memory, threads, and perform disassembly operations.
*   ✅ **Bi-directional AI/LLM Support**: Facilitates advanced AI/LLM command and response handling.
*   ✅ **Dynamic Plugin Reloading**: Plugin can be reloaded without restarting x64dbg.
*   ✅ **Extensibility**: Supports custom expression functions and debugger menu extensions.

## Client Integrations

### Cursor Support
To connect Cursor, add the following to your Cursor configuration:
```json
{
  "mcpServers": {
    "AgentSmithers X64Dbg MCP Server": {
      "url": "http://127.0.0.1:3001/sse"
    }
  }
}
```

### Claude Desktop Support
The `MCPProxy STIDO<->SSE Bridge` is required for Claude Desktop integration.
Download it from: [https://github.com/AgentSmithers/MCPProxy-STDIO-to-SSE/tree/master](https://github.com/AgentSmithers/MCPProxy-STDIO-to-SSE/tree/master)

Add the following to your Claude configuration:
```json
{
  "mcpServers": {
    "x64Dbg": {
      "command": "C:\MCPProxy-STDIO-to-SSE.exe",
      "args": ["http://localhost:3001"]
    }
  }
}
```

### Windsurf Support
The `MCPProxy STIDO<->SSE Bridge` is also required for Windsurf.
Download it from: [https://github.com/AgentSmithers/MCPProxy-STDIO-to-SSE/tree/master](https://github.com/AgentSmithers/MCPProxy-STDIO-to-SSE/tree/master)

Add the following to your Windsurf configuration:
```json
{
  "mcpServers": {
    "AgentSmithers x64Dbg STDIO<->SSE": {
      "command": "C:\MCPProxy-STDIO-to-SSE.exe",
      "args": ["http://localhost:3001"]
    }
  }
}
```

*Known Issue*: Context deadline exceeded (timeout) issue may occur with directly using SSE.

## Sample Conversations
*   **AI Tasked with loading a file, counting internal modules, and labeling important material functions:**
    [https://github.com/AgentSmithers/x64DbgMCPServer/blob/master/Sample1](https://github.com/AgentSmithers/x64DbgMCPServer/blob/master/Sample1)
*   **Singleshot Speedhack Identification:**
    [https://github.com/AgentSmithers/x64DbgMCPServer/blob/master/Sample2](https://github.com/AgentSmithers/x64DbgMCPServer/blob/master/Sample2)

## Prerequisites
To build and run this project, you'll need:
*   Visual Studio Build Tools (2019 v16.7 or later)
*   .NET Framework 4.7.2 SDK
*   3F/DllExport

## Getting Started
1.  **Clone or Fork the Project**:
    ```bash
    git clone https://github.com/AgentSmithers/x64DbgMCPServer
    ```
2.  **Download and Run DllExport**:
    Download `DLlExport.bat` from [https://github.com/3F/DllExport/releases/download/1.8/DllExport.bat](https://github.com/3F/DllExport/releases/download/1.8/DllExport.bat) and place it in the root folder of the project. Then, run `DllExport.bat`.
3.  **Configure DllExport GUI**:
    In the DllExport GUI:
    *   Check the `Installed` checkbox.
    *   Set the `Namespace for DllExport` to `System.Runtime.InteropServices`.
    *   Choose the target platform (`x64` or `x86`).
    *   Click `Apply`.
4.  **Build the Solution**:
    Open the solution in Visual Studio and build it.
    *   📌 **Tip**: If `x64DbgMCPServer.dll` is generated in the output folder, rename it to `x64DbgMCPServer.dp64` for x64dbg to load the plugin.
5.  **Deploy the Plugin**:
    Copy the compiled files (from `x64DbgMCPServer\bin\x64\Debug`) into the x64dbg plugin folder (e.g., `x96\release\x64\plugins\x64DbgMCPServer`).
    
6.  **Load and Start the Server**:
    *   Start the x64dbg Debugger.
    *   Navigate to `Plugins` -> `Click "Start MCP Server"`.
    *   Connect to `http://127.0.0.1:3001/sse` with your preferred MCP Client.

Sample Debug log when loaded:

## Sample Commands (using the X64Dbg MCP Client)
The MCP server facilitates powerful AI-assisted reverse engineering. Once the MCP server is running (via the plugin menu in x64dbg), you can issue commands like:
```
ExecuteDebuggerCommand command=init C:\InjectGetTickCount\InjectSpeed.exe
ExecuteDebuggerCommand command="AddFavouriteCommand Log s, NameOfCmd"
ReadDismAtAddress addressStr=0x000000014000153f, byteCount=5
ReadMemAtAddress addressStr=00007FFA1AC81000, byteCount=5
WriteMemToAddress addressStr=0x000000014000153f, byteString=90 90 90 90 90 90
CommentOrLabelAtAddress addressStr=0x000000014000153f, value=Test, mode=Comment
CommentOrLabelAtAddress addressStr=0x000000014000153f, value=
GetAllRegisters
GetLabel addressStr=0x000000014000153f
GetAllActiveThreads
GetAllModulesFromMemMap
GetCallStack
```
These commands return JSON or text-formatted output that's suitable for ingestion by AI models or integration scripts. Example:

## Available Commands

Here is a list of commands supported by the X64Dbg MCP Server plugin:

*   **StartMCPServer**: Start the MCP server.
*   **StopMCPServer**: Stop the MCP server.
*   **ExecuteDebuggerCommand**: Executes a debugger command synchronously using x64dbg's command engine. Example: `ExecuteDebuggerCommand command=init c:\Path\To\Program.exe\r\nNote: See ListDebuggerCommands for list of applicable commands. Once a program is loaded new available functions can be viewed from the tools/list`
*   **ListDebuggerCommands**: Example: `ListDebuggerCommands`
*   **DbgValFromString**: Example: `DbgValFromString value=$pid`
*   **WriteMemToAddress**: Example: `WriteMemToAddress address=0x12345678, byteString=0F FF 90`
*   **CommentOrLabelAtAddress**: Example: `CommentOrLabelAtAddress address=0x12345678, value=LabelTextGoeshere, mode=Label\r\nExample: CommentOrLabelAtAddress address=0x12345678, value=LabelTextGoeshere, mode=Comment\r\n`
*   **SetBreakpoint**: Set a breakpoint at the address. (Equivalent to bp [address]) Example: `SetBreakpoint address=0x12345678`
*   **ClearBreakpoint**: Remove a breakpoint at the address. (Equivalent to bc [address]) Example: `ClearBreakpoint address=0x12345678`
*   **ToggleBreakpoint**: Enable/disable a breakpoint. Example: `ToggleBreakpoint address=0x12345678`
*   **SetHardwareBreakpoint**: Set a hardware breakpoint. (Equivalent to bph [address]) Example: `SetHardwareBreakpoint address=0x12345678`
*   **Run**: Continue program execution. (Equivalent to run or g) Example: `Run`
*   **Pause**: Pause program execution. Example: `Pause`
*   **StepInto**: Step into function. (Equivalent to sti) Example: `StepInto`
*   **StepOver**: Step over function. (Equivalent to sto) Example: `StepOver`
*   **StepOut**: Step out of current function. (Equivalent to sso) Example: `StepOut`
*   **AttachToProcess**: Attach to an existing process by its PID. Example: `AttachToProcess pid=1234`
*   **DetachFromProcess**: Detach from the current process. Example: `DetachFromProcess`
*   **TerminateProcess**: Terminate the debugged process. (Equivalent to kill) Example: `TerminateProcess`
*   **SwitchThread**: Switch to the specified thread. Example: `SwitchThread threadId=123`
*   **FindPattern**: Search for a given byte pattern in memory. Example: `FindPattern pattern=90 89 78`
*   **GetModuleInfo**: Get detailed information about a specific module (address, size, entry point). Example: `GetModuleInfo moduleName=kernel32.dll`
*   **GetEntryPoint**: Get the entry point address of the current module or program. Example: `GetEntryPoint`
*   **GetLabel**: Example: `GetLabel addressStr=0x12345678`
*   **GetAllModulesFromMemMap**: Example: `GetAllModulesFromMemMap`
*   **GetCallStack**: Example: `GetCallStack
Example: GetCallStack, maxFrames=32`
*   **GetAllActiveThreads**: Example: `GetAllActiveThreads`
*   **GetAllRegisters**: Example: `GetAllRegisters`
*   **ReadDismAtAddress**: Example: `ReadDismAtAddress address=0x12345678, byteCount=100`
*   **DumpModuleToFile**: Example: `DumpModuleToFile pfilepath=C:\Output.txt`

## Debugging
The `DotNetPlugin.Impl` project includes build post-commands for faster debugging. Update them to reflect the correct path to your x64dbg installation. Upon rebuilding, x64dbg will auto-load the new plugin, and you can reattach to the x64dbg instance if needed.
```
xcopy /Y /I "$(TargetDir)*.*" "C:\Users\User\Desktop\x96\release\x64\plugins\x64DbgMCPServer"
C:\Users\User\Desktop\x96\release\x64\x64dbg.exe
```

## Development Notes

### How It Works
The MCP server operates by running a simple HTTP listener that routes incoming commands to C# methods marked with the `[Command]` attribute. These methods are designed to perform various debugger-related operations (e.g., memory reads, disassembly, setting breakpoints) and return structured data back to the MCP client.

### Actively Working On
Not every command is fully implemented. I am actively working on extending this project to support full stack, thread, and module dumps for AI querying and analysis.

### Known Issues
*   `ExecuteDebuggerCommand` currently returns `true` if the command was successfully executed by x64dbg, not necessarily based on the command's internal results.
*   The currently compiled version listens on all IPs on port 3001, which requires Administrative privileges. Future releases will aim to detect this and default to listening only on `127.0.0.1` to allow use without administrative privileges.

## Acknowledgements & Integration Notes
One of the most satisfying aspects of this project was overcoming the challenge of building an HTTP server entirely self-contained — no Kestrel, no ASP.NET, just raw `HttpListener` powering your reverse engineering automation.

I plan to continue improving this codebase as part of my journey into AI-assisted analysis, implementation security, and automation tooling.

If you'd like help creating your own integration, extending this plugin, or discussing potential use cases — feel free to reach out (see contact info in the repo or my profile). I'm eager to collaborate and learn with others exploring this space.

💻 Let's reverse engineer smarter. Not harder.

Cheers 🎉
Https://ControllingTheInter.net
