# Contratos publicados pelo Geo

## O que há em `contracts/`

| Arquivo | Conteúdo |
|---|---|
| `openapi.geo.json` | Contrato REST do Geo (OpenAPI), gerado pela API |
| `codigos-de-erro.json` | Lista dos códigos de erro que o Geo emite, com status HTTP e título |
| `descricoes-de-erros.json` | Descrição pública de cada código de erro `uniplus.geo.*` |

Nenhum desses arquivos é editado à mão, exceto as descrições, que são escritas por pessoas.

## Códigos de erro

- **Formato:** `versaoDoFormato` 1 e a lista `codigos`, ordenada por código, cada item com `codigo`, `status` e `titulo`.
- **De onde vem:** é gerado pelo teste `CodigosDeErroPublicados`, que reúne os códigos registrados no
  host do Geo (consultas, importação, fronteira HTTP, paginação e demais registros). Código novo, removido, ou
  status e título alterados reprovam o teste até a aprovação.
- **Quem é dono:** o Geo publica todos os códigos que emite. Ele descreve só os `uniplus.geo.*`; os códigos
  comuns (`uniplus.validacao`, `uniplus.auth.*`, `uniplus.cursor.*` e semelhantes) são descritos pela API do
  Uni+, e o portal confere a correspondência entre os dois repositórios.

## Descrições públicas

Cada código `uniplus.geo.*` publicado tem uma entrada em `descricoes-de-erros.json`:

- **Formato:** `versaoDoFormato` 1 e a lista `descricoes`, ordenada por código.
- **Seções obrigatórias:** `oQueAconteceu` e `comoResolver`, com pelo menos um bloco de texto cada.
  `ondeAparece` é opcional, mas não pode ter bloco vazio.
- **Requisitos:** a lista `requisitos` é obrigatória (pode ser vazia) e cada item usa o formato
  `UNI-REQ-NNNN`, de requisitos que existam no portal.
- **Linguagem:** escreva para quem recebe o erro, em português claro, sem termos internos de implementação.
- **Sem dado pessoal:** nada de CPF, e-mail ou sequência de 11 ou mais algarismos, nem em exemplos.
- **Links:** só endereços HTTPS ou caminhos absolutos do portal (por exemplo, `/erros/<código>`).
- **Conferência:** o teste `DescricoesDeErrosPublicadas` confere os pares código e descrição nos dois sentidos,
  as seções, os requisitos e o texto. O Markdown e os links são conferidos pelo portal ao sincronizar.

Regra de trabalho: código de erro novo exige a descrição dele no mesmo PR.

## Como aprovar

```bash
dotnet test tests/Unifesspa.Geo.IntegrationTests/Unifesspa.Geo.IntegrationTests.csproj \
  --filter "CodigosDeErroPublicados|DescricoesDeErrosPublicadas|SpecRuntime"
```

Quando o resultado difere do arquivo versionado, o teste falha sem alterá-lo e informa:

- as divergências em linhas curtas (código novo, removido ou alterado);
- o caminho do arquivo obtido, numa pasta própria da execução, sob o diretório temporário (`unifesspa-geo-resultados/`);
- o comando `cp`, para executar na raiz do repositório depois de revisar a diferença
  (`git diff --no-index <versionado> <obtido>`).

Depois do `cp`, rode o teste de novo, com compilação, para atualizar a cópia na saída. As descrições são
escritas por pessoas: o teste nunca reescreve o texto delas: aponta a ordem e a formatação fora do padrão e as regras de conteúdo
que o texto não cumpre (pares, seções, requisitos, dado pessoal).
