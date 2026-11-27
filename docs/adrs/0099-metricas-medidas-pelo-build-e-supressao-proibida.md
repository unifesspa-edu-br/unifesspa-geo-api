---
status: "accepted"
date: "2026-11-27"
decision-makers:
  - "Tech Lead (CTIC)"
consulted: []
informed:
  - "Equipe Uni+"
---

# ADR-0099: Tamanho e complexidade medidos pelo build e nenhuma supressão de aviso

## Contexto e enunciado do problema

O Geo limita o tamanho do código (arquivo, método e parâmetros), mas só conferia o limite à mão, na revisão, e aceitava aviso suprimido com justificativa. O resultado foi dívida que ninguém media: a primeira medição pelo build mostrou 284 métodos acima de 15 linhas e 30 acima de 7 parâmetros, e o repositório tem 72 supressões de aviso.

O Uni+ já resolveu o mesmo problema: os analisadores medem no build e a supressão de aviso é proibida (ADR-0155, ADR-0160 e ADR-0161 do repositório da API do Uni+). O Geo é independente do restante do Uni+ ([ADR-0090](0090-modulo-geo-localidades.md)): esta ADR registra a decisão no Geo, sem copiar as de lá.

## Drivers da decisão

- A conferência tem de ser a mesma para todos e não depender de quem revisa.
- A dívida existente não pode parar o build de quem entrega código novo.
- Nenhum aviso é escondido: o que excede se corrige.
- Os limites do Geo devem ser os da organização, os mesmos do Uni+.

## Opções consideradas

- **A**: continuar na revisão manual.
- **B**: ligar os analisadores como erro em todo o repositório de uma vez.
- **C**: ligar os analisadores, com as cinco métricas como aviso enquanto o projeto tem dívida, e passar o projeto a erro quando ele zera a dívida; nenhuma supressão.

## Resultado da decisão

**Escolhida:** "C", porque mede tudo agora sem parar o repositório por dívida antiga e impede que a dívida cresça.

- O build usa Sonar, Meziantou e Roslynator, cada regra com um dono só. Valem o número do aviso e estes limites: método com até 15 linhas (`MA0051`), arquivo com até 400 linhas de código (`S104`), até 7 parâmetros (`S107`), complexidade cognitiva até 15 (`S3776`) e ciclomática até 10 (`S1541`).
- Enquanto um projeto tem dívida de métrica, as cinco regras avisam (`WarningsNotAsErrors`); o projeto que zera a dívida entra em `MetricasBarradas` e a regra passa a barrar o build nele. A dívida é paga por área, em issues próprias.
- Supressão de aviso é proibida: nada de `SuppressMessage`, `GlobalSuppressions.cs`, `#pragma warning disable` nem `NoWarn`. O aviso se corrige no código.
- Uma regra pode ser desligada para uma camada inteira por caminho no `.editorconfig`, com o motivo escrito em uma linha: configuração de mapeamento do banco (só `MA0051`) e migrações geradas pela ferramenta.
- Uma regra só fica rebaixada a sugestão com issue que a fecha e linha no `CLAUDE.md` dizendo que é temporária.
- As supressões que já existem e o resto da dívida de métrica são trabalho das issues de dívida; nenhuma supressão nova entra a partir desta decisão.

## Consequências

### Positivas

- Tamanho e complexidade passam a ser conferidos no build, em produção e em teste.
- A dívida fica medida e dividida por área; quem toca um método acima do limite o refatora no mesmo trabalho.

### Negativas

- Até as issues de dívida fecharem, o build avisa em milhares de linhas e os avisos podem ser ignorados.
- Atualizar o Sonar exige atualizar a lista de regras desligadas (`.globalconfig`), senão regra nova nasce como aviso e vira erro.

## Confirmação

- Um arquivo, um método ou uma lista de parâmetros acima do limite, num projeto barrado, derruba o build apontando o local; no projeto com dívida, aparece como aviso.
- A revisão confere que nenhuma supressão de aviso entrou; nenhum analisador barra `SuppressMessage` e `#pragma`.
