# -*- coding: utf-8 -*-
import json, sys, io
d = json.load(io.open(sys.argv[1], encoding="utf-8"))
out = io.open(sys.argv[2], "w", encoding="utf-8")
def p(*a): print(*a, file=out)
for setname, per in d.items():
    p("=====", setname, "files", len(per), "lines", sum(x["lines"] for x in per),
      "blob_has_cr", sum(1 for x in per if x["blob_has_cr"]))
    keys = per[0]["raw"].keys()
    for k in keys:
        fl = [(x["file"].split("/")[-1], x["raw"][k]) for x in per if x["raw"][k]]
        p(f"[{k}] files={len(fl)} lines={sum(len(v) for _, v in fl)}")
        for name, v in fl[:60]:
            p("   ", name, v[:25], "..." if len(v) > 25 else "")
    # lexer views
    p("--- lexer diffs ---")
    for x in per:
        name = x["file"].split("/")[-1]
        for eol in ("LF", "CRLF"):
            v = x["lex"][eol]
            sv, a, b = v["server"], v["c25"], v["c26"]
            msgs = []
            for key in ("q", "delim", "semi"):
                if not (sv[key] == a[key] == b[key]):
                    msgs.append(f"{key}: srv={sv[key]} c25={a[key]} c26={b[key]}")
            sat = set(map(tuple, sv["at"])); aat = set(map(tuple, a["at"])); bat = set(map(tuple, b["at"]))
            if not (sat == aat == bat):
                msgs.append(f"at: srv-c25={sorted(sat-aat)} c25-srv={sorted(aat-sat)} srv-c26={sorted(sat-bat)} c26-srv={sorted(bat-sat)}")
            if msgs:
                p(name, eol, " | ".join(msgs))
    # code-visible ? and @ summary
    p("--- server-view code ? ---")
    for x in per:
        q = x["lex"]["LF"]["server"]["q"]
        if q: p(x["file"].split("/")[-1], q)
    p("--- server-view code @user (non @@) count by file ---")
    tot = 0
    for x in per:
        at = [t for t in x["lex"]["LF"]["server"]["at"] if not t[1].startswith("@@")]
        if at:
            tot += len(at)
    p("total @user tokens in code:", tot, "files:", sum(1 for x in per if any(not t[1].startswith('@@') for t in x["lex"]["LF"]["server"]["at"])))
    p("semis total:", sum(x["lex"]["LF"]["server"]["semi"] for x in per),
      "files with >=2 statements:", sum(1 for x in per if x["lex"]["LF"]["server"]["semi"] >= 2))
out.close()
