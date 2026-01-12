# **AGENTNET_ARCHITECTURE.md**  
### *Architectural invariants and non‑negotiable rules for AI collaborators*

---

## **TL;DR for AI collaborators (read this first)**

Agent.NET has a strict architectural model. These rules are **not optional**. If a proposed change violates them, the change is invalid even if it compiles, even if tests pass, and even if it “seems to work.”

**You must not improvise.**  
If you are unsure, stop and ask.

### **Core truths you must preserve**

- **MAF is the execution engine.**  
  All workflow steps run through MAF. No custom interpreters. No loops over steps.

- **DurableTask is the suspension engine.**  
  Only DTFx may suspend or resume orchestrations. No hot async. No continuations. No reflection.

- **The workflow graph is declarative.**  
  Building a workflow must never run business logic or perform I/O.

- **Cold async only.**  
  Steps describe work; they do not run work.

- **Event boundaries reset the type flow.**  
  The step before `awaitEvent` must return `unit`.  
  Any data needed after the event must be stored in workflow context.

- **Typed flow is enforced.**  
  The CE builder must ensure that step outputs match the next step’s input.  
  Ill‑typed workflows must fail at compile time.

- **Context is the only durable state.**  
  Nothing flows implicitly across suspension points.

If you are modifying durable execution, workflow construction, or anything involving `TaskOrchestrationContext`, you must re‑read this document before making changes.

---

# **0. Purpose**

This document defines the **architectural invariants** of Agent.NET / AgentNet.Durable.

These are **hard constraints**, not suggestions:

- If a change conflicts with these rules, the change is **invalid**, even if tests pass.
- If you are unsure whether a change violates an invariant, you must stop and ask for clarification instead of “fixing” it by improvisation.

This file exists so that future AI collaborators (including you, Claude) **preserve the architecture** instead of gradually rewriting it.

---

# **1. Core architecture**

## **1.1 MAF is the execution engine**

**Invariants:**

- **All workflow step execution goes through MAF.**
- MAF is responsible for:
  - **Step execution**
  - **Routing**
  - **Parallel branches**
  - **Resilience wrappers** (retries, timeouts, fallbacks, etc.)

**Non‑negotiable:**

- You **must not**:
  - Introduce a custom interpreter for workflow steps.
  - Execute steps “directly” in a loop instead of using MAF.
  - Bypass MAF when running workflows.

If you find yourself thinking, *“Let’s just interpret the steps ourselves,”* stop. That is a violation of the architecture.

---

## **1.2 DurableTask is the suspension engine**

DurableTask (DTFx) owns **suspension, replay, and resumption**.

**Invariants:**

- Durable orchestrators are **not** normal async functions.
- They are **deterministic workflows disguised as async**.
- Only DTFx is allowed to:
  - Suspend orchestrators.
  - Resume orchestrators.
  - Control which thread they run on.
  - Await durable primitives.

**Non‑negotiable:**

- The durable executor must **never**:
  - Await durable primitives.
  - Wrap durable primitives in `Task.Run`, `ContinueWith`, or other schedulers.
  - Create its own replay model.

DurableTask is the **only** suspension engine. MAF and Agent.NET must cooperate with it, not re‑implement it.

---

## **1.3 Workflow graph is declarative**

Workflows are modeled as a **graph of steps** (the `WorkflowStep` union).

**Invariants:**

- The graph describes *what* should happen, not *how* it executes.
- No step should run at graph construction time.
- No side effects at graph construction time.
- Execution happens later, through:
  - MAF (in‑process execution), or
  - DurableTask + durable executor (durable orchestration).

**Non‑negotiable:**

- You must **not**:
  - Execute business logic when building the workflow.
  - Use graph construction to perform I/O or durable operations.
  - Mutate external state during graph construction.

The graph is a **pure description**, not a runtime.

---

### **1.3.1 Typed Flow Invariant**

The workflow graph is a **typed pipeline**. The output type of each step must match the input type of the next step. The CE builder enforces this statically using phantom types.

**Invariants:**

- Ill‑typed workflows must fail at **graph construction time**, not runtime.
- The CE builder must thread types through the workflow so that:

  ```
  step : ('a -> Task<'b>) -> Workflow<'input,'a> -> Workflow<'input,'b>
  ```

