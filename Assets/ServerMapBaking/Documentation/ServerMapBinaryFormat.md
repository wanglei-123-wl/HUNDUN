# Server Map Export V1

Each baked map is exported to one folder named `{sceneId}_{mapId}`. All coordinates are Unity world coordinates: X/Z are the horizontal plane, Y is height, and one Unity unit remains one server unit.

## Files

- `manifest.json`: camelCase map description.
- `heightfield.bin`: raw little-endian `float32` height values.
- `walkable.bin`: raw `byte` walkability values, where `0` is blocked and `1` is walkable.
- `colliders.bin`: primitive collider records with the `GCOL` header.
- `navmesh.bin`: Unity NavMesh vertices, indices and `int32` area values.
- `bake.report.txt`: editor-only bake summary.

## Manifest

`sceneId` and `mapId` are UTF-8 strings with a maximum of 64 characters. `origin` is the grid lower-left corner on the world X/Z plane, using `boundsMin.x` and `boundsMin.z`; `origin.y` is always `0` in V1.

Grid dimensions use `ceil`:

```text
width  = ceil((boundsMax.x - boundsMin.x) / cellSize)
height = ceil((boundsMax.z - boundsMin.z) / cellSize)
```

Grid indexing is row-major:

```text
index = gridZ * width + gridX
```

Samples use the cell center:

```text
sampleX = origin.x + (gridX + 0.5) * cellSize
sampleZ = origin.z + (gridZ + 0.5) * cellSize
```

## Heightfield

`heightfield.bin` contains exactly `width * height` little-endian `float32` values. Each value is the absolute world-space Y coordinate. Cells outside the original bounds or without valid terrain write IEEE 754 `NaN`.

## Walkable

`walkable.bin` contains exactly `width * height` bytes. A cell is `1` only when the sampled ground height is valid and the cell center matches a nearby Unity NavMesh point. Invalid or padded cells are `0`.

## Colliders

`colliders.bin` starts with this little-endian header:

```text
Magic         4 bytes  ASCII GCOL
Version       uint16   1
HeaderSize    uint16   20
ColliderCount uint32
RecordBytes   uint32   48
Reserved      uint32   0
```

Each fixed-size record is:

```text
colliderId   uint32
colliderType uint32  1 Box, 2 Sphere, 3 Capsule, 4 TriangleMesh
flags        uint32
centerX      float32
centerY      float32
centerZ      float32
rotationX    float32 degrees, normalized to [0, 360)
rotationY    float32 degrees, normalized to [0, 360)
rotationZ    float32 degrees, normalized to [0, 360)
sizeX        float32
sizeY        float32
sizeZ        float32
```

Box `sizeX/Y/Z` is the world-space box size. Sphere uses diameter on all three size axes. Capsule V1 uses Unity Y-axis capsules and writes `(diameter, height, diameter)`.

## NavMesh

`navmesh.bin` is little-endian:

```text
vertexCount int32
vertices    vertexCount * float32 x/y/z
indexCount  int32
indices     indexCount * int32
areaCount   int32
areas       areaCount * int32
```

`indexCount` must be divisible by `3`, and `areaCount` must equal `indexCount / 3`.

## Regions

`ServerRegion` is intentionally not exported in V1. The manifest writes `regionsFile: null`.
