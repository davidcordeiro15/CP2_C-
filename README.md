# ExpenseHub — API de reembolsos

API REST desenvolvida para o Checkpoint 2 de C#. Permite cadastrar despesas, enviar solicitações de reembolso, aprovar ou reprovar pedidos, registrar pagamentos simulados e consultar o histórico das operações.

O acesso combina autenticação JWT, roles, propriedade da despesa e estado atual. Os dados são persistidos em Oracle por meio do Entity Framework Core.

## Tecnologias

| Componente | Tecnologia |
|---|---|
| Plataforma | .NET 10 / ASP.NET Core, Minimal APIs |
| Persistência | Entity Framework Core e Oracle Database, compatibilidade SQL 19c |
| Provider | `Oracle.EntityFrameworkCore` 10.23.26301 |
| Usuários e roles | ASP.NET Core Identity 10.0.12 |
| Autenticação | JWT Bearer 10.0.12 |
| Ferramenta de migrations | `dotnet-ef` 10.0.12, manifesto local |
| Testes | MSTest 4.0.2 |
| Contrato da API | OpenAPI em Development |
| Qualidade | GitHub Actions, analisadores .NET e Gitleaks |

## Estrutura

```text
sources/
  ExpenseHub.slnx
  ExpenseHub.Api/
    Auth/              # Login e emissão de tokens
    Configuration/     # Opções de JWT e seed
    Data/              # DbContext, factory e migrations
    Expenses/          # Serviços, regras, DTOs e repositório
    Initialization/    # Seed do administrador e roles
    Models/            # Entidades de domínio e usuário
    Security/          # Roles e policies
    Users/             # Cadastro e gerenciamento de roles
    Program.cs         # Configuração e endpoints
  ExpenseHub.UnitTests/
docs/                  # Requisitos, rubrica e matriz de autorização
scripts/               # Análise oficial de qualidade
tests/                 # Testes dos scripts de qualidade
```

## Pré-requisitos

- SDK .NET 10.
- Git.
- Acesso a um Oracle Database compatível com a configuração SQL 19c e permissão para criar os objetos da aplicação.
- Postman, cliente HTTP do Visual Studio ou outra ferramenta para enviar requisições.
- Para executar a qualidade local: PowerShell 7+ e Gitleaks disponível no `PATH` (o CI utiliza 8.30.1).

O banco é necessário para executar a API. Os testes unitários executam sem banco, rede ou serviço externo. Não há frontend ou deploy obrigatório.

## Executar localmente

Os comandos abaixo usam PowerShell e devem ser executados na raiz do repositório.

### 1. Clonar e restaurar

```powershell
git clone https://github.com/davidcordeiro15/CP2_C-.git
cd CP2_C-
dotnet tool restore
dotnet restore .\sources\ExpenseHub.slnx
```

### 2. Configurar os segredos

O projeto já possui `UserSecretsId`; não é necessário executar `dotnet user-secrets init`.

Substitua os placeholders pelos valores do seu ambiente. As credenciais não são fornecidas pelo Git e não devem ser adicionadas ao `appsettings.json` versionado.

```powershell
dotnet user-secrets set "ConnectionStrings:Oracle" 'User Id=<USUARIO>;Password=<SENHA>;Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=<HOST>)(PORT=1521))(CONNECT_DATA=(SID=<SID>)));' --project .\sources\ExpenseHub.Api

dotnet user-secrets set "SeedAdmin:Enabled" "true" --project .\sources\ExpenseHub.Api
dotnet user-secrets set "SeedAdmin:Email" '<EMAIL-ADMIN>' --project .\sources\ExpenseHub.Api
dotnet user-secrets set "SeedAdmin:Password" '<SENHA-FORTE-ADMIN>' --project .\sources\ExpenseHub.Api
```

O descriptor acima usa SID. Se o banco utilizar service name, ajuste `CONNECT_DATA` conforme a configuração fornecida pelo administrador do banco. No ambiente acadêmico utilizado, o host é `oracle.fiap.com.br`, a porta é `1521` e o SID é `orcl`; cada pessoa deve configurar suas próprias credenciais e verificar a disponibilidade de acesso.

Gere uma chave JWT aleatória localmente e grave-a sem inserir um literal no código:

```powershell
$jwtBytes = New-Object byte[] 32
$jwtRng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$jwtRng.GetBytes($jwtBytes)
$jwtRng.Dispose()
$jwtKey = [Convert]::ToBase64String($jwtBytes)
dotnet user-secrets set "Jwt:SigningKey" $jwtKey --project .\sources\ExpenseHub.Api
Remove-Variable jwtBytes, jwtRng, jwtKey
```

