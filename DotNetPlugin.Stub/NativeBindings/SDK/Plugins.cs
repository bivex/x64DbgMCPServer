using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using DotNetPlugin.NativeBindings.Win32;
using System.Threading;

namespace DotNetPlugin.NativeBindings.SDK {
// https://github.com/x64dbg/x64dbg/blob/development/src/dbg/_plugins.h
public static class Plugins {
    public const int PLUG_SDKVERSION = 1;

    public delegate void CBPLUGIN ( CBTYPE cbType, IntPtr callbackInfo );

    public delegate bool CBPLUGINCOMMAND ( string[] args );

    public delegate nuint CBPLUGINEXPRFUNCTION ( nuint[] args, object userdata );
    public delegate nuint CBPLUGINEXPRFUNCTION_RAWARGS ( int argc, IntPtr argv, object userdata );

#if AMD64
    private const string dll = "x64dbg.dll";
#else
    private const string dll = "x32dbg.dll";
#endif
    private const CallingConvention cdecl = CallingConvention.Cdecl;

    #region Logging

    [DllImport ( dll, CallingConvention = cdecl, ExactSpelling = true )]
    public static extern void _plugin_logprint ( [MarshalAs ( UnmanagedType.LPUTF8Str )] string text );

    [DllImport ( dll, CallingConvention = cdecl, ExactSpelling = true )]
    public static extern void _plugin_logputs ( [MarshalAs ( UnmanagedType.LPUTF8Str )] string text );

    #endregion

    #region Event Callbacks

    [UnmanagedFunctionPointer ( CallingConvention.Cdecl )]
    private delegate void CBPLUGIN_NATIVE ( CBTYPE cbType, IntPtr callbackInfo );

    private static CBPLUGIN_NATIVE[] _pluginCallbacks = new CBPLUGIN_NATIVE[ ( int ) CBTYPE.CB_LAST];

    [DllImport ( dll, CallingConvention = cdecl, EntryPoint = nameof ( _plugin_registercallback ), ExactSpelling = true )]
    private static extern void _plugin_registercallback_native ( int pluginHandle, CBTYPE cbType,
            CBPLUGIN_NATIVE cbPlugin );

    public static void _plugin_registercallback ( int pluginHandle, CBTYPE cbType, CBPLUGIN cbPlugin )
    {
        if ( cbPlugin == null )
        { throw new ArgumentNullException ( nameof ( cbPlugin ) ); }

        if ( cbType < 0 || _pluginCallbacks.Length <= ( int ) cbType )
        { throw new ArgumentOutOfRangeException ( nameof ( cbType ) ); }

        CBPLUGIN_NATIVE callback = ( cbType, callbackInfo ) =>
        {
            try
            {
                cbPlugin ( cbType, callbackInfo );
            }
            catch ( Exception ex )
            {
                PluginMain.LogUnhandledException ( ex );
            }
        };

        Monitor.Enter ( _pluginCallbacks );
        try {
            _pluginCallbacks[ ( int ) cbType] = callback;

            _plugin_registercallback_native ( pluginHandle, cbType, callback );
        } finally {
            Monitor.Exit ( _pluginCallbacks );
        }
    }

    [DllImport ( dll, CallingConvention = cdecl, EntryPoint = nameof ( _plugin_unregistercallback ), ExactSpelling = true )]
    private static extern bool _plugin_unregistercallback_native ( int pluginHandle, CBTYPE cbType );

    public static bool _plugin_unregistercallback ( int pluginHandle, CBTYPE cbType )
    {
        if ( cbType < 0 || _pluginCallbacks.Length <= ( int ) cbType )
        { throw new ArgumentOutOfRangeException ( nameof ( cbType ) ); }

        Monitor.Enter ( _pluginCallbacks );
        try {
            var success = _plugin_unregistercallback_native ( pluginHandle, cbType );

            if ( success )
            { _pluginCallbacks[ ( int ) cbType] = null; }

            return success;
        } finally {
            Monitor.Exit ( _pluginCallbacks );
        }
    }

    #endregion

    #region Commands

    [UnmanagedFunctionPointer ( CallingConvention.Cdecl )]
    private delegate bool CBPLUGINCOMMAND_NATIVE ( int argc, IntPtr argv );

