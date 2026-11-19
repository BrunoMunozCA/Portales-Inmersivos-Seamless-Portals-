# Seamless Portals con gravedad dinámica

Portales sin cortes para **Unity 6 (URP)**. El jugador atraviesa un portal sin ver ningún salto y su
**gravedad cambia** según la inclinación del portal de salida.

## Qué incluye

- Portales que se ven a sí mismos y entre ellos (recursión cruzada).
- Teletransporte sin saltos: posición, rotación y velocidad se transforman entre portales.
- **Gravedad que gira** con el portal: si sales por un portal inclinado, tu "abajo" cambia.
- Objetos que cruzan a medias: se ven cortados por el plano del portal y tienen un clon con colliders al otro lado.
- Tamaño del portal configurable (`Size`).

## Requisitos

- Unity 6 con **URP**.
- Para `PlayerMovement`: Input System nuevo o antiguo (ambos funcionan).
- Dos Shader Graphs hechos a mano: `PortalScreen` y `SlicedLit`.

## Scripts

| Script | Función |
|---|---|
| `Portal.cs` | Un extremo del par de portales: detecta objetos que cruzan y ajusta el tamaño. |
| `PortalManager.cs` | Renderiza lo que se ve a través de cada portal. Solo uno por escena. |
| `PortalTraveller.cs` | Lo llevan los objetos que cruzan: teletransporte, clon visual y clon físico. |
| `CustomGravity.cs` | Gravedad en cualquier dirección. |
| `PlayerMovement.cs` | Movimiento en primera persona relativo al "arriba" actual. |

## Montaje rápido

1. Crea dos objetos `Portal`, cada uno con un `BoxCollider` y el script `Portal`.
2. A cada uno añádele un hijo `Screen` (un cubo **sin collider**) con el material del shader `PortalScreen`.
3. Enlaza los portales entre sí: `Linked Portal` de A apunta a B, y el de B a A.
4. Pon un objeto vacío con `PortalManager` en la escena.
5. Crea el jugador: `Rigidbody` (Use Gravity **off**, Interpolation **None**), `CapsuleCollider`, `PortalTraveller`, `CustomGravity` y `PlayerMovement`. La cámara es hija del jugador y tiene el tag **MainCamera**.

## Reglas importantes

- El eje **Z azul** de cada portal apunta **hacia dentro de la pared**.
- Se entra caminando hacia +Z y se sale por el lado -Z del otro portal.
- La **escala del portal debe ser (1,1,1)**. Para cambiar su tamaño se usa el campo `Size`.
- El hueco de la pared debe ser real y medir lo mismo que `Size`.
- El jugador usa **Rigidbody**, no `CharacterController`.

## Cambiar la gravedad

Gira el portal de salida sobre su eje **Z** (roll). Con un roll de 90°, el suelo del jugador pasa a ser una pared.
Más detalles en `DOCUMENTACION.md`.

## Limitaciones

- Los portales deben tener escala 1.
- El clon físico solo copia colliders Box, Sphere, Capsule y Mesh.
- Cada nivel de recursión es un render extra; hay un límite configurable.
- El shader de corte (`SlicedLit`) afecta solo a los objetos que cruzan portales.
