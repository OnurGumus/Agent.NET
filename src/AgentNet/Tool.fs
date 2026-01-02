namespace AgentNet

open System
open System.Reflection
open System.Xml
open System.Xml.Linq
open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Quotations.Patterns

/// Represents a parameter description for a tool
type ParamDescriptor = {
    Name: string
    Description: string
    Type: Type
}

/// Represents an AI tool that can be invoked by an agent
type ToolDef = {
    Name: string
    Description: string
    Parameters: ParamDescriptor list
    Invoke: obj[] -> obj
}

/// Functions for building tools using a pipeline approach
type Tool private () =

    /// Gets the Invoke method with the most parameters (handles curried functions)
    static member private getInvokeMethod (fnType: Type) =
        fnType.GetMethods()
        |> Array.filter (fun m -> m.Name = "Invoke")
        |> Array.maxBy (fun m -> m.GetParameters().Length)

    /// Creates a tool from a function
    static member create (fn: 'a -> 'b) : ToolDef =
        let fnType = fn.GetType()
        let invokeMethod = Tool.getInvokeMethod fnType
        let parameters =
            invokeMethod.GetParameters()
            |> Array.map (fun p -> {
                Name = p.Name |> Option.ofObj |> Option.defaultValue "arg"
                Description = p.Name |> Option.ofObj |> Option.defaultValue "arg"
                Type = p.ParameterType
            })
            |> Array.toList

        {
            Name = fnType.Name
            Description = "Tool function"
            Parameters = parameters
            Invoke = fun args -> invokeMethod.Invoke(fn, args)
        }

    /// Creates a tool from a function with an explicit name and description
    static member create (name: string, description: string, fn: 'a -> 'b) : ToolDef =
        let fnType = fn.GetType()
        let invokeMethod = Tool.getInvokeMethod fnType
        let parameters =
            invokeMethod.GetParameters()
            |> Array.map (fun p -> {
                Name = p.Name |> Option.ofObj |> Option.defaultValue "arg"
                Description = p.Name |> Option.ofObj |> Option.defaultValue "arg"
                Type = p.ParameterType
            })
            |> Array.toList

        {
            Name = name
            Description = description
            Parameters = parameters
            Invoke = fun args -> invokeMethod.Invoke(fn, args)
        }

    /// Overrides the tool name
    static member withName (name: string) (tool: ToolDef) : ToolDef =
        { tool with Name = name }

    /// Overrides the tool description
    static member describe (description: string) (tool: ToolDef) : ToolDef =
        { tool with Description = description }

    /// Overrides a parameter description
    static member describeParam (paramName: string) (description: string) (tool: ToolDef) : ToolDef =
        { tool with
            Parameters =
                tool.Parameters
                |> List.map (fun p ->
                    if p.Name = paramName then { p with Description = description }
                    else p) }
