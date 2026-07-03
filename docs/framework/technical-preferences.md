# Technical Preferences — Pangea Skirmish

## Engine & Language

- **Engine**: Unity 6000.5.1f1
- **Language**: C#
- **Render Pipeline**: Universal Render Pipeline (URP) 17.5.0
- **Input System**: Input System 1.19.0

## Naming Conventions

- Classes: PascalCase (e.g., `RoundManager`)
- Public fields/properties: PascalCase (e.g., `MoveSpeed`)
- Private fields: _camelCase (e.g., `_moveSpeed`)
- Methods: PascalCase (e.g., `TakeDamage()`)
- Files: PascalCase matching class (e.g., `RoundManager.cs`)
- Constants: PascalCase or UPPER_SNAKE_CASE
- Namespace: `PangeaSkirmish`

## Input & Platform

- **Target Platforms**: PC, Mobile
- **Input Methods**: Keyboard/Mouse, Touch
- **Primary Input**: Keyboard/Mouse
- **Gamepad Support**: None
- **Touch Support**: Full
- **Platform Notes**: Mobile touch input via Input System. No gamepad support planned.

## Performance Budgets

- **Target Framerate**: 60 FPS
- **Frame Budget**: 16.6ms
- **Max Draw Calls**: 100
- **Max Triangles**: 100k
- **Max Texture Size**: 2048x2048

## Testing

- **Framework**: NUnit + Unity Test Framework 1.7.0
- **Test Location**: `Assets/Tests/EditMode/`
- **Existing Tests**: GridManagerTests.cs, SpellBookTests.cs, AttributeStatsTests.cs

## Engine Specialists

- **Primary**: unity-specialist
- **Language/Code Specialist**: unity-specialist (C# review — primary covers it)
- **Shader Specialist**: unity-shader-specialist (Shader Graph, HLSL, URP/HDRP materials)
- **UI Specialist**: unity-ui-specialist (UI Toolkit UXML/USS, UGUI Canvas, runtime UI)
- **Additional Specialists**: unity-dots-specialist (ECS, Jobs system, Burst compiler), unity-addressables-specialist (asset loading, memory management, content catalogs)
- **Routing Notes**: Invoke primary for architecture and general C# code review. Invoke DOTS specialist for any ECS/Jobs/Burst code. Invoke shader specialist for rendering and visual effects. Invoke UI specialist for all interface implementation. Invoke Addressables specialist for asset management systems.

### File Extension Routing

| File Extension / Type | Specialist to Spawn |
|-----------------------|---------------------|
| Game code (.cs files) | unity-specialist |
| Shader / material files (.shader, .shadergraph, .mat) | unity-shader-specialist |
| UI / screen files (.uxml, .uss, Canvas prefabs) | unity-ui-specialist |
| Scene / prefab / level files (.unity, .prefab) | unity-specialist |
| Native extension / plugin files (.dll, native plugins) | unity-specialist |
| General architecture review | unity-specialist |

## Forbidden Patterns

[TO BE CONFIGURED]

## Allowed Libraries

- URP 17.5.0 (render pipeline)
- Cinemachine 3.1.7 (camera)
- ProBuilder 6.1.2 (level design)
- Visual Effect Graph 17.5.0 (VFX)
- Input System 1.19.0 (input handling)
