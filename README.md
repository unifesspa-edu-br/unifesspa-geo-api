# Unifesspa Geo API

API institucional transversal de localidades, endereçamento e georreferência da
UNIFESSPA.

Este repositório foi criado para separar o Geo do monólito modular Uni+ e
governar código, contrato OpenAPI, testes, imagem e release de forma
independente.

## Estado do Bootstrap

O branch `bootstrap/732-extract-geo-history` contém a extração histórica inicial
dos paths Geo vindos de `unifesspa-edu-br/uniplus-api`.

Os projetos extraídos foram renomeados para `Unifesspa.Geo.*`. O código
foundation necessário foi copiado para este repositório como código próprio,
também sob `Unifesspa.Geo.*`.

## Contrato

O contrato V1 deve preservar a semântica atual do Geo:

- `GET /openapi/geo.json`
- rotas públicas Geo existentes
- responses `application/problem+json`
- cursor pagination
- HATEOAS
- health checks
- vendor media types

O baseline inicial está em `contracts/openapi.geo.json`.

## Desenvolvimento

Consulte `docs/development.md`.

## Release e Consumo

Consulte `docs/release.md`.

## Proveniência

Consulte `docs/extraction-provenance.md`.
