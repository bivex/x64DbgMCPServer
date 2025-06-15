using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using DotNetPlugin.NativeBindings.SDK;

namespace DotNetPlugin.Mcp {
public class McpCommandDispatcher {
    private readonly Dictionary<string, MethodInfo> _commands = new Dictionary<string, MethodInfo>
    ( StringComparer.OrdinalIgnoreCase );
    private readonly Type _commandSourceType;

    public McpCommandDispatcher ( Type commandSourceType )
    {
        _commandSourceType = commandSourceType;
        // Reflect and register [Command] methods from the target type
        foreach ( var method in commandSourceType.GetMethods ( BindingFlags.Static | BindingFlags.Public |
                  BindingFlags.NonPublic ) )
        {
            var attr = method.GetCustomAttribute<CommandAttribute>();
            if ( attr != null )
            {
                _commands[attr.Name] = method;
            }
        }
    }

    public object HandleToolCall ( string toolName, Dictionary<string, object> arguments, bool isActivelyDebugging )
    {
        MethodInfo methodInfo;
        bool commandFound;
        lock ( _commands )
        {
            commandFound = _commands.TryGetValue ( toolName, out methodInfo );
        }

        if ( !commandFound )
        {
            throw new InvalidOperationException ( $"Tool '{toolName}' not found." );
        }

        var attribute = methodInfo.GetCustomAttribute<CommandAttribute>();
        if ( attribute == null || attribute.X64DbgOnly || ( attribute.DebugOnly && !isActivelyDebugging ) )
        {
            throw new InvalidOperationException (
                $"Command '{toolName}' is not available in this context, you must begin debugging an application first!" );
        }

        var parameters = methodInfo.GetParameters();
        var invokeArgs = new object[parameters.Length];

        for ( int i = 0; i < parameters.Length; i++ )
        {
            var param = parameters[i];
            object argValue = null;
            bool argProvided = arguments.TryGetValue ( param.Name, out argValue );

            if ( argProvided && argValue != null )
            {
                try
                {
                    invokeArgs[i] = ConvertArgumentType ( argValue, param.ParameterType, param.Name );
                }
                catch ( Exception convEx )
                {
                    throw new ArgumentException ( $"Cannot convert argument '{param.Name}' for tool '{toolName}'. Error: {convEx.Message}",
                                                  convEx );
                }
            }
            else if ( param.IsOptional )
            {
                invokeArgs[i] = param.DefaultValue;
            }
            else
            {
                throw new ArgumentException ( $"Missing required argument: '{param.Name}' for tool '{toolName}'" );
            }
        }

        try
        {
            return methodInfo.Invoke ( null, invokeArgs );
        }
        catch ( TargetInvocationException tie )
        {
            throw tie.InnerException ?? tie;
        }
    }

    public List<object> GetToolsList ( bool isDebuggerAttached, bool isActivelyDebugging )
    {
        var toolsList = new List<object>();

        lock ( _commands )
        {
            foreach ( var command in _commands )
            {
                string commandName = command.Key;
                MethodInfo methodInfo = command.Value;
                var attribute = methodInfo.GetCustomAttribute<CommandAttribute>();

                if ( attribute != null && !attribute.X64DbgOnly && ( !attribute.DebugOnly || ( isDebuggerAttached
                        && isActivelyDebugging ) ) )
                {
                    var parameters = methodInfo.GetParameters();
                    var properties = new Dictionary<string, object>();
                    var required = new List<string>();

                    foreach ( var param in parameters )
                    {
                        string paramName = param.Name;
                        string paramType = GetJsonSchemaType ( param.ParameterType );
                        string paramDescription = $"Parameter '{paramName}' for {commandName}";

                        object parameterSchema;

                        if ( param.ParameterType.IsArray )
                        {
                            Type? elementType = param.ParameterType.GetElementType();
                            if ( elementType != null )
                            {
                                string itemSchemaType = GetJsonSchemaType ( elementType );
                                parameterSchema = new
                                {
                                    type = "array",
                                    description = paramDescription,
                                    items = new
                                    {
                                        type = itemSchemaType
                                    }
                                };
                            }
                            else
                            {
                                parameterSchema = new
                                {
                                    type = "array",
                                    description = $"{paramDescription} (Warning: Could not determine array element type)"
                                };
                            }
                        }
                        else
                        {
                            parameterSchema = new
                            {
                                type = paramType,
                                description = paramDescription
                            };
                        }

                        properties[paramName] = parameterSchema;

                        if ( !param.IsOptional )
                        {
                            required.Add ( paramName );
                        }
                    }

                    toolsList.Add ( new
                    {
                        name = commandName,
                        description = string.IsNullOrEmpty ( attribute.MCPCmdDescription ) ? $"Executes the {commandName} command." : attribute.MCPCmdDescription,
                        inputSchema = new
                        {
                            title = commandName,
                            description = string.IsNullOrEmpty ( attribute.MCPCmdDescription ) ? $"Input schema for {commandName}." : attribute.MCPCmdDescription,
                            type = "object",
                            properties = properties,
                            required = required.ToArray()
                        }
                    } );
                }
            }
        }
        return toolsList;
    }

