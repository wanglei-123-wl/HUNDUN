import argparse,collections,json,pathlib
p=argparse.ArgumentParser();p.add_argument('--plan',type=pathlib.Path,required=True);p.add_argument('--stage1',type=pathlib.Path,required=True);p.add_argument('--output',type=pathlib.Path,required=True);args=p.parse_args()
read=lambda p:json.loads(p.read_text(encoding='utf-8-sig'))
plan=read(args.plan);a={r['path']:r for r in read(args.stage1/'native-dependency-graph.json')['assets']};e={r['path']:r for r in plan['entries']}
m={r['name']:r['packingMode'] for r in read(args.stage1/'groups-and-existing-addresses.json')['groups']};m.update({r['name']:'PackTogether' for r in plan['newGroups']});m['RemoteGameContent']='PackSeparately'
def bucket(path):
    r=e[path];return r['targetGroup']+('/'+r['guid'] if m[r['targetGroup']]=='PackSeparately' else '/all')
g=collections.defaultdict(set)
for path in e:
    own=bucket(path);pending=[path];seen=set()
    while pending:
        n=pending.pop()
        if n in seen:continue
        seen.add(n)
        if n!=path and n in e and bucket(n)!=own:g[own].add(bucket(n));continue
        pending.extend(a.get(n,{}).get('directDependencies',[]))
cycles=[];visited=set();active=[]
def visit(node):
    if node in active:
        cycle=active[active.index(node):]+[node]
        if cycle not in cycles:cycles.append(cycle)
        return
    if node in visited:return
    active.append(node)
    for other in sorted(g[node]):visit(other)
    active.pop();visited.add(node)
for node in sorted(set(g)):visit(node)
report={'status':'succeeded' if not cycles else 'failed','packingBuckets':len({bucket(p) for p in e}),
    'crossBucketDependencyEdges':sum(len(v) for v in g.values()),'cycles':cycles,'scope':'Imported source dependency graph prediction; native bundle build validation remains required.'}
args.output.write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8');print(json.dumps(report,ensure_ascii=True));raise SystemExit(0 if not cycles else 1)
