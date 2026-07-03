# Unity 6.5 Current Best Practices

Last verified: 2026-07-03

## URP Performance Best Practices

### Render Graph (New in URP 17)
- Use Render Graph system instead of legacy Render Graph (removed in 6.5)
- Reduce render passes — each pass costs CPU and GPU time
- Use `AddRasterRenderPass` instead of other render pass types when possible
- Use `ContextContainer` to read/write color buffer directly instead of blitting
- Use Render Graph Viewer to identify why passes can't merge

### GPU Resident Drawer
- Enable GPU Resident Drawer for automatic GPU instancing
- Reduces draw calls by using BatchRendererGroup API
- Frees CPU processing time

### GPU Occlusion Culling
- Use GPU occlusion culling instead of CPU-based
- Improves performance in scenes with heavy occlusion

### Spatial Temporal Post-Processing (STP)
- Enable STP for GPU-optimized upscaling
- Works on desktop, console, and mobile with compute shader support
- Set in URP Asset > Quality > Upscaling Filter > STP

### Memory Optimization
- Disable HDR if not needed (reduces color buffer size)
- If HDR needed, set HDR Precision to 32 Bit
- On mobile: set Store Actions to Auto or Discard
- Enable SRP Batcher for persistent material data in GPU memory
- Use depth texture wisely — set Depth Texture Mode to After Transparents

### CPU Optimization
- Reduce shadow Max Distance to process fewer objects
- Reduce Cascade Count to reduce render passes
- Disable Additional Lights shadows if not needed
- Enable Conservative Enclosing Sphere for shadows

### GPU Optimization
- Reduce or disable MSAA (saves memory bandwidth)
- Disable Holes if not needed
- Enable Native RenderPass for Vulkan, Metal, DirectX 12
- Use Depth Priming Mode: Auto/Forced on PC, Disabled on mobile
- Avoid Complex Lit shader unless necessary
- On mobile: use Baked Lit for static, Simple Lit for dynamic objects

### Volume Framework
- CPU performance optimizations in 6.5 (especially mobile)
- Set global volume default values, override in quality settings

## General Unity 6 Best Practices

### Input System
- Use new Input System (already in project)
- Define action maps for different contexts (menu, gameplay, UI)

### Cinemachine
- Cinemachine 3.1.7 is current — use for all camera management
- Use CinemachineBrain for camera transitions

### Testing
- Use NUnit + Unity Test Framework
- Keep tests in Assets/Tests/EditMode/
- Use PlayMode tests for integration testing

### Code Organization
- Use namespace `PangeaSkirmish`
- PascalCase for classes and public members
- _camelCase for private fields
- Keep MonoBehaviours focused on single responsibility

## References

- [URP Performance Settings](https://docs.unity3d.com/6/Documentation/Manual/urp/optimize-for-better-performance.html)
- [URP Render Graph Optimization](https://docs.unity3d.com/6/Documentation/Manual/urp/render-graph-optimize.html)
- [What's New in URP 17](https://docs.unity3d.com/6/Documentation/Manual/urp/whats-new/urp-whats-new.html)
- [Configure for Better Performance](https://docs.unity3d.com/6000.6/Documentation/Manual/urp/configure-for-better-performance.html)