    private static Dictionary<string, CBPLUGINCOMMAND_NATIVE> _commandCallbacks = new
    Dictionary<string, CBPLUGINCOMMAND_NATIVE>();

    [DllImport ( dll, CallingConvention = cdecl, EntryPoint = nameof ( _plugin_registercommand ), ExactSpelling = true )]
    private static extern bool _plugin_registercommand_native ( int pluginHandle, [MarshalAs (
                UnmanagedType.LPUTF8Str )] string command, CBPLUGINCOMMAND_NATIVE cbCommand, bool debugonly );

    public static bool _plugin_registercommand ( int pluginHandle, string command, CBPLUGINCOMMAND cbCommand,
            bool debugonly )
    {
        if ( command == null )
        { throw new ArgumentNullException ( nameof ( command ) ); }

        if ( cbCommand == null )
        { throw new ArgumentNullException ( nameof ( cbCommand ) ); }

        CBPLUGINCOMMAND_NATIVE callback = ( argc, argv ) =>
        {
            try
            {
                string[] argvArray;
                if ( argc > 0 )
                {
                    argvArray = new string[argc];
                    for ( int i = 0, ofs = 0; i < argvArray.Length; i++, ofs += IntPtr.Size )
                    { argvArray[i] = Marshal.ReadIntPtr ( argv, ofs ).MarshalToStringUTF8(); }
                }
                else
                { argvArray = Array.Empty<string>(); }

                return cbCommand ( argvArray );
            }
            catch ( Exception ex )
            {
                PluginMain.LogUnhandledException ( ex );
                return false;
            }
        };

        Monitor.Enter ( _commandCallbacks );
        try {
            // The CLR protects the delegate from being GC'd only for the duration of the call, so we need to keep a reference to it until unregistration.
            var success = _plugin_registercommand_native ( pluginHandle, command, callback, debugonly );

            if ( success )
            { _commandCallbacks[command] = callback; }

            return success;
        } finally {
            Monitor.Exit ( _commandCallbacks );
        }
    }

    [DllImport ( dll, CallingConvention = cdecl, EntryPoint = nameof ( _plugin_unregistercommand ), ExactSpelling = true )]
    private static extern bool _plugin_unregistercommand_native ( int pluginHandle, [MarshalAs (
                UnmanagedType.LPUTF8Str )] string command );

    public static bool _plugin_unregistercommand ( int pluginHandle, string command )
    {
        if ( command == null )
        { throw new ArgumentNullException ( nameof ( command ) ); }

        Monitor.Enter ( _commandCallbacks );
        try {
            var success = _plugin_unregistercommand_native ( pluginHandle, command );

            if ( success )
            { _commandCallbacks.Remove ( command ); }

            return success;
        } finally {
            Monitor.Exit ( _commandCallbacks );
        }
    }

    #endregion

    #region Expression Functions

    [UnmanagedFunctionPointer ( CallingConvention.Cdecl )]
    private delegate nuint CBPLUGINEXPRFUNCTION_NATIVE ( int argc, IntPtr argv, IntPtr userdata );

    private static Dictionary<string, ( CBPLUGINEXPRFUNCTION_NATIVE, GCHandle ) > _expressionFunctionCallbacks = new
    Dictionary<string, ( CBPLUGINEXPRFUNCTION_NATIVE, GCHandle ) >();

    [DllImport ( dll, CallingConvention = cdecl, EntryPoint = nameof ( _plugin_registerexprfunction ),
                 ExactSpelling = true )]
    private static extern bool _plugin_registerexprfunction_native ( int pluginHandle, [MarshalAs (
                UnmanagedType.LPUTF8Str )] string name, int argc,
            CBPLUGINEXPRFUNCTION_NATIVE cbCommand, IntPtr userdata );

    private static bool _plugin_registerexprfunction_core ( int pluginHandle, string name, int argc,
            CBPLUGINEXPRFUNCTION_NATIVE callback, object userdata )
    {
        var userdataHandle = userdata != null ? GCHandle.Alloc ( userdata ) : default;
        try
        {
            Monitor.Enter ( _expressionFunctionCallbacks );
            try {
                // The CLR protects the delegate from being GC'd only for the duration of the call, so we need to keep a reference to it until unregistration.
                var success = _plugin_registerexprfunction_native ( pluginHandle, name, argc, callback,
                              GCHandle.ToIntPtr ( userdataHandle ) );

                if ( success )
                { _expressionFunctionCallbacks[name] = ( callback, userdataHandle ); }

                return success;
            } finally {
                Monitor.Exit ( _expressionFunctionCallbacks );
            }
        }
        catch
        {
            if ( userdataHandle.IsAllocated )
            { userdataHandle.Free(); }
            throw;
        }
    }

