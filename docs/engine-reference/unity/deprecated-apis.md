# Unity 6.5 Deprecated APIs

Last verified: 2026-07-03

## Deprecated → Replacement Table

| Deprecated API | Replacement | Since |
|----------------|-------------|-------|
| `GameObject.active` | `SetActive()`, `activeSelf`, `activeInHierarchy` | 6000.5 |
| `Component.rigidbody` | `GetComponent<Rigidbody>()` | 6000.5 |
| `Component.animation` | `GetComponent<Animation>()` | 6000.5 |
| `GameObject.rigidbody` | `GetComponent<Rigidbody>()` | 6000.5 |
| `FindObjectsByType(sortMode)` | Sort manually after finding | 6000.5 |
| `AssetImportContext.GetArtifactFilePath` | `GetArtifactData` | 6000.5 |
| `AssetImportContext.OutputArtifactFilePath` | Deprecated — use alternatives | 6000.5 |
| `ScriptableRendererData.useNativeRenderPass` | URP uses Render Graph by default | 6000.5 |
| `PlayerSettings.useAnimatedAutorotation` | Not supported on iOS 16+ | 6000.5 |
| `EntitiesJournaling` | Remove dependencies | 6000.5 |
| `EntityGuid.a`, `EntityGuid.b`, `OriginatingId`, `OriginatingSubId` | Use EntityId | 6000.5 |
| `RayTracingInstanceMaterialCRC.instanceID` | Use EntityId | 6000.5 |
| `AdaptiveProbeVolumes.BakeAdditionalRequests(int[])` | Use `EntityId[]` | 6000.5 |
| `HierarchyFlattenedNodeChildren` | `HierarchyFlattenedChildrenEnumerable` | 6000.5 |
| `HierarchyViewNodesEnumerable` | `HierarchyViewModelNodesEnumerable` | 6000.5 |
| `Multiplayer Widgets` | Unity Building Blocks | 6000.5 |
| `LightingSettings.DenoiserType.Optix` | `LightingSettings.DenoiserType.OpenImage` | 6000.5 |

## Not Used in This Project

The following deprecated APIs are NOT relevant to Pangea Skirmish:
- Entities package APIs (EntitiesJournaling, EntityGuid fields)
- VR Module
- Render Pipeline Converter (old Material upgrade)
- Multiplayer Widgets
- AdaptiveProbeVolumes (lightmap baking)

## APIs to Watch

Scan codebase for these patterns:
- `\.active` (GameObject.active)
- `Component\.rigidbody` or `Component\.animation`
- `FindObjectsByType.*sortMode`

## References

- [Unity 6000.5.0f1 release notes](https://unityreleases.com/releases/6000.5.0f1)
- [Unity 6000.5.0a7 release notes](https://unityreleases.com/releases/6000.5.0a7)
