using System;

namespace DotNetPlugin {
[AttributeUsage ( AttributeTargets.Method )]
public class CommandAttribute : Attribute {
    public string Name { get; private set; }
    public bool DebugOnly { get; set; }
    public bool X64DbgOnly { get; set; }
    public bool MCPOnly { get; set; }
    public string MCPCmdDescription { get; set; }

    public CommandAttribute ( string name )
    {
        Name = name;
    }
}
}