A chave deve possuir pelo menos 32 caracteres. A senha do Admin deve atender à política do Identity: pelo menos seis caracteres, com maiúscula, minúscula, número e caractere especial; utilize uma senha forte com pelo menos 12 caracteres.

| Configuração | Finalidade |
|---|---|
| `ConnectionStrings:Oracle` | Conexão com o banco |
| `Jwt:SigningKey` | Assinatura dos tokens |
| `Jwt:Issuer` | Emissor; padrão `ExpenseHub` |
| `Jwt:Audience` | Destinatário; padrão `ExpenseHub` |
| `Jwt:ExpirationMinutes` | Validade; padrão 60 minutos |
| `SeedAdmin:Enabled` | Habilita explicitamente o seed de Admin e roles |
| `SeedAdmin:Email` | E-mail da conta Admin inicial |
| `SeedAdmin:Password` | Senha utilizada ao criar a conta Admin |

Alternativamente, utilize variáveis de ambiente com `__` no lugar de `:`, como `ConnectionStrings__Oracle`, `Jwt__SigningKey` e `SeedAdmin__Enabled`. User Secrets é uma configuração local de desenvolvimento e não acompanha o clone.

### 3. Criar ou atualizar o banco

Confira a conexão antes de aplicar migrations. Prefira um schema destinado ao ExpenseHub: tabelas Identity possuem nomes comuns a outros projetos.

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet ef database update --project .\sources\ExpenseHub.Api --startup-project .\sources\ExpenseHub.Api
```

A aplicação não aplica migrations automaticamente no startup. A migration inicial cria as tabelas Identity e as entidades `Expense`, `ExpenseCategory`, `ExpenseHistory` e `PaymentRecord`.

Runtime e factory de design-time utilizam `OracleSQLCompatibility.DatabaseVersion19`. Os booleanos do Identity são armazenados como `NUMBER(1)`; valores monetários usam `NUMBER(19,2)`.

### 4. Iniciar a API

```powershell
dotnet run --project .\sources\ExpenseHub.Api
```

O perfil local utiliza `http://localhost:5245` em Development. Confira a URL efetiva na mensagem `Now listening on:`.

- Saúde da aplicação: `GET http://localhost:5245/health`.
- Documento OpenAPI em Development: `GET http://localhost:5245/openapi/v1.json`.

O projeto expõe o documento OpenAPI; não há interface Swagger UI configurada.

### Seeds

No startup são criadas, se ausentes, as categorias `Meals`, `Travel`, `Office`, `Entertainment` e `Health`.

Com `SeedAdmin:Enabled=true`, são criadas as cinco roles e somente a conta Admin inicial. Os demais usuários devem ser cadastrados por `POST /register`.

O seed não redefine a senha de um Admin já existente. O e-mail configurado não pode pertencer a uma conta sem Admin, e a conta inicial deve permanecer exclusivamente Admin enquanto o seed estiver habilitado. Para testar roles acumuladas, utilize outros usuários.

## Perfis e autorização

| Role | Leitura | Escrita |
|---|---|---|
| `Employee` | Próprias despesas em qualquer estado | Criar, editar e enviar seus rascunhos |
| `Approver` | Despesas `Submitted` | Aprovar ou reprovar despesas enviadas de outro usuário |
| `Finance` | Despesas `Approved` e `Paid` | Pagar despesas aprovadas de outro usuário |
| `Auditor` | Todas as despesas e históricos | Sem escrita concedida por essa role |
| `Admin` | Usuários e roles | Gerenciar roles; sem acesso funcional a despesas por essa role |

Roles acumulam permissões, preservando a proibição de autoaprovação, autorreprovação e autopagamento. Por exemplo, `Auditor + Employee` pode consultar todas as despesas e criar as próprias.

O cadastro não atribui roles automaticamente. Após o Admin alterar as roles, faça novo login para obter um token com as permissões atualizadas. A alteração não modifica as claims dos tokens já emitidos.

## Fluxo de reembolso

| Estado atual | Operação | Próximo estado |
|---|---|---|
| — | Criar | `Draft` |
| `Draft` | Editar | `Draft` |
| `Draft` | Enviar | `Submitted` |
| `Submitted` | Aprovar | `Approved` |
| `Submitted` | Reprovar com justificativa | `Rejected` |
| `Approved` | Pagar | `Paid` |

`Rejected` e `Paid` são finais. Não existem exclusão, cancelamento, reabertura ou reenvio. Repetir uma transição retorna `409`, sem novo histórico ou pagamento.

Cada despesa possui um único valor. Descrição e justificativa devem ter de 10 a 500 caracteres; valor entre R$ 0,01 e R$ 2.147.483.647,00, sem fração inferior a um centavo; data obrigatória e não futura; categoria existente.

