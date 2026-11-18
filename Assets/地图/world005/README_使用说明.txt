world005 药灵谷 · 南北边界补山版 V04-Boundary05 / LOD3 / NAV3
2026-11-18 · 300 × 800 米 · Unity 2022.3 / URP 14

导入 UnityImport~/world005_URP.unitypackage，选择 Import。
打开 Assets/world005/Generated/world005.unity 查看完整灯光、材质和演示相机。
将 Assets/world005/Generated/world005.prefab 拖入游戏场景，包含模型、材质、三档 LOD、碰撞和烘焙导航。
旧版请完整导入覆盖，再打开更新后的场景；原有 prefab 实例随同名资源更新。保持导入包内目录名和 .meta。

本版补齐南北两端的可见山体。降低峰高，拓宽山脚，使用连续草岩坡面接入原有侧坡，溪口后方有实体岩壁。
近景 235,387，中景 99,654，远景 19,944 三角面。采用 Unity LODGroup 按屏幕占比切换。
三档完整 FBX：Models/world005.fbx、Models/world005_LOD1.fbx、Models/world005_LOD2.fbx。
FBX 提供几何、UV、材质槽；Unity 的碰撞、导航和 LOD 组合配置请使用 prefab 或场景。

两块新山体用原近景网格作静态非 Trigger MeshCollider。四周空气墙保留；三档 LOD 共用原始碰撞与导航。
导航已按新山体投影更新排除范围并重新烘焙，引用 Navigation/world005_NavMesh.asset。
角色 NavMeshAgent 使用 Humanoid（ID 0），Walkable 区域，半径 0.36 m、高 1.84 m、台阶 0.35 m、坡度 43°。
PlayerSpawn 为测试起点。全图主线请按 Navigation/RouteWaypoints.json 的途经点分段设置目标；长距离单次查询仍可能返回部分路径。
数据使用地图根节点局部坐标。地图可平移、绕 Y 轴旋转，缩放保持 1。
重新烘焙：Tools > World005 > Bake Navigation。重新生成整图：Tools > World005 > Build or Refresh Map。

Play 后右键旋转、滚轮缩放、WASD 平移、Shift 加速、Home 俯瞰；演示相机用于观察。
相机开启 URP 后处理、深度纹理和不透明纹理，以显示雾、水和调色。
Previews/Boundary_总览.png、Boundary_南端谷内.png、Boundary_北端谷内.png 为本版 Unity 实景。
验收数据见 Documentation/Boundary验收.txt、LODFBX复验.json、NavMesh验收.txt、NavMesh导入复验.txt、NavMesh连通性复验.txt。
Source~ 包含 Blender 源文件、boundary_mountains.py、原地图生成脚本和设计数据。
