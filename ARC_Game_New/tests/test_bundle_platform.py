#!/usr/bin/env python3
"""Hermetic tests for the contributor-bundle platform (Phase 1). No network/Unity/LLM.

Run:  env -u ALL_PROXY -u HTTP_PROXY -u HTTPS_PROXY -u http_proxy -u https_proxy \
          ./.venv/bin/python test_bundle_platform.py
"""
from __future__ import annotations

import json
from pathlib import Path

from pydantic import ValidationError

from provider_registry import Provider, resolve, is_valid, valid_names
from cora_schema import CoraConfig, Bundle, BundleManifest
from bundle import load_bundle, BundleError
from agent_config import AgentConfig, load_config

_FAILS: list[str] = []


def check(name: str, cond: bool):
    print(f"  {'ok  ' if cond else 'FAIL'} {name}")
    if not cond:
        _FAILS.append(name)


def expect_raises(name: str, fn, exc=Exception):
    try:
        fn()
        check(name, False)
    except exc:
        check(name, True)


_GOOD = {
    "agent_order_rule": "sequential",
    "agents": [
        {"subagent_name": "Director", "role": "director", "actor_type": "manual"},
        {"subagent_name": "Food Officer", "role": "subagent", "actor_type": "continuous",
         "provider": "cmu-gateway", "llm_model": "gpt-4o-mini",
         "subaction_space": [{"category": "task_choice", "group": "food"}],
         "subobservation_space": ["sessionInfo", "tasks:food"],
         "system_prompt": "You are the Food Officer."}],
}


def test_provider_registry():
    print("provider_registry")
    check("cmu-gateway resolves", resolve("cmu-gateway").backend == "openai"
          and resolve("cmu-gateway").key_env == "OPENAI_API_KEY")
    check("anthropic-ddmlab key_env", resolve("anthropic-ddmlab").key_env == "DDMLAB_ANTHROPIC_API_KEY")
    check("ollama-local base_url", resolve(Provider.ollama_local).base_url == "http://localhost:11434/v1")
    check("is_valid", is_valid("anthropic") and not is_valid("nope"))
    check("5 providers", len(valid_names()) == 5)
    expect_raises("unknown provider raises", lambda: resolve("nope"), KeyError)


def test_schema():
    print("cora_schema")
    check("valid config", len(CoraConfig.model_validate(_GOOD).agents) == 2)
    expect_raises("exfil fields rejected", lambda: CoraConfig.model_validate(
        {**_GOOD, "agents": _GOOD["agents"][:1] + [{**_GOOD["agents"][1],
         "llm_endpoint": "https://evil", "api_key_env": "ANTHROPIC_API_KEY"}]}), ValidationError)
    expect_raises("unknown key rejected", lambda: CoraConfig.model_validate(
        {**_GOOD, "agents": _GOOD["agents"][:1] + [{**_GOOD["agents"][1], "systemPrompt": "x"}]}),
        ValidationError)
    expect_raises("provider-less llm actor rejected", lambda: CoraConfig.model_validate(
        {"agent_order_rule": "sequential", "agents": [
            {"subagent_name": "D", "role": "director", "actor_type": "manual"},
            {"subagent_name": "X", "role": "subagent", "actor_type": "continuous"}]}), ValidationError)
    expect_raises("zero-director rejected", lambda: CoraConfig.model_validate(
        {"agent_order_rule": "sequential", "agents": [_GOOD["agents"][1]]}), ValidationError)
    expect_raises("bad provider name rejected", lambda: CoraConfig.model_validate(
        {**_GOOD, "agents": _GOOD["agents"][:1] + [{**_GOOD["agents"][1], "provider": "hax"}]}),
        ValidationError)
    expect_raises("bad manifest semver", lambda: BundleManifest(name="a/b", author="x", version="1.0"),
                  ValidationError)
    expect_raises("bad manifest name", lambda: BundleManifest(name="noslash", author="x", version="1.0.0"),
                  ValidationError)
    check("_comment tolerated", CoraConfig.model_validate({**_GOOD, "_comment": "hi"}) is not None)


