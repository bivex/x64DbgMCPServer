using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using DotNetPlugin.NativeBindings;
using DotNetPlugin.NativeBindings.Script;
using DotNetPlugin.NativeBindings.SDK;
using x64DbgMCPServer.Properties;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using static DotNetPlugin.NativeBindings.SDK.Bridge;

namespace DotNetPlugin {
partial class Plugin {

    //Commmand Toolnames should be ^[a-zA-Z0-9_-]{1,64}$
    //[Command("DotNetpluginTestCommand")]
    public static void cbNetTestCommand ( string[] args )
    {
        Console.WriteLine ( ".Net test command!" );
        string empty = string.Empty;
        string Left = Interaction.InputBox ( "Enter value pls", "NetTest", "", -1, -1 );
        if ( Left == null | Operators.CompareString ( Left, "", false ) == 0 )
        { Console.WriteLine ( "cancel pressed!" ); }
        else
        { Console.WriteLine ( $"line: {Left}" ); }
    }

    //[Command("DotNetDumpProcess", DebugOnly = true)]
    public static bool cbDumpProcessCommand ( string[] args )
    {
        var addr = args.Length >= 2 ? Bridge.DbgValFromString ( args[1] ) : Bridge.DbgValFromString ( "cip" );
        Console.WriteLine ( $"addr: {addr.ToPtrString()}" );

        Module.ModuleInfo modinfo;
        if ( !TryGetModuleInfo ( addr, out modinfo ) )
        {
            Console.Error.WriteLine ( $"Module.InfoFromAddr failed for address {addr.ToPtrString()}..." );
            return false;
        }
        Console.WriteLine ( $"InfoFromAddr success, base: {modinfo.@base.ToPtrString()}" );

        string fileName = ShowSaveFileDialogForModule ( modinfo.name );
        if ( string.IsNullOrEmpty ( fileName ) )
        {
            Console.WriteLine ( "File save dialog cancelled." );
            return false;
        }

        nuint hProcess = Bridge.DbgValFromString ( "$hProcess" );
        if ( !PerformProcessDump ( hProcess, modinfo.@base, fileName, addr ) )
        {
            Console.Error.WriteLine ( $"DumpProcess failed for module {modinfo.name}..." );
            return false;
        }

        Console.WriteLine ( $"Dumping done!" );
        return true;
    }

    private static bool TryGetModuleInfo ( nuint address, out Module.ModuleInfo modInfo )
    {
        modInfo = new Module.ModuleInfo();
        return Module.InfoFromAddr ( address, ref modInfo );
    }

    private static string ShowSaveFileDialogForModule ( string moduleName )
    {
        string fileName = null;
        var saveFileDialog = new SaveFileDialog
        {
            Filter = "Executables (*.dll,*.exe)|*.exe|All Files (*.*)|*.*",
            RestoreDirectory = true,
            FileName = moduleName
        };

        var t = new Thread ( () =>
        {
            if ( saveFileDialog.ShowDialog() == DialogResult.OK )
            {
                fileName = saveFileDialog.FileName;
            }
        } );
        t.SetApartmentState ( ApartmentState.STA );
        t.Start();
        t.Join();

        return fileName;
    }

    private static bool PerformProcessDump ( nuint hProcess, nuint baseAddress, string fileName, nuint entryPoint )
    {
        return TitanEngine.DumpProcess ( ( nint ) hProcess, ( nint ) baseAddress, fileName, entryPoint );
    }

    //[Command("DotNetModuleEnum", DebugOnly = true)]
    public static void cbModuleEnum ( string[] args )
    {
        foreach ( var mod in Module.GetList() )
        {
            Console.WriteLine ( $"{mod.@base.ToPtrString()} {mod.name}" );
            foreach ( var section in Module.SectionListFromAddr ( mod.@base ) )
            { Console.WriteLine ( $"    {section.addr.ToPtrString()} \"{section.name}\"" ); }
        }
    }

    static SimpleMcpServer GSimpleMcpServer;

    [Command ( "StartMCPServer", X64DbgOnly = true, DebugOnly = false )]
    public static void cbStartMCPServer ( string[] args )
    {
        // --- Check if already initialized ---
        if ( GSimpleMcpServer != null )
        {
            Console.WriteLine ( "MCPServer instance already exists. Start command ignored." );
            return; // Don't create a new one
        }
        Console.WriteLine ( "Starting MCPServer..." );
        try
        {
            // Create new instance and assign it to the static field
            GSimpleMcpServer = new SimpleMcpServer ( typeof ( DotNetPlugin.Plugin ) );
            GSimpleMcpServer.Start(); // Start the newly created server
            Console.WriteLine ( "MCPServer Started." );
        }
        catch ( Exception ex )
        {
            Console.WriteLine ( $"Failed to start MCPServer: {ex.Message}" );
            GSimpleMcpServer = null;
        }
    }

    [Command ( "StopMCPServer", X64DbgOnly = true, DebugOnly = false )]
    public static void cbStopMCPServer ( string[] args )
    {
        if ( GSimpleMcpServer == null )
        {
            Console.WriteLine ( "MCPServer instance not found (already stopped or never started). Stop command ignored." );
            return; // Nothing to stop
        }
        Console.WriteLine ( "Stopping MCPServer..." );
        try
        {
            GSimpleMcpServer.Stop();
            GSimpleMcpServer = null;
            Console.WriteLine ( "MCPServer Stopped." );
        }
        catch ( Exception ex )
        {
            Console.WriteLine ( $"Error stopping MCPServer: {ex.Message}" );
        }
    }

    /// <summary>
    /// Executes a debugger command synchronously using x64dbg's command engine.
    ///
    /// This function wraps the native `DbgCmdExecDirect` API to simplify command execution.
    /// It blocks until the command has finished executing.
    ///
    /// Examples:
    ///   ExecuteDebuggerCommand("init C:\Path\To\Program.exe");   // Loads an executable
    ///   ExecuteDebuggerCommand("stop");                          // Restarts the current debugging session
    ///   ExecuteDebuggerCommand("run");                              // Starts execution
    /// </summary>
    /// <param name="command">The debugger command string to execute.</param>
    /// <returns>True if the command executed successfully, false otherwise.</returns>
    [Command ( "ExecuteDebuggerCommand", DebugOnly = false, MCPOnly = true,
               MCPCmdDescription =
                   "Example: ExecuteDebuggerCommand command=init c:\\Path\\To\\Program.exe\\r\\nNote: See ListDebuggerCommands for list of applicable commands. Once a program is loaded new available functions can be viewed from the tools/list" )]
    public static bool ExecuteDebuggerCommand ( string command )
    {
        Console.WriteLine ( "Executing DebuggerCommand: " + command );
        return DbgCmdExec ( command );
    }

    [Command ( "ListDebuggerCommands", DebugOnly = false, MCPOnly = true,
               MCPCmdDescription = "Example: ListDebuggerCommands" )]
    public static string ListDebuggerCommands ( string subject = "" )
    {
        subject = subject?.Trim().ToLowerInvariant();

        // Mapping user input to resource keys
        var map = new Dictionary<string, string>
        {
            { "debugcontrol", Resources.DebugControl },
            { "gui", Resources.GUI },
            { "search", Resources.Search },
            { "threadcontrol", Resources.ThreadControl }
        };

        if ( string.IsNullOrWhiteSpace ( subject ) )
        {
            return "Available options:\n- debugcontrol\n- gui\n- search\n- threadcontrol\n\nExample:\nListDebuggerCommands subject=gui";
        }

        if ( map.TryGetValue ( subject, out string json ) )
        {
            return json;
        }

        return "Unknown subject group. Try one of:\n- DebugControl\n- GUI\n- Search\n- ThreadControl";
    }

    [Command ( "DbgValFromString", DebugOnly = false, MCPOnly = true,
               MCPCmdDescription = "Example: DbgValFromString value=$pid" )]
    public static string DbgValFromString ( string value ) // = "$hProcess"
    {
        Console.WriteLine ( "Executing DbgValFromString: " + value );
        return "0x" + Bridge.DbgValFromString ( value ).ToHexString();
    }
    public static nuint DbgValFromStringAsNUInt ( string value ) // = "$hProcess"
    {
        Console.WriteLine ( "Executing DbgValFromString: " + value );
        return Bridge.DbgValFromString ( value );
    }


    [Command ( "ExecuteDebuggerCommandDirect", DebugOnly = false )]
    public static bool ExecuteDebuggerCommandDirect ( string[] args )
    {
        return ExecuteDebuggerCommandDirect ( args );
    }

    //[Command("ReadMemory", DebugOnly = false)]
    //public static bool ReadMemory(string[] args)
    //{
    //    if (args.Length != 2)
    //    {
    //        Console.WriteLine("Usage: ReadMemory <address> <size>");
    //        return false;
    //    }

    //    try
    //    {
    //        // Parse address (supports hex or decimal)
    //        nuint address = (nuint)Convert.ToUInt64(
    //            args[0].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? args[0].Substring(2) : args[0],
    //            args[0].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 16 : 10
    //        );

    //        // Parse size
    //        uint size = uint.Parse(args[1]);

    //        var memory = ReadMemory(address, size);

    //        if (memory == null)
    //        {
    //            Console.WriteLine($"[ReadMemory] Failed to read memory at 0x{address:X}");
    //            return false;
    //        }