    public static bool _plugin_registerexprfunction ( int pluginHandle, string name, int argc,
            CBPLUGINEXPRFUNCTION cbFunction, object userdata )
    {
        if ( name == null )
        { throw new ArgumentNullException ( nameof ( name ) ); }

        if ( cbFunction == null )
        { throw new ArgumentNullException ( nameof ( cbFunction ) ); }

        CBPLUGINEXPRFUNCTION_NATIVE callback = ( argc, argv, userdata ) =>
        {
            try
            {
                nuint[] argvArray;
                if ( argc > 0 )
                {
                    argvArray = new nuint[argc];
                    for ( int i = 0, ofs = 0; i < argvArray.Length; i++, ofs += IntPtr.Size )
                    { argvArray[i] = ( nuint ) ( nint ) Marshal.ReadIntPtr ( argv, ofs ); }
                }
                else
                { argvArray = Array.Empty<nuint>(); }

                object userdataObj = userdata != IntPtr.Zero ? GCHandle.FromIntPtr ( userdata ) : null;
                return cbFunction ( argvArray, userdataObj );
            }
            catch ( Exception ex )
            {
                PluginMain.LogUnhandledException ( ex );
                return default;
            }
        };

        return _plugin_registerexprfunction_core ( pluginHandle, name, argc, callback, userdata );
    }

    public static bool _plugin_registerexprfunction ( int pluginHandle, string name, int argc,
            CBPLUGINEXPRFUNCTION_RAWARGS cbFunction, object userdata )
    {
        if ( name == null )
        { throw new ArgumentNullException ( nameof ( name ) ); }

        if ( cbFunction == null )
        { throw new ArgumentNullException ( nameof ( cbFunction ) ); }

        CBPLUGINEXPRFUNCTION_NATIVE callback = ( argc, argv, userdata ) =>
        {
            try
            {
                object userdataObj = userdata != IntPtr.Zero ? GCHandle.FromIntPtr ( userdata ) : null;
                return cbFunction ( argc, argv, userdataObj );
            }
            catch ( Exception ex )
            {
                PluginMain.LogUnhandledException ( ex );
                return default;
            }
        };

        return _plugin_registerexprfunction_core ( pluginHandle, name, argc, callback, userdata );
    }

    [DllImport ( dll, CallingConvention = cdecl, EntryPoint = nameof ( _plugin_unregisterexprfunction ),
                 ExactSpelling = true )]
    private static extern bool _plugin_unregisterexprfunction_native ( int pluginHandle, [MarshalAs (
                UnmanagedType.LPUTF8Str )] string name );

    public static bool _plugin_unregisterexprfunction ( int pluginHandle, string name )
    {
        if ( name == null )
        { throw new ArgumentNullException ( nameof ( name ) ); }

        Monitor.Enter ( _expressionFunctionCallbacks );
        try {
            var success = _plugin_unregisterexprfunction_native ( pluginHandle, name );

            if ( success )
            {
                if ( _expressionFunctionCallbacks.TryGetValue ( name, out var callbackInfo ) )
                {
                    if ( callbackInfo.Item2.IsAllocated )
                    { callbackInfo.Item2.Free(); }

                    _expressionFunctionCallbacks.Remove ( name );
                }
            }

            return success;
        } finally {
            Monitor.Exit ( _expressionFunctionCallbacks );
        }
    }

    #endregion

    #region Menus

    [DllImport ( dll, CallingConvention = cdecl, ExactSpelling = true )]
    public static extern int _plugin_menuadd ( int hMenu, [MarshalAs ( UnmanagedType.LPUTF8Str )] string title );

    [DllImport ( dll, CallingConvention = cdecl, ExactSpelling = true )]
    public static extern bool _plugin_menuaddentry ( int hMenu,
            int hEntry, [MarshalAs ( UnmanagedType.LPUTF8Str )] string title );

