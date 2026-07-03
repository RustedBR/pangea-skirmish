# Unity 6.5 Breaking Changes

Last verified: 2026-07-03

## Overview

Unity 6000.5.x (Unity 6.5) introduces several breaking changes, primarily around:
- Android minimum API level raised to 26 (Android 8.0)
- Legacy Render Graph compiler removed
- VR Module removed
- Entities ForEach and Aspects removed
- InstanceID → EntityId migration
- Various deprecated API removals

## Key Breaking Changes

### Android
- **Minimum Android API**: Raised to 26 (Android 8.0)
- **Android x86_64 support**: Removed
- **AGP version**: Updated to 9.0.0
- **ReplayKit support**: Removed

### Rendering
- **Legacy Render Graph compiler**: Removed — use new Render Graph interface
- **Render Pipeline Converter**: Removed old Material and Material Reference Upgrade
- **ScriptableRendererData.useNativeRenderPass**: Deprecated — URP runs with Render Graph by default

### Entities Package
- **Entities.ForEach**: Removed
- **Aspects**: Removed
- **EntitiesJournaling**: Deprecated (scheduled for removal)
- **EntityId**: Changed to 8 bytes (tentative)
- **OpenGL ES support for Entities Graphics**: Deprecated

### InstanceID → EntityId Migration
- **EntityId** replaces InstanceID in many APIs
- **FindObjectsByType** methods with FindObjectsSortMode: Deprecated
- **TryGetObjectIdentifier**: Now requires EntityId instead of int
- **RayTracingInstanceMaterialCRC.instanceID**: Deprecated, use EntityId

### GameObject API
- **GameObject.active**: Obsolete — use SetActive(), activeSelf, or activeInHierarchy
- **GameObject.SetActiveRecursively()**: Formally obsoleted
- **Component.animation**: Removed
- **Component.rigidbody**: Removed — use GetComponent<Rigidbody>()
- **GameObject.rigidbody**: Removed — use GetComponent<Rigidbody>()

### Asset Pipeline
- **AssetImportContext.GetArtifactFilePath**: Deprecated — use GetArtifactData
- **AssetImportContext.OutputArtifactFilePath**: Deprecated
- **AssetDatabase APIs**: Obsolete ones removed

### Other
- **Internal profiler**: Removed (was deprecated since Unity 2017.x)
- **PlayerSettings.useAnimatedAutorotation**: Obsolete (not supported on iOS 16+)
- **HierarchyFlattenedNodeChildren**: Renamed to HierarchyFlattenedChildrenEnumerable
- **HierarchyViewNodesEnumerable**: Renamed to HierarchyViewModelNodesEnumerable
- **Multiplayer Widgets**: Deprecated — use Unity Building Blocks

## Migration Notes for Pangea Skirmish

This project does NOT use:
- Entities package (no ECS code)
- VR Module
- Android x86_64 builds
- Render Pipeline Converter

**Potential impact:**
- If using `GameObject.active` anywhere → migrate to SetActive()
- If using `Component.rigidbody` or `Component.animation` → migrate to GetComponent<>()
- URP 17.5.0 should be compatible with the new Render Graph (no legacy compiler usage expected)

## References

- [Planned breaking changes in Unity 6.5](https://discussions.unity.com/t/planned-breaking-changes-in-unity-6-5-updated-2026-03-27/1694205)
- [Unity 6000.5.0f1 release notes](https://unityreleases.com/releases/6000.5.0f1)
- [Unity 6000.5.0a7 release notes](https://unityreleases.com/releases/6000.5.0a7)
- [Unity 6000.5.0a9 release notes](https://unityreleases.com/releases/6000.5.0a9)
