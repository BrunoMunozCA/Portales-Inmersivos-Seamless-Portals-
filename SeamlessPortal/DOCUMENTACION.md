# Documentación: portales y cambio de gravedad

## 1. Cómo funciona un portal

Cada portal está enlazado con otro. Lo que ves por el portal A es lo que verías desde una cámara situada en el portal B, y viceversa.

**Convención:**

- El lado visible de un portal es su **-Z**.
- Se entra caminando hacia **+Z** y se sale por el lado **-Z** del portal enlazado.
- Por eso el eje Z azul de cada portal apunta hacia dentro de la pared.

### 1.1 La visión

1. `PortalManager` coloca una cámara virtual en la pose que tendría el jugador "al otro lado" del portal.
2. Esa cámara renderiza la escena a una `RenderTexture`.
3. La pantalla del portal (`Screen`) muestra esa textura usando la **posición en pantalla** de cada píxel, no los UV del cubo. Así la imagen encaja con el resto del mundo.
4. La cámara virtual usa un plano de recorte oblicuo, para que no se vea lo que hay entre ella y el portal de salida.

La pantalla se estira y desplaza cada frame para que el near plane de la cámara nunca la atraviese (anti-clipping).

### 1.2 La recursión

Un portal puede verse a sí mismo o a otros portales. Para resolverlo, `PortalManager` renderiza un **árbol de vistas**:

- Cada nodo es "ver el portal P desde esta cámara".
- Primero se renderizan los niveles más profundos y después los superficiales, porque cada nivel necesita la imagen del siguiente.
- Parámetros:
  - `Recursion Limit`: cuántos niveles se renderizan.
  - `Max Renders Per Frame`: presupuesto total de renders. Se reparte entre los portales visibles.
  - `Resolution Scale`: baja la resolución de las texturas para ganar rendimiento.

Si un portal no tiene presupuesto, usa la imagen del frame anterior en vez de verse negro.

### 1.3 El teletransporte

`Portal` guarda, por cada objeto dentro de su trigger, de qué lado del plano estaba. Cuando pasa del lado -Z al +Z:

1. Se calcula la nueva posición y rotación con la matriz de un portal al otro:
   `destino * Flip * origen⁻¹ * objeto`
   (`Flip` es un giro de 180° en Y, porque se sale por la cara contraria).
2. La velocidad y la velocidad angular se rotan con la misma rotación.
3. El objeto pasa a ser responsabilidad del portal de salida.

### 1.4 Objetos que cruzan a medias

Mientras un objeto está dentro del trigger:

- **Clon visual:** se instancia una copia del hijo `Graphics` en la pose transformada.
- **Corte:** el shader `SlicedLit` descarta los fragmentos que quedan pasado el plano del portal (`dot(posición - centro, normal) > 0`). El original se corta con el portal de entrada y el clon con el de salida.
- **Clon físico:** un objeto invisible con Rigidbody **kinematic** y copias de los colliders sigue al original en el otro lado, para empujar lo que encuentre allí.

### 1.5 Tamaño

El campo `Size` del portal (ancho × alto en metros) ajusta a la vez la pantalla, el trigger y el cálculo de visibilidad. `Trigger Depth` y `Trigger Padding` controlan el grosor y el margen del trigger.

---

## 2. Cómo funciona el cambio de gravedad

### 2.1 La idea

Al teletransportarse, un objeto sufre una **rotación** entre el espacio del portal de entrada y el del de salida. Esa misma rotación se aplica al vector de gravedad.

```
delta = rotación(salida) * Flip * rotación(entrada)⁻¹
nuevaGravedad = delta * gravedadActual
```

Si el portal de salida está inclinado, `delta` incluye esa inclinación y el "abajo" del objeto cambia con ella.

### 2.2 Qué hace cada script

- **`PortalTraveller.Teleport`** calcula `delta`, la aplica a la velocidad y llama a `CustomGravity.RotateGravity(delta)`.
- **`CustomGravity`** aplica cada `FixedUpdate` una fuerza en la dirección `down` (`Use Gravity` del Rigidbody queda desactivado).
- Si `Align Rotation` está marcado (jugador), el cuerpo se **endereza** hasta que su "arriba" coincide con `-down`, con una interpolación suave (`Align Speed`).
- Si está desmarcado (cajas, pelotas), el objeto no se endereza: solo rota su vector de gravedad.
- **`PlayerMovement`** mueve y gira al jugador respecto a su propio "arriba" (`transform.up`), así que funciona con cualquier gravedad.

### 2.3 Cómo inclinar la gravedad

Gira el portal de salida sobre su eje **Z** (roll):

| Roll del portal de salida (Z) | Efecto en el jugador |
|---|---|
| 0° | La gravedad no cambia. |
| 90° | El suelo pasa a ser una pared. |
| 180° | La gravedad se invierte (caminas por el techo). |

Dos portales con el mismo roll no cambian la gravedad: lo que importa es la **diferencia** entre la rotación de entrada y la de salida.

### 2.4 Detalles a tener en cuenta

- El jugador debe usar **Rigidbody + CapsuleCollider**. `CharacterController` no sirve porque su cápsula está siempre alineada con el eje Y del mundo.
- Con la gravedad inclinada, el "arriba" del portal será horizontal, así que diseña los niveles pensando en la orientación final.
- Los objetos que cruzan también cambian de gravedad: una caja que sale por un portal inclinado "cae" hacia la nueva dirección.

---

## 3. Referencia de ajustes

### Portal
| Campo | Descripción |
|---|---|
| Linked Portal | Portal de destino. El enlace debe ser en las dos direcciones. |
| Screen | Cubo hijo con el material `PortalScreen`. |
| Size | Ancho y alto del hueco. |
| Trigger Depth / Padding | Grosor total y margen del trigger. |

### PortalManager
| Campo | Descripción |
|---|---|
| Portal Cam | Cámara compartida. Si está vacía, se crea sola. |
| Recursion Limit | Niveles de recursión. |
| Resolution Scale | Escala de resolución de las texturas. |
| Max Renders Per Frame | Presupuesto de renders por frame. |
| Far Clip Multiplier | Amplía el far plane de la cámara del portal. |
| Hdr Textures | Evita pantallas más oscuras que el mundo real. |
| Use Last Frame Fallback | Usa la imagen anterior cuando no hay presupuesto. |

### CustomGravity
| Campo | Descripción |
|---|---|
| Gravity | Intensidad de la gravedad. |
| Down | Dirección actual (cambia sola al cruzar un portal). |
| Align Speed | Velocidad con la que el jugador se endereza. |
| Align Rotation | Marcado en el jugador, desmarcado en los objetos. |

---

## 4. Problemas frecuentes

| Problema | Causa probable |
|---|---|
| Pantalla gris o negra | En el Shader Graph, la propiedad no está en **Scope = Per Material** o su **Reference** no es `_MainTex`. |
| No puedo volver a entrar por el portal de salida | El enlace solo va en una dirección o el portal está orientado al revés. |
| Salgo "al revés" | Gira el portal de salida 180° en Y. |
| El jugador no cruza | El trigger es más delgado que el hueco, o falta `PortalTraveller`. |
| Imagen del portal retrasada | `PortalManager` debe ejecutarse después del movimiento del jugador (ya lleva `DefaultExecutionOrder(1000)`). |
| El portal se ve negro a distancia | Sube `Far Clip Multiplier` y comprueba el `Far` de la cámara principal. |
| Los objetos no se cortan | `_SliceCenter` y `_SliceNormal` deben estar en **Scope = Per Material** en `SlicedLit`. |
