import argparse,json,pathlib,re
p=argparse.ArgumentParser();p.add_argument('--source',type=pathlib.Path,required=True);p.add_argument('--artifact',type=pathlib.Path,required=True);a=p.parse_args()
read=lambda p:json.loads(p.read_text(encoding='utf-8-sig'))
plan=read(a.artifact/'migration-plan.json');entries={};groups={};meta_guids={}
for file in (a.source/'Assets/AddressableAssetsData/AssetGroups').rglob('*.meta'):
    match=re.search(r'^guid: ([a-f0-9]{32})$',file.read_text(encoding='utf-8-sig'),re.M)
    if match:
        guid=match.group(1);assert guid not in meta_guids,'Duplicate asset GUID';meta_guids[guid]=str(file)
for file in (a.source/'Assets/AddressableAssetsData/AssetGroups').glob('*.asset'):
    text=file.read_text(encoding='utf-8-sig');name=re.search(r'^  m_GroupName: (.+)$',text,re.M)
    if not name:continue
    group=name.group(1);group_guid=re.search(r'^  m_GUID: ([a-f0-9]{32})$',text,re.M).group(1)
    groups[group]={'path':file.relative_to(a.source).as_posix(),'internalGroupGuid':group_guid}
    for ref in re.findall(r'^    - \{fileID: 11400000, guid: ([a-f0-9]{32}), type: 2\}',text,re.M):assert ref in meta_guids,ref
    blocks=re.findall(r'^  - m_GUID: ([a-f0-9]{32})\r?\n(.*?)(?=^  - m_GUID:|^  m_ReadOnly:)',text,re.M|re.S)
    for guid,body in blocks:
        address=re.search(r'^    m_Address: (.+)$',body,re.M).group(1)
        assert guid not in entries,guid
        entries[guid]={'address':address,'group':group}
assert len({g['internalGroupGuid'] for g in groups.values()})==len(groups),'Duplicate internal group identity.'
for r in plan['entries']:
    assert entries[r['guid']]=={'address':r['address'],'group':r['targetGroup']},r['path']
assert len(entries)==len(plan['entries'])
settings=(a.source/plan['settingsFile']).read_text(encoding='utf-8-sig')
group_block=re.search(r'^  m_GroupAssets:\r?\n(.*?)(?=^  [A-Za-z_][A-Za-z_0-9]*:|\Z)',settings,re.M|re.S)
assert group_block,'Missing group registry'
references=re.findall(r'^  - \{fileID: 11400000, guid: ([a-f0-9]{32}), type: 2\}',group_block.group(1),re.M)
assert len(references)==len(groups)
assert all(g in meta_guids for g in references)
schema=a.source/'Assets/AddressableAssetsData/AssetGroups/Schemas/RemoteGameContent_BundledAssetGroupSchema.asset'
assert re.search(r'^  m_BundleMode: 1$',schema.read_text(encoding='utf-8-sig'),re.M)
result={'status':'succeeded','explicitResourceEntries':len(entries),'groupObjects':len(groups),'newRpoGroups':sum(g.startswith('RPO_') for g in groups),
    'sourceMatchesNativeVerifiedPlan':True,'groupAssetAndSchemaGuidsResolved':True,'internalGroupIdsUnique':True,
    'resourceBuildPerformed':False,'playerBuildPerformed':False,'resourceBuildReadinessVerified':False}
(a.artifact/'source-metadata-verification.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
(a.artifact/'applied-group-registry.json').write_text(json.dumps(groups,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(result,ensure_ascii=True))