    [DllImport ( dll, CallingConvention = cdecl, ExactSpelling = true )]
    public static extern bool _plugin_menuaddseparator ( int hMenu );

    [DllImport ( dll, CallingConvention = cdecl, ExactSpelling = true )]
    public static extern void _plugin_menuseticon ( int hMenu, ref BridgeBase.ICONDATA icon );

    [DllImport ( dll, CallingConvention = cdecl, ExactSpelling = true )]
    public static extern void _plugin_menuentryseticon ( int pluginHandle, int hEntry, ref BridgeBase.ICONDATA icon );

    [DllImport ( dll, CallingConvention = cdecl, ExactSpelling = true )]
    public static extern void _plugin_menuentrysetchecked ( int pluginHandle, int hEntry, bool @checked );

    [DllImport ( dll, CallingConvention = cdecl, ExactSpelling = true )]
    public static extern void _plugin_menusetvisible ( int pluginHandle, int hMenu, bool visible );

    [DllImport ( dll, CallingConvention = cdecl, ExactSpelling = true )]
    public static extern void _plugin_menuentrysetvisible ( int pluginHandle, int hEntry, bool visible );

    [DllImport ( dll, CallingConvention = cdecl, ExactSpelling = true )]
    public static extern void _plugin_menusetname ( int pluginHandle,
            int hMenu, [MarshalAs ( UnmanagedType.LPUTF8Str )] string name );

    [DllImport ( dll, CallingConvention = cdecl, ExactSpelling = true )]
    public static extern void _plugin_menuentrysetname ( int pluginHandle,
            int hEntry, [MarshalAs ( UnmanagedType.LPUTF8Str )] string name );

    [DllImport ( dll, CallingConvention = cdecl, ExactSpelling = true )]
    public static extern void _plugin_menuentrysethotkey ( int pluginHandle,
            int hEntry, [MarshalAs ( UnmanagedType.LPUTF8Str )] string hotkey );

    [DllImport ( dll, CallingConvention = cdecl, ExactSpelling = true )]
    public static extern bool _plugin_menuremove ( int hMenu );

    [DllImport ( dll, CallingConvention = cdecl, ExactSpelling = true )]
    public static extern bool _plugin_menuentryremove ( int pluginHandle, int hEntry );

    [DllImport ( dll, CallingConvention = cdecl, ExactSpelling = true )]
    public static extern bool _plugin_menuclear ( int hMenu );

    #endregion

    [DllImport ( dll, CallingConvention = cdecl, ExactSpelling = true )]
    public static extern void _plugin_debugskipexceptions ( bool skip );

#pragma warning disable 0649

    [Serializable]
    public unsafe struct PLUG_INITSTRUCT
    {
        public int pluginHandle;
        public int sdkVersion;
        public int pluginVersion;

        private const int pluginNameSize = 256;
        public fixed byte pluginNameBytes[pluginNameSize];
        public string pluginName
        {
            get
            {
                fixed ( byte* ptr = pluginNameBytes )
                    return new IntPtr ( ptr ).MarshalToStringUTF8 ( pluginNameSize );
            }
            set
            {
                fixed ( byte* ptr = pluginNameBytes )
                    value.MarshalToPtrUTF8 ( new IntPtr ( ptr ), pluginNameSize );
            }
        }
    }

    [Serializable]
    public struct PLUG_SETUPSTRUCT
    {
        public IntPtr hwndDlg; //gui window handle
        public int hMenu; //plugin menu handle
        public int hMenuDisasm; //plugin disasm menu handle
        public int hMenuDump; //plugin dump menu handle
        public int hMenuStack; //plugin stack menu handle
        public int hMenuGraph; //plugin graph menu handle
        public int hMenuMemmap; //plugin memory map menu handle
        public int hMenuSymmod; //plugin symbol module menu handle
    }

