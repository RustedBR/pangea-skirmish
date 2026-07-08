# Guia — Armas e Weapon Anim Editor (Pangea Skirmish)

> **Estado atual (2026-07-08):** o `WeaponOverlay` está **DESLIGADO por padrão**
> (`GameTuning.weaponOverlayEnabled = false`). Os sprites TinyTactics JÁ trazem a
> arma desenhada e animada nos frames de corpo (attack/walking), então o overlay
> era redundante e causava a arma **desalinhada/flutuando** por cima do corpo.
>
> Com o overlay desligado, a arma do próprio corpo é usada e aparece alinhada.

---

## 1. Como ligar o overlay (se quiser separar a arma do corpo)

1. No Unity, abra `Assets/Resources/GameTuning.asset`.
2. Seção **ARMAS (OVERLAY)** → marque **`weaponOverlayEnabled` = true**.
3. **OBRIGATÓRIO:** antes de rodar, alinhe cada arma na janela abaixo.
   Sem alinhar, a arma volta a flutuar (usa o offset fixo `weaponAttackOffset`/`weaponCarryOffset`).

---

## 2. Weapon Anim Editor — alinhamento frame a frame

**Menu:** `Pangea Skirmish → Animation → Weapon Anim Editor`

Janela que posiciona a arma isolada (`Sprites/TinyTactics/Weapons/{id}attackSE/NE`)
**por cima** do corpo de referência (`Sprites/TinyTactics/Characters/{classe}`),
vendo os dois juntos num canvas de pixel. Salva curvas de posição/rotação/escala
diretamente nos clips `{id}_Attack{SE,NE}.anim` que o jogo usa.

### Controles
| Ação | Tecla / Mouse |
|------|---------------|
| Arrastar a arma | botão esquerdo (drag) |
| Mover 1px | setas |
| Mover 4px | Shift + setas |
| Girar ±5° | Q / E |
| Escala ∓5% | Z / X |
| Frente/atrás do personagem | B |
| Frame anterior/próximo | `,` / `.` |
| Zoom no canvas | scroll do mouse |

### Passo a passo do alinhamento
1. Selecione a **Arma** (ex.: Hatchet), o **Corpo** de referência (fighter/mage/cleric)
   e a **Direção** (SE ou NE — o flip SW/NW espelha sozinho no runtime).
2. Para cada frame da timeline (0 a 5):
   - Arraste a arma até casar com a mão/posição do corpo.
   - Ajuste rotação/escala se necessário.
   - Frames 1 e 3 da arma são **vazios na arte** → desative-os (botão "SKIPADO")
     se não precisar, para não mostrar arma inexistente.
3. Use o mini-preview "jogo" (canto superior direito) para conferir o tamanho real.
4. Clique em **SALVAR NO CLIP** (verde quando há mudanças não salvas).
5. Repita para **todas as armas** × **todas as classes** × **SE e NE**.

> As curvas de posição valem para TODAS as classes que usarem a arma
> (o offset é global por arma+direção).

### Gerar clips (quando houver nova arma)
**Menu:** `Pangea Skirmish → Animation → Gerar Animações de Armas`
- Cria os `.anim` + `WeaponOverlayBase.controller` + `{id}Overlay.overrideController`.
- **Não destrutivo:** só reescreve a curva de SPRITE; preserva as curvas de
  posição/rotação feitas na janela. Re-salve na janela se regenerar.

---

## 3. Arquivos envolvidos
| Arquivo | Função |
|---------|--------|
| `Assets/Scripts/GameTuning.cs` | flag `weaponOverlayEnabled` + offsets |
| `Assets/Scripts/Unit.cs` | `EquipWeapon()` — cria o overlay só se `weaponOverlayEnabled` |
| `Assets/Scripts/WeaponOverlay.cs` | lógica do overlay (SpriteRenderer + Animator) |
| `Assets/Editor/WeaponAnimEditorWindow.cs` | janela de alinhamento |
| `Assets/Editor/WeaponAnimationBuilder.cs` | geração dos clips/controller |
| `Assets/Resources/Animations/Weapons/` | clips `.anim` + overrides |
| `Assets/Resources/Sprites/TinyTactics/Weapons/` | spritesheets de arma isolada |
| `Assets/Resources/Sprites/TinyTactics/Characters/` | corpos de referência |

---

## 4. Armas sem arte (não mostram overlay mesmo ligado)
`ShortBow`, `LongBow`, `ApprenticeWand` não têm spritesheet em
`Sprites/TinyTactics/Weapons/` → o overlay dá `HasSprites=False` e é oculto.
Para habilitá-las: crie o spritesheet `{id}attackSE.png` / `{id}attackNE.png`
(6 frames 32x48) e rode "Gerar Animações de Armas".