    //        Console.WriteLine($"[ReadMemory] {size} bytes at 0x{address:X}:");

    //        for (int i = 0; i < memory.Length; i += 16)
    //        {
    //            var chunk = memory.Skip(i).Take(16).ToArray();
    //            string hex = BitConverter.ToString(chunk).Replace("-", " ").PadRight(48);
    //            string ascii = string.Concat(chunk.Select(b => b >= 32 && b <= 126 ? (char)b : '.'));
    //            Console.WriteLine($"{address + (nuint)i:X8}: {hex} {ascii}");
    //        }

    //        return true;
    //    }
    //    catch (Exception ex)
    //    {
    //        Console.WriteLine($"[ReadMemory] Error: {ex.Message}");
    //        return false;
    //    }
    //}


    public static byte[] ReadMemory ( nuint address, uint size )
    {
        byte[] buffer = new byte[size];
        if ( !Bridge.DbgMemRead ( address, buffer, size ) ) // assume NativeBridge is a P/Invoke wrapper
        { return null; }
        return buffer;
    }

    private static string ReadMemoryString ( nuint address, int maxSize )
    {
        try
        {
            var memory = ReadMemory ( address, ( uint ) maxSize );
            if ( memory == null )
            { return null; }

            int nullTerminationIndex = Array.IndexOf ( memory, ( byte ) 0 );
            if ( nullTerminationIndex <= 0 ) // if not found or empty string
            {
                return null;
            }

            string result = Encoding.ASCII.GetString ( memory, 0, nullTerminationIndex );
            if ( result.All ( c => c >= 0x20 && c < 0x7F ) )
            {
                return result;
            }
            return null;
        }
        catch
        {
            return null; // Failed to read memory
        }
    }

    //[Command("WriteMemory", DebugOnly = false)]
    //public static bool WriteMemory(string[] args)
    //{
    //    if (args.Length < 2)
    //    {
    //        Console.WriteLine("Usage: WriteMemory <address> <byte1> <byte2> ...");
    //        Console.WriteLine("Example: WriteMemory 0x7FF600001000 48 8B 05");
    //        return false;
    //    }

    //    try
    //    {
    //        // Parse address (hex or decimal)
    //        nuint address = (nuint)Convert.ToUInt64(
    //            args[0].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? args[0].Substring(2) : args[0],
    //            args[0].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 16 : 10
    //        );

    //        // Parse byte values (can be "48", "0x48", etc.)
    //        byte[] data = args.Skip(1).Select(b =>
    //        {
    //            b = b.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? b.Substring(2) : b;
    //            return byte.Parse(b, NumberStyles.HexNumber);
    //        }).ToArray();

    //        // Dump what we're about to write
    //        Console.WriteLine($"[WriteMemory] Writing {data.Length} bytes to 0x{address:X}:");
    //        Console.WriteLine(BitConverter.ToString(data).Replace("-", " "));

    //        // Perform the memory write
    //        if (!WriteMemory(address, data))
    //        {
    //            Console.WriteLine($"[WriteMemory] Failed to write to memory at 0x{address:X}");
    //            return false;
    //        }

    //        Console.WriteLine($"[WriteMemory] Successfully wrote to 0x{address:X}");
    //        return true;
    //    }
    //    catch (Exception ex)
    //    {
    //        Console.WriteLine($"[WriteMemory] Error: {ex.Message}");
    //        return false;
    //    }
    //}

    public static bool WriteMemory ( nuint address, byte[] data )
    {
        return Bridge.DbgMemWrite ( address, data, ( uint ) data.Length );
    }

    //[Command("WriteBytesToAddress", DebugOnly = true)]
    //public static bool WriteBytesToAddress(string[] args)
    //{
    //    if (args.Length < 2)
    //    {
    //        Console.WriteLine("Usage: WriteBytesToAddress <address> <byte1> <byte2> ...");
    //        Console.WriteLine("Example: WriteBytesToAddress 0x7FF600001000 48 8B 05");
    //        return false;
    //    }

    //    string addressStr = args[0];

    //    try
    //    {
    //        // Convert string[] to byte[]
    //        byte[] data = args.Skip(1).Select(b =>
    //        {
    //            b = b.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? b.Substring(2) : b;
    //            return byte.Parse(b, NumberStyles.HexNumber);
    //        }).ToArray();

    //        // Dump what we're about to write
    //        Console.WriteLine($"[WriteBytesToAddress] Writing {data.Length} bytes to {addressStr}:");
    //        Console.WriteLine(BitConverter.ToString(data).Replace("-", " "));

    //        // Call existing function
    //        return WriteBytesToAddress(addressStr, data);
    //    }
    //    catch (Exception ex)
    //    {
    //        Console.WriteLine($"[WriteBytesToAddress] Error: {ex.Message}");
    //        return false;
    //    }
    //}
    //public static bool WriteBytesToAddress(string addressStr, byte[] data)
    //{
    //    if (data == null || data.Length == 0)
    //    {
    //        Console.WriteLine("Data is null or empty.");
    //        return false;
    //    }

    //    if (!ulong.TryParse(addressStr.Replace("0x", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong parsed))
    //    {
    //        Console.WriteLine($"Invalid address: {addressStr}");
    //        return false;
    //    }

    //    IntPtr ptr = new IntPtr((long)parsed);
    //    nuint address = (nuint)ptr.ToInt64();

    //    bool success = WriteMemory(address, data);

    //    if (success)
    //    {
    //        Console.WriteLine($"Successfully wrote {data.Length} bytes at 0x{address:X}");
    //    }
    //    else
    //    {
    //        Console.WriteLine($"Failed to write memory at 0x{address:X}");
    //    }

    //    return success;
    //}

    [Command ( "WriteMemToAddress", DebugOnly = true, MCPOnly = true,
               MCPCmdDescription = "Example: WriteMemToAddress address=0x12345678, byteString=0F FF 90" )]
    public static string WriteMemToAddress ( string address, string byteString )
    {
        try
        {
            if ( string.IsNullOrWhiteSpace ( byteString ) )
            { return "Error: Byte string is empty."; }

            // Parse address
            if ( !ulong.TryParse ( address.Replace ( "0x", "" ), NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                                   out ulong parsed ) )
            { return $"Error: Invalid address: {address}"; }

            nuint MyAddresses = ( nuint ) parsed;

            // Parse byte string (e.g., "90 89 78")
            string[] byteParts = byteString.Split ( new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries );
            byte[] data = byteParts.Select ( b =>
            {
                if ( b.StartsWith ( "0x", StringComparison.OrdinalIgnoreCase ) )
                { b = b.Substring ( 2 ); }
                return byte.Parse ( b, NumberStyles.HexNumber );
            } ).ToArray();

            if ( data.Length == 0 )
            { return "Error: No valid bytes found to write."; }

            // Write memory
            bool success = WriteMemory ( MyAddresses, data );

            if ( success )
            {
                return $"Successfully wrote {data.Length} byte(s) to 0x{MyAddresses:X}:\r\n{BitConverter.ToString(data)}";
            }
            else
            {
                return $"Failed to write memory at 0x{(uint)MyAddresses:X}";
            }
        }
        catch ( Exception ex )
        {
            return $"[WriteBytesToAddress] Error: {ex.Message}";
        }
    }

    [Command ( "CommentOrLabelAtAddress", DebugOnly = true, MCPOnly = true,
               MCPCmdDescription =
                   "Example: CommentOrLabelAtAddress address=0x12345678, value=LabelTextGoeshere, mode=Label\\r\\nExample: CommentOrLabelAtAddress address=0x12345678, value=LabelTextGoeshere, mode=Comment\\r\\n" )]
    public static string CommentOrLabelAtAddress ( string address, string value, string mode = "Label" )
    {
        try
        {
            bool success = false;
            // Parse address
            if ( !ulong.TryParse ( address.Replace ( "0x", "" ), NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                                   out ulong parsed ) )
            { return $"Error: Invalid address: {address}"; }

            nuint MyAddresses = ( nuint ) parsed;

            if ( string.Equals ( mode, "Label", StringComparison.OrdinalIgnoreCase ) )
            {
                success = Bridge.DbgSetLabelAt ( MyAddresses, value );
                Console.WriteLine ( $"Label '{value}' added at {MyAddresses:X} (byte pattern match)" );
            }
            else if ( string.Equals ( mode, "Comment", StringComparison.OrdinalIgnoreCase ) )
            {
                success = Bridge.DbgSetCommentAt ( MyAddresses, value );
                Console.WriteLine ( $"Comment '{value}' added at {MyAddresses:X} (byte pattern match)" );
            }
            if ( success )
            {
                return $"Successfully wrote {value} to addressStr as {mode}";
            }
            else
            {
                return $"Failed to write memory at 0x{MyAddresses:X}";
            }
        }
        catch ( Exception ex )
        {
            return $"[WriteBytesToAddress] Error: {ex.Message}";
        }
    }

    [Command ( "SetBreakpoint", DebugOnly = true, MCPOnly = true,
               MCPCmdDescription = "Set a breakpoint at the address. (Equivalent to bp [address])\r\nExample: SetBreakpoint address=0x12345678" )]
    public static string SetBreakpoint ( string address )
    {
        try
        {
            if ( DbgCmdExec ( $"bp {address}" ) )
            {
                return $"Breakpoint set at {address}";
            }
            return $"Failed to set breakpoint at {address}";
        }
        catch ( Exception ex )
        {
            return $"[SetBreakpoint] Error: {ex.Message}";
        }
    }

