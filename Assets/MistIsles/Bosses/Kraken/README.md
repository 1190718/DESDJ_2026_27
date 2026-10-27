# Kraken: Guardião da Drowned Isle

Boss final do Mist Isles. É um kraken low-poly que fica na água junto à ilha e ataca com os tentáculos.
O combate usa a regra escondida da Drowned Isle: **a água protege o jogador** e é também **a fraqueza do guardião** (US14, US15, US17).

Requisitos: Unity 6 (6000.x) com URP. Não precisa de Blender instalado.

---

## 1. Instalação (5 minutos)

1. Copiar a pasta `Assets/MistIsles/Bosses/Kraken` para o projeto, mantendo a estrutura.
2. Esperar que o Unity importe tudo.
3. Menu **Tools > Mist Isles > Kraken > Setup Kraken Assets**.
   Esta ferramenta:
   - configura os FBX (rig Generic, nomes dos clips, loops);
   - cria os materiais URP em `Materials/`;
   - cria os Animator Controllers em `Generated/`;
   - cria os prefabs em `Prefabs/`: `Kraken_Boss`, `Kraken_EruptTentacle`, `Kraken_InkBlob` e `Kraken_InkPuddle`.

   Pode ser executada as vezes que forem precisas.

## 2. Montar a arena

1. Arrastar `Prefabs/Kraken_Boss` para a cena.
   - **O pivot do prefab tem de ficar à altura da superfície da água.** A parte de baixo do corpo fica submersa.
   - Rodar o prefab para ficar virado (eixo Z azul) para a ilha.
   - A linha de costa deve ficar a **cerca de 6 a 8 m** à frente do kraken.
2. Selecionar o kraken para ver os gizmos de alcance:
   - linhas vermelhas: onde caem os slams (4 a 12 m);
   - linha amarela: arco do sweep;
   - esfera verde: zona do grab;
   - esfera ciano: posição do ponto fraco quando está atordoado. Tem de ficar acessível a partir da costa ou da água rasa.
3. Criar zonas de água rasa com **Tools > Mist Isles > Kraken > Create Shallow Water Zone**.
   - Colocar uma ou mais zonas na faixa de água rasa entre o kraken e a ilha, dentro do alcance dos slams.
   - A zona só é testada em planta (XZ). A altura só precisa de cobrir a superfície da água.
4. O jogador tem de ter a tag **Player**. Também é possível arrastá-lo para o campo `player`.

## 3. Ligação ao resto do jogo

| O que é preciso | Como fazer |
|---|---|
| Acordar o boss depois do Cartographer's Card (US08) | Chamar `krakenBoss.Awaken()`. Em alternativa, usar `awakenRadius > 0` ou `awakenOnStart`. |
| Dano ao jogador | Implementar `IKrakenDamageable` no script de vida do jogador, **ou** ter um método `TakeDamage(int)` no jogador (é chamado por `SendMessage`). O evento `onPlayerDamaged` também está disponível. |
| Espada a acertar no ponto fraco | Escolher uma de três opções: (a) dar à espada um collider trigger com a tag `PlayerWeapon` (é preciso criar a tag); (b) no script da espada, `hit.GetComponentInParent<KrakenWeakPoint>()?.TakeDamage(dano)`; (c) `SendMessage("TakeDamage", int)` no collider atingido. |
| Dissipar a névoa ao vencer (US09) | Ligar o evento `onDefeated` ao sistema da névoa ou de conclusão da ilha. |
| Barra de vida | Adicionar `KrakenBossHealthBar` a um Canvas com um `Slider` e arrastar o boss para o campo. |
| VFX, som e camera shake | Eventos `onSlamImpact(Vector3)`, `onRoar`, `onStunned`, `onWeakPointHit`, `onWeakPointBlocked`, `onInkDissolved(Vector3)`, `onPhaseChanged(int)`. |

O grab desativa o `CharacterController` e o `ThirdPersonController` (Starter Assets) enquanto o jogador está preso. Outros componentes podem ser adicionados em `disableOnPlayerWhileGrabbed`.

## 4. Como funciona o combate

**Regra da ilha.** A água rasa protege o jogador e é a fraqueza do guardião.
- Dentro de uma zona de água rasa, o grab falha e a tinta dissolve-se.
- Se um **slam cair dentro de água rasa**, o tentáculo fica preso e o kraken fica **atordoado**: cai para a frente sobre a costa e expõe os **cristais** da cabeça (o ponto fraco).
- O ponto fraco só leva dano enquanto o kraken está atordoado. Fora disso, o golpe é bloqueado (evento `onWeakPointBlocked`).
- O slam continua a magoar o jogador se lhe acertar. A estratégia é atrair o slam para a água e desviar-se.

**Fases (US17)**, com 100 de vida por omissão:

