"""bench.decision: a turn becomes well-formed SystemOne questions, and any answer becomes calls the
executor accepts (executed, or refused for a game reason -- never malformed)."""
import random

from bench import decision
from cora import executor
from cora.env import DECISIONS
from cora.observation import observe
from oracle.sim.env import SimEnv


def _fake_answers(questions: dict, rng: random.Random) -> dict:
    """A server's answers: random calibrated-looking distributions in the SystemOne shape."""
    out = {}
    for qid, q in questions.items():
        if q["type"] == "choice":
            opts = list(q["criteria"])
            w = [rng.random() for _ in opts]
            out[qid] = {"choice": opts[w.index(max(w))], "probabilities": {o: x / sum(w) for o, x in zip(opts, w)}}
        elif q["type"] == "score":
            w = [rng.random() for _ in q["criteria"]]
            out[qid] = {"score": sum(i * x for i, x in enumerate(w)) / sum(w), "probabilities": [x / sum(w) for x in w]}
        else:
            out[qid] = {"probability": rng.random()}
    return out


def test_questions_are_well_formed_and_calls_execute():
    rng = random.Random(0)
    env = SimEnv(seed=5503, max_episode_steps=40, manual_transfers=False)
    env.reset()
    seen = set(); statuses = set()
    for i in range(DECISIONS):
        obs = observe(env.game_state, env.get_valid_actions())
        body, decode = decision.build_request(obs, "test")
        for qid, q in body["questions"].items():
            assert q["type"] in ("choice", "score", "noul"), qid
            if q["type"] == "choice":
                assert 2 <= len(q["criteria"]) <= 20, (qid, len(q["criteria"]))
            seen.add(qid.split("_")[0])
        calls = decision.tool_calls(_fake_answers(body["questions"], rng), decode, mode="sample", rng=rng)
        results, (_, _r, term, trunc, _info) = executor.execute_turn(env, calls)
        statuses |= {r.status for r in results}
        assert all(r.status != "invalid" for r in results), [(r.tool, r.args, r.reason) for r in results if r.status == "invalid"]
        if term or trunc:
            break
    assert {"task", "build", "hire", "staff"} <= seen
    assert "executed" in statuses


def test_reads_clef_answer_shapes():
    """The exact shapes Clef's systemone() returns (joint_schema_model.systemone_answer): a noul is
    {"noul": P(true)}, a score's probabilities are keyed by level string. Run 73566 read every noul
    as false (the key was unknown) and so never staffed a building."""
    decode = {"staff_0": ("staff", "Kitchen Alpha"), "hire_trained": ("hire", "trained")}
    answers = {"staff_0": {"type": "noul", "noul": 0.83},
               "hire_trained": {"type": "score", "score": 2.9, "confidence": 0.6,
                                "probabilities": {"0": 0.05, "1": 0.05, "2": 0.1, "3": 0.6, "4": 0.1, "5": 0.1}}}
    assert decision.tool_calls(answers, decode) == [("staff", {"site": "Kitchen Alpha"}),
                                                    ("hire", {"kind": "trained", "count": 3})]


def test_argmax_reads_each_answer_shape():
    decode = {"task_FOOD_X": ("task", "FOOD_X"), "build": ("build", None), "hire_trained": ("hire", "trained"),
              "train": ("train", None), "staff_0": ("staff", "Shelter Alpha")}
    answers = {"task_FOOD_X": {"choice": "c2", "probabilities": {"skip": 0.1, "c0": 0.2, "c2": 0.7}},
               "build": {"choice": "kitchen", "probabilities": {"none": 0.3, "kitchen": 0.6, "shelter": 0.1}},
               "build_site": {"choice": "s4", "probabilities": {"s3": 0.4, "s4": 0.6}},
               "hire_trained": {"score": 2.2, "probabilities": [0.1, 0.1, 0.6, 0.2, 0, 0]},
               "train": {"score": 0.1, "probabilities": [0.9, 0.1]},
               "staff_0": {"probability": 0.8}}
    assert decision.tool_calls(answers, decode) == [
        ("task", {"task_id": "FOOD_X", "choice_id": 2}), ("build", {"type": "kitchen", "site_id": 4}),
        ("hire", {"kind": "trained", "count": 2}), ("staff", {"site": "Shelter Alpha"})]