    [Command ( "ClearBreakpoint", DebugOnly = true, MCPOnly = true,
               MCPCmdDescription = "Remove a breakpoint at the address. (Equivalent to bc [address])\r\nExample: ClearBreakpoint address=0x12345678" )]
    public static string ClearBreakpoint ( string address )
    {
        try
        {
            if ( DbgCmdExec ( $"bc {address}" ) )
            {
                return $"Breakpoint cleared at {address}";
            }
            return $"Failed to clear breakpoint at {address}";
        }
        catch ( Exception ex )
        {
            return $"[ClearBreakpoint] Error: {ex.Message}";
        }
    }

    [Command ( "ToggleBreakpoint", DebugOnly = true, MCPOnly = true,
               MCPCmdDescription = "Enable/disable a breakpoint.\r\nExample: ToggleBreakpoint address=0x12345678" )]
    public static string ToggleBreakpoint ( string address )
    {
        try
        {
            if ( DbgCmdExec ( $"bpt {address}" ) )
            {
                return $"Breakpoint toggled at {address}";
            }
            return $"Failed to toggle breakpoint at {address}";
        }
        catch ( Exception ex )
        {
            return $"[ToggleBreakpoint] Error: {ex.Message}";
        }
    }

    [Command ( "SetHardwareBreakpoint", DebugOnly = true, MCPOnly = true,
               MCPCmdDescription = "Set a hardware breakpoint. (Equivalent to bph [address])\r\nExample: SetHardwareBreakpoint address=0x12345678" )]
    public static string SetHardwareBreakpoint ( string address )
    {
        try
        {
            if ( DbgCmdExec ( $"bph {address}" ) )
            {
                return $"Hardware breakpoint set at {address}";
            }
            return $"Failed to set hardware breakpoint at {address}";
        }
        catch ( Exception ex )
        {
            return $"[SetHardwareBreakpoint] Error: {ex.Message}";
        }
    }

    [Command ( "Run", DebugOnly = true, MCPOnly = true,
               MCPCmdDescription = "Continue program execution. (Equivalent to run or g)\r\nExample: Run" )]
    public static string Run()
    {
        try
        {
            if ( DbgCmdExec ( "run" ) )
            {
                return "Program execution continued.";
            }
            return "Failed to continue program execution.";
        }
        catch ( Exception ex )
        {
            return $"[Run] Error: {ex.Message}";
        }
    }

    [Command ( "Pause", DebugOnly = true, MCPOnly = true,
               MCPCmdDescription = "Pause program execution.\r\nExample: Pause" )]
    public static string Pause()
    {
        try
        {
            if ( DbgCmdExec ( "pause" ) )
            {
                return "Program execution paused.";
            }
            return "Failed to pause program execution.";
        }
        catch ( Exception ex )
        {
            return $"[Pause] Error: {ex.Message}";
        }
    }

    [Command ( "StepInto", DebugOnly = true, MCPOnly = true,
               MCPCmdDescription = "Step into function. (Equivalent to sti)\r\nExample: StepInto" )]
    public static string StepInto()
    {
        try
        {
            if ( DbgCmdExec ( "sti" ) )
            {
                return "Step into successful.";
            }
            return "Failed to step into.";
        }
        catch ( Exception ex )
        {
            return $"[StepInto] Error: {ex.Message}";
        }
    }

    [Command ( "StepOver", DebugOnly = true, MCPOnly = true,
               MCPCmdDescription = "Step over function. (Equivalent to sto)\r\nExample: StepOver" )]
    public static string StepOver()
    {
        try
        {
            if ( DbgCmdExec ( "sto" ) )
            {
                return "Step over successful.";
            }
            return "Failed to step over.";
        }
        catch ( Exception ex )
        {
            return $"[StepOver] Error: {ex.Message}";
        }
    }

    [Command ( "StepOut", DebugOnly = true, MCPOnly = true,
               MCPCmdDescription = "Step out of current function. (Equivalent to sso)\r\nExample: StepOut" )]
    public static string StepOut()
    {
        try
        {
            if ( DbgCmdExec ( "sso" ) )
            {
                return "Step out successful.";
            }
            return "Failed to step out.";
        }
        catch ( Exception ex )
        {
            return $"[StepOut] Error: {ex.Message}";
        }
    }

    [Command ( "AttachToProcess", DebugOnly = true, MCPOnly = true,
               MCPCmdDescription = "Attach to an existing process by its PID.\r\nExample: AttachToProcess pid=1234" )]
    public static string AttachToProcess ( string pid )
    {
        try
        {
            if ( DbgCmdExec ( $"attach {pid}" ) )
            {
                return $"Attached to process {pid}.";
            }
            return $"Failed to attach to process {pid}.";
        }
        catch ( Exception ex )
        {
            return $"[AttachToProcess] Error: {ex.Message}";
        }
    }

    [Command ( "DetachFromProcess", DebugOnly = true, MCPOnly = true,
               MCPCmdDescription = "Detach from the current process.\r\nExample: DetachFromProcess" )]
    public static string DetachFromProcess()
    {
        try
        {
            if ( DbgCmdExec ( "detach" ) )
            {
                return "Detached from current process.";
            }
            return "Failed to detach from current process.";
        }
        catch ( Exception ex )
        {
            return $"[DetachFromProcess] Error: {ex.Message}";
        }
    }

    [Command ( "TerminateProcess", DebugOnly = true, MCPOnly = true,
               MCPCmdDescription = "Terminate the debugged process. (Equivalent to kill)\r\nExample: TerminateProcess" )]
    public static string TerminateProcess()
    {
        try
        {
            if ( DbgCmdExec ( "kill" ) )
            {
                return "Debugged process terminated.";
            }
            return "Failed to terminate debugged process.";
        }
        catch ( Exception ex )
        {
            return $"[TerminateProcess] Error: {ex.Message}";
        }
    }

    [Command ( "SwitchThread", DebugOnly = true, MCPOnly = true,
               MCPCmdDescription = "Switch to the specified thread.\r\nExample: SwitchThread threadId=123" )]
    public static string SwitchThread ( string threadId )
    {
        try
        {
            if ( DbgCmdExec ( $"thread {threadId}" ) )
            {
                return $"Switched to thread {threadId}.";
            }
            return $"Failed to switch to thread {threadId}.";
        }
        catch ( Exception ex )
        {
            return $"[SwitchThread] Error: {ex.Message}";
        }
    }

    [Command ( "FindPattern", DebugOnly = true, MCPOnly = true,
               MCPCmdDescription = "Search for a given byte pattern in memory.\r\nExample: FindPattern pattern=90 89 78" )]
    public static string FindPattern ( string pattern )
    {
        try
        {
            // Get current module info to define search range
            nuint cip = Bridge.DbgValFromString ( "cip" );
            Module.ModuleInfo modInfo;
            if ( !TryGetModuleInfo ( cip, out modInfo ) )
            {
                return "Error: Could not get current module information to define search range.";
            }

            // Convert space-separated hex string to byte array
            string[] byteParts = pattern.Split ( new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries );
            byte[] data = byteParts.Select ( b =>
            {
                if ( b.StartsWith ( "0x", StringComparison.OrdinalIgnoreCase ) )
                { b = b.Substring ( 2 ); }
                return byte.Parse ( b, NumberStyles.HexNumber );
            } ).ToArray();

            if ( data.Length == 0 )
            {
                return "Error: No valid bytes found in pattern.";
            }

            string hexPattern = BitConverter.ToString ( data ).Replace ( "-", " " );
            string command = $"pattern {modInfo.@base.ToPtrString()} {modInfo.size.ToPtrString()} {hexPattern}";

            if ( DbgCmdExec ( command ) )
            {
                return $"Pattern search initiated in module {modInfo.name} ({modInfo.@base.ToPtrString()} - {(modInfo.@base + modInfo.size).ToPtrString()}). Results will appear in x64dbg log.";
            }
            return $"Failed to initiate pattern search.";
        }
        catch ( Exception ex )
        {
            return $"[FindPattern] Error: {ex.Message}";
        }
    }

    [Command ( "GetModuleInfo", DebugOnly = true, MCPOnly = true,
               MCPCmdDescription = "Get detailed information about a specific module (address, size, entry point).\r\nExample: GetModuleInfo moduleName=kernel32.dll" )]
    public static string GetModuleInfo ( string moduleName )
    {
        try
        {
            var modules = GetAllModulesFromMemMapFunc();
            foreach ( var mod in modules )
            {
                if ( mod.Name.Equals ( moduleName, StringComparison.OrdinalIgnoreCase ) )
                {
                    return $"Module: {mod.Name}\r\nPath: {mod.Path}\r\nBase Address: 0x{mod.Base:X16}\r\nSize: 0x{mod.Size:X}";
                }
            }
            return $"Module '{moduleName}' not found.";
        }
        catch ( Exception ex )
        {
            return $"[GetModuleInfo] Error: {ex.Message}";
        }
    }

