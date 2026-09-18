#!/usr/bin/env python3
"""Smoke-test: can flagship VLMs read spatial structure off the synthetic map?

Sends arc_dashboard.png to several vision models via the CMU gateway and asks
purely spatial questions (no distance table, no text state dump) to see whether
the rendered tile map alone conveys: terrain, the road network, river/forest
barriers, and which build-sites are well-placed to serve communities in deficit.

Run:  python vlm_map_smoketest.py [image.png]
"""
import base64, sys, textwrap
import openai
import llm_smoke_test as smoke

MODELS = [
    "us.anthropic.claude-opus-4-8",
    "gpt-5.5",
    "gemini/gemini-3.1-pro-preview",
]
# Optional 2nd CLI arg: comma-separated model filter (substring match).

PROMPT = textwrap.dedent("""\
    This is a top-down operations map for a disaster-response resource game.
    Use ONLY the image — you have no other data.

    1. PERCEPTION. Describe the terrain you see: where are the rivers (blue),
       roads (grey), and forest/mountain (dark green) tiles? Roughly how is the
       road network laid out?
    2. ENTITIES. List the labelled communities, the motel, and the build-sites
       (#id). For each community, state its food status. Where is each build-site
       relative to the communities and roads?
    3. SPATIAL REASONING. Three communities are in food deficit. Which 2-3
       build-sites look best positioned to supply them (near roads, central,
       not cut off by river/forest)? Are any sites isolated by water or terrain?
    4. Give ONE strategic insight you can draw purely from the spatial layout.

    Be concrete and cite positions (e.g. "#12 is just south-west of Community01").
    If something is ambiguous in the image, say so.
    """)


def encode(path):
    with open(path, "rb") as f:
        return base64.b64encode(f.read()).decode()


def ask(client, model, b64):
    msgs = [{"role": "user", "content": [
        {"type": "text", "text": PROMPT},
        {"type": "image_url", "image_url": {"url": f"data:image/png;base64,{b64}"}},
    ]}]
    kw = {"model": model, "messages": msgs}
    # Reasoning models (gpt-5.x, gemini-3.x) spend a large hidden budget before any
    # visible text, so give plenty of headroom AND cap reasoning effort or the
    # answer comes back empty (all tokens consumed by hidden reasoning).
    if model.startswith("gpt-5"):
        kw["max_completion_tokens"] = 6000
        kw["reasoning_effort"] = "low"
    else:
        kw["max_tokens"] = 4000
    resp = client.chat.completions.create(**kw)
    return resp.choices[0].message.content, resp.usage


def main():
    img = sys.argv[1] if len(sys.argv) > 1 else "arc_dashboard.png"
    flt = sys.argv[2].split(",") if len(sys.argv) > 2 else None
    models = [m for m in MODELS if not flt or any(f in m for f in flt)]
    b64 = encode(img)
    client = openai.OpenAI(api_key=smoke.load_env_key(), base_url=smoke.GATEWAY_BASE)
    print(f"Image: {img}  ({len(b64)} b64 chars)\n")
    for model in models:
        print("=" * 90)
        print(f"MODEL: {model}")
        print("=" * 90)
        try:
            text, usage = ask(client, model, b64)
            print(text.strip())
            if usage:
                print(f"\n[tokens in={usage.prompt_tokens} out={usage.completion_tokens}]")
        except Exception as e:
            print(f"ERROR: {type(e).__name__}: {e}")
        print()


if __name__ == "__main__":
    main()
