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
type Tool = {
    Name: string
    Description: string
    Parameters: ParamDescriptor list
    Invoke: obj[] -> obj
}

/// Functions for building tools using a pipeline approach
[<RequireQualifiedAccess>]
module Tool =

    /// Extracts the method name from a quotation expression
    let extractName (expr: Expr) : string option =
        let rec extract e =
            match e with
            | Call(_, mi, _) -> Some mi.Name
            | Lambda(_, body) -> None  // Anonymous function
            | Let(_, _, body) -> extract body
            | _ -> None
        extract expr

    /// Extracts parameter info from a MethodInfo
    let extractParams (mi: MethodInfo) : ParamDescriptor list =
        mi.GetParameters()
        |> Array.map (fun p -> {
            Name = p.Name
            Description = p.Name // Default to param name
            Type = p.ParameterType
        })
        |> Array.toList

    /// Tries to load XML documentation for a method
    let tryGetXmlDoc (mi: MethodInfo) : (string * Map<string, string>) option =
        try
            let assembly = mi.DeclaringType.Assembly
            let xmlPath =
                assembly.Location
                |> fun loc -> System.IO.Path.ChangeExtension(loc, ".xml")

            if System.IO.File.Exists(xmlPath) then
                let doc = XDocument.Load(xmlPath)
                let memberName = $"M:{mi.DeclaringType.FullName}.{mi.Name}"

                let memberNode =
                    doc.Descendants(XName.Get "member")
                    |> Seq.tryFind (fun m ->
                        m.Attribute(XName.Get "name")
                        |> Option.ofObj
                        |> Option.map (fun a -> a.Value.StartsWith(memberName))
                        |> Option.defaultValue false)

                match memberNode with
                | Some node ->
                    let summary =
                        node.Element(XName.Get "summary")
                        |> Option.ofObj
                        |> Option.map (fun e -> e.Value.Trim())
                        |> Option.defaultValue ""

                    let paramDescs =
                        node.Elements(XName.Get "param")
                        |> Seq.choose (fun p ->
                            let name = p.Attribute(XName.Get "name") |> Option.ofObj |> Option.map (fun a -> a.Value)
                            let desc = p.Value.Trim()
                            name |> Option.map (fun n -> n, desc))
                        |> Map.ofSeq

                    Some (summary, paramDescs)
                | None -> None
            else None
        with _ -> None

    /// Gets the Invoke method with the most parameters (handles curried functions)
    let private getInvokeMethod (fnType: Type) =
        fnType.GetMethods()
        |> Array.filter (fun m -> m.Name = "Invoke")
        |> Array.maxBy (fun m -> m.GetParameters().Length)

    /// Creates a tool from a single-parameter function
    let fromFn (fn: 'a -> 'b) : Tool =
        let fnType = fn.GetType()
        let invokeMethod = getInvokeMethod fnType
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

    /// Creates a tool from an anonymous function with an explicit name
    let fromFnNamed (name: string) (description: string) (fn: 'a -> 'b) : Tool =
        let fnType = fn.GetType()
        let invokeMethod = getInvokeMethod fnType
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

    /// Overrides the tool description
    let describe (description: string) (tool: Tool) : Tool =
        { tool with Description = description }

    /// Overrides a parameter description
    let describeParam (paramName: string) (description: string) (tool: Tool) : Tool =
        { tool with
            Parameters =
                tool.Parameters
                |> List.map (fun p ->
                    if p.Name = paramName then { p with Description = description }
                    else p) }

    /// Overrides the tool name
    let withName (name: string) (tool: Tool) : Tool =
        { tool with Name = name }
