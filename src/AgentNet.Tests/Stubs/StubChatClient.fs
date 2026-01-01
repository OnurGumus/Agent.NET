namespace AgentNet.Tests.Stubs

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.AI

/// A configurable stub implementation of IChatClient for testing.
/// Can be programmed with expected responses per test scenario.
type StubChatClient() =
    let mutable responses = Map.empty<string, string>
    let mutable defaultResponse = "Default response"
    let mutable callHistory = ResizeArray<string>()

    /// Sets a response for a specific input pattern.
    /// The pattern is matched against the last user message content.
    member this.SetResponse(inputPattern: string, response: string) =
        responses <- responses |> Map.add inputPattern response

    /// Sets the default response when no pattern matches.
    member this.SetDefaultResponse(response: string) =
        defaultResponse <- response

    /// Gets the history of all messages sent to this client.
    member this.CallHistory = callHistory |> Seq.toList

    /// Clears all configured responses and call history.
    member this.Reset() =
        responses <- Map.empty
        callHistory.Clear()

    interface IChatClient with
        member this.GetResponseAsync(chatMessages: IEnumerable<ChatMessage>, options: ChatOptions, cancellationToken: CancellationToken) =
            task {
                // Extract the last user message content
                let lastUserMessage =
                    chatMessages
                    |> Seq.filter (fun m -> m.Role = ChatRole.User)
                    |> Seq.tryLast
                    |> Option.map (fun m -> m.Text)
                    |> Option.defaultValue ""

                // Record the call
                callHistory.Add(lastUserMessage)

                // Find matching response or use default
                let responseText =
                    responses
                    |> Map.tryPick (fun pattern response ->
                        if lastUserMessage.Contains(pattern) then Some response
                        else None)
                    |> Option.defaultValue defaultResponse

                // Create the ChatResponse
                let message = ChatMessage(ChatRole.Assistant, responseText)
                return ChatResponse(message)
            }

        member this.GetStreamingResponseAsync(chatMessages: IEnumerable<ChatMessage>, options: ChatOptions, cancellationToken: CancellationToken) =
            // For testing, we don't need streaming - just return empty
            AsyncSeq.empty<ChatResponseUpdate>()

        member this.GetService(serviceType: Type, serviceKey: obj) : obj =
            null

        member this.Dispose() = ()

/// Helper module for creating async sequences
and AsyncSeq =
    static member empty<'T>() : IAsyncEnumerable<'T> =
        { new IAsyncEnumerable<'T> with
            member _.GetAsyncEnumerator(_) =
                { new IAsyncEnumerator<'T> with
                    member _.Current = Unchecked.defaultof<'T>
                    member _.MoveNextAsync() = ValueTask<bool>(false)
                    member _.DisposeAsync() = ValueTask()
                }
        }
