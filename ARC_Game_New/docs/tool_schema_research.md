# Tool-schema structure for CORA officers — research synthesis

_Question: keep one broad `execute_commands` tool whose argument is a free-text tag DSL
(`<build>Kitchen,3</build>`, `<hire>untrained,4</hire>`, `<transfer>food|people,SRC,DST,N</transfer>`,
`<task>FOOD_C01,0</task>`), or split into narrow typed tools (`build`, `hire`, `train`,
`transfer`, `answer_task`) with structured JSON args (enums/required/strict)?_

Three parallel research agents (industry practice, academic literature, RL action-space).
**No code changed. This is a decision memo, not an implementation.**

---

## The one fact that reframes everything

The three arms of this repo — LLM **officers** (Anthropic function-calling), the **RL** policy
(`arc_game_gym_env_tcp.py`, `gym.spaces.Text`), and the **benchmark** — **share the tag grammar
via one `cmd_parser`, not the tool schemas.** Officers put the DSL in the `execute_commands`
string arg; the RL policy emits the *same* DSL as raw text; both are parsed by
`cmd_parser.parse_commands`. So "splitting the officer tool" only changes the **officer
presentation layer** — it does not, by itself, touch the RL policy.

---

## What each agent found

### A. Industry / practitioner  →  SPLIT
- Consensus **against a "god-tool + free-text DSL string"** (MLflow, "God Agent"): narrow typed
  tools with enums/required fields "outperform broad API wrappers on every reliability and
  governance metric"; a generic string-dispatch tool ≈ the `query_database(sql)` antipattern.
- The **DSL-leak into prose** (`<task>FOOD_C01,0</task>` in chat) is a *direct consequence* of the
  grammar living in a free-text string the model composes in content. Native structured tool
  calls put args in the tool channel, so that syntax "no longer exists in its action vocabulary."
- Anthropic's own "consolidate" advice is about coherent *workflows* (`schedule_event`), **not**
  many unrelated actions behind one string; they push strict/typed inputs + enums.
- **5 tools is well inside the safe zone** (caution ~10–20; Tool Search only pays off at 10+ tools
  / 10K+ tokens). Five focused schemas likely total ≤ the current ~1,940-token single schema.
- Caveat: to keep "many commands at once," the harness must execute **parallel tool calls**.
- Sources: Anthropic *Writing effective tools*, *Advanced tool use*; OpenAI function-calling guide;
  MLflow tool-use best practices; arXiv 2605.24660.

### B. Academic  →  SPLIT (typed args) but DON'T strait-jacket reasoning
- Typed args are strongly supported: BFCL grades args by **AST match** (needs typed fields);
  Gorilla — structured generation "substantially mitigates hallucination"; **constrained/grammar
  decoding makes malformed tags & out-of-enum values structurally impossible** (vs your parse-time
  failures). Tool-count scaling doesn't punish 1→5.
- **Genuine counter-evidence:** *Natural Language Tools* (2510.14453) beats rigid function-calling
  by **+18.4pp** and cuts variance 70%; *Let Me Speak Freely* + *Constraint Tax* show strict-JSON
  **reasoning** hurts and can suppress tool-calling — **most in open-weight models.**
- Reconciliation everyone converges on: **"reason free, then emit structured"** — typed action +
  an *unconstrained* thinking channel; not JSON-only.
- No direct n≈5 fat-DSL-vs-typed study exists → **A/B in your own harness.**
- Sources: Gorilla (2305.15334), ToolLLM/ToolBench (2307.16789), BFCL (ICML 2025), ToolACE (ICLR
  2025), "Less is More" (2411.15399), NLT (2510.14453), Let Me Speak Freely (2408.02442),
  Constraint Tax (2606.25605), CodeAct (2402.01030).

### C. RL action-space  →  KEEP the shared text DSL (this is the complication)
- The authoritative agentic-RL survey (2509.02547) formalizes a **single unified policy** with tool
  calls **delimited inline as text** (`<action_start>…<action_end>`) — essentially the current design.
- **Every major RL-for-tool-use framework trains on the same inline tagged text used at inference:**
  ToolRL (NeurIPS 2025; XML-tagged calls, split **format+correctness** reward, ~15–17% over SFT/base),
  VerlTool (2509.01055; per-tool stop-tokens, observation-masked loss), Search-R1 (2503.09516),
  ToRL (2503.23383), RAGEN/StarPO (2504.20073). **None trains on a provider-native structured object
  and infers on another.**
- **Train/inference mismatch (TITO, HF; 2605.14220):** RL backprops on *exactly the tokens the policy
  emitted*. A structured tool-call is serialized by the SDK template — a **re-encoding seam**. If you
  ever RL-train the *officers'* tool-calls on-policy, that seam is a documented instability source.
- Verdict: unifying one text grammar across arms is **sound and well-supported**; the split's
  benefits are about *supervised inference ergonomics*, not RL learnability or cross-arm parity.
- Its option #3 (key): *if you want a typed ergonomic layer, make it a **thin adapter that serializes
  to the exact DSL string**; treat the DSL as the on-policy action for RL — never RL-train on the
  structured object.*
