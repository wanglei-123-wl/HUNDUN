world010 已烘焙地面导航

在 Unity URP 导入 UnityImport~/world010_URP.unitypackage，并打开 Assets/world010/Generated/world010.unity。
使用同名 prefab 时，根节点的 Map Navigation 自动注册 Navigation/world010_NavMesh.asset。地图可以平移和绕 Y 轴转向，缩放请保持 1。
角色的 NavMeshAgent 选择 Humanoid（ID 0），Area Mask 包含 Walkable，并从蓝色可走区域开始。PlayerSpawn 可作为测试起点。
烘焙规格为半径 0.36 m、高 1.84 m、最大台阶 0.35 m、最大坡度 43°。三档 LOD 共用这份导航。
打开地图场景后可用 Tools > World010 > Bake Navigation 按这套参数重新烘焙。NavMeshData 是场景和 prefab 共用的资产。
预览见 Previews/NavMesh_总览.png，详细寻路测试见 Documentation/NavMesh验收.txt。

world010 三档 LOD 版

整图可见三角面。近景 192,312，中景 99,762，远景 19,848。
使用 Unity 原生 LODGroup。各个地形块和物件按相机屏幕占比分别切换，同一帧可以同时出现三档。
整图统计为逐档配置的三角面合计。实际每帧计数随镜头、视野与阴影变化。
Unity 验证环境为 2022.3.62f1 / URP 14。默认使用工程的 LOD Bias 和最高 LOD 设置。
导入 UnityImport~/world010_URP.unitypackage，然后双击 Assets/world010/Generated/world010.unity。
在其他场景中使用 Assets/world010/Generated/world010.prefab。旧版本的预制体实例可原位更新。
三档各有完整 FBX，分别为 Models/world010.fbx、world010_LOD1.fbx 和 world010_LOD2.fbx。
少量本身面数很低的网格跨档复用，三档完整组合已设置在预制体和场景里。
Previews 的 LOD0/LOD1/LOD2 图用于逐档对照。

world010  地脉火窟  V04 洞窟美术重制

地图尺寸 300 × 300 米。FBX 可见模型 192,312 三角面，碰撞单独配置。
场景包面向 Unity 2022.3 / URP 14，实际验证结果见 Documentation/Unity验证.txt。

导入 UnityImport~/world010_URP.unitypackage，选择 Import。
在 Project 窗口打开 Assets/world010/Generated/world010.unity 查看灯光、材质和演示相机。
将 Assets/world010/Generated/world010.prefab 拖入你的游戏场景可使用模型及碰撞。
完整光影以同名示例场景为准。项目相机需打开 URP 后处理、深度纹理和不透明纹理，供调色、局部雾和水面使用。
Play 后使用右键旋转、滚轮缩放、WASD 平移、Shift 加速、Home 俯瞰。
PlayerSpawn 是角色出生点标记。演示相机为观察相机。
在火窟中按 Home 查看开启剖切的地图总览，缩放回洞内会恢复洞顶。

Models 内为二进制 FBX。贴图在 Textures，Shader 在 Shaders，碰撞输入在 Collision。
Source~ 内含可编辑 Blender 文件和批准的设计数据。地图版本按该目录整体保留。
Previews 内是 Unity 实景预览。将整个地图目录复制到 Assets 时，保持原目录名。
先保存当前场景，再通过 Tools > World010 > Build or Refresh Map 重新生成示例场景和预制体。
