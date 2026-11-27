---
status: "accepted"
date: "2026-11-27"
decision-makers:
  - "Tech Lead (CTIC)"
consulted: []
informed:
  - "Equipe Uni+"
---

# ADR-0098: Base dos Correios atualizada pelo envio do pacote pela API

## Contexto e enunciado do problema

A carga da base dos Correios ([ADR-0092](0092-etl-carga-dne-reference-data.md)) dependia de um banco de preparação à parte, com papel e segredo próprios. O operador restaurava nele o conteúdo do pacote executando o SQL do arquivo e depois disparava a carga por um pedido JSON que apenas informava a versão. Esse desenho tinha três problemas: executava SQL vindo de um arquivo externo, mantinha uma infraestrutura inteira só para a preparação e publicava a base em etapas separadas, de modo que uma falha no meio deixava versões misturadas.

A operação é rara (cerca de três vezes por ano, por uma única pessoa com o papel `plataforma-admin`), e o desenho deve ser proporcional a isso.

## Drivers da decisão

- Nunca executar SQL contido no arquivo recebido.
- Uma falha não pode alterar a base publicada.
- Menos infraestrutura para operar (sem banco, papel e segredo de preparação).
- Contexto Geo independente do restante do Uni+ ([ADR-0090](0090-modulo-geo-localidades.md)).
- Proteção do arquivo enquanto ele existe no servidor, sem custo desproporcional.

## Opções consideradas

- **A**: manter o banco de preparação e o disparo por JSON com a versão.
- **B**: receber o pacote pela API, guardá-lo cifrado pelo aplicativo e carregá-lo.
- **C**: receber o pacote pela API, guardá-lo em disco persistente.
- **D**: receber o pacote pela API, guardá-lo em volume em memória do pod, sem cifra no aplicativo, e carregá-lo em esquema temporário do próprio banco do Geo.

## Resultado da decisão

**Escolhida:** "D", porque elimina a infraestrutura de preparação sem criar dependência nova e sem manter o arquivo em disco.

- O operador envia o pacote em `POST /api/admin/geo/importacoes` com `Content-Type: application/zip`, no corpo da requisição, com o papel `plataforma-admin` do realm configurado.
- O arquivo é recebido em fluxo e gravado em volume em memória do pod, com permissão `0600` e nome igual ao identificador da execução. Não há cifra no aplicativo: uma chave guardada na memória do mesmo processo não protegeria contra quem já lê essa memória, e o volume em memória já evita a gravação em disco. Decisão do Tech Lead. O limite padrão do pacote é 128 MiB (`Geo:Etl:TamanhoMaximoDoPacote`).
- O diretório do zip é conferido na hora do envio, sem descompactar: as 15 tabelas esperadas, nenhuma tabela desconhecida ou entrada cifrada, todas no topo do zip ou numa única pasta, os arquivos que acompanham o pacote ignorados sem leitura, limite de entradas configurável e limites de tamanho, e uma única versão (AAAAMM). As faltas voltam juntas numa só resposta.
- A carga em segundo plano lê o SQL com um leitor de gramática fechada: reconhece só os comandos conhecidos do pacote, valida nomes e colunas contra uma lista fechada e nunca executa SQL do arquivo. Os dados vão para tabelas de um esquema temporário do próprio banco do Geo e daí para a base publicada.
- A base é publicada numa única transação, junto com a conclusão da execução. Se qualquer parte falhar, a versão anterior continua publicada.
- Uma trava consultiva do PostgreSQL é obtida no próprio pedido e mantida até o fim da carga. Há uma carga por vez; na partida, a trava livre indica que toda execução ainda em andamento foi abandonada.
- O arquivo e o esquema temporário são descartados em sucesso e em falha; na partida do serviço, o arquivo é apagado e o esquema temporário só é descartado quando a instância obtém a trava. A trava não é descartada na partida: ela só é tentada (ver a emenda de 27/11/2026).
- Enviar de novo o pacote cujo resumo SHA-256 é o da última carga concluída devolve 200 com essa execução, sem repetir a carga. Não existe opção de forçar a recarga do mesmo pacote.
- A versão do pacote não pode ser anterior à última aplicada. A versão vem do conteúdo, nunca do nome do arquivo.

## Alternativas descartadas

