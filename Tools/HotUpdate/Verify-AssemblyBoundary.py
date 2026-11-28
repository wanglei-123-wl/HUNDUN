"""Audit every Assets script, including future scripts outside Assets/Scripts."""
import argparse
import collections
import hashlib
import json
import pathlib
import re


def inspect(root):
    assets = root / "Assets"
    definitions = {}
    by_directory = {}
    by_guid = {}
    failures = []
    for path in sorted(assets.rglob("*.asmdef")):
        data = json.loads(path.read_text(encoding="utf-8-sig"))
        name = data["name"]
        if name in definitions:
            failures.append("Duplicate assembly: " + name)
        definitions[name] = dict(data, path=path.relative_to(root).as_posix())
        by_directory[path.parent] = name
        meta = pathlib.Path(str(path) + ".meta")
        match = re.search(r"(?m)^guid: (\w+)", meta.read_text(encoding="utf-8-sig"))
        by_guid[match[1]] = name
    for path in sorted(assets.rglob("*.asmref")):
        target = json.loads(path.read_text(encoding="utf-8-sig"))["reference"]
        if target.startswith("GUID:"):
            target = by_guid.get(target[5:], target)
        if target not in definitions:
            failures.append("Unresolved assembly reference: " + str(path))
        if path.parent in by_directory:
            failures.append("Multiple assembly owners: " + str(path.parent))
        by_directory[path.parent] = target

    scripts = []
    script_guids = {}
    for path in sorted(assets.rglob("*.cs")):
        owner = next((by_directory[p] for p in path.parents if p in by_directory), None)
        relative = path.relative_to(root).as_posix()
        meta = pathlib.Path(str(path) + ".meta")
        match = re.search(r"(?m)^guid: (\w+)", meta.read_text(encoding="utf-8-sig")) if meta.exists() else None
        guid = match[1] if match else None
        if not owner or not guid:
            failures.append("Unowned script or missing GUID: " + relative)
        if guid in script_guids:
            failures.append("Duplicate script GUID: " + relative)
        script_guids[guid] = relative
        editor = "Editor" in definitions.get(owner, {}).get("includePlatforms", [])
        if any(part.lower() == "editor" for part in path.parts) and not editor:
            failures.append("Editor source in runtime assembly: " + relative)
        if not editor and owner not in ("QiDao.GameLogic", "QiDao.Bootstrap", "QiDao.Contracts", "EZhex1991.EZSoftBone"):
            failures.append("Unapproved fixed runtime assembly: " + relative)
        if owner == "QiDao.Bootstrap" and not relative.startswith("Assets/Bootstrap/Runtime/"):
            failures.append("Business script leaked into bootstrap: " + relative)
        if owner == "QiDao.Contracts" and not relative.startswith("Assets/Bootstrap/Contracts/"):
            failures.append("Business script leaked into contracts: " + relative)
        scripts.append(dict(path=relative, assembly=owner, editorOnly=editor, guid=guid,
                            sha256=hashlib.sha256(path.read_bytes()).hexdigest()))

    graph = {name: [by_guid.get(r[5:], r) if r.startswith("GUID:") else r
                    for r in data.get("references", [])] for name, data in definitions.items()}
    for name in ("QiDao.Bootstrap", "QiDao.Contracts"):
        pending, seen = list(graph.get(name, [])), set()
        while pending:
            dependency = pending.pop()
            if dependency in seen:
                continue
            seen.add(dependency)
            if dependency in ("QiDao.GameLogic", "Assembly-CSharp", "Assembly-CSharp-firstpass"):
                failures.append(name + " depends on business assembly " + dependency)
            pending.extend(graph.get(dependency, []))
    def visit(name, stack):
        if name in stack:
            failures.append("Assembly cycle: " + " -> ".join(stack + [name]))
            return
        for dep in graph.get(name, []):
            if dep in graph:
                visit(dep, stack + [name])
    for name in graph:
        visit(name, [])

    # Record the asset-backed types that must resolve in native Unity after moves.
    bindings = {}
    for suffix in ("*.unity", "*.prefab", "*.asset", "*.controller"):
        for path in assets.rglob(suffix):
            text = path.read_text(encoding="utf-8-sig", errors="replace")
            for guid in re.findall(r"m_Script: \{fileID: 11500000, guid: (\w+)", text):
                if guid in script_guids:
                    bindings[guid] = script_guids[guid]
            if re.search(r"Assembly-CSharp(?:-firstpass)?(?:\s|,|::)", text):
                failures.append("Serialized predefined-assembly type needs migration: " + path.relative_to(root).as_posix())
    return dict(status="failed" if failures else "succeeded", failures=failures, assemblies=definitions,
                counts=dict(collections.Counter(s["assembly"] for s in scripts)), scripts=scripts,
                assetScriptBindings=[dict(guid=g, path=p) for g, p in sorted(bindings.items())])


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--project", type=pathlib.Path, default=pathlib.Path(__file__).resolve().parents[2])
    parser.add_argument("--output", type=pathlib.Path, required=True)
    args = parser.parse_args()
    report = inspect(args.project.resolve())
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({k: report[k] for k in ("status", "counts", "failures")}, ensure_ascii=False))
    raise SystemExit(0 if report["status"] == "succeeded" else 1)
