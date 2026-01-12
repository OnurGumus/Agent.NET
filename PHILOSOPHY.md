# **PHILOSOPHY.md**  
### *Why Agent.NET works the way it does*

Agent.NET is not just a workflow library. It is a deliberate fusion of three worlds:

- F#’s **cold, declarative computation model**  
- MAF’s **resilient execution engine**  
- DurableTask’s **deterministic suspension model**

This document explains the *why* behind the architectural invariants. It is not a specification; it is the conceptual foundation that makes the specification make sense.

If you understand this file, the invariants in `AGENTNET_ARCHITECTURE.md` will feel inevitable.

---

# **1. The core idea: workflows as cold, typed descriptions**

Agent.NET workflows are not “running code.”  
They are **descriptions of work**.

This is the same idea behind:

- F# `Async`
- LINQ expression trees
- SQL query builders
- Elm update loops
- React render functions

A workflow is a **pure value** that describes:

- what steps exist  
- in what order  
- with what types  
- and what durable boundaries  

Nothing runs during construction.  
Nothing performs I/O.  
Nothing touches the outside world.

This is what makes workflows:

- replayable  
- serializable  
- inspectable  
- optimizable  
- durable  

And it’s why the CE builder must enforce type flow:  
the workflow graph is a *typed pipeline*, not a bag of steps.

---

# **2. The cold/hot split: the heart of the system**

Agent.NET draws a bright line between:

### **Cold layer (F#)**  
- builds the workflow graph  
- enforces types  
- describes durable boundaries  
- contains no runtime state  
- is deterministic and pure  

### **Hot layer (C# + MAF + DurableTask)**  
- executes steps  
- performs I/O  
- handles retries, timeouts, and routing  
- interacts with DurableTask  
- owns suspension and resumption  

This split is not an implementation detail.  
It is the *soul* of the architecture.

It ensures:

- deterministic replay  
- no accidental side effects  
- no hot async in orchestrators  
- no business logic in durable code  
- no reflection or dynamic dispatch at runtime  
- no “magic” hidden in lambdas  

The cold layer describes.  
The hot layer executes.  
DurableTask suspends.

Each layer does exactly one job.

---

# **3. Why MAF is the execution engine**

MAF is designed for:

- structured concurrency  
- routing  
- retries  
- timeouts  
- fallbacks  
- parallel branches  
- cancellation  
- orchestration of arbitrary async work  

It is the perfect engine for executing workflow steps because:

- it is deterministic  
- it is explicit  
- it is resilient  
- it is composable  
- it is predictable  
- it is not tied to DurableTask  

MAF gives Agent.NET the ability to run:

- in‑process workflows  
- durable workflows  
- hybrid workflows  
- test workflows  
- simulated workflows  

All with the same graph.

This is why the architecture forbids “interpreting steps manually.”  
MAF is the executor.  
Not you.  
Not the durable executor.  
Not a custom loop.

---

# **4. Why DurableTask is the suspension engine**

DurableTask is the only system in .NET that provides:

- deterministic replay  
- event sourcing for orchestrations  
- durable timers  
- external event handling  
- orchestration state management  
- replay‑safe execution  

It is not a general async runtime.  
It is a **deterministic state machine disguised as async**.

This is why:

- you cannot use `Task.Run`  
- you cannot use `Task.Delay`  
- you cannot use `ContinueWith`  
- you cannot await non‑durable tasks  
- you cannot capture orchestrator context  
- you cannot introduce reflection  

DurableTask is extremely powerful, but only if you respect its rules.

Agent.NET exists to make those rules easy to follow.

---

# **5. Why event boundaries reset the type flow**

This is one of the most important conceptual truths:

### **External events are suspension points.  
They do not continue the previous computation.**

When an orchestrator waits for an event:

- the workflow stops  
- the world changes  
- a human or system responds  
- the workflow resumes with new input  

The previous step’s output is irrelevant.  
It is not part of the event.  
It is not part of the resume.  
It is not part of the durable state.

This is why:

- the step before `awaitEvent` must return `unit`  
- any needed data must be stored in workflow context  
- the CE builder enforces this invariant  
- type flow resets at the event boundary  

This is not a limitation.  
It is the correct semantic model for durable workflows.

---

# **6. Why context is the only durable state**

Because durable workflows are:

- long‑running  
- replayed  
- event‑driven  
- externally resumed  

Implicit state is dangerous.

Explicit state is safe.

Context is:

- durable  
- replay‑safe  
- serializable  
- explicit  
- predictable  
- inspectable  

If you need data after an event, you store it in context.  
If you don’t store it, you don’t have it.

This keeps the workflow honest.

---

# **7. Why type safety matters**

Agent.NET is designed so that:

- if a workflow compiles  
- it is structurally correct  
- it is type‑correct  
- it is durable‑correct  
- it is replay‑correct  

The CE builder is not a convenience.  
It is a **type‑level guardian**.

It ensures:

- step outputs match step inputs  
- event boundaries are respected  
- durable semantics are preserved  
- no runtime unboxing errors occur  
- the workflow graph is always valid  

This is why the CE builder uses phantom types.  
This is why `awaitEvent` requires `unit`.  
This is why type flow is enforced.

The type system is not an implementation detail.  
It is part of the architecture.

---

# **8. Why these rules exist**

These rules are not arbitrary.  
They are not stylistic.  
They are not “best practices.”

They exist because:

- DurableTask requires determinism  
- MAF requires purity  
- F# workflows require cold semantics  
- replay requires explicit state  
- orchestration requires predictable flow  
- correctness requires type safety  

Agent.NET is the intersection of these constraints.

The architecture is not accidental.  
It is the only design that satisfies all requirements simultaneously.

---

# **9. The philosophy in one sentence**

### **Agent.NET is a typed, declarative workflow language that compiles to a resilient execution engine (MAF) and a deterministic suspension engine (DurableTask), with explicit state and enforced semantics.**

If you keep that sentence in mind, every invariant will make sense.

---