    [Command ( "GetEntryPoint", DebugOnly = true, MCPOnly = true,
               MCPCmdDescription = "Get the entry point address of the current module or program.\r\nExample: GetEntryPoint" )]
    public static string GetEntryPoint()
    {
        try
        {
            nuint entryPoint = Bridge.DbgValFromString ( "entrypoint" );
            if ( entryPoint != 0 )
            {
                return $"Entry Point: 0x{entryPoint:X16}";
            }
            return "Entry point not found or not available.";
        }
        catch ( Exception ex )
        {
            return $"[GetEntryPoint] Error: {ex.Message}";
        }
    }

    public static bool PatchWithNops ( string[] args )
    {
        return PatchWithNops ( args[0], Convert.ToInt32 ( args[1] ) );
    }
    public static bool PatchWithNops ( string addressStr, int nopCount = 7 )
    {
        if ( !ulong.TryParse ( addressStr.Replace ( "0x", "" ), NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                               out ulong parsed ) )
        {
            Console.WriteLine ( $"Invalid address: {addressStr}" );
            return false;
        }

        IntPtr ptr = new IntPtr ( ( long ) parsed );
        nuint address = ( nuint ) ptr.ToInt64();

        byte[] nops = Enumerable.Repeat ( ( byte ) 0x90, nopCount ).ToArray();
        bool success = WriteMemory ( address, nops );

        if ( success )
        {
            Console.WriteLine ( $"Successfully patched {nopCount} NOPs at 0x{address:X}" );
        }
        else
        {
            Console.WriteLine ( $"Failed to write memory at 0x{address:X}" );
        }

        return success;
    }

    /// <summary>
    /// Parses a string of hexadecimal byte values separated by hyphens into a byte array.
    /// </summary>
    /// <param name="pattern">
    /// A string containing hexadecimal byte values, e.g., "75-38" or "90-90-CC".
    /// Each byte must be two hex digits and separated by hyphens.
    /// </param>
    /// <returns>
    /// A byte array representing the parsed hex values.
    /// </returns>
    /// <example>
    /// byte[] bytes = ParseBytePattern("75-38"); // returns new byte[] { 0x75, 0x38 }
    /// </example>
    public static byte[] ParseBytePattern ( string pattern )
    {
        return pattern.Split ( '-' ).Select ( b => Convert.ToByte ( b, 16 ) ).ToArray();
    }

    //[Command("GetLabel", DebugOnly = true)]
    //public static bool GetLabel(string[] args)
    //{
    //    if (args.Length != 1)
    //    {
    //        Console.WriteLine("Usage: GetLabel <address>");
    //        Console.WriteLine("Example: GetLabel 0x7FF600001000");
    //        return false;
    //    }

    //    try
    //    {
    //        // Parse address (supports hex and decimal)
    //        nuint address = (nuint)Convert.ToUInt64(
    //            args[0].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? args[0].Substring(2) : args[0],
    //            args[0].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 16 : 10
    //        );

    //        string label = GetLabel(address);

    //        if (label != null)
    //        {
    //            Console.WriteLine($"[GetLabel] Label at 0x{address:X}: {label}");
    //            return true;
    //        }
    //        else
    //        {
    //            Console.WriteLine($"[GetLabel] No label found at 0x{address:X}");
    //            return false;
    //        }
    //    }
    //    catch (Exception ex)
    //    {
    //        Console.WriteLine($"[GetLabel] Error: {ex.Message}");
    //        return false;
    //    }
    //}

    [Command ( "GetLabel", DebugOnly = true, MCPOnly = true,
               MCPCmdDescription = "Example: GetLabel addressStr=0x12345678" )]
    public static string GetLabel ( string addressStr )
    {
        try
        {
            // Parse address (supports hex or decimal)
            nuint address = ( nuint ) Convert.ToUInt64 (
                                addressStr.StartsWith ( "0x", StringComparison.OrdinalIgnoreCase ) ? addressStr.Substring ( 2 ) : addressStr,
                                addressStr.StartsWith ( "0x", StringComparison.OrdinalIgnoreCase ) ? 16 : 10
                            );

            string label = GetLabel ( address );

            if ( !string.IsNullOrEmpty ( label ) )
            { return $"[GetLabel] Label at 0x{address:X}: {label}"; }
            else
            { return $"[GetLabel] No label found at 0x{address:X}"; }
        }
        catch ( Exception ex )
        {
            return $"[GetLabel] Error: {ex.Message}";
        }
    }

    public static string GetLabel ( nuint address )
    {
        return Bridge.DbgGetLabelAt ( address, SEGMENTREG.SEG_DEFAULT, out var label ) ? label : null;
    }


    string TryGetDereferencedString ( nuint address )
    {
        var data = ReadMemory ( address, 64 ); // read 64 bytes (arbitrary)
        int end = Array.IndexOf ( data, ( byte ) 0 );
        if ( end <= 0 )
        {
            return null;
        }
        return Encoding.ASCII.GetString ( data, 0, end );
    }


    public static void LabelIfCallTargetMatches ( string[] args )
    {
        if ( args.Length < 2 )
        {
            Console.WriteLine ( "Usage: LabelIfCallTargetMatches <address> <targetAddress> [labelOrComment] [mode: Label|Comment]" );
            Console.WriteLine ( "Example: LabelIfCallTargetMatches 0x7FF600001000 0x7FF600002000 MyLabel Label" );
            return;
        }

        try
        {
            // Parse input addresses
            nuint address = ( nuint ) Convert.ToUInt64 (
                                args[0].StartsWith ( "0x", StringComparison.OrdinalIgnoreCase ) ? args[0].Substring ( 2 ) : args[0],
                                args[0].StartsWith ( "0x", StringComparison.OrdinalIgnoreCase ) ? 16 : 10
                            );

            nuint targetAddress = ( nuint ) Convert.ToUInt64 (
                                      args[1].StartsWith ( "0x", StringComparison.OrdinalIgnoreCase ) ? args[1].Substring ( 2 ) : args[1],
                                      args[1].StartsWith ( "0x", StringComparison.OrdinalIgnoreCase ) ? 16 : 10
                                  );

            // Optional label + mode
            string value = "test";
            string mode = "Label";

            if ( args.Length == 3 )
            {
                value = args[2];
            }
            else if ( args.Length >= 4 )
            {
                value = args[args.Length - 2];
                mode = args[args.Length - 1];
            }

            // Disassemble at the given address
            Bridge.BASIC_INSTRUCTION_INFO disasm = new Bridge.BASIC_INSTRUCTION_INFO();
            Bridge.DbgDisasmFastAt ( address, ref disasm );


            LabelIfCallTargetMatches ( address, ref disasm, targetAddress, value, mode );
        }
        catch ( Exception ex )
        {
            Console.WriteLine ( $"[LabelIfCallTargetMatches] Error: {ex.Message}" );
        }
    }
    public static void LabelIfCallTargetMatches ( nuint address, ref Bridge.BASIC_INSTRUCTION_INFO disasm,
            nuint targetAddress, string value = "test", string mode = "Label" )
    {
        if ( disasm.addr == targetAddress )
        {
            if ( string.Equals ( mode, "Label", StringComparison.OrdinalIgnoreCase ) )
            {
                Bridge.DbgSetLabelAt ( address, value );
                Console.WriteLine ( $"Label '{value}' added at {address:X}" );
            }
            else if ( string.Equals ( mode, "Comment", StringComparison.OrdinalIgnoreCase ) )
            {
                Bridge.DbgSetCommentAt ( address, value );
                Console.WriteLine ( $"Comment '{value}' added at {address:X}" );
            }
        }
    }

    public static bool LabelMatchingInstruction ( string[] args )
    {
        if ( args.Length < 2 )
        {
            Console.WriteLine ( "Usage: LabelMatchingInstruction <address> <instruction> [labelOrComment] [mode: Label|Comment]" );
            Console.WriteLine ( "Example: LabelMatchingInstruction 0x7FF600001000 \"jnz 0x140001501\" MyLabel Label" );
            return false;
        }

        try
        {
            // Parse address
            nuint address = ( nuint ) Convert.ToUInt64 (
                                args[0].StartsWith ( "0x", StringComparison.OrdinalIgnoreCase ) ? args[0].Substring ( 2 ) : args[0],
                                args[0].StartsWith ( "0x", StringComparison.OrdinalIgnoreCase ) ? 16 : 10
                            );

            string instruction = args[1];
            string label = "test";
            string mode = "Label";

            if ( args.Length == 3 )
            {
                label = args[2];
            }
            else if ( args.Length >= 4 )
            {
                label = args[args.Length - 2];
                mode = args[args.Length - 1];
            }

            Bridge.BASIC_INSTRUCTION_INFO disasm = new Bridge.BASIC_INSTRUCTION_INFO();
            Bridge.DbgDisasmFastAt ( address, ref disasm );

            LabelMatchingInstruction ( address, ref disasm, instruction, label, mode );
            return true;
        }
        catch ( Exception ex )
        {
            Console.WriteLine ( $"[LabelMatchingInstruction] Error: {ex.Message}" );
            return false;
        }
    }
    public static void LabelMatchingInstruction ( nuint address, ref Bridge.BASIC_INSTRUCTION_INFO disasm,
            string targetInstruction = "jnz 0x0000000140001501", string value = "test", string mode = "Label" )
    {
        if ( string.Equals ( disasm.instruction, targetInstruction, StringComparison.OrdinalIgnoreCase ) )
        {
            if ( string.Equals ( mode, "Label", StringComparison.OrdinalIgnoreCase ) )
            {
                Bridge.DbgSetLabelAt ( address, value );
                Console.WriteLine ( $"Label 'test' added at {address:X}" );
            }
            else if ( string.Equals ( mode, "Comment", StringComparison.OrdinalIgnoreCase ) )
            {
                Bridge.DbgSetCommentAt ( address, value );
                Console.WriteLine ( $"Comment 'test' added at {address:X}" );
            }
        }
    }

