# Prompts de IA (IA-0)

Cada función de IA tiene sus prompts versionados como archivos Markdown **en el contexto dueño de la función**:

```
<Contexto>/Infrastructure/Ai/Prompts/<feature>@<n>.md
```

- `<feature>`: nombre de la función en kebab-case (`weekly-summary`, `meal-ideas`, `suggested-questions`,
  `practitioner-monitoring-summary`, `diagnosis-suggestion`, `guideline-suggestions`, `plan-adjustment-proposal`,
  `meal-photo-recognition`).
- `<n>`: versión entera. Se usa siempre la más alta; las anteriores **no se borran ni se editan** (son la traza de
  `ai_generations.prompt_version`). Cambiar un prompt = archivo nuevo con `n + 1`.
- El csproj los incrusta en el ensamblado (`AiPrompts/<archivo>`); `PromptCatalog` los lee sin referenciar ningún
  contexto. Este README no sigue el patrón y no se carga.

Cada archivo declara, en este orden: rol, reglas éticas (tono de invitación y nunca acusación, sin diagnóstico en
textos para el paciente, nunca modificar el plan), el idioma (`{{language}}` se reemplaza por `es` o `en`) y, al
final, el schema de salida en un bloque así (se quita del texto antes de enviarlo y se manda como
`responseJsonSchema`):

````markdown
## Output schema

```json
{ "type": "object", "properties": { "...": {} }, "required": ["..."], "additionalProperties": false }
```
````

El input que llega al modelo ya está seudonimizado (sin nombres, email ni ids reales); el prompt no debe pedir datos
identificativos. La única imagen que se envía es la foto del plato de IN-7 (`meal-photo-recognition`), sin metadatos
(EXIF/XMP/ICC) y sin ningún dato del paciente; nunca se guarda (en `ai_generations` solo queda su SHA-256).
