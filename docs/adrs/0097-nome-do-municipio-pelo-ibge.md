---
status: "accepted"
date: "2026-11-26"
decision-makers:
  - "Tech Lead (CTIC)"
consulted: []
informed:
  - "Equipe Uni+"
---

# ADR-0097: Nome do município pelo IBGE

## Contexto e enunciado do problema

O nome do município chega ao Geo pelo dataset da DNE ([ADR-0092](0092-etl-carga-dne-reference-data.md)), mas a DNE e o IBGE não escrevem todos os nomes do mesmo jeito. Em 86 dos 5.571 municípios os nomes divergem: em 47 deles o nome oficial tem apóstrofo e a DNE o tira («Pau DArco» no lugar de «Pau D'Arco»), e nos outros 39 a diferença é de outro tipo («Eldorado dos Carajás» no lugar de «Eldorado do Carajás»).

O Uni+ congela esse nome em documento legal: a base legal do Fator de Atenuação das Desigualdades de Acesso, a versão publicada do processo seletivo e a inscrição guardam o nome que o Geo devolveu. Quem digita o nome do edital na busca também precisa encontrar o município.

## Drivers da decisão

- O nome que vale é o oficial do IBGE, que o edital usa.
- A carga deve continuar repetível: recarregar a mesma versão não pode mudar o resultado.
- A origem do nome deve estar no repositório e ser conferível, sem depender de rede na hora da carga.
- A DNE é proprietária e não é versionada: não dá para corrigi-la no repositório.
- O contrato REST não pode mudar.

## Opções consideradas

- **A**: corrigir a DNE antes da carga, fora do Geo.
- **B**: tabela de correções só com os municípios que divergem.
- **C**: consultar o IBGE no momento da carga.
- **D**: lista completa dos municípios do IBGE versionada no repositório, aplicada por código na carga.

## Resultado da decisão

**Escolhida:** "D — lista completa do IBGE versionada, aplicada por código na carga", porque deixa a origem do nome no repositório, não depende de rede nem de quem restaura a DNE, e cobre os 86 casos de hoje e os que o IBGE vier a criar.

- A carga grava, para cada município, o nome do IBGE correspondente ao código IBGE, com a grafia e a caixa do IBGE («Pau D'Arco»).
- A lista tem todos os 5.571 municípios (código, UF e nome), fica embutida no assembly e é gerada por um comando documentado em `docs/geo-etl-dataset-dne.md`; não se edita à mão.
- Código que não consta na lista não aborta a carga: o município mantém o nome da DNE e o fato é registrado no relatório da carga.
- O `nome_normalizado` do município é derivado do nome gravado, sem acento, caixa nem apóstrofo, e a busca de município aplica a mesma regra ao termo digitado: «pau d'arco», «pau darco» e «PAU D’ARCO» encontram o mesmo município.
- A busca de bairro, distrito, logradouro, grande usuário e complemento não muda: seguem o texto sem acento da DNE, que mantém o apóstrofo (`´` e `` ` ``). As duas formas existem porque as colunas vêm de fontes diferentes.
- A coluna `cidade_sem_acento` da DNE deixa de ser lida.

## Consequências

### Positivas

- O nome devolvido pelo Geo é o do edital, em todos os 5.571 municípios.
- A carga continua idempotente: o nome e o Guid se mantêm na recarga.
- Sem migração, sem mudança de coluna, de chave natural nem de contrato REST.

### Negativas

- Ambiente que já tem cidades só passa a devolver os nomes oficiais depois de uma nova importação da mesma versão da DNE com a imagem atualizada; o seed não recarrega sozinho.
- Atualizar a lista (município novo ou renomeado) exige um commit e uma imagem nova.
- Nomes já gravados em outros módulos (base legal, versão publicada do processo, inscrição) ficam como estão até serem regravados.
- A base local pronta do `uniplus-api` traz o banco do Geo com os nomes antigos e precisa ser gerada de novo.

## Confirmação

- Testes de integração: a lista tem os 5.571 códigos únicos de 7 dígitos; a carga grava «Pau D'Arco» no Pará e no Tocantins mesmo quando a DNE entrega «Pau DArco»; a recarga mantém nome e Guid; código fora da lista mantém o nome da DNE e gera amostra no relatório.
- Testes da API: a busca com e sem apóstrofo acha os municípios, e o filtro de UF separa os dois Pau D'Arco; a busca de bairro e de logradouro com `´` e `` ` `` continua achando.

## Mais informações

- Complementa a [ADR-0092](0092-etl-carga-dne-reference-data.md): a carga continua por upsert na chave natural; muda só a origem do nome do município.
