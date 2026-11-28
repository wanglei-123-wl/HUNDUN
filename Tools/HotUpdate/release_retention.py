"""Reference-based retention planning. This module never deletes files."""
from release_common import *

def plan_retention(state, policy, now=None):
    now = now or utcnow()
    require(policy['schemaVersion'] == 1, 'Unsupported retention policy.')
    count = policy['published']['keepSuccessfulVersionsPerCompatibilityChannel']
    grace = dt.timedelta(days=policy['published']['retiredVersionGraceDays'])
    abandoned = dt.timedelta(hours=policy['staging']['abandonedUploadGraceHours'])
    require(type(count) is int and count >= 3 and grace >= dt.timedelta(days=7) and abandoned >= dt.timedelta(hours=24),
            'Retention cannot silently reduce the agreed protection policy.')
    keep = {}; publications = state['publications']; current = state.get('currentId')
    def protect(key, reason):
        keep.setdefault(key, []).append(reason)
    groups = {}
    for key, item in publications.items():
        if item['status'] == 'published':
            groups.setdefault(channel_key(item['release']), []).append((key, item))
        if item.get('pinned'):
            protect(key, 'compatibility-pinned')
        if key == current:
            protect(key, 'current')
        if item['status'] == 'published' and key != current:
            if not item.get('retiredAtUtc'):
                protect(key, 'missing-retirement-record')
            elif now - parse_time(item['retiredAtUtc']) < grace:
                protect(key, 'retirement-grace')
    for items in groups.values():
        items.sort(key=lambda pair: (parse_time(pair[1]['publishedAtUtc']), pair[1]['release']['sequence']), reverse=True)
        for key, _ in items[:count]:
            protect(key, 'recent-successful-publication')
    staged = {}
    for key, item in publications.items():
        if item['status'] == 'staged':
            staged.setdefault(channel_key(item['release']), []).append((key, item))
    for items in staged.values():
        items.sort(key=lambda pair: parse_time(pair[1]['stagedAtUtc']), reverse=True)
        for key, _ in items[:policy['staging']['keepLatestVerifiedCandidatePerCompatibilityChannel']]:
            protect(key, 'latest-verified-candidate')
    for key, upload in state.get('uploads', {}).items():
        if key in state.get('activeOperationIds', []):
            protect(key, 'active-upload')
        elif now - parse_time(upload['updatedAtUtc']) < abandoned:
            protect(key, 'upload-retention-grace')
    for key, item in publications.items():
        if item['status'] not in ('published', 'staged'):
            protect(key, 'unrecognized-or-active-publication-state')
    # An offered/failed upload can reference an already installed resource or blob.
    releases = [item['release'] for key, item in publications.items() if key in keep]
    releases += [u['release'] for key, u in state.get('uploads', {}).items() if key in keep]
    resources = {r['resourceReleaseId'] for r in releases}
    blobs = {r['codeSha256'] for r in releases}
    for key, publication in publications.items():
        if key in keep:
            # Keep the signed client proof/control blobs needed to materialize
            # a retained publication as a forward-sequence rollback candidate.
            blobs.update(record['sha256'] for record in publication.get('descriptor', {}).get('packageManifest', {}).get('files', {}).values())
    for rid in resources:
        if rid in state['resources']:
            blobs.update(record['sha256'] for record in state['resources'][rid]['files'].values())
    for key, upload in state.get('uploads', {}).items():
        if key in keep:
            blobs.update(record['sha256'] for record in upload['files'].values())
    return {'schemaVersion': 1, 'createdAtUtc': timestamp(now), 'keepPublications': keep,
            'removePublications': sorted(set(publications) - set(keep)),
            'removeUploads': sorted(set(state.get('uploads', {})) - set(keep)),
            'keepResources': sorted(resources), 'removeResources': sorted(set(state['resources']) - resources),
            'keepBlobs': sorted(blobs), 'removeBlobs': sorted(set(state['blobs']) - blobs)}
