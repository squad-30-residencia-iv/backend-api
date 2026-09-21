# 📋 Especificação Técnica: [ID-US] - [Nome da Funcionalidade]

> Status: In Progress
> História de Usuário Relacionada: [Link ou ID da Issue]
> Responsável: @usuario
> Data: AAAA-MM-DD

---

## 1. Contexto & Regras de Negócio

- Objetivo da funcionalidade no backend.
- Validações de entrada e regras de domínio necessárias.
- Tratamento de exceções esperado.

---

## 2. Contratos & Schemas (DTOs / Records)

Definição dos contratos C# (Request e Response):

```csharp
public record ExampleRequest(string Name, bool IsActive);
public record ExampleResponse(Guid Id, string Name, bool IsActive);
```

---

## 3. Especificação do Endpoint / Serviço

- Rota/Assinatura: [MÉTODO] /api/v1/recurso
- Autenticação / Autorização: Obrigatória / Pública / Roles
- Respostas HTTP esperadas:
  - 200/201: Sucesso (Payload retornado)
  - 400: Validação inválida (ProblemDetails RFC 7807)
  - 404: Recurso não encontrado
  - 500: Erro interno não tratado

---

## 4. Checklist de Implementação

- [ ] 1. Criar DTOs/Records de entrada e saída
- [ ] 2. Implementar validações (ex: FluentValidation ou DataAnnotations)
- [ ] 3. Criar casos de uso/serviços de domínio
- [ ] 4. Criar controller / minimal API endpoint
- [ ] 5. Escrever testes unitários e de integração
- [ ] 6. Validar localmente via `dotnet test`

---

## 5. Casos de Teste Essenciais

- [ ] Cenário Feliz: Retorna 200/201 com dados válidos persistidos/processados.
- [ ] Cenário de Validação: Retorna 400 ao enviar dados incompletos/inválidos.
- [ ] Cenário de Exceção: Trata falhas e retorna status code adequado.