    public static void LabelMatchingBytes ( string[] args )
    {
        if ( args.Length < 2 )
        {
            Console.WriteLine ( "Usage: LabelMatchingBytes <address> <byte1> <byte2> ... [labelOrComment] [mode: Label|Comment]" );
            Console.WriteLine ( "Example: LabelMatchingBytes 0x7FF600001000 48 8B 05 MyLabel Label" );
            return;
        }

        try
        {
            // Parse address
            nuint address = ( nuint ) Convert.ToUInt64 (
                                args[0].StartsWith ( "0x", StringComparison.OrdinalIgnoreCase ) ? args[0].Substring ( 2 ) : args[0],
                                args[0].StartsWith ( "0x", StringComparison.OrdinalIgnoreCase ) ? 16 : 10
                            );

            // Default values
            string value = "test";
            string mode = "Label";

            // Determine how many arguments belong to byte pattern
            int byteCount = args.Length - 1;

            if ( args.Length >= 3 )
            {
                string lastArg = args[args.Length - 1];
                string secondLastArg = args[args.Length - 2];

                bool lastIsMode = lastArg.Equals ( "Label", StringComparison.OrdinalIgnoreCase )
                                  || lastArg.Equals ( "Comment", StringComparison.OrdinalIgnoreCase );

                if ( lastIsMode )
                {
                    mode = lastArg;
                    value = secondLastArg;
                    byteCount -= 2;
                }
                else
                {
                    value = lastArg;
                    byteCount -= 1;
                }
            }

            // Parse bytes
            var pattern = args.Skip ( 1 ).Take ( byteCount ).Select ( b =>
            {
                if ( b.StartsWith ( "0x", StringComparison.OrdinalIgnoreCase ) )
                { b = b.Substring ( 2 ); }
                return byte.Parse ( b, NumberStyles.HexNumber );
            } ).ToArray();

            // Call the memory-labeling function
            LabelMatchingBytes ( address, pattern, value, mode );
        }
        catch ( Exception ex )
        {
            Console.WriteLine ( $"[LabelMatchingBytes] Error: {ex.Message}" );
        }
    }




    public static void LabelMatchingBytes ( nuint address, byte[] pattern, string value = "test", string mode = "Label" )
    {
        try
        {
            byte[] actualBytes = ReadMemory ( address, ( uint ) pattern.Length );

            if ( actualBytes.Length != pattern.Length )
            { return; }

            for ( int i = 0; i < pattern.Length; i++ )
            {
                if ( actualBytes[i] != pattern[i] )
                { return; }
            }

            if ( string.Equals ( mode, "Label", StringComparison.OrdinalIgnoreCase ) )
            {
                Bridge.DbgSetLabelAt ( address, value );
                Console.WriteLine ( $"Label '{value}' added at {address:X} (byte pattern match)" );
            }
            else if ( string.Equals ( mode, "Comment", StringComparison.OrdinalIgnoreCase ) )
            {
                Bridge.DbgSetCommentAt ( address, value );
                Console.WriteLine ( $"Comment '{value}' added at {address:X} (byte pattern match)" );
            }
        }
        catch
        {
            // Fail quietly on bad memory read
        }
    }

    // Function returns List of tuples: (Module Name, Full Path, Base Address, Total Size)
    public static List< ( string Name, string Path, nuint Base, nuint Size ) > GetAllModulesFromMemMapFunc()
    {
        var finalResult = new List< ( string Name, string Path, nuint Base, nuint Size ) >();
        MEMMAP_NATIVE nativeMemMap = new MEMMAP_NATIVE();

        try
        {
            if ( !DbgMemMap ( ref nativeMemMap ) )
            {
                Console.WriteLine ( "[GetAllModulesFromMemMapFunc] DbgMemMap call failed." );
                return finalResult;
            }

            if ( nativeMemMap.page == IntPtr.Zero || nativeMemMap.count == 0 )
            {
                return finalResult;
            }

            var allocationRegions = GetMemoryMapRegions ( nativeMemMap );
            finalResult = ProcessAllocationRegions ( allocationRegions );
            SortModulesByBaseAddress ( finalResult );
        }
        catch ( Exception ex )
        {
            Console.WriteLine ( $"[GetAllModulesFromMemMapFunc] Exception: {ex.Message}\n{ex.StackTrace}" );
            throw;
        }
        finally
        {
            if ( nativeMemMap.page != IntPtr.Zero )
            {
                //BridgeFree(nativeMemMap.page); // Ensure this is called!
            }
        }
        return finalResult;
    }

    private static Dictionary<nuint, List< ( nuint Base, nuint Size, string Info ) >> GetMemoryMapRegions ( MEMMAP_NATIVE nativeMemMap )
    {
        var allocationRegions = new Dictionary<nuint, List< ( nuint Base, nuint Size, string Info ) >>();
        int sizeOfMemPage = Marshal.SizeOf<MEMPAGE>();

        for ( int i = 0; i < nativeMemMap.count; i++ )
        {
            IntPtr currentPagePtr = new IntPtr ( nativeMemMap.page.ToInt64() + ( long ) i * sizeOfMemPage );
            MEMPAGE memPage = Marshal.PtrToStructure<MEMPAGE> ( currentPagePtr );

            if ( ( memPage.mbi.Type & MEM_IMAGE ) == MEM_IMAGE )
            {
                nuint allocBase = ( nuint ) memPage.mbi.AllocationBase.ToInt64();
                nuint baseAddr = ( nuint ) memPage.mbi.BaseAddress.ToInt64();
                nuint regionSize = memPage.mbi.RegionSize;
                string infoString = memPage.info ?? string.Empty;

                if ( !allocationRegions.ContainsKey ( allocBase ) )
                {
                    allocationRegions[allocBase] = new List< ( nuint Base, nuint Size, string Info ) >();
                }
                allocationRegions[allocBase].Add ( ( baseAddr, regionSize, infoString ) );
            }
        }
        return allocationRegions;
    }

    private static List< ( string Name, string Path, nuint Base, nuint Size ) > ProcessAllocationRegions ( Dictionary<nuint, List< ( nuint Base, nuint Size, string Info ) >> allocationRegions )
    {
        var result = new List< ( string Name, string Path, nuint Base, nuint Size ) >();
        foreach ( var kvp in allocationRegions )
        {
            nuint allocBase = kvp.Key;
            var regions = kvp.Value;

            if ( regions.Count > 0 )
            {
                ( string moduleName, string modulePath ) = GetModuleNameAndPath ( allocBase, regions );
                nuint totalSize = CalculateModuleSize ( regions );

                result.Add ( ( moduleName, modulePath, allocBase, totalSize ) );
            }
        }
        return result;
    }

    private static ( string moduleName, string modulePath ) GetModuleNameAndPath ( nuint allocBase, List< ( nuint Base, nuint Size, string Info ) > regions )
    {
        string modulePath = "Unknown Module";
        var mainRegion = regions.FirstOrDefault ( r => r.Base == allocBase );

        if ( mainRegion.Info != null && !string.IsNullOrEmpty ( mainRegion.Info ) )
        {
            modulePath = mainRegion.Info;
        }
        else
        {
            var firstInfoRegion = regions.FirstOrDefault ( r => !string.IsNullOrEmpty ( r.Info ) );
            if ( firstInfoRegion.Info != null )
            {
                modulePath = firstInfoRegion.Info;
            }
        }

        string finalModuleName = System.IO.Path.GetFileName ( modulePath );
        if ( string.IsNullOrEmpty ( finalModuleName ) )
        {
            finalModuleName = modulePath;
            if ( string.IsNullOrEmpty ( finalModuleName ) )
            {
                finalModuleName = $"Module@0x{allocBase:X16}";
                modulePath = finalModuleName;
            }
        }
        return ( finalModuleName, modulePath );
    }

    private static nuint CalculateModuleSize ( List< ( nuint Base, nuint Size, string Info ) > regions )
    {
        nuint minRegionBase = regions[0].Base;
        nuint maxRegionEnd = regions[0].Base + regions[0].Size;
        for ( int i = 1; i < regions.Count; i++ )
        {
            if ( regions[i].Base < minRegionBase )
            {
                minRegionBase = regions[i].Base;
            }
            nuint currentEnd = regions[i].Base + regions[i].Size;
            if ( currentEnd > maxRegionEnd )
            {
                maxRegionEnd = currentEnd;
            }
        }
        return maxRegionEnd - minRegionBase;
    }

    private static void SortModulesByBaseAddress ( List< ( string Name, string Path, nuint Base, nuint Size ) > modules )
    {
        modules.Sort ( ( a, b ) =>
        {
            if ( a.Base < b.Base )
            {
                return -1;
            }
            if ( a.Base > b.Base )
            {
                return 1;
            }
            return 0;
        } );
    }