Proprietário, estado, ator e horários são definidos pelo servidor. O histórico registra criação, alterações relevantes e transições, com instante UTC, estados e justificativa quando aplicável. A edição sem mudança não gera um registro adicional.

## Endpoints

Nas rotas protegidas, envie `Authorization: Bearer <ACCESS_TOKEN>`. Bodies JSON utilizam `Content-Type: application/json`.

| Método | Rota | Acesso / operação |
|---|---|---|
| GET | `/health` | Público; verificação de execução |
| POST | `/register` | Público; cadastro sem roles |
| POST | `/login` | Público; autenticação |
| GET | `/api/admin/users` | Admin; listar usuários |
| PUT | `/api/admin/users/{id}/roles` | Admin; substituir roles |
| POST | `/api/expenses` | Employee; criar rascunho |
| PUT | `/api/expenses/{id}` | Employee proprietário; editar Draft |
| GET | `/api/expenses` | Role funcional; listar no escopo permitido |
| GET | `/api/expenses/{id}` | Role funcional; consultar despesa visível |
| POST | `/api/expenses/{id}/submit` | Employee proprietário; enviar Draft |
| POST | `/api/expenses/{id}/approve` | Approver não proprietário; aprovar Submitted |
| POST | `/api/expenses/{id}/reject` | Approver não proprietário; reprovar Submitted |
| POST | `/api/expenses/{id}/pay` | Finance não proprietário; pagar Approved |
| GET | `/api/expenses/{id}/history` | Mesma visibilidade da despesa |

### Respostas

| Status | Significado |
|---|---|
| 200 | Operação concluída ou consulta realizada |
| 201 | Usuário ou rascunho criado |
| 400 | Entrada inválida |
| 401 | Token ausente/inválido ou credenciais de login incorretas |
| 403 | Usuário autenticado sem permissão contextual ou role necessária |
| 404 | Recurso inexistente ou fora do escopo permitido |
| 409 | Estado incompatível ou transição repetida |

Erros utilizam `ProblemDetails` ou `ValidationProblem`. O estado atual é serializado como número pelo comportamento padrão dos enums; consulte [ExpenseStatus.cs](sources/ExpenseHub.Api/Models/ExpenseStatus.cs) para os valores.

## Roteiro de validação manual

Use Postman ou outro cliente HTTP. Os exemplos abaixo usam placeholders: substitua credenciais e tokens somente no seu ambiente, sem versioná-los.

### 1. Login do Admin

```http
POST /login
Content-Type: application/json

{
  "email": "<EMAIL-ADMIN>",
  "password": "<SENHA-ADMIN>"
}
```

A resposta contém `accessToken`, `tokenType` e `expiresIn` (segundos). Utilize o token nas operações administrativas.

### 2. Cadastro e atribuição de roles

```http
POST /register
Content-Type: application/json

{
  "email": "employee@example.com",
  "password": "<SENHA-FORTE>"
}
```

Guarde o `id` retornado ou consulte `GET /api/admin/users` com o token Admin. Atribua a role:

```http
PUT /api/admin/users/<USER_ID>/roles
Authorization: Bearer <TOKEN-ADMIN>
Content-Type: application/json

{
  "roles": ["Employee"]
}
```

Essa operação substitui a lista de roles; para acumular permissões, envie a lista completa desejada. O Admin não pode remover sua própria role Admin.

Repita o cadastro para outro Employee, um Approver, um Finance e um Auditor. Atribua as respectivas roles e faça login com cada usuário depois da atribuição.

### 3. Identificar uma categoria existente

Não há endpoint de listagem de categorias nesta implementação. Consulte os IDs no Oracle, por exemplo pelo SQL Developer:

```sql
SELECT "Id", "Name"
FROM "ExpenseCategories"
ORDER BY "Id";
```

Não presuma que os IDs começam em 1 em um banco reutilizado.

### 4. Criar e editar um rascunho

Use o token Employee. Substitua `categoryId` por um ID obtido na consulta e utilize uma data não futura.

```http
POST /api/expenses
Authorization: Bearer <TOKEN-EMPLOYEE>
Content-Type: application/json

{
  "description": "Almoço durante reunião de trabalho",
  "amount": 50.00,
  "expenseDate": "2026-10-01",
  "categoryId": 1
}
```

O esperado é `201`, com o identificador e estado Draft. Para editar, envie os mesmos quatro campos em `PUT /api/expenses/<EXPENSE_ID>` enquanto a despesa estiver em Draft.

### 5. Enviar, aprovar e pagar

Execute em sequência, sem body:

| Usuário | Requisição | Esperado |
|---|---|---|
| Employee proprietário | `POST /api/expenses/<EXPENSE_ID>/submit` | 200; Submitted |
| Approver diferente do proprietário | `POST /api/expenses/<EXPENSE_ID>/approve` | 200; Approved |
| Finance diferente do proprietário | `POST /api/expenses/<EXPENSE_ID>/pay` | 200; Paid |
| Proprietário ou Auditor | `GET /api/expenses/<EXPENSE_ID>/history` | 200; histórico das operações |