    public enum CBTYPE
    {
        CB_INITDEBUG, //PLUG_CB_INITDEBUG
        CB_STOPDEBUG, //PLUG_CB_STOPDEBUG
        CB_CREATEPROCESS, //PLUG_CB_CREATEPROCESS
        CB_EXITPROCESS, //PLUG_CB_EXITPROCESS
        CB_CREATETHREAD, //PLUG_CB_CREATETHREAD
        CB_EXITTHREAD, //PLUG_CB_EXITTHREAD
        CB_SYSTEMBREAKPOINT, //PLUG_CB_SYSTEMBREAKPOINT
        CB_LOADDLL, //PLUG_CB_LOADDLL
        CB_UNLOADDLL, //PLUG_CB_UNLOADDLL
        CB_OUTPUTDEBUGSTRING, //PLUG_CB_OUTPUTDEBUGSTRING
        CB_EXCEPTION, //PLUG_CB_EXCEPTION
        CB_BREAKPOINT, //PLUG_CB_BREAKPOINT
        CB_PAUSEDEBUG, //PLUG_CB_PAUSEDEBUG
        CB_RESUMEDEBUG, //PLUG_CB_RESUMEDEBUG
        CB_STEPPED, //PLUG_CB_STEPPED
        CB_ATTACH, //PLUG_CB_ATTACHED (before attaching, after CB_INITDEBUG)
        CB_DETACH, //PLUG_CB_DETACH (before detaching, before CB_STOPDEBUG)
        CB_DEBUGEVENT, //PLUG_CB_DEBUGEVENT (called on any debug event)
        CB_MENUENTRY, //PLUG_CB_MENUENTRY
        CB_WINEVENT, //PLUG_CB_WINEVENT
        CB_WINEVENTGLOBAL, //PLUG_CB_WINEVENTGLOBAL
        CB_LOADDB, //PLUG_CB_LOADSAVEDB
        CB_SAVEDB, //PLUG_CB_LOADSAVEDB
        CB_FILTERSYMBOL, //PLUG_CB_FILTERSYMBOL
        CB_TRACEEXECUTE, //PLUG_CB_TRACEEXECUTE
        CB_SELCHANGED, //PLUG_CB_SELCHANGED
        CB_ANALYZE, //PLUG_CB_ANALYZE
        CB_ADDRINFO, //PLUG_CB_ADDRINFO
        CB_VALFROMSTRING, //PLUG_CB_VALFROMSTRING
        CB_VALTOSTRING, //PLUG_CB_VALTOSTRING
        CB_MENUPREPARE, //PLUG_CB_MENUPREPARE
        CB_STOPPINGDEBUG, //PLUG_CB_STOPDEBUG
        CB_LAST
    }

    [Serializable]
    public struct PLUG_CB_INITDEBUG
    {
        public Utf8StringRef szFileName;
    }

    [Serializable]
    public struct PLUG_CB_CREATEPROCESS
    {
        private IntPtr CreateProcessInfoPtr;
        public StructRef<CREATE_PROCESS_DEBUG_INFO> CreateProcessInfo => new StructRef<CREATE_PROCESS_DEBUG_INFO>
        ( CreateProcessInfoPtr );

        private IntPtr modInfoPtr;
        public IMAGEHLP_MODULE64? modInfo => modInfoPtr.ToStruct<IMAGEHLP_MODULE64>();

        public Utf8StringRef DebugFileName;

        private IntPtr fdProcessInfoPtr; //WAPI.PROCESS_INFORMATION
        public StructRef<PROCESS_INFORMATION> fdProcessInfo => new StructRef<PROCESS_INFORMATION> ( fdProcessInfoPtr );
    }

    [Serializable]
    public struct PLUG_CB_EXITPROCESS
    {
        private IntPtr ExitProcessPtr;
        public StructRef<EXIT_PROCESS_DEBUG_INFO> ExitProcess => new StructRef<EXIT_PROCESS_DEBUG_INFO> ( ExitProcessPtr );
    }

    [Serializable]
    public struct PLUG_CB_CREATETHREAD
    {
        private IntPtr CreateThreadPtr;
        public StructRef<CREATE_THREAD_DEBUG_INFO> CreateThread => new StructRef<CREATE_THREAD_DEBUG_INFO> ( CreateThreadPtr );

        public uint dwThreadId;
    }

    [Serializable]
    public struct PLUG_CB_EXITTHREAD
    {
        private IntPtr ExitThreadPtr;
        public StructRef<EXIT_THREAD_DEBUG_INFO> ExitThread => new StructRef<EXIT_THREAD_DEBUG_INFO> ( ExitThreadPtr );

        public uint dwThreadId;
    }