    [Command ( "GetAllModulesFromMemMap", DebugOnly = true, MCPOnly = true,
               MCPCmdDescription = "Example: GetAllModulesFromMemMap" )]
    public static string GetAllModulesFromMemMap()
    {
        try
        {
            // Update expected tuple type
            var modules = GetAllModulesFromMemMapFunc(); // Returns List<(string Name, string Path, nuint Base, nuint Size)>

            if ( modules.Count == 0 )
            { return "[GetAllModulesFromMemMap] No image modules found in memory map."; }

            var output = new StringBuilder();
            output.AppendLine ( $"[GetAllModulesFromMemMap] Found {modules.Count} image modules:" );

            // Update foreach destructuring and output line
            output.AppendLine ( $"{"Name",-30} {"Path",-70} {"Base Address",-18} {"End Address",-18} {"Size",-10}" );
            output.AppendLine ( new string ( '-', 150 ) ); // Separator line

            foreach ( ( string Name, string Path, nuint Base, nuint Size ) in modules )
            {
                nuint End = Base + Size;
                // Add Path to the output, adjust spacing as needed
                output.AppendLine ( $"{Name,-30} {Path,-70} 0x{Base:X16} 0x{End:X16} 0x{Size:X}" );
            }

            return output.ToString().TrimEnd();
        }
        catch ( Exception ex )
        {
            return $"[GetAllModulesFromMemMap] Error: {ex.Message}\n{ex.StackTrace}";
        }
    }


    // Define a struct to hold the frame info we can gather
    public struct CallStackFrameInfo
    {
        public nuint FrameAddress; // Value of RBP for this frame
        public nuint ReturnAddress; // Address execution returns to
        public nuint FrameSize;     // Calculated size (approx)
    }

    /// <summary>
    /// Represents the resolved symbols for a memory address.
    /// </summary>
    public class AddressSymbols {
        public string Module { get; set; } = "N/A";
        public string Label { get; set; } = "N/A";
        public string Comment { get; set; } = "";
    }

    /// <summary>
    /// Walks the current call stack by traversing the RBP chain.
    /// This is not always reliable but works for standard x64 calling conventions.
    /// </summary>
    /// <param name="maxFrames">The maximum number of frames to walk.</param>
    /// <returns>An enumerable of <see cref="CallStackFrameInfo"/> for each valid frame found.</returns>
    public static IEnumerable<CallStackFrameInfo> WalkStackFrames ( int maxFrames = 32 )
    {
        if ( !TryGetInitialStackPointers ( out nuint rbp, out nuint rsp ) )
        {
            yield break; // Stop iteration if initial pointers are invalid
        }

        nuint currentRbp = rbp;
        nuint previousRbp = 0;

        for ( int i = 0; i < maxFrames; i++ )
        {
            if ( !TryGetFrameInfo ( currentRbp, out nuint returnAddress, out nuint nextRbp ) )
            {
                break; // Stop if we can't read frame info (end of stack or invalid memory)
            }

            nuint frameSize = CalculateFrameSize ( currentRbp, previousRbp );

            yield return new CallStackFrameInfo
            {
                FrameAddress = currentRbp,
                ReturnAddress = returnAddress,
                FrameSize = frameSize
            };

            previousRbp = currentRbp;
            currentRbp = nextRbp;

            if ( !IsNextRbpValid ( currentRbp, previousRbp, rsp ) )
            {
                break; // Stop if the next frame pointer is invalid
            }
        }
    }


    private static bool TryGetInitialStackPointers ( out nuint rbp, out nuint rsp )
    {
        rbp = DbgValFromStringAsNUInt ( "rbp" );
        rsp = DbgValFromStringAsNUInt ( "rsp" );

        if ( rbp == 0 || rbp < rsp )
        {
            Console.WriteLine ( "[GetCallStackFunc] Initial RBP is invalid or below RSP." );
            return false;
        }
        return true;
    }

    private static bool TryReadNuintFromMemory ( nuint address, out nuint value )
    {
        byte[] buffer = new byte[sizeof ( ulong )];
        if ( DbgMemRead ( address, buffer, ( nuint ) sizeof ( ulong ) ) )
        {
            value = ( nuint ) BitConverter.ToUInt64 ( buffer, 0 );
            return true;
        }
        value = 0;
        Console.WriteLine ( $"[GetCallStackFunc] Failed to read memory at 0x{address:X}" );
        return false;
    }

    private static bool TryGetFrameInfo ( nuint currentRbp, out nuint returnAddress, out nuint nextRbp )
    {
        returnAddress = 0;
        nextRbp = 0;

        if ( !TryReadNuintFromMemory ( currentRbp + ( nuint ) sizeof ( ulong ), out returnAddress ) )
        {
            return false;
        }

        if ( returnAddress == 0 )
        {
            Console.WriteLine ( "[GetCallStackFunc] Reached null return address." );
            return false;
        }

        return TryReadNuintFromMemory ( currentRbp, out nextRbp );
    }

    private static nuint CalculateFrameSize ( nuint currentRbp, nuint previousRbp )
    {
        if ( previousRbp == 0 )
        {
            return 0;
        }
        // Avoid nonsensical size if RBP decreased or is not what we expect
        if ( currentRbp > previousRbp )
        {
            return 0;
        }
        return previousRbp - currentRbp;
    }

    private static bool IsNextRbpValid ( nuint currentRbp, nuint previousRbp, nuint rsp )
    {
        if ( currentRbp == 0 || currentRbp < rsp || currentRbp <= previousRbp )
        {
            Console.WriteLine ( $"[GetCallStackFunc] Invalid next RBP (0x{currentRbp:X}). Previous=0x{previousRbp:X}, RSP=0x{rsp:X}. Stopping walk." );
            return false;
        }
        return true;
    }


    [Command ( "GetCallStack", DebugOnly = true, MCPOnly = true,
               MCPCmdDescription = "Example: GetCallStack\r\nExample: GetCallStack, maxFrames=32" )]
    public static string GetCallStack ( int maxFrames = 32 )
    {
        try
        {
            var callstackFrames = WalkStackFrames ( maxFrames ).ToList(); // Eagerly evaluate for count

            if ( callstackFrames.Count == 0 )
            {
                return "[GetCallStack] Call stack could not be retrieved (check RBP validity or use debugger UI).";
            }

            var output = new StringBuilder();
            output.AppendLine ( $"[GetCallStack] Retrieved {callstackFrames.Count} frames (RBP walk, may be inaccurate):" );
            output.AppendLine ( $"{"Frame",-5} {"Frame Addr",-18} {"Return Addr",-18} {"Size",-10} {"Module",-25} {"Label Symbol",-40} {"Comment"}" );
            output.AppendLine ( new string ( '-', 130 ) );

            for ( int i = 0; i < callstackFrames.Count; i++ )
            {
                var frame = callstackFrames[i];
                TryResolveSymbols ( frame.ReturnAddress, out AddressSymbols symbols );

                // Format the output line
                output.AppendLine (
                    $"{$"[ {i}]",-5} 0x{frame.FrameAddress:X16} 0x{frame.ReturnAddress:X16} {($"0x {frame.FrameSize: X}"),-10} {symbols.Module,-25} {symbols.Label,-40} {symbols.Comment}" );
            }

            return output.ToString().TrimEnd(); // remove trailing newline
        }
        catch ( Exception ex )
        {
            return $"[GetCallStack] Error: {ex.Message}\n{ex.StackTrace}";
        }
    }

    /// <summary>
    /// Resolves symbol information (module, label, comment) for a given memory address.
    /// This method encapsulates the P/Invoke complexity of calling DbgAddrInfoGet.
    /// </summary>
    /// <param name="address">The address to resolve.</param>
    /// <param name="symbols">The resolved symbol information.</param>
    /// <returns>True if any symbol information was retrieved, false otherwise.</returns>
    private static bool TryResolveSymbols ( nuint address, out AddressSymbols symbols )
    {
        symbols = new AddressSymbols();
        const int MAX_MODULE_SIZE_BUFF = 256;
        const int MAX_LABEL_SIZE_BUFF = 256;
        const int MAX_COMMENT_SIZE_BUFF = 512;

        IntPtr ptrModule = IntPtr.Zero;
        IntPtr ptrLabel = IntPtr.Zero;
        IntPtr ptrComment = IntPtr.Zero;

        try
        {
            ptrModule = Marshal.AllocHGlobal ( MAX_MODULE_SIZE_BUFF );
            ptrLabel = Marshal.AllocHGlobal ( MAX_LABEL_SIZE_BUFF );
            ptrComment = Marshal.AllocHGlobal ( MAX_COMMENT_SIZE_BUFF );

            Marshal.WriteByte ( ptrModule, 0, 0 );
            Marshal.WriteByte ( ptrLabel, 0, 0 );
            Marshal.WriteByte ( ptrComment, 0, 0 );

            var addrInfo = new BRIDGE_ADDRINFO_NATIVE
            {
                module = ptrModule,
                label = ptrLabel,
                comment = ptrComment,
                flags = ADDRINFOFLAGS.flagmodule | ADDRINFOFLAGS.flaglabel | ADDRINFOFLAGS.flagcomment
            };

            if ( DbgAddrInfoGet ( address, 0, ref addrInfo ) )
            {
                symbols.Module = Marshal.PtrToStringAnsi ( addrInfo.module ) ?? "N/A";
                symbols.Label = Marshal.PtrToStringAnsi ( addrInfo.label ) ?? "N/A";
                string retrievedComment = Marshal.PtrToStringAnsi ( addrInfo.comment ) ?? "";

                if ( !string.IsNullOrEmpty ( retrievedComment ) )
                {
                    // Handle auto-comment marker (\1)
                    symbols.Comment = ( retrievedComment.Length > 0 && retrievedComment[0] == '\x01' )
                                      ? retrievedComment.Substring ( 1 )
                                      : retrievedComment;
                }
                return true;
            }
            else
            {
                // Fallback to get module info only if the full query fails
                var modInfoOnly = new BRIDGE_ADDRINFO_NATIVE { flags = ADDRINFOFLAGS.flagmodule, module = ptrModule };
                Marshal.WriteByte ( ptrModule, 0, 0 ); // Clear buffer before reuse

                if ( DbgAddrInfoGet ( address, 0, ref modInfoOnly ) )
                {
                    symbols.Module = Marshal.PtrToStringAnsi ( modInfoOnly.module ) ?? "Lookup Failed";
                }
                else
                {
                    symbols.Module = "Lookup Failed";
                }
                return false;
            }
        }
        finally
        {
            if ( ptrModule != IntPtr.Zero )
            {
                Marshal.FreeHGlobal ( ptrModule );
            }
            if ( ptrLabel != IntPtr.Zero )
            {
                Marshal.FreeHGlobal ( ptrLabel );
            }
            if ( ptrComment != IntPtr.Zero )
            {
                Marshal.FreeHGlobal ( ptrComment );
            }
        }
    }

