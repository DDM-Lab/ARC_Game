"""Minimal, self-contained CMU AI gateway call — prints the response and token usage.

Run:  python demo_gateway_call.py
"""
import openai

API_KEY  = "PASTE_YOUR_CMU_GATEWAY_KEY_HERE"   # <-- inline your key here
BASE_URL = "https://ai-gateway.andrew.cmu.edu/v1"
MODEL    = "us.anthropic.claude-opus-4-8"        # e.g. gpt-5.5, gemini-2.5-pro, ...

client = openai.OpenAI(api_key=API_KEY, base_url=BASE_URL)

resp = client.chat.completions.create(
    model=MODEL,
    messages=[{"role": "user", "content": "Say hello in one sentence."}],
    max_tokens=100,
)

print("Model   :", MODEL)
print("Response:", resp.choices[0].message.content)
print()
u = resp.usage
print(f"Input tokens : {u.prompt_tokens}")
print(f"Output tokens: {u.completion_tokens}")
print(f"Total tokens : {u.total_tokens}")
print()
print("Full usage object:", u.model_dump())   # shows reasoning_tokens etc. if the model reports them