    [Serializable]
    public struct PLUG_CB_SYSTEMBREAKPOINT
    {
        public IntPtr reserved;
    }

    [Serializable]
    public struct PLUG_CB_LOADDLL
    {
        private IntPtr LoadDllPtr;
        public StructRef<LOAD_DLL_DEBUG_INFO> LoadDll => new StructRef<LOAD_DLL_DEBUG_INFO> ( LoadDllPtr );

        private IntPtr modInfoPtr;
        public IMAGEHLP_MODULE64? modInfo => modInfoPtr.ToStruct<IMAGEHLP_MODULE64>();

        public Utf8StringRef modname;
    }

    [Serializable]
    public struct PLUG_CB_UNLOADDLL
    {
        private IntPtr UnloadDllPtr;
        public StructRef<UNLOAD_DLL_DEBUG_INFO> UnloadDll => new StructRef<UNLOAD_DLL_DEBUG_INFO> ( UnloadDllPtr );
    }

    [Serializable]
    public struct PLUG_CB_OUTPUTDEBUGSTRING
    {
        private IntPtr DebugStringPtr;
        public StructRef<OUTPUT_DEBUG_STRING_INFO> DebugString => new StructRef<OUTPUT_DEBUG_STRING_INFO> ( DebugStringPtr );
    }

    [Serializable]
    public struct PLUG_CB_EXCEPTION
    {
        private IntPtr ExceptionPtr;
        public StructRef<EXCEPTION_DEBUG_INFO> Exception => new StructRef<EXCEPTION_DEBUG_INFO> ( ExceptionPtr );
    }

    public static class BridgeBpConstants {
        public const int MAX_BREAKPOINT_SIZE = 256;
        public const int MAX_MODULE_SIZE = 256;
        public const int MAX_CONDITIONAL_EXPR_SIZE = 256;
        public const int MAX_CONDITIONAL_TEXT_SIZE = 256;
    }
    [Flags]
    public enum BPXTYPE : uint
    {
        None = 0,
        Normal = 1,
        Hardware = 2,
        Memory = 4,
        Dll = 8,
        Exception = 16
    }

    [Serializable]
    public struct BRIDGEBP
    {
        public BPXTYPE type;
        public IntPtr addr;         // duint
        //public StructRef<BREAKPOINT> breakpoint => new StructRef<BREAKPOINT>(addr);

        [MarshalAs ( UnmanagedType.I1 )]
        public bool enabled;
        [MarshalAs ( UnmanagedType.I1 )]
        public bool singleshoot;
        [MarshalAs ( UnmanagedType.I1 )]
        public bool active;

        // fixed-size ANSI buffers
        [MarshalAs ( UnmanagedType.ByValTStr, SizeConst = BridgeBpConstants.MAX_BREAKPOINT_SIZE )]
        public string name;

        [MarshalAs ( UnmanagedType.ByValTStr, SizeConst = BridgeBpConstants.MAX_MODULE_SIZE )]
        public string mod;

        public ushort slot;

        // extended part
        public byte typeEx;      // BPHWTYPE / BPMEMTYPE / BPDLLTYPE / BPEXTYPE
        public byte hwSize;      // BPHWSIZE
        public uint hitCount;

        [MarshalAs ( UnmanagedType.I1 )]
        public bool fastResume;
        [MarshalAs ( UnmanagedType.I1 )]
        public bool silent;

        [MarshalAs ( UnmanagedType.ByValTStr, SizeConst = BridgeBpConstants.MAX_CONDITIONAL_EXPR_SIZE )]
        public string breakCondition;

        [MarshalAs ( UnmanagedType.ByValTStr, SizeConst = BridgeBpConstants.MAX_CONDITIONAL_TEXT_SIZE )]
        public string logText;

        [MarshalAs ( UnmanagedType.ByValTStr, SizeConst = BridgeBpConstants.MAX_CONDITIONAL_EXPR_SIZE )]
        public string logCondition;

        [MarshalAs ( UnmanagedType.ByValTStr, SizeConst = BridgeBpConstants.MAX_CONDITIONAL_TEXT_SIZE )]
        public string commandText;

        [MarshalAs ( UnmanagedType.ByValTStr, SizeConst = BridgeBpConstants.MAX_CONDITIONAL_EXPR_SIZE )]
        public string commandCondition;
    }