| Fase | Vida | Ataques | Ritmo |
|---|---|---|---|
| 1 | 100–66% | Slam, Sweep, Tinta (sobretudo à distância) | normal |
| 2 | 66–33% | + Grab, Tinta dupla | animações 12% mais rápidas, menos pausa |
| 3 | < 33% | + Tentáculos que irrompem do chão debaixo do jogador, Tinta tripla | 25% mais rápidas, atordoamento mais curto |

Entre fases, o kraken ruge (`Roar`) e fica invulnerável durante a animação.

**Ataques**

- **Slam (L/R):** levanta um tentáculo da frente e bate na costa (4 a 12 m). O kraken orienta-se para o tentáculo cair em cima do jogador.
- **Sweep:** varre a costa da direita para a esquerda a cerca de 1 m de altura. Para escapar, é preciso afastar-se ou saltar.
- **Grab:** agarra o jogador, levanta-o e atira-o para trás na ilha. Falha se o jogador estiver em água rasa.
- **Tinta:** cospe bolas de tinta em arco. Em terra deixam uma poça que causa dano ao longo do tempo. Em água rasa dissolvem-se.
- **Summon (fase 3):** mergulha e faz surgir tentáculos debaixo do jogador, com aviso visual (anéis de espuma) 1 s antes. Enquanto o jogador está em água rasa, os tentáculos atacam a última posição em terra.

Todos os valores (dano, tempos, pesos dos ataques por fase, raios) estão expostos no Inspector do `KrakenBoss`.
As regras da água podem ser desligadas individualmente (`waterProtectsFromGrabAndInk`, `slamIntoWaterStuns`, `weakPointOnlyWhenStunned`, `eruptAvoidsPlayerInWater`).

Para testar sem combate: botão direito no componente `KrakenBoss` > **Debug: Force Stun** ou **Debug: Kill** (em Play Mode).

## 5. Animações (30 fps, rig Generic)

| Clip | Duração | Loop | Uso |
|---|---|---|---|
| Hidden | 1.0 s | sim | submerso, antes do encontro |
| Emerge | 4.0 s | | entrada com rugido |
| Idle | 4.0 s | sim | |
| Roar | 3.0 s | | mudança de fase |
| Attack_Slam_L / Attack_Slam_R | 2.4 s | | impacto aos 1.12 s |
| Attack_Sweep | 2.8 s | | ativo 0.72–1.55 s |
| Attack_Grab | 3.4 s | | agarra aos 1.15 s, larga aos 2.68 s |
| Attack_Ink | 2.6 s | | disparos aos 1.0, 1.25 e 1.5 s |
| Attack_Summon | 5.0 s | | submerso 0.8–4.0 s |
| Stunned_Enter / Stunned_Loop / Stunned_Hit / Stunned_Exit | 1.4 / 2.0 / 0.6 / 1.3 s | Loop: sim | |
| Death | 6.0 s | | afunda e os cristais desaparecem |

Os tempos dos eventos estão em `Scripts/KrakenAnimData.cs`, que é gerado automaticamente a partir do Blender.

## 6. Ficheiros

- `Models/`: FBX do kraken (73 ossos, cerca de 2.6k vértices), do tentáculo que irrompe e dos props (tinta, poça, anel de aviso).
- `Scripts/`: runtime (`KrakenBoss`, `KrakenWaterZone`, `KrakenWeakPoint`, `KrakenInkBlob`, `KrakenInkPuddle`, `KrakenEruptTentacle`, `KrakenBossHealthBar`, `KrakenCore`, `KrakenAnimData`).
- `Scripts/Editor/KrakenSetup.cs`: ferramenta de setup.
- `Source~/`: o `.blend` e os scripts Python que geram o modelo e as animações. O Unity ignora pastas terminadas em `~`.

### Regenerar o modelo (opcional, precisa de Blender 4.4+ ou do módulo `bpy`)

```
python build.py <pasta_saida>          # modelo, rig, 15 animações, Kraken.fbx, Kraken.blend
python build_props.py <pasta_saida>    # tentáculo que irrompe e props
python gen_csharp.py <pasta_saida> ../../Scripts/KrakenAnimData.cs
```

As poses estão em `kanim.py`, onde cada clip é uma função do tempo. As cores estão em `kmodel.py` (`PALETTE`) e em `KrakenSetup.cs` (`Palette`).

## 7. Notas e limitações

- Os scripts foram compilados contra stubs da API do Unity, mas **ainda não foram testados dentro do Unity**. Na primeira execução convém confirmar que:
  - o kraken fica virado para +Z;
  - a esfera ciano do gizmo coincide com os cristais durante o `Stunned_Loop`.
- Os acertos dos tentáculos são calculados por geometria (segmentos dos ossos contra a cápsula do jogador). Não são precisos colliders nem layers.
- Se o terreno da costa estiver muito acima da água (mais de 1 m), o slam pode passar por dentro do chão. Nesse caso, subir o kraken alguns centímetros ou baixar a costa junto à água.