    [Command ( "GetAllActiveThreads", DebugOnly = true, MCPOnly = true,
               MCPCmdDescription = "Example: GetAllActiveThreads" )]
    public static string GetAllActiveThreads()
    {
        try
        {
            // Get the list of threads with the extended information
            var threads = GetAllActiveThreadsFunc(); // This now returns List<(int, uint, ulong, ulong, string)>
            var output = new StringBuilder();

            output.AppendLine ( $"[GetAllActiveThreads] Found {threads.Count} active threads:" );

            // Update the foreach loop to destructure the new tuple elements
            foreach ( var ( ThreadNumber, ThreadId, EntryPoint, TEB, ThreadName ) in threads )
            {
                // Update the output line to include ThreadNumber and ThreadName
                // Adjust formatting as desired
                output.AppendLine (
                    $"Num: {ThreadNumber,3} | TID: {ThreadId,6} | EntryPoint: 0x{EntryPoint:X16} | TEB: 0x{TEB:X16} | Name: {ThreadName}" );
            }

            return output.ToString().TrimEnd(); // Removes trailing newline
        }
        catch ( Exception ex )
        {
            // Add more detail to the error if possible
            return $"[GetAllActiveThreads] Error: {ex.Message}\n{ex.StackTrace}";
        }
    }

    // Updated function signature and List type to include ThreadNumber and ThreadName
    public static
    List< ( int ThreadNumber, uint ThreadId, ulong EntryPoint, ulong TEB, string ThreadName ) > GetAllActiveThreadsFunc()
    {
        // Update the list's tuple definition
        var result = new List< ( int ThreadNumber, uint ThreadId, ulong EntryPoint, ulong TEB, string ThreadName ) >();
        THREADLIST_NATIVE nativeList = new THREADLIST_NATIVE();

        try
        {
            DbgGetThreadList ( ref nativeList );

            if ( nativeList.list != IntPtr.Zero && nativeList.count > 0 )
            {
                int sizeOfAllInfo = Marshal.SizeOf<THREADALLINFO>();
                // Console.WriteLine($"DEBUG: Marshal.SizeOf<THREADALLINFO>() = {sizeOfAllInfo}"); // Keep for debugging

                for ( int i = 0; i < nativeList.count; i++ )
                {
                    IntPtr currentPtr = new IntPtr ( nativeList.list.ToInt64() + ( long ) i * sizeOfAllInfo );
                    THREADALLINFO threadInfo = Marshal.PtrToStructure<THREADALLINFO> ( currentPtr );

                    // Add the extended information to the result list
                    // This now matches the List's tuple definition
                    result.Add ( (
                                     threadInfo.BasicInfo.ThreadNumber,
                                     threadInfo.BasicInfo.ThreadId,
                                     threadInfo.BasicInfo.ThreadStartAddress, // ulong
                                     threadInfo.BasicInfo.ThreadLocalBase,    // ulong
                                     threadInfo.BasicInfo.threadName          // string
                                 ) );
                }
            }
            else if ( nativeList.list == IntPtr.Zero && nativeList.count > 0 )
            {
                // Handle potential error case where count > 0 but list pointer is null
                Console.WriteLine (
                    $"[GetAllActiveThreadsFunc] Warning: nativeList.count is {nativeList.count} but nativeList.list is IntPtr.Zero." );
            }
        }
        catch ( Exception ex )
        {
            // Log or handle exceptions during marshalling/processing
            Console.WriteLine ( $"[GetAllActiveThreadsFunc] Exception during processing: {ex.Message}\n{ex.StackTrace}" );
            // Optionally re-throw or return partial results depending on desired behavior
            throw; // Re-throwing is often appropriate unless you want to suppress errors
        }
        finally
        {
            if ( nativeList.list != IntPtr.Zero )
            {
                // Console.WriteLine($"DEBUG: Calling BridgeFree for IntPtr {nativeList.list}"); // Add debug log
                //BridgeFree(nativeList.list); // Free the allocated memory - UNCOMMENT THIS!
            }
        }

        return result;
    }

    //public static List<(uint ThreadId, nuint EntryPoint, nuint TEB)> GetAllActiveThreadsFunc()
    //{
    //    var result = new List<(uint, nuint, nuint)>();

    //    THREADLIST threadList = new THREADLIST
    //    {
    //        Entries = new THREADENTRY[256]
    //    };

    //    DbgGetThreadList(ref threadList);

    //    for (int i = 0; i < threadList.Count; i++)
    //    {
    //        var t = threadList.Entries[i];
    //        result.Add((t.ThreadId, t.ThreadEntry, t.TebBase));
    //    }

    //    return result;
    //}



    [Command ( "GetAllRegisters", DebugOnly = true, MCPOnly = true, MCPCmdDescription = "Example: GetAllRegisters" )]
    public static string GetAllRegistersAsStrings()
    {
        string[] regNames = new[]
        {
            "rax", "rbx", "rcx", "rdx",
            "rsi", "rdi", "rbp", "rsp",
            "r8",  "r9",  "r10", "r11",
            "r12", "r13", "r14", "r15",
            "rip"
        };

        List<string> result = new List<string>();

        foreach ( string reg in regNames )
        {
            try
            {
                nuint val = Bridge.DbgValFromString ( reg );
                result.Add ( $"{reg.ToUpper(),-4}: {val.ToPtrString()}" );
            }
            catch
            {
                result.Add ( $"{reg.ToUpper(),-4}: <unavailable>" );
            }
        }

        return string.Join ( "\r\n", result );
    }


