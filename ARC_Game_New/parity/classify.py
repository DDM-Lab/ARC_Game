import re, subprocess, sys, json, collections

MECH = ["Tasks/TaskSystem.cs","Delivery/Vehicle.cs","GlobalClock.cs","Map/ClientStayTracker.cs",
        "Delivery/DeliverySystem.cs","Actions/ActionExecutor.cs","Tasks/ClientRelocationHandler.cs",
        "SatisfactionAndBudget.cs","DailyReport/DailyReportData.cs","Flood/FloodSystem.cs",
        "ScenarioLoader/GameConfigLoader.cs","Tasks/TaskDetailUI.cs","WeatherSystem.cs",
        "Tasks/CommunityFoodDepletionManager.cs","Tasks/FoodDeliveryHandler.cs",
        "Tasks/BudgetAllocationManager.cs","WorkerAssignment/WorkerTrainingSystem.cs",
        "WorkerAssignment/WorkerRequestSystem.cs","WorkerAssignment/WorkerSystem.cs",
        "DailyReport/DailyReportUI.cs","Tasks/FloodTaskGenerator.cs","Map/BuildingSystem.cs"]
BASE="ARC_Game_New/Assets/Scripts/"

# markers that make a hunk LLM/instrumentation-only
INSTR = re.compile(r"SnapshotDebug|GymServer|Gym[A-Z]|WebSocket|AgentConversation|TypingIndicator|"
                   r"ServerLauncher|GuiInteraction|RewardMetricsTracker|GameSnapshot|CoraFile|"
                   r"CoraSaveLoad|SafeInputField|UiSelfTest|EpisodeRepro|\[GymServer\]|DescribeSubscribers")
LOGGING = re.compile(r"Debug\.Log|Debug\.LogWarning|Debug\.LogError|GameLogPanel|LogPlayerAction|"
                     r"LogError|LogTaskEvent|LogResourceChange|LogMetricsChange|showDebugInfo")
RNG = re.compile(r"Random\.(value|Range|InitState|state)")

rows=[]
for rel in MECH:
    p=BASE+rel
    try:
        d=subprocess.run(["git","diff","-U0","origin/main-bugfixes","HEAD","--",p],
                         capture_output=True,text=True,check=True).stdout
    except subprocess.CalledProcessError:
        continue
    if not d.strip(): continue
    hunks=re.split(r"\n(?=@@ )", d)
    for h in hunks[1:]:
        head=h.split("\n",1)[0]
        body="\n".join(l for l in h.split("\n") if l[:1] in "+-" and not l.startswith(("+++","---")))
        added=[l for l in body.split("\n") if l.startswith("+")]
        removed=[l for l in body.split("\n") if l.startswith("-")]
        code=lambda ls:[l[1:].strip() for l in ls
                        if l[1:].strip() and not l[1:].strip().startswith(("//","///","*","/*"))]
        ca,cr=code(added),code(removed)
        if not ca and not cr: kind="comment-only"
        elif INSTR.search(body): kind="instrumentation/LLM"
        elif all(LOGGING.search(l) for l in ca) and not cr: kind="logging-only"
        elif not cr: kind="ADDITION"
        else: kind="MODIFIES EXISTING"
        rows.append({"file":rel,"hunk":head,"kind":kind,
                     "rng": bool(RNG.search(body)),
                     "added":len(ca),"removed":len(cr),
                     "sample":"; ".join((ca or cr)[:2])[:150]})
json.dump(rows, open("/tmp/claude-501/ledger.json","w"), indent=1)
c=collections.Counter(r["kind"] for r in rows)
print(f"  hunks analysed: {len(rows)}")
for k,v in c.most_common(): print(f"    {k:22s} {v}")
print(f"    touching RNG        : {sum(1 for r in rows if r['rng'])}")