def test_bundle_compose():
    print("bundle compose")
    full = {"manifest": {"name": "lab/full", "author": "a", "version": "1.0.0"}, "config": _GOOD}
    check("full loads", load_bundle(full)["agents"][1]["provider"] == "cmu-gateway")
    delta = {"manifest": {"name": "lab/terse", "author": "a", "version": "1.1.0"},
             "delta": {"agents": [{"subagent_name": "Food Officer", "system_prompt": "T.",
                                   "provider": "anthropic"}]}}
    r = load_bundle(delta, base_config=_GOOD)
    fo = next(a for a in r["agents"] if a["subagent_name"] == "Food Officer")
    check("delta overrides prompt", fo["system_prompt"] == "T.")
    check("delta overrides provider", fo["provider"] == "anthropic")
    check("delta keeps model", fo["llm_model"] == "gpt-4o-mini")
    expect_raises("ghost override rejected", lambda: load_bundle(
        {"manifest": {"name": "x/y", "author": "a", "version": "1.0.0"},
         "delta": {"agents": [{"subagent_name": "Ghost", "system_prompt": "z"}]}},
        base_config=_GOOD), BundleError)
    expect_raises("delta without base rejected", lambda: load_bundle(delta), BundleError)
    expect_raises("major api mismatch refused", lambda: load_bundle(
        {"manifest": {"name": "x/y", "author": "a", "version": "1.0.0", "cora_api_version": "2.0"},
         "config": _GOOD}), BundleError)
    expect_raises("config-xor-delta enforced", lambda: load_bundle(
        {"manifest": {"name": "x/y", "author": "a", "version": "1.0.0"}, "config": _GOOD,
         "delta": {"agents": []}}), BundleError)
    expect_raises("reserved tools enforced", lambda: load_bundle(
        {"manifest": {"name": "x/y", "author": "a", "version": "1.0.0"}, "config": _GOOD,
         "tools": [{"x": 1}]}), BundleError)


def _mk(**kw) -> AgentConfig:
    base = dict(subagent_name="X", role="subagent", actor_type="continuous", num_choices=None,
                max_actions_per_package=None, num_turns=None, max_actions_per_turn=None,
                talkinghead_endpoint=None, subaction_space=[], subobservation_space=["all"],
                llm_provider=None, llm_model=None, llm_endpoint=None, llm_port=None,
                api_key_env=None, turn_token_budget=None, system_prompt=None)
    base.update(kw)
    return AgentConfig(**base)


def test_agent_config_resolution():
    print("agent_config resolution")
    a = _mk(provider="cmu-gateway")
    check("provider -> triple", (a.llm_provider, a.llm_endpoint, a.api_key_env)
          == ("openai", "https://ai-gateway.andrew.cmu.edu/v1", "OPENAI_API_KEY"))
    expect_raises("both provider+raw rejected",
                  lambda: _mk(provider="anthropic", api_key_env="ANTHROPIC_API_KEY"), ValueError)
    expect_raises("bad provider rejected", lambda: _mk(provider="nope"), ValueError)
    b = _mk(llm_provider="anthropic", api_key_env="ANTHROPIC_API_KEY")  # legacy path unchanged
    check("legacy path preserved", b.llm_provider == "anthropic" and b.provider is None)


def test_live_config_migrated():
    print("live config (post-migration)")
    rc = load_config("config/continuous_all_officers_ddmlab.json")
    off = [a for a in rc.agents if a.role == "subagent"]
    check("ddmlab officers use provider enum", all(a.provider == "anthropic-ddmlab" for a in off))
    check("ddmlab resolves to DDMLAB key env",
          all(a.api_key_env == "DDMLAB_ANTHROPIC_API_KEY" and a.llm_provider == "anthropic" for a in off))
    check("no officer carries raw endpoint/secret in file",
          '"api_key_env"' not in Path("config/continuous_all_officers_ddmlab.json").read_text())


if __name__ == "__main__":
    for t in (test_provider_registry, test_schema, test_bundle_compose,
              test_agent_config_resolution, test_live_config_migrated):
        t()
    print()
    if _FAILS:
        print(f"FAILED ({len(_FAILS)}): {_FAILS}")
        raise SystemExit(1)
    print("ALL PASSED")
