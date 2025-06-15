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

    public IReadOnlyDictionary<string, string> GetCommandList()
    {
        var commandList = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        lock (_commands)
        {
            foreach (var command in _commands)
            {
                var attribute = command.Value.GetCustomAttribute<CommandAttribute>();
                if (attribute != null)
                {
                    commandList[command.Key] = string.IsNullOrEmpty(attribute.MCPCmdDescription)
                        ? $"Executes the {command.Key} command."
                        : attribute.MCPCmdDescription;
                }
            }
        }
        return commandList;
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

    /// <summary>
    /// Converts a given object to the required parameter type.
    /// This method handles conversions for primitive types, enums, GUIDs, and arrays.
    /// It is designed to be robust in handling various input types that might come from
    /// deserialized data.
    /// </summary>
    /// <param name="argValue">The value to convert.</param>
    /// <param name="requiredType">The target type to convert to.</param>
    /// <param name="paramName">The name of the parameter, used for crafting helpful error messages.</param>
    /// <returns>The converted object.</returns>
    /// <exception cref="ArgumentNullException">Thrown when a null value is provided for a non-nullable type.</exception>
    /// <exception cref="InvalidCastException">Thrown when conversion is not possible.</exception>
    private object ConvertArgumentType ( object argValue, Type requiredType, string paramName )
    {
        if ( argValue == null )
        {
            if ( requiredType.IsValueType && Nullable.GetUnderlyingType ( requiredType ) == null )
            {
                throw new ArgumentNullException ( paramName,
                                                  $"Null provided for non-nullable parameter '{paramName}' of type {requiredType.Name}" );
            }
            return null;
        }

        if ( requiredType.IsInstanceOfType ( argValue ) )
        {
            return argValue;
        }

        if ( requiredType.IsArray )
        {
            if ( argValue is System.Collections.ICollection collection )
            {
                return ConvertArrayArgument ( collection, requiredType, paramName );
            }
        }

        if ( requiredType == typeof ( Guid ) )
        {
            return Guid.Parse ( argValue.ToString() );
        }

        if ( requiredType.IsEnum )
        {
            return System.Enum.Parse ( requiredType, argValue.ToString(), ignoreCase: true );
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

    /// <summary>
    /// Converts a collection of objects into a typed array by converting each element.
    /// </summary>
    /// <param name="collection">The collection to convert, which can be an ArrayList or object[].</param>
    /// <param name="requiredArrayType">The target array type (e.g., typeof(int[])).</param>
    /// <param name="paramName">The name of the parameter for error reporting.</param>
    /// <returns>A new array of the specified element type containing the converted elements.</returns>
    private object ConvertArrayArgument ( System.Collections.ICollection collection, Type requiredArrayType, string paramName )
    {
        var elementType = requiredArrayType.GetElementType();
        if ( elementType == null )
        {
            throw new InvalidOperationException ( $"Cannot determine element type for array '{paramName}'." );
        }

        var typedArray = Array.CreateInstance ( elementType, collection.Count );
        int i = 0;
        foreach ( var item in collection )
        {
            try
            {
                typedArray.SetValue ( ConvertArgumentType ( item, elementType, $"{paramName}[{i}]" ), i );
            }
            catch ( Exception ex )
            {
                throw new InvalidCastException (
                    $"Cannot convert array element at index {i} for parameter '{paramName}'. See inner exception for details.", ex );
            }
            i++;
        }
        return typedArray;
    }
}
}