# 🛠️ Guia de Desenvolvimento: Fluxo de Trabalho e SDD (.NET)

Este repositório adota a metodologia **Spec-Driven Development (SDD)** combinada com governança assistida por IA. O objetivo é garantir rastreabilidade, padronização de contratos de API e integridade técnica antes de qualquer código de produção ser escrito.

---

## 1. Setup Inicial Obrigatório (Configuração de Hooks)

Por questões de segurança padrão do Git, automações locais de hooks não são ativadas sozinhas após o clone.

Logo após clonar o repositório, abra o terminal na raiz do projeto e execute uma única vez:

```bash
git config core.hooksPath scripts/hooks
```

> **Por que isso é necessário?**  
> Esse comando vincula o script de `pre-push` do repositório ao seu Git local. Ele assegura que ninguém envie código se a suíte de testes falhar (`dotnet test`) ou se o relatório de estado (`docs/PROJECT_STATE.md`) não tiver sido atualizado.

---

## 2. Entendendo o Ciclo de Vida do SDD

Nenhum endpoint, serviço ou entidade de banco deve ser criado sem uma especificação prévia. O ciclo de trabalho é composto por três etapas:

1. **Início (`resume-session`):** Resgate o estado atual em `docs/PROJECT_STATE.md`, defina o escopo da tarefa e gere a especificação técnica em `docs/specs/in-progress/US-[ID]-[nome].md` usando o template oficial.
2. **Implementação (`implement-spec`):** O código é implementado respeitando a ordem: Contratos (DTOs/Records) $\rightarrow$ Validações/Regras de Domínio $\rightarrow$ Endpoints/Controllers $\rightarrow$ Testes unitários/integração.
3. **Fechamento (`save-session`):** Após validação via `dotnet test`, a especificação é movida para `docs/specs/completed/`, o arquivo `docs/PROJECT_STATE.md` é atualizado e um commit semântico é gerado.

---

## 3. Como Operar o Fluxo no Seu Editor

### Opção A: Utilizando o Cursor (Recomendado)

O Cursor já possui regras modulares integradas em `.cursor/rules/`. Você só precisa invocar os prompts no chat da IDE:

1. **Iniciar uma tarefa:**  
   Abra o chat (`Ctrl + L` ou `Cmd + L`) e envie:
   ```text
   @resume-session.md Quero implementar a história [ID-US] com o objetivo de [descrever brevemente o escopo].
   ```
2. **Implementar o código:**  
   Com a especificação aprovada em `docs/specs/in-progress/`, envie:
   ```text
   @implement-spec.md
   ```
3. **Encerrar e consolidar a memória:**  
   Após os testes passarem, envie:
   ```text
   @save-session.md
   ```

---

### Opção B: Utilizando VS Code, Visual Studio, JetBrains Rider ou Outras IAs

Se você utiliza outro editor ou assistentes externos (Claude Web, ChatGPT, GitHub Copilot, Gemini):

1. **Início da Sessão:**
   - Abra o arquivo `docs/prompts/resume-session.md`.
   - Copie o conteúdo e envie ao seu assistente de IA junto com o conteúdo de `docs/PROJECT_STATE.md`.
   - Solicite a criação da especificação técnica baseada em `docs/specs/templates/spec-template.md`.
   - Salve o arquivo gerado dentro de `docs/specs/in-progress/US-[ID]-[nome].md`.

2. **Implementação:**
   - Abra o arquivo `docs/prompts/implement-spec.md` e envie o conteúdo da sua spec ativa ao assistente de IA.
   - Implemente o código C# respeitando o checklist da spec.
   - Execute a suíte de testes no terminal:
     ```bash
     dotnet test --verbosity normal
     ```
   - Só avance quando todos os testes passarem sem erros.

3. **Fechamento da Sessão:**
   - Abra o arquivo `docs/prompts/save-session.md`.
   - Mova a spec de `docs/specs/in-progress/` para `docs/specs/completed/` e altere o cabeçalho para `Status: Completed`.
   - Abra `docs/PROJECT_STATE.md`, adicione a entrega em **Últimas Entregas Concluídas** e atualize as **Tarefas em Andamento**.
   - Crie o commit semântico e envie:
     ```bash
     git add .
     git commit -m "feat(api): implementa contratos e endpoints da US-[ID]"
     git push origin [sua-branch]
     ```

---

## 4. Travas e Boas Práticas

- **Não use `--no-verify` no push:** Se o push for bloqueado, verifique a saída do terminal. Isso indica que os testes falharam ou que o arquivo `docs/PROJECT_STATE.md` não foi modificado nesta ramificação.
- **Padronização de Quebras de Linha (`LF`):** Arquivos de scripts em `scripts/` e arquivos de governança devem ser salvos com terminação Unix (`LF`) para evitar falhas de execução no Git e em containers Docker.
