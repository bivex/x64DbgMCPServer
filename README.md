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

| Command | Description | Example |
|---|---|---|
| `StartMCPServer` | Starts the MCP server. | `StartMCPServer` |
| `StopMCPServer` | Stops the MCP server. | `StopMCPServer` |
| `ExecuteDebuggerCommand` | Executes a debugger command synchronously using x64dbg's command engine. | `ExecuteDebuggerCommand command=init C:\\Path\\To\\Program.exe` |
| `ListDebuggerCommands` | Lists available debugger commands by subject (e.g., debugcontrol, gui, search, threadcontrol). | `ListDebuggerCommands subject=gui` |
| `DbgValFromString` | Converts a debugger value string (e.g., "$pid") to its numerical representation. | `DbgValFromString value=$pid` |
| `WriteMemToAddress` | Writes a sequence of bytes to a specified memory address. | `WriteMemToAddress address=0x12345678, byteString=0F FF 90` |
| `CommentOrLabelAtAddress` | Adds a comment or label to a specified memory address. | `CommentOrLabelAtAddress address=0x12345678, value=MyLabel, mode=Label` |
| `SetBreakpoint` | Sets a software breakpoint at the specified address (equivalent to `bp [address]`). | `SetBreakpoint address=0x12345678` |
| `ClearBreakpoint` | Removes a software breakpoint at the specified address (equivalent to `bc [address]`). | `ClearBreakpoint address=0x12345678` |
| `ToggleBreakpoint` | Toggles (enables/disables) a breakpoint at the specified address. | `ToggleBreakpoint address=0x12345678` |
| `SetHardwareBreakpoint` | Sets a hardware breakpoint at the specified address (equivalent to `bph [address]`). | `SetHardwareBreakpoint address=0x12345678` |
| `Run` | Continues program execution (equivalent to `run` or `g`). | `Run` |
| `Pause` | Pauses program execution. | `Pause` |
| `StepInto` | Steps into the next function call (equivalent to `sti`). | `StepInto` |
| `StepOver` | Steps over the current function call (equivalent to `sto`). | `StepOver` |
| `StepOut` | Steps out of the current function (equivalent to `sso`). | `StepOut` |
| `AttachToProcess` | Attaches the debugger to an existing process by its Process ID (PID). | `AttachToProcess pid=1234` |
| `DetachFromProcess` | Detaches the debugger from the current process. | `DetachFromProcess` |
| `TerminateProcess` | Terminates the currently debugged process (equivalent to `kill`). | `TerminateProcess` |
| `SwitchThread` | Switches the debugger's context to the specified thread. | `SwitchThread threadId=123` |
| `FindPattern` | Searches for a given byte pattern in the current module's memory. | `FindPattern pattern=90 89 78` |
| `GetModuleInfo` | Retrieves detailed information about a specific loaded module (address, size, entry point). | `GetModuleInfo moduleName=kernel32.dll` |
| `GetEntryPoint` | Retrieves the entry point address of the current module or program. | `GetEntryPoint` |
| `GetLabel` | Retrieves the label associated with a specified memory address. | `GetLabel addressStr=0x12345678` |
| `GetAllModulesFromMemMap` | Lists all image modules found in the memory map with their base address, size, and path. | `GetAllModulesFromMemMap` |
| `GetCallStack` | Walks and retrieves information about the current call stack frames. | `GetCallStack maxFrames=10` |
| `GetAllActiveThreads` | Retrieves a list of all active threads in the debugged process. | `GetAllActiveThreads` |
| `GetAllRegisters` | Retrieves the current values of all general-purpose registers. | `GetAllRegisters` |
| `ReadDismAtAddress` | Disassembles a specified range of memory starting at a given address. | `ReadDismAtAddress address=0x12345678, byteCount=100` |
| `DumpModuleToFile` | Dumps register state and module disassembly to a specified file. | `DumpModuleToFile pfilepath=C:\\Output.txt` |

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
