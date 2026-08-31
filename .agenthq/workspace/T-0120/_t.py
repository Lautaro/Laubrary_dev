import json, sys, urllib.request

URL = "http://127.0.0.1:8778/api/task?project=Laubrary_Dev&id=T-0120&reader=agent"
d = json.load(urllib.request.urlopen(URL))
mode = sys.argv[1] if len(sys.argv) > 1 else "todos"
if mode == "todos":
    for t in d["todos"]:
        if len(sys.argv) > 2 and sys.argv[2] == "open" and t["checked"]:
            continue
        print(t["index"], "[x]" if t["checked"] else "[ ]", t["text"][:110])
        for m in t.get("messages", []):
            print("     msg:", (m.get("text") or "")[:200].replace("\n", " "))
elif mode == "new":
    # unread user messages / questions
    for t in d["todos"]:
        for m in t.get("messages", []):
            if m.get("actor") == "user":
                print("TODO", t["index"], "USER:", m.get("text"))
    for i, q in enumerate(d.get("questions", [])):
        print("Q", i, q.get("actor"), q.get("text")[:400])
        for a in q.get("answers", []) or []:
            print("  A", a.get("actor"), (a.get("text") or "")[:400])
elif mode == "status":
    print(d["frontmatter"].get("status"), d["frontmatter"].get("updated"))
