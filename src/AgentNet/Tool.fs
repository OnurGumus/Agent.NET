namespace AgentNet

open System
open System.Collections.Concurrent
open System.IO
open System.Reflection
open System.Xml.Linq
open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Quotations.Patterns

/// Parameter information extracted from function signature and XML docs
type ParamInfo = {
    Name: string
    Description: string
    Type: Type
}

/// Represents an AI tool that can be invoked by an agent
type ToolDef = {
    Name: string
    Description: string
    Parameters: ParamInfo list
    MethodInfo: MethodInfo
}

/// Functions for building tools using a pipeline approach
type Tool private () =

    // Cache for loaded XML documentation per assembly
    static let xmlDocCache = ConcurrentDictionary<Assembly, XDocument option>()

    /// Tries to load the XML documentation file for an assembly
    static let tryLoadXmlDoc (assembly: Assembly) =
        xmlDocCache.GetOrAdd(assembly, fun asm ->
            try
                let assemblyPath = asm.Location
                let xmlPath = Path.ChangeExtension(assemblyPath, ".xml")
                if File.Exists(xmlPath) then
                    Some (XDocument.Load(xmlPath))
                else
                    None
            with _ -> None
        )

    /// Gets the XML documentation member name for a method
    static let getXmlMemberName (mi: MethodInfo) =
        let typeName = mi.DeclaringType.FullName.Replace("+", ".")
        let paramTypes =
            mi.GetParameters()
            |> Array.map (fun p -> p.ParameterType.FullName)
            |> String.concat ","
        if String.IsNullOrEmpty(paramTypes) then
            $"M:{typeName}.{mi.Name}"
        else
            $"M:{typeName}.{mi.Name}({paramTypes})"

    /// Tries to find the member element in XML documentation
    static let tryFindMemberElement (mi: MethodInfo) =
        tryLoadXmlDoc mi.DeclaringType.Assembly
        |> Option.bind (fun doc ->
            let memberName = getXmlMemberName mi
            let ns = XNamespace.None
            doc.Descendants(ns + "member")
            |> Seq.tryFind (fun el ->
                el.Attribute(XName.Get("name"))
                |> Option.ofObj
                |> Option.map (fun a -> a.Value = memberName)
                |> Option.defaultValue false
            )
        )

    /// Extracts the summary text from a member element
    static let extractSummary (memberEl: XElement) =
        memberEl.Element(XName.Get("summary"))
        |> Option.ofObj
        |> Option.map (fun s -> s.Value.Trim())
        |> Option.defaultValue ""

    /// Extracts parameter descriptions from a member element
    static let extractParamDescriptions (memberEl: XElement) =
        memberEl.Elements(XName.Get("param"))
        |> Seq.choose (fun paramEl ->
            paramEl.Attribute(XName.Get("name"))
            |> Option.ofObj
            |> Option.map (fun nameAttr -> nameAttr.Value, paramEl.Value.Trim())
        )
        |> Map.ofSeq

    /// Builds ParamInfo list from MethodInfo, optionally with XML descriptions
    static let buildParamInfo (mi: MethodInfo) (descriptions: Map<string, string>) =
        mi.GetParameters()
        |> Array.map (fun p ->
            let desc = descriptions |> Map.tryFind p.Name |> Option.defaultValue ""
            { Name = p.Name; Description = desc; Type = p.ParameterType }
        )
        |> Array.toList

    /// Extracts MethodInfo from a quotation
    static let extractMethodInfo (expr: Expr) =
        let rec extract (e: Expr) =
            match e with
            | Lambda(_, body) -> extract body
            | Call(_, mi, _) -> Some mi
            | _ -> None
        extract expr

    /// Creates a tool from a quoted function expression
    static member create (expr: Expr<'a -> 'b>) : ToolDef =
        match extractMethodInfo expr with
        | Some mi ->
            let parameters = buildParamInfo mi Map.empty
            {
                Name = mi.Name
                Description = ""
                Parameters = parameters
                MethodInfo = mi
            }
        | None ->
            failwithf "Could not extract method info from quotation: %A" expr

    /// Creates a tool from a quoted function expression, using XML docs for description
    static member createWithDocs (expr: Expr<'a -> 'b>) : ToolDef =
        match extractMethodInfo expr with
        | Some mi ->
            let memberEl = tryFindMemberElement mi
            let description = memberEl |> Option.map extractSummary |> Option.defaultValue ""
            let paramDescs = memberEl |> Option.map extractParamDescriptions |> Option.defaultValue Map.empty
            let parameters = buildParamInfo mi paramDescs
            {
                Name = mi.Name
                Description = description
                Parameters = parameters
                MethodInfo = mi
            }
        | None ->
            failwithf "Could not extract method info from quotation: %A" expr

    /// Sets the tool description
    static member describe (description: string) (tool: ToolDef) : ToolDef =
        { tool with Description = description }