    [Command ( "ReadDismAtAddress", DebugOnly = true, MCPOnly = true,
               MCPCmdDescription = "Example: ReadDismAtAddress address=0x12345678, byteCount=100" )]
    public static string ReadDismAtAddress ( string address, int byteCount )
    {
        try
        {
            // Parse address string
            nuint startAddress = ParseAddressString ( address );
            return DisassembleMemoryRange ( startAddress, byteCount );
        }
        catch ( Exception ex )
        {
            return $"[ReadDismAtAddress] Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Parses a string representation of an address into a nuint value.
    /// </summary>
    /// <param name="address">The address string, with or without 0x prefix</param>
    /// <returns>The parsed address as nuint</returns>
    private static nuint ParseAddressString ( string address )
    {
        bool isHex = address.StartsWith ( "0x", StringComparison.OrdinalIgnoreCase );
        string valuePart = isHex ? address.Substring ( 2 ) : address;
        int baseValue = isHex ? 16 : 10;

        return ( nuint ) Convert.ToUInt64 ( valuePart, baseValue );
    }

    /// <summary>
    /// Disassembles a range of memory starting at the specified address.
    /// </summary>
    /// <param name="startAddress">The starting address for disassembly</param>
    /// <param name="byteCount">Maximum number of bytes to disassemble</param>
    /// <returns>A string containing the disassembled instructions</returns>
    private static string DisassembleMemoryRange ( nuint startAddress, int byteCount )
    {
        int instructionCount = 0;
        int bytesRead = 0;
        const int MAX_INSTRUCTIONS = 5000;
        var output = new StringBuilder();
        nuint currentAddress = startAddress;

        while ( instructionCount < MAX_INSTRUCTIONS && bytesRead < byteCount )
        {
            ProcessLabelIfPresent ( output, currentAddress );

            var disasm = new Bridge.BASIC_INSTRUCTION_INFO();
            Bridge.DbgDisasmFastAt ( currentAddress, ref disasm );

            if ( disasm.size == 0 )
            {
                currentAddress += 1;
                bytesRead += 1;
                continue;
            }

            string inlineString = GetInlineString ( disasm );
            AppendDisassembledInstruction ( output, currentAddress, disasm, inlineString );

            currentAddress += ( nuint ) disasm.size;
            bytesRead += disasm.size;
            instructionCount++;
        }

        AppendLimitMessages ( output, instructionCount, bytesRead, MAX_INSTRUCTIONS, byteCount );
        return output.ToString();
    }

    /// <summary>
    /// Processes and outputs any label present at the given address.
    /// </summary>
    /// <param name="output">The StringBuilder to append output to</param>
    /// <param name="address">The address to check for labels</param>
    private static void ProcessLabelIfPresent ( StringBuilder output, nuint address )
    {
        string label = GetLabel ( address );
        if ( !string.IsNullOrEmpty ( label ) )
        {
            output.AppendLine();
            output.AppendLine ( $"{label}:" );
        }
    }

    /// <summary>
    /// Extracts a string from the address referenced in the instruction, if possible.
    /// </summary>
    /// <param name="disasm">The disassembled instruction info</param>
    /// <returns>The string if found, null otherwise</returns>
    private static string GetInlineString ( Bridge.BASIC_INSTRUCTION_INFO disasm )
    {
        nuint ptr = GetPotentialStringPointer ( disasm );
        if ( ptr == 0 )
        { return null; }

        try
        {
            return ExtractStringFromMemory ( ptr );
        }
        catch
        {
            // Ignore bad memory access
            return null;
        }
    }

    /// <summary>
    /// Gets a potential string pointer from the instruction based on its type.
    /// </summary>
    /// <param name="disasm">The disassembled instruction info</param>
    /// <returns>A potential pointer to a string, or 0 if none found</returns>
    private static nuint GetPotentialStringPointer ( Bridge.BASIC_INSTRUCTION_INFO disasm )
    {
        if ( disasm.type == 1 ) // value (immediate)
        { return disasm.value.value; }
        else if ( disasm.type == 2 ) // address
        { return disasm.addr; }
        return 0;
    }

    /// <summary>
    /// Extracts a null-terminated ASCII string from the specified memory address.
    /// </summary>
    /// <param name="address">The memory address to read from</param>
    /// <returns>The extracted string, or null if not a valid ASCII string</returns>
    private static string ExtractStringFromMemory ( nuint address )
    {
        var strData = ReadMemory ( address, 64 );
        int len = Array.IndexOf ( strData, ( byte ) 0 );

        if ( len <= 0 )
        { return null; }

        var decoded = Encoding.ASCII.GetString ( strData, 0, len );

        // Check if the string contains only printable ASCII characters
        if ( decoded.All ( c => c >= 0x20 && c < 0x7F ) )
        { return decoded; }

        return null;
    }

    /// <summary>
    /// Appends a disassembled instruction to the output.
    /// </summary>
    /// <param name="output">The StringBuilder to append to</param>
    /// <param name="address">The address of the instruction</param>
    /// <param name="disasm">The disassembled instruction info</param>
    /// <param name="inlineString">Optional inline string reference to display</param>
    private static void AppendDisassembledInstruction (
        StringBuilder output,
        nuint address,
        Bridge.BASIC_INSTRUCTION_INFO disasm,
        string inlineString )
    {
        string bytes = BitConverter.ToString ( ReadMemory ( address, ( uint ) disasm.size ) );
        output.Append ( $"{address.ToPtrString()}  {bytes,-20}  {disasm.instruction}" );

        if ( inlineString != null )
        { output.Append ( $"    ; \"{inlineString}\"" ); }

        output.AppendLine();
    }

    /// <summary>
    /// Appends messages about any limits reached during disassembly.
    /// </summary>
    /// <param name="output">The StringBuilder to append to</param>
    /// <param name="instructionCount">Number of instructions processed</param>
    /// <param name="bytesRead">Number of bytes read</param>
    /// <param name="maxInstructions">Maximum allowed instructions</param>
    /// <param name="byteCount">Maximum allowed bytes</param>
    private static void AppendLimitMessages (
        StringBuilder output,
        int instructionCount,
        int bytesRead,
        int maxInstructions,
        int byteCount )
    {
        if ( instructionCount >= maxInstructions )
        { output.AppendLine ( $"; Max instruction limit ({maxInstructions}) reached" ); }

        if ( bytesRead >= byteCount )
        { output.AppendLine ( $"; Byte read limit ({byteCount}) reached" ); }
    }

    [Command ( "DumpModuleToFile", DebugOnly = true, MCPOnly = true,
               MCPCmdDescription = "Example: DumpModuleToFile pfilepath=C:\\Output.txt" )]
    public static void DumpModuleToFile ( string pfilepath )
    {
        Console.WriteLine ( $"Attempting to dump module info to: {pfilepath}" );

        try
        {
            var cip = Bridge.DbgValFromString ( "cip" );
            if ( !TryGetModuleInfo ( cip, out var modInfo ) )
            {
                Console.Error.WriteLine ( $"Error: Could not find module information for address {cip.ToPtrString()}. Is the debugger attached and running?" );
                return;
            }

            using ( var writer = new StreamWriter ( pfilepath, false, Encoding.UTF8 ) )
            {
                DumpHeader ( writer, modInfo.name );
                DumpRegisters ( writer, cip );
                DumpDisassembly ( writer, modInfo );
                DumpFooter ( writer );
            }

            Console.WriteLine ( $"Successfully dumped module '{modInfo.name}' and registers to {pfilepath}" );
        }
        catch ( UnauthorizedAccessException ex )
        {
            Console.Error.WriteLine ( $"Error: Access denied writing to '{pfilepath}'. Try running x64dbg as administrator or choose a different path. Details: {ex.Message}" );
        }
        catch ( IOException ex )
        {
            Console.Error.WriteLine ( $"Error: An I/O error occurred while writing to '{pfilepath}'. Details: {ex.Message}" );
        }
        catch ( Exception ex ) // Catch-all for other unexpected errors
        {
            Console.Error.WriteLine ( $"An unexpected error occurred: {ex.GetType().Name} - {ex.Message}" );
            Console.Error.WriteLine ( ex.StackTrace ); // Log stack trace for debugging
        }
    }

    private static void DumpHeader ( StreamWriter writer, string moduleName )
    {
        writer.WriteLine ( "--- Current Register State ---" );
        writer.WriteLine ( $"Module: {moduleName}" );
        writer.WriteLine ( $"Timestamp: {DateTime.Now}" );
        writer.WriteLine ( "-----------------------------" );
    }

    private static void DumpRegisters ( StreamWriter writer, nuint cip )
    {
        var registers = new[]
        {
            "RAX", "RBX", "RCX", "RDX", "RSI", "RDI", "RBP", "RSP",
            "R8", "R9", "R10", "R11", "R12", "R13", "R14", "R15",
            "EFlags"
        };

        writer.WriteLine ( $"RIP: {cip.ToPtrString()}" );
        foreach ( var reg in registers )
        {
            writer.WriteLine ( $"{reg}: {Bridge.DbgValFromString(reg.ToLower()).ToPtrString()}" );
        }
        writer.WriteLine ( "-----------------------------" );
        writer.WriteLine();
    }

    private static void DumpDisassembly ( StreamWriter writer, Module.ModuleInfo modInfo )
    {
        writer.WriteLine ( $"--- Disassembly for {modInfo.name} ({modInfo.@base.ToPtrString()} - {(modInfo.@base + modInfo.size).ToPtrString()}) ---" );
        writer.WriteLine ( "-----------------------------" );

        nuint currentAddr = modInfo.@base;
        var endAddr = modInfo.@base + modInfo.size;
        const int MAX_INSTRUCTIONS = 10000;
        int instructionCount = 0;

        while ( currentAddr < endAddr && instructionCount < MAX_INSTRUCTIONS )
        {
            string label = GetLabel ( currentAddr );
            if ( !string.IsNullOrEmpty ( label ) )
            {
                writer.WriteLine();
                writer.WriteLine ( $"{label}:" );
            }

            var disasm = new Bridge.BASIC_INSTRUCTION_INFO();
            Bridge.DbgDisasmFastAt ( currentAddr, ref disasm );
            if ( disasm.size == 0 )
            {
                writer.WriteLine ( $"{currentAddr.ToPtrString()}  (could not disassemble)" );
                currentAddr++;
                continue;
            }

            bool foundInlineString = TryDumpInlineString ( writer, disasm );

            string bytes = BitConverter.ToString ( ReadMemory ( currentAddr, ( uint ) disasm.size ) );
            writer.WriteLine ( $"{currentAddr.ToPtrString()}  {bytes,-20}  {disasm.instruction}" );

            currentAddr += ( nuint ) disasm.size;
            instructionCount++;
        }

        if ( instructionCount >= MAX_INSTRUCTIONS )
        {
            writer.WriteLine();
            writer.WriteLine ( $"--- Instruction limit ({MAX_INSTRUCTIONS}) reached. Dump truncated. ---" );
        }
        writer.WriteLine ( "-----------------------------" );
    }

    private static bool TryDumpInlineString ( StreamWriter writer, Bridge.BASIC_INSTRUCTION_INFO disasm )
    {
        nuint destAddr = 0;
        if ( disasm.type == 1 ) // value (immediate)
        {
            destAddr = disasm.value.value;
        }
        else if ( disasm.type == 2 ) // address
        {
            destAddr = disasm.addr;
        }

        if ( destAddr != 0 )
        {
            var inlineString = ReadMemoryString ( destAddr, 128 );
            if ( !string.IsNullOrEmpty ( inlineString ) )
            {
                writer.WriteLine ( $"    ; \"{inlineString}\"" );
                return true;
            }
        }
        return false;
    }

    private static void DumpFooter ( StreamWriter writer )
    {
        writer.WriteLine ( "--- Dump Complete ---" );
    }
}
}
