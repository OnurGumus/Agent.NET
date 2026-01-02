namespace AgentNet

open System.Reflection
open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Quotations.Patterns

/// Represents an AI tool that can be invoked by an agent
type ToolDef = {
    Name: string
    Description: string
    MethodInfo: MethodInfo
}

/// Functions for building tools using a pipeline approach
type Tool private () =

    /// Creates a tool from a quoted function expression
    static member create (expr: Expr<'a -> 'b>) : ToolDef =
        let rec extractMethodInfo (e: Expr) =
            match e with
            | Lambda(_, body) -> extractMethodInfo body
            | Call(_, mi, _) -> Some mi
            | _ -> None

        match extractMethodInfo expr with
        | Some mi ->
            {
                Name = mi.Name
                Description = ""
                MethodInfo = mi
            }
        | None ->
            failwithf "Could not extract method info from quotation: %A" expr

    /// Sets the tool description
    static member describe (description: string) (tool: ToolDef) : ToolDef =
        { tool with Description = description }