    //[Serializable]
    //public struct BREAKPOINT
    //{
    //    public UIntPtr lpBREAKPOINT;
    //}

    [Serializable]
    public struct PLUG_CB_BREAKPOINT
    {
        public IntPtr breakpointptr;
        public BRIDGEBP? breakpoint => breakpointptr.ToStruct<BRIDGEBP>();
    }

    [Serializable]
    public struct PLUG_CB_PAUSEDEBUG
    {
        public IntPtr reserved;
    }

    [Serializable]
    public struct PLUG_CB_RESUMEDEBUG
    {
        public IntPtr reserved;
    }

    [Serializable]
    public struct PLUG_CB_STEPPED
    {
        public IntPtr reserved;
    }

    [Serializable]
    public struct PLUG_CB_ATTACH
    {
        public uint dwProcessId;
    }

    [Serializable]
    public struct PLUG_CB_DETACH
    {
        private IntPtr fdProcessInfoPtr;
        public StructRef<PROCESS_INFORMATION> fdProcessInfo => new StructRef<PROCESS_INFORMATION> ( fdProcessInfoPtr );
    }

    [Serializable]
    public struct PLUG_CB_DEBUGEVENT
    {
        private IntPtr DebugEventPtr;
        public StructRef<DEBUG_EVENT> DebugEvent => new StructRef<DEBUG_EVENT> ( DebugEventPtr );
    }

    [Serializable]
    public struct PLUG_CB_MENUENTRY
    {
        public int hEntry;
    }

    [Serializable]
    public struct PLUG_CB_WINEVENT
    {
        private IntPtr messagePtr;
        public StructRef<MSG> message => new StructRef<MSG> ( messagePtr );

        public IntPtr result;
        public BlittableBoolean retval;
    }

    [Serializable]
    public struct PLUG_CB_WINEVENTGLOBAL
    {
        private IntPtr messagePtr;
        public StructRef<MSG> message => new StructRef<MSG> ( messagePtr );

        public BlittableBoolean retval;
    }

    [Serializable]
    public struct PLUG_CB_LOADSAVEDB
    {
        // TODO: add definition for struct json_t
        public IntPtr root;
        public int loadSaveType;
    }

    [Serializable]
    public struct PLUG_CB_FILTERSYMBOL
    {
        public Utf8StringRef symbol;
        public BlittableBoolean retval;
    }

    [Serializable]
    public struct PLUG_CB_TRACEEXECUTE
    {
        public nuint cip;
        public BlittableBoolean stop;
    }

    [Serializable]
    public struct PLUG_CB_SELCHANGED
    {
        public int hWindow;
        public nuint VA;
    }

    [Serializable]
    public struct PLUG_CB_ANALYZE
    {
        public BridgeBase.BridgeCFGraphList graph;
    }

    [Serializable]
    public struct PLUG_CB_ADDRINFO
    {
        public nuint addr;

        private IntPtr addrinfoPtr;
        public StructRef<BridgeBase.BRIDGE_ADDRINFO> addrinfo => new StructRef<BridgeBase.BRIDGE_ADDRINFO> ( addrinfoPtr );

        public BlittableBoolean retval;
    }

    [Serializable]
    public struct PLUG_CB_VALFROMSTRING
    {
        public Utf8StringRef @string;
        public nuint value;

        private IntPtr value_sizePtr;
        public StructRef<int> value_size => new StructRef<int> ( value_sizePtr );

        private IntPtr isvarPtr;
        public StructRef<BlittableBoolean> isvar => new StructRef<BlittableBoolean> ( isvarPtr );

        private IntPtr hexonlyPtr;
        public StructRef<BlittableBoolean> hexonly => new StructRef<BlittableBoolean> ( hexonlyPtr );

        public BlittableBoolean retval;
    }

    [Serializable]
    public struct PLUG_CB_VALTOSTRING
    {
        public Utf8StringRef @string;
        public nuint value;
        public BlittableBoolean retval;
    }

    [Serializable]
    public struct PLUG_CB_MENUPREPARE
    {
        public BridgeBase.GUIMENUTYPE hMenu;
    }

    [Serializable]
    public struct PLUG_CB_STOPDEBUG
    {
        public IntPtr reserved;
    }
}
}