- Sources: 2509.02547, ToolRL, VerlTool (2509.01055), Search-R1 (2503.09516), ToRL, RAGEN, TITO,
  2605.14220, grammar-constrained decoding (2502.05111).

---

## The conflict, and how it resolves

Industry/academic optimize the **officer inference** surface (reliability, no DSL-leak) → *split*.
RL optimizes **training/eval parity across arms** → *keep one text grammar*.

They reconcile on a design both camps actually name — **unify at the parser, not the surface:**

> Give the officers typed tools (`build`/`hire`/`train`/`transfer`/`answer_task`, enums+strict) whose
> handlers **serialize to the exact same `cmd_parser` tags**. The gym RL policy keeps emitting the DSL
> as text. Same parser, same semantics, same benchmark — the *only* thing that diverges is how the
> officer expresses an action, which the gym RL policy never sees.

This gives the officer-side reliability + kills the leak, **while leaving the RL/benchmark grammar
untouched.**

### The decision hinge (one question decides it)
**Do you intend to RL-train the *officers'* own tool-calls on-policy, or only the separate gym text policy?**
- **Only the gym text policy** (the current/stated plan — "RL across LLMs via the gym wrapper"):
  the officer surface and the RL policy are *different surfaces sharing the parser*. Splitting the
  officer tool with an adapter → tags is **safe and recommended** — no TITO seam, RL parity intact.
- **RL-train the officers on-policy** (structured tool-call tokens get gradients): **keep the DSL**
  (or the TITO seam bites). Then add ToolRL-style format+correctness reward + grammar-constrained
  decoding to recover most validation benefits inside one representation.

---

## Options (ranked for the current plan: RL = gym text policy)

1. **Typed officer tools → adapter → `cmd_parser` tags** _(recommended, satisfies all three)_.
   Officers get enums/strict/no-leak; RL/benchmark unchanged; one parser. Requires: parallel tool
   calls in the officer loop; keep the reasoning channel free (don't force JSON-only). A/B vs today.
2. **Keep one `execute_commands` DSL tool, add grammar-constrained decoding + a hard output-side
   tag-strip** _(minimal, unification-preserving)_. Fixes malformed args + the leak without a
   surface change; closest to the RL literature's "keep the grammar" stance. Lower ceiling on the
   ergonomics/leak fix than option 1.
3. **Split everywhere, give RL its own structured action space** _(not recommended)_ — abandons the
   parser unification and the RL-framework norm for no cited benefit.

---

## Honest uncertainty
- **No paper studies this exact trade-off head-on** at n≈5 with the RL-parity constraint; the memo
  assembles adjacent evidence (NLT, TITO, the RL frameworks). Medium-high confidence on *direction*.
- **Model-dependence is real:** open-weight models (your RL set) swing most on structure (NLT gains,
  constraint-tax losses) — validate per-model, not on one frontier result.
- **NLT's +18.4pp** is prompt-level, non-game domains — don't transfer the magnitude.
- **Tool-count degradation** is robust practitioner consensus + indirect benchmark evidence, not a
  controlled law; irrelevant at n=5 regardless.
- **Whichever way you go, A/B it in the benchmark harness** — it's the one arbiter the literature
  keeps pointing back to.

---

## DECISION (locked 2026-08-04) — given the training goal

**Stated goal:** RL-train a policy AND fine-tune/distill that same policy on frontier-model
(officer) rollouts, with **one prompting structure shared across both games**.

That goal is the decision hinge, and it lands on **Option 2 — KEEP the unified text DSL; do NOT
split into typed tools.** Reasoning:

- Distilling frontier officer rollouts into the policy *is* the "RL-train the officers on-policy"
  branch: the officers' emitted actions become the policy's SFT targets. So the officer action
  surface = the SFT corpus = the RL action surface → they must be **one representation**.
- Typed tool-calls would make the SFT corpus structured tool_use tokens while the gym policy is
  RL'd on DSL text — the **TITO re-encoding seam** (2605.14220), now *active* because the frontier
  rollouts are training data.
- Typed tools would also give the frontier officer a **different prompt** (JSON schemas) than the
  distilled policy sees (DSL grammar), breaking "same prompting structure" between teacher/student
  and across games.

**Resolution:** keep the one DSL / one prompt structure, and fix the DSL's real problems in-place
(the reasons we opened this) rather than by splitting:

- **(a) grammar-consolidation + leak-strip** — define the tag grammar ONCE (execute_commands tool
  schema), stop re-teaching it in the per-turn OPTIONS rows, and strip any command-tag syntax from
  director-facing messages. Low-risk; shrinks the prompt; removes the leak. **← starting here.**
- **(b) game-agnostic prompt template + grammar-constrained decoding** — parameterize obs+grammar
  by each game's vocabulary (shared across CORA + the other game) and make actions valid-by-
  construction; ToolRL-style format+correctness reward for RL. **Deferred — training-prep step,
  not now.**
- Honest caveat unchanged: typed tools would still edge out the DSL on single-shot officer
  reliability; constrained decoding (b) closes most of that gap. A/B in the benchmark harness.