    // Helper method to convert C# types to JSON schema types
    private string GetJsonSchemaType ( Type type )
    {
        if ( type == typeof ( string ) )
        {
            return "string";
        }
        if ( type == typeof ( int ) || type == typeof ( long ) || type == typeof ( short ) ||
                type == typeof ( uint ) || type == typeof ( ulong ) || type == typeof ( ushort ) )
        { return "integer"; }
        if ( type == typeof ( float ) || type == typeof ( double ) || type == typeof ( decimal ) )
        { return "number"; }
        if ( type == typeof ( bool ) )
        {
            return "boolean";
        }
        if ( type.IsArray )
        {
            return "array";
        }
        return "object";
    }

    private object ConvertArgumentType ( object argValue, Type requiredType, string paramName )
    {
        if ( argValue == null )
        {
            if ( requiredType.IsClass || Nullable.GetUnderlyingType ( requiredType ) != null )
            {
                return null;
            }
            throw new ArgumentNullException ( paramName,
                                              $"Null provided for non-nullable parameter '{paramName}' of type {requiredType.Name}" );
        }

        if ( requiredType.IsInstanceOfType ( argValue ) )
        {
            return argValue;
        }

        if ( requiredType == typeof ( int ) )
        {
            return Convert.ToInt32 ( argValue );
        }
        if ( requiredType == typeof ( long ) )
        {
            return Convert.ToInt64 ( argValue );
        }
        if ( requiredType == typeof ( short ) )
        {
            return Convert.ToInt16 ( argValue );
        }
        if ( requiredType == typeof ( byte ) )
        {
            return Convert.ToByte ( argValue );
        }
        if ( requiredType == typeof ( uint ) )
        {
            return Convert.ToUInt32 ( argValue );
        }
        if ( requiredType == typeof ( ulong ) )
        {
            return Convert.ToUInt64 ( argValue );
        }
        if ( requiredType == typeof ( ushort ) )
        {
            return Convert.ToUInt16 ( argValue );
        }
        if ( requiredType == typeof ( sbyte ) )
        {
            return Convert.ToSByte ( argValue );
        }
        if ( requiredType == typeof ( float ) )
        {
            return Convert.ToSingle ( argValue );
        }
        if ( requiredType == typeof ( double ) )
        {
            return Convert.ToDouble ( argValue );
        }
        if ( requiredType == typeof ( decimal ) )
        {
            return Convert.ToDecimal ( argValue );
        }
        if ( requiredType == typeof ( bool ) )
        {
            return Convert.ToBoolean ( argValue );
        }
        if ( requiredType == typeof ( Guid ) )
        {
            return Guid.Parse ( argValue.ToString() );
        }

        if ( requiredType.IsEnum )
        {
            return System.Enum.Parse ( requiredType, argValue.ToString(), ignoreCase: true );
        }

        if ( requiredType.IsArray && argValue is System.Collections.ArrayList list )
        {
            var elementType = requiredType.GetElementType();
            var typedArray = Array.CreateInstance ( elementType, list.Count );
            for ( int j = 0; j < list.Count; j++ )
            {
                try
                {
                    typedArray.SetValue ( Convert.ChangeType ( list[j], elementType ), j );
                }
                catch ( Exception ex )
                {
                    throw new InvalidCastException (
                        $"Cannot convert array element '{list[j]}' to type '{elementType.Name}' for parameter '{paramName}'.", ex );
                }
            }
            return typedArray;
        }
        if ( requiredType.IsArray && argValue is object[] objArray )
        {
            var elementType = requiredType.GetElementType();
            var typedArray = Array.CreateInstance ( elementType, objArray.Length );
            for ( int j = 0; j < objArray.Length; j++ )
            {
                try
                {
                    typedArray.SetValue ( Convert.ChangeType ( objArray[j], elementType ), j );
                }
                catch ( Exception ex )
                {
                    throw new InvalidCastException (
                        $"Cannot convert array element '{objArray[j]}' to type '{elementType.Name}' for parameter '{paramName}'.", ex );
                }
            }
            return typedArray;
        }

        try
        {
            return Convert.ChangeType ( argValue, requiredType, System.Globalization.CultureInfo.InvariantCulture );
        }
        catch ( Exception ex )
        {
            throw new InvalidCastException (
                $"Cannot convert value '{argValue}' (type: {argValue.GetType().Name}) to required type '{requiredType.Name}' for parameter '{paramName}'.",
                ex );
        }
    }
}
}