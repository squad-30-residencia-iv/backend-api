# 💾 Fechamento de Sessão (Save Session)

Você deve consolidar e persistir o estado da sessão de desenvolvimento.

1. Se a demanda foi finalizada e os testes passaram com sucesso:
   - Mova a spec de `docs/specs/in-progress/` para `docs/specs/completed/`.
   - Altere o cabeçalho da spec de `Status: In Progress` para `Status: Completed`.
2. Atualize `docs/PROJECT_STATE.md`:
   - Mova a tarefa concluída para a seção "Últimas Entregas Concluídas".
   - Atualize "Tarefas em Andamento" com os próximos passos lógicos.
   - Registre eventuais decisões técnicas tomadas.
3. Gere uma sugestão de mensagem de commit semântico seguindo Conventional Commits (ex: `feat(api): adiciona endpoint X`).