- **A (banco de preparação):** executa SQL externo e mantém infraestrutura, papel e segredo só para preparar.
- **B (cifra no aplicativo):** não protege contra quem lê a memória do processo e acrescenta custo de processamento sem ganho real sobre o volume em memória.
- **C (disco persistente):** deixa o arquivo em disco, em cópias de segurança e entre reinícios, sem necessidade.

## Consequências

### Positivas

- Somem o banco de preparação, seu papel e seu segredo.
- Nenhum SQL do arquivo é executado.
- Falha em qualquer ponto mantém a base anterior.
- Repetir o envio é seguro.

### Negativas

- A transação longa gera registros mortos por alguns minutos e impede implantar durante a carga.
- Depois de um reinício, o pacote precisa ser reenviado.
- O volume em memória e o limite de memória do pod precisam ser dimensionados.

### Neutras

- Mantém a [ADR-0092](0092-etl-carga-dne-reference-data.md) (upsert por chave natural e vigência por versão) e a [ADR-0097](0097-nome-do-municipio-pelo-ibge.md) (nome do município pelo IBGE).

## Confirmação

- Testes de integração cobrem o envio (200, 202, 401, 403, 409, 413, 415, 422), a conferência do zip, a repetição e a reversão integral em falha.
- A carga em homologação, com o pacote completo, precede a produção; a produção só recebe a versão nova por ordem do Tech Lead.
- Infraestrutura: o volume em memória, o diretório do pacote e o limite de memória ficam no chart de implantação, a cargo da equipe de infraestrutura.

## Emenda de 27/11/2026

Esclarece dois itens do resultado da decisão, sem mudá-la.

**Ordem do pedido.** Antes de ler o corpo, o envio é recusado com 409 se houver execução em andamento registrada ou outro envio sendo recebido na mesma instância. Depois o corpo é gravado no volume em memória e o diretório do zip é conferido (413, 415, 422). Só então a trava consultiva é obtida; seguem a guarda de versão (409), a repetição do pacote já publicado (200) e o registro da execução (202). A trava não cobre o recebimento: dois envios simultâneos em instâncias diferentes são recebidos cada um no volume da sua instância, e o que não obtém a trava recebe 409 e tem o arquivo apagado. Cada instância guarda no máximo um pacote.

**Descarte.** O arquivo do pacote é apagado assim que o esquema temporário é populado, em toda recusa do pedido (inclusive a repetição com 200), em toda falha ou cancelamento e, na partida, com tudo o que houver no diretório do pacote. O esquema temporário é descartado ao fim da carga, em sucesso ou falha, e na partida quando a instância obtém a trava. A trava é solta ao fim da carga ou na recusa posterior a ela, e não sobrevive ao processo; na partida ela só é tentada, para saber se as execuções em andamento foram abandonadas.

## Emenda de 27/11/2026 — execução abandonada

A trava livre autoriza, em qualquer momento e não só na partida, a tratar a execução em andamento como abandonada (a emenda seguinte protege o desfecho quando a trava é solta com a carga ainda viva). Antes de ler o corpo, o envio só é recusado com 409 se houver execução em andamento e a trava estiver ocupada. Com a trava livre, os esquemas temporários que sobraram são descartados e então as execuções em andamento são encerradas como falha, a trava é solta e o envio segue. Se o descarte falhar, as execuções seguem em andamento, o envio recebe o erro do servidor e o encerramento é tentado de novo no envio seguinte ou na partida. Isso substitui, na ordem do pedido da emenda anterior, a recusa por qualquer execução em andamento registrada. Consequência: uma falha ao registrar o fim de uma carga não bloqueia o envio seguinte até um reinício.

## Emenda de 27/11/2026 — conclusão condicional

O banco pode soltar a trava com a carga ainda executando, quando cai a conexão parada que a segura. Nesse caso um envio seguinte, ou a partida do serviço, encerra a execução como falha, e a carga antiga ainda termina o seu trabalho.

A conclusão só é gravada se a execução ainda está em andamento, na mesma transação da publicação. Se a execução já foi encerrada como falha, a conclusão é recusada, a transação inteira é desfeita e a base anterior continua publicada; a versão da carga antiga não conta como aplicada e o mesmo pacote pode ser reenviado. O encerramento, por sua vez, não grava falha sobre uma carga que acabou de concluir: nesse caso nada é encerrado e o envio segue.

Consequências: o histórico nunca conta uma carga concluída que não vale, e depois de uma queda da trava com a carga viva o pacote precisa ser reenviado. A decisão anterior não é reaberta.
