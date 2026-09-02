import json, os, sys
sys.path.insert(0, os.getcwd())
from cora_search import SearchableEnv
EXE=("Build/Headless/macOS/ARC_Headless.app/Contents/MacOS/"
     "Collaborative Operations And Resource Management with Agentic AI")
SP=os.environ["SP"]
env=SearchableEnv(unity_exe_path=EXE, unity_port=9920, seed=555,
                  auto_start_unity=True, connection_timeout=90,
                  unity_log_path=os.path.join(SP,"flood_marks.log"))
env.reset()
snaps=[]
for _ in range(10):
    s=json.loads(env._send_request({"type":"save_state"})["state"])
    f=s["flood"]
    snaps.append({"tileX":f["tileX"],"tileY":f["tileY"],
                  "lastWeather":f["lastWeatherType"],"weather":s["weather"]["current"]})
    env.advance_round()
json.dump(snaps, open(os.path.join(SP,"flood_snaps.json"),"w"))
env.close()
