namespace AgentNet

open System

/// Configuration for an agent
type AgentConfig = {
    Name: string option
    Instructions: string
    Tools: Tool list
}

/// Represents an AI agent that can chat and use tools
type Agent = {
    Config: AgentConfig
    Chat: string -> Async<string>
}

/// Builder for the agent computation expression
type AgentBuilder() =

    member _.Yield(_) : AgentConfig =
        {
            Name = None
            Instructions = ""
            Tools = []
        }

    /// Sets the agent's name
    [<CustomOperation("name")>]
    member _.Name(config: AgentConfig, name: string) =
        { config with Name = Some name }

    /// Sets the agent's instructions/system prompt
    [<CustomOperation("instructions")>]
    member _.Instructions(config: AgentConfig, instructions: string) =
        { config with Instructions = instructions }

    /// Adds a single tool to the agent
    [<CustomOperation("add")>]
    member _.Add(config: AgentConfig, tool: Tool) =
        { config with Tools = config.Tools @ [tool] }

    /// Adds multiple tools to the agent (tuple of 2)
    [<CustomOperation("tools")>]
    member _.Tools2(config: AgentConfig, (t1, t2): Tool * Tool) =
        { config with Tools = config.Tools @ [t1; t2] }

    /// Adds multiple tools to the agent (tuple of 3)
    [<CustomOperation("tools")>]
    member _.Tools3(config: AgentConfig, (t1, t2, t3): Tool * Tool * Tool) =
        { config with Tools = config.Tools @ [t1; t2; t3] }

    /// Adds multiple tools to the agent (tuple of 4)
    [<CustomOperation("tools")>]
    member _.Tools4(config: AgentConfig, (t1, t2, t3, t4): Tool * Tool * Tool * Tool) =
        { config with Tools = config.Tools @ [t1; t2; t3; t4] }

    /// Adds multiple tools to the agent (tuple of 5)
    [<CustomOperation("tools")>]
    member _.Tools5(config: AgentConfig, (t1, t2, t3, t4, t5): Tool * Tool * Tool * Tool * Tool) =
        { config with Tools = config.Tools @ [t1; t2; t3; t4; t5] }

    /// Adds a list of tools to the agent
    [<CustomOperation("addTools")>]
    member _.AddTools(config: AgentConfig, tools: Tool list) =
        { config with Tools = config.Tools @ tools }

/// The agent computation expression builder instance
[<AutoOpen>]
module AgentCE =
    let agent = AgentBuilder()
