# 🤖 Project Agent Guide (agents.md)

Este documento define as instruções universais para assistentes de Inteligência Artificial (Cursor, GitHub Copilot, Claude, ChatGPT, etc.) que atuarem neste repositório.

---

## 🏛️ 1. Contexto e Diretrizes Arquiteturais

- **Tecnologia Principal:** C# / .NET (Web API).
- **Metodologia:** Spec-Driven Development (SDD).
- **Premissa Central:** Nenhum código de produção deve ser gerado sem uma especificação prévia aprovada em `docs/specs/in-progress/`.
- **Determinismo Técnico:** Todo código submetido precisa compilar sem warnings e passar na suíte de testes (`dotnet test`).
- **Padrões de Código:**
  - Uso de Records para DTOs e contratos de entrada/saída.
  - Injeção de dependência via interfaces.
  - Validações centralizadas e respostas de erro estruturadas.

---

## 🔄 2. Ciclo Operacional SDD

Qualquer assistente que iniciar uma sessão de desenvolvimento deve seguir estritamente este fluxo:

1. **Início da Sessão (`resume-session`):**
   - Ler `docs/PROJECT_STATE.md` para entender as pendências e o foco atual.
   - Criar a especificação técnica em `docs/specs/in-progress/US-[ID]-[nome].md` usando o modelo em `docs/specs/templates/spec-template.md`.
   - **Proibição:** Não implementar código de produção nesta etapa; foque exclusivamente em contratos, regras e checklist.

2. **Implementação da Spec (`implement-spec`):**
   - Seguir rigorosamente o checklist da spec ativa.
   - Ordem de implementação: Contratos/DTOs $\rightarrow$ Regras de Domínio $\rightarrow$ Endpoints/Controllers $\rightarrow$ Testes unitários/integração.
   - Rodar/validar o comando `dotnet test`.

3. **Fechamento e Memória (`save-session`):**
   - Mover o arquivo de especificação para `docs/specs/completed/`.
   - Atualizar o status interno para `Status: Completed`.
   - Atualizar `docs/PROJECT_STATE.md` (concluídos e pendências).
   - Propor mensagem semântica de commit (ex.: `feat(api): ...`).

---

## 🧰 3. Catálogo de Skills (Área Extensível para o Squad 2)

> **Nota para o Squad 2:** Adicionem abaixo instruções pontuais e receitas técnicas para que qualquer IA saiba executar comandos e rotinas do projeto com precisão.

### [Skill] Execução de Testes e Validação

- Para verificar integridade antes de qualquer push:
  ```bash
  dotnet test --verbosity normal
  ```
