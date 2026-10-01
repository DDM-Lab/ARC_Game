"""The game-action tools: the one definition of the typed tools every front end offers.

The benchmark, the RL policy and the GUI officers all act through these tools
(build/hire/train/staff/deconstruct/task/transfer); cora.executor resolves and runs the calls.
This module only defines them and renders them in each consumer's shape:

  openai_tools()     OpenAI function-tool shape (benchmark, officers; anthropic_native converts)
  arc_tools_yaml()   Verlog's arc_tools.yaml (sglang tool_config), so RL trains on the same surface

Each tool's `params` are ordered; `required` defaults to all of them. `manual_only` tools are
offered only when manual transfers are enabled.
"""
from __future__ import annotations

_TASK_ID_DESC = ("The task's id string, copied exactly as shown in this turn's observation (e.g. "
                 "BUDGET_DAILY). Must match a task actually offered this turn — ids not listed "
                 "this turn are dropped.")


TOOLS: list[dict] = [
    {
        "name": "build",
        "description": "Start construction of a new building at an available site.",
        "params": [
            ("type", {"type": "string", "enum": ["kitchen", "shelter", "casework"],
                      "description": "The building type to construct."}),
            ("site_id", {"type": "integer",
                         "description": "An integer site id from `available.buildSites` this turn."}),
        ],
    },
    {
        "name": "hire",
        # Pure-rules policy: the description states what the action does, not tradeoffs. "cost more" and
        # "cheap/expensive" are biasing framings and are omitted — the price and
        # workforce-unit numbers already appear in the kind param description and
        # in the WORKFORCE section of the system prompt, so redundant + biased
        # phrasing is strictly worse than a neutral rule.
        "description": "Hire N workers of the given kind.",
        "params": [
            ("kind", {"type": "string", "enum": ["untrained", "trained"],
                      "description": ("untrained: 1 workforce unit at `costs.hireUntrained`. "
                                      "trained: 2 workforce units at `costs.hireTrained`. "
                                      "Both prices print under `costs:` each turn.")}),
            ("count", {"type": "integer", "description": "Number of workers to hire, 1-127."}),
        ],
    },
    {
        "name": "train",
        "description": "Train N of your existing untrained workers into trained workers.",
        "params": [
            ("count", {"type": "integer", "description": "Number of untrained workers to promote to trained, 1-127."}),
        ],
    },
    {
        "name": "staff",
        # Measured failure (Talos session 1bb785d1): officers read `count` as a number of
        # WORKERS, sent count=2 meaning two trained workers (= 4 units, exactly the need), got it
        # converted to one worker and rejected, concluded "partial staffing isn't allowed", and
        # left three buildings unstaffed with the workers free. So `count` is now optional and
        # omitting it staffs the building fully, which is the only staffing the game accepts.
        "description": ("Staff an already-built facility from the free pool. A building only runs when "
                        "FULLY staffed, so normally omit count and it is staffed with exactly the "
                        "workforce it needs (trained workers count 2 units, untrained 1). Workers hired "
                        "this turn are not free yet: they arrive a few rounds later (listed as arriving)."),
        "params": [
            ("site", {"type": "string",
                      "description": ("The facility name, copied exactly as printed in `available.needStaff` this "
                                      "turn (names look like 'Shelter Alpha', 'Kitchen Bravo', 'Casework Charlie'). "
                                      "Matching is case-insensitive substring. A facility not listed in `needStaff` "
                                      "is either fully staffed or not yet built, and the call is dropped.")}),
            ("count", {"type": "integer",
                       "description": ("Optional, in workforce UNITS (not workers): trained = 2 units, untrained = 1. "
                                       "Leave it out to staff fully. A count below the building's need is refused, "
                                       "because partial staffing is not allowed.")}),
        ],
        "required": ["site"],
    },
    {
        "name": "deconstruct",
        "description": "Tear down an existing building. Frees the site and refunds nothing.",
        "params": [
            ("site", {"type": "string",
                      "description": ("The facility name, copied exactly as printed in this turn's facilities "
                                      "list (e.g. 'Shelter Alpha'). Matching is case-insensitive substring.")}),
        ],
    },
    {
        "name": "task",
        "description": ("Respond to an active task by selecting one of its offered choices. Tasks and their "
                        "choices are enumerated at the top of each observation."),
        "params": [
            # The id form must match what the observation renders (stable tokens, cora.observation
            # task_token). A schema conditions the model harder than prose: when this example
            # disagreed with the observation, the example ids were the most-rejected ids.
            ("task_id", {"type": "string", "description": _TASK_ID_DESC}),
            # NOT 0-based and NOT contiguous: choice ids are the ids Unity prints for that task this
            # turn, and infeasible options are dropped from the list rather than renumbered, so a task
            # routinely offers e.g. {1,3}. The old "0-based" claim contradicted the system prompt.
            ("choice_id", {"type": "integer",
                           "description": ("The integer choice_id printed for that choice in this turn's task list. "
                                           "Ids are not 0-based and not contiguous — a task may offer only {1,3}. "
                                           "Use exactly the ids shown for that task this turn.")}),
        ],
    },
    {
        "name": "transfer",
        "description": ("Move a quantity of a resource from one facility to another using a free vehicle. "
                        "Only available when manual_transfers is enabled."),
        "manual_only": True,
        "params": [
            ("resource", {"type": "string", "enum": ["food", "people"],
                          "description": "Which resource to move."}),
            ("source", {"type": "string", "description": "Source facility name (substring match)."}),
            ("dest", {"type": "string", "description": "Destination facility name (substring match)."}),
            ("qty", {"type": "integer", "description": "Quantity to move; snapped to the nearest offered amount."}),
        ],
    },
]

TOOL_BY_NAME: dict[str, dict] = {t["name"]: t for t in TOOLS}


def tools(manual_transfers: bool = False) -> list[dict]:
    """The canonical tool defs active for this mode (drops manual-only tools unless enabled)."""
    return [t for t in TOOLS if manual_transfers or not t.get("manual_only")]


def param_names(tool: dict) -> list[str]:
    return [name for name, _ in tool["params"]]


def _json_schema(tool: dict) -> dict:
    """JSON-Schema object for a tool's arguments (shared by every provider shape)."""
    props = {name: dict(spec) for name, spec in tool["params"]}
    return {"type": "object", "properties": props,
            "required": list(tool.get("required") or param_names(tool))}


def openai_tools(manual_transfers: bool = False) -> list[dict]:
    """The tools in OpenAI shape: {type: function, function: {name, description, parameters}}."""
    return [
        {"type": "function",
         "function": {"name": t["name"], "description": t["description"], "parameters": _json_schema(t)}}
        for t in tools(manual_transfers)
    ]


def arc_tools_yaml(manual_transfers: bool = False,
                   class_name: str = "verl.tools.arc_env_tool.ArcEnvTool") -> str:
    """Generate Verlog's arc_tools.yaml (sglang tool_config) from the canonical schema.

    Emitted as a build artifact — the Verlog fork consumes this file at rollout so the RL
    policy is shown the SAME tools the live officer offers. Never hand-edit the yaml.
    """
    import yaml  # local import: only needed when generating the RL artifact
    entries = []
    for t in tools(manual_transfers):
        entries.append({
            "class_name": class_name,
            "config": {"type": "native"},
            "tool_schema": {
                "type": "function",
                "function": {"name": t["name"], "description": t["description"],
                             "parameters": _json_schema(t)},
            },
        })
    return yaml.safe_dump({"tools": entries}, sort_keys=False, width=100)
