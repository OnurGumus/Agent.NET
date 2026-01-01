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

/// Base builder for the agent computation expression.
/// Provides all custom operations but no Run method.
/// Inherit from this and add Run to create a concrete builder.
type AgentBuilderBase() =

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

    /// Adds a list of tools to the agent
    [<CustomOperation("tools")>]
    member _.AddTools(config: AgentConfig, tools: Tool list) =
        { config with Tools = config.Tools @ tools }