### 6. Reprovar uma segunda despesa

Crie e envie outra despesa. Com o token Approver, envie:

```http
POST /api/expenses/<EXPENSE_ID>/reject
Authorization: Bearer <TOKEN-APPROVER>
Content-Type: application/json

{
  "justification": "Despesa fora da política de reembolso da empresa."
}
```

O esperado é `200`, estado Rejected e justificativa no histórico. O Approver isolado deixa de visualizar o recurso depois da decisão, pois sua leitura é restrita a Submitted.

### 7. Casos negativos

- Rota protegida sem token: `401`.
- Admin isolado criando despesa: `403`.
- Employee consultando, editando ou enviando despesa de outro Employee: `404`.
- Proprietário com Approver tentando decidir a própria despesa: `403`.
- Proprietário com Finance tentando pagar a própria despesa: `403`.
- Editar uma despesa já enviada ou repetir envio/decisão/pagamento: `409`.
- Valor `10.555`, data futura ou justificativa nula: `400`.
- Auditor isolado tentando escrever: `403`.
- Admin tentando remover a própria role Admin: `403`.

Confira também persistência após reiniciar a API e ausência de registros duplicados no pagamento e histórico. Testes unitários com fakes não substituem validações reais de HTTP, SQL, concorrência e rollback no Oracle.

## Testes e qualidade

```powershell
dotnet build .\sources\ExpenseHub.slnx
dotnet test .\sources\ExpenseHub.slnx --logger trx --results-directory .\artifacts\test-results
dotnet format .\sources\ExpenseHub.slnx --verify-no-changes
git diff --check
```

A suíte cobre autenticação, seed, gerenciamento de roles, validações, ownership, visibilidade, transições, histórico e pagamento usando fakes em memória. Use o resultado do runner para a contagem atual de casos; métodos parametrizados podem executar vários casos.

Para a análise oficial, com PowerShell 7+ e Gitleaks no PATH:

```powershell
pwsh -File .\scripts\Invoke-CodeQuality.ps1 -Ci
```

O workflow [code-quality](.github/workflows/build.yml) executa no GitHub Actions e publica o artefato `code-quality-report`, com `report.json`, `report.md` e logs. Consulte [Actions](https://github.com/davidcordeiro15/CP2_C-/actions) e as [regras de qualidade](docs/code-quality-rules.md).

Workflow aprovado não significa automaticamente score 100. A pontuação e os findings devem ser conferidos no relatório da execução correspondente ao commit entregue. Resultados de testes e relatórios locais não devem ser versionados.

## Solução de problemas

| Problema | Verificação |
|---|---|
| MSBuild não encontra projeto ao configurar secrets | Execute na raiz usando `--project .\sources\ExpenseHub.Api` |
| Conexão Oracle ausente | Configure `ConnectionStrings:Oracle` em User Secrets ou variável de ambiente |
| ORA-00902 em coluna BOOLEAN | Confira compatibilidade 19 e migrations atualizadas com `NUMBER(1)` |
| Objeto já existe após migration interrompida | Inspecione os objetos e histórico; DDL Oracle pode ficar parcialmente aplicado. Não apague tabelas de outros projetos |
| Seed recusa a senha do Admin | Configure senha com maiúscula, minúscula, número e símbolo |
| Alterar SeedAdmin:Password não muda o login existente | O seed não redefine senha de conta já criada |
| Usuário cadastrado recebe 403 | Atribua role com Admin e faça novo login |
| `/swagger` retorna 404 | Utilize `/openapi/v1.json`; não existe Swagger UI configurada |
| Script falha no operador `??` | Execute com PowerShell 7+, não Windows PowerShell 5.1 |

## Documentação do trabalho

- [Enunciado](docs/ENUNCIADO.md)
- [Requisitos e contratos](docs/REQUISITOS.md)
- [Matriz de autorização](docs/MATRIZ-AUTORIZACAO.md)
- [Rubrica](docs/RUBRICA.md)
- [Processo no GitHub](docs/PROCESSO-GITHUB.md)
- [Uso de IA](docs/USO-DE-IA.md)
- [Regras de qualidade](docs/code-quality-rules.md)

Backlog central: [Racass/checkpoint-csharpracass-expensehub](https://github.com/Racass/checkpoint-csharpracass-expensehub/issues).

Para a entrega, informe a URL pública deste repositório e o SHA final da main, após concluir os merges e conferir o CI correspondente. Credenciais devem ser configuradas separadamente no ambiente de quem executar a aplicação.
