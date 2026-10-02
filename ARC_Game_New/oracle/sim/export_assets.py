"""Per-choice TaskData fields the `sim_constants` RPC does not export, read from the assets.

    python -m oracle.sim.export_assets           # -> oracle/sim/corpus/task_assets.json

The RPC exports each choice's impacts, quantities and destinations but not these three, which
the bench-v6 build reads at answer time:
  costPerUnit               a priced FoodPacks choice charges costPerUnit x the resolved
                            quantity (AgentChoice.ChargedBudget)
  enableMultipleDeliveries  SingleSourceMultiDest / MultiSourceSingleDest routing
  requireFullQuantity       a queued food choice needs kitchens that cover the whole quantity
  deliveryCargoType         0 = Population, 1 = FoodPacks (which choices move people)
It also writes corpus/prefab_fields.json: BuildingResourceStorage fields of the building prefabs
the RPC does not export either (caseworkDeparturesPerRound: clients leaving a casework site
each round). Generated, never hand-edited: re-run it whenever a TaskData asset or building
prefab changes (the same rule as the RPC exports in the corpus directory).
"""
import glob
import json
import os
import re

from oracle.sim import paths as P

FIELDS = ("costPerUnit", "enableMultipleDeliveries", "requireFullQuantity", "deliveryCargoType")


def read_asset(path) -> tuple:
    """(taskId, {choiceId: {field: value}}) from one TaskData .asset (Unity YAML)."""
    task_id, choices, cur = None, {}, None
    for line in open(path, errors="ignore"):
        m = re.match(r"  taskId: (\S+)", line)
        if m:
            task_id = m.group(1)
        m = re.match(r"  - choiceId: (-?\d+)", line)
        if m:
            cur = choices.setdefault(int(m.group(1)), {})
            continue
        m = re.match(r"    (\w+): (.+?)\s*$", line)
        if m and cur is not None and m.group(1) in FIELDS:
            v = m.group(2)
            cur[m.group(1)] = float(v) if "." in v else int(v)
    return task_id, choices


PREFABS = {"CaseworkSite": "Casework.prefab", "Shelter": "Shelter.prefab", "Kitchen": "Kitchen.prefab"}
PREFAB_FIELDS = ("caseworkDeparturesPerRound",)


def read_prefab(path) -> dict:
    out = {}
    for line in open(path, errors="ignore"):
        m = re.match(r"\s+(\w+): (-?[\d.]+)\s*$", line)
        if m and m.group(1) in PREFAB_FIELDS:
            out[m.group(1)] = float(m.group(2)) if "." in m.group(2) else int(m.group(2))
    return out


def main():
    out = {}
    for path in sorted(glob.glob(os.path.join(P.ROOT, "Assets/Scripts/Tasks/TaskData/*.asset"))):
        task_id, choices = read_asset(path)
        if task_id and choices:
            out[task_id] = {str(k): v for k, v in sorted(choices.items())}
    dest = os.path.join(P.PKG, "corpus", "task_assets.json")
    json.dump(out, open(dest, "w"), indent=1, sort_keys=True)
    print(f"wrote {dest}: {len(out)} tasks")
    prefabs = {kind: read_prefab(os.path.join(P.ROOT, "Assets/Prefabs", f)) for kind, f in PREFABS.items()}
    dest = os.path.join(P.PKG, "corpus", "prefab_fields.json")
    json.dump(prefabs, open(dest, "w"), indent=1, sort_keys=True)
    print(f"wrote {dest}: {prefabs}")


if __name__ == "__main__":
    main()