- `AwaitEvent` must refine the output type to the event payload type, but only when the preceding step returns `unit`.

This invariant prevents runtime unboxing errors and ensures that the workflow graph is always type‑correct.

---

# **2. Cold vs hot async**

This is the heart of AgentNet.Durable’s design.

## **2.1 Cold async model (what we are)**

Agent.NET workflows follow a **cold, declarative async model**, similar to F# `Async`:

- Steps represent **computation descriptions**, not running tasks.
- `AwaitEvent`, `Delay`, and other durable operations:
  - **describe** a suspension.
  - return durable primitives.
- DTFx decides when/where the actual wait and resume happen.

**Invariants:**

- A workflow step is **cold** until the runtime executes it.
- The durable executor must pass **declarative intentions** to DTFx, not run “hot” tasks.

---

### **2.1.1 Suspension Points Reset the Type Flow**

Durable suspension points (`AwaitEvent`, `Delay`, etc.) represent **semantic resets** in the workflow. They do not continue the previous computation’s output.

**Invariants:**

- The type flow before and after a suspension point must be explicit.
- Suspension points must not implicitly depend on the output of the previous step.
- Any state that must persist across a suspension point must be stored in workflow context.

This ensures that durable workflows remain deterministic and replay‑safe.

---

## **2.2 Hot async is forbidden in orchestrators**

“Hot” async (e.g., C# `Task` started immediately, resumed on thread pool) is **not allowed** inside durable orchestration.

**Forbidden in durable execution:**

- `Task.Run(...)`
- `Task.Delay(...)`
- `ContinueWith(...)`
- `Task.WhenAll(...)` on **non‑durable** tasks
- Any custom scheduler that moves work off DTFx’s orchestrator thread
- Awaiting **non‑durable** tasks inside the orchestrator

If you think, *“I’ll just use a continuation,”* that is almost certainly illegal here.

---

# **3. Durable boundaries**

This is where most subtle bugs appear. These rules are strict.

## **3.1 AwaitEvent**

`AwaitEvent` is the core bridge between the workflow graph and DTFx external events.

**Correct model:**

- The CE builds a node that says:
  - “At this point, call `ctx.WaitForExternalEvent<'T>(eventName)`.”
- The durable executor:
  - Calls that method.
  - Returns the resulting **durable primitive** to DTFx.
- DTFx owns suspension and resumption.

**Invariants:**

- `AwaitEvent` must:
  - Create a function that, given `TaskOrchestrationContext`, returns `Task<'T>` / `Task` from **DurableTask primitives only**.
  - **Not** await inside this function.
  - **Not** wrap in `Task<obj>` via continuations.

---

### **3.1.1 Event Boundary Invariant**

External events represent **durable suspension points**. They do not carry forward the output of the previous step.

**Invariants:**

- The step immediately preceding an `AwaitEvent` **must return `unit`**.
- `AwaitEvent` always has the effective type:

  ```
  unit -> 'EventPayload
  ```

- Any data that must survive across an event boundary must be stored explicitly in **workflow context**, not passed implicitly through step outputs.

**Non‑negotiable:**

- You must **not** design workflows where the output of a step is implicitly “carried through” an external event.
- You must **not** modify `AwaitEvent` to accept arbitrary input types.
- You must **not** allow step outputs to flow across durable suspension points.

---

## **3.2 Delay**

(unchanged)

---

## **3.3 No custom durable interpreters**

(unchanged)

---

# **4. Reflection rules**

(unchanged)

---

# **5. Executor rules**

## **5.1 MAF executor (InProcess)**

(unchanged)

---

## **5.2 Durable executor**

(unchanged)

---

### **5.3 Context as Durable State**

Workflow context is the **only** mechanism for carrying data across durable suspension points.

**Invariants:**

- Step outputs do not automatically flow across `AwaitEvent`.
- Executors must treat context as the durable state boundary.
- If a step’s output is needed after an event, it must be written to context before the event and read back afterward.

This keeps durable execution explicit, predictable, and aligned with DTFx semantics.

---

# **6. Forbidden operations**

(unchanged)

---

# **7. Extension points**

(unchanged)

---

# **8. Non‑extension points**

(unchanged)

---

# **9. If you are unsure**

(unchanged)

---